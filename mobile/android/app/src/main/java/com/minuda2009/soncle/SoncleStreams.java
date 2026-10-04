// Soncle · by minuda2009 (https://github.com/minuda2009) · GPL-3.0-or-later
package com.minuda2009.soncle;

import android.webkit.WebResourceRequest;
import android.webkit.WebResourceResponse;

import java.io.ByteArrayInputStream;
import java.io.ByteArrayOutputStream;
import java.io.IOException;
import java.io.InputStream;
import java.net.HttpURLConnection;
import java.net.URL;
import java.util.HashMap;
import java.util.Map;
import java.util.concurrent.ConcurrentHashMap;

/**
 * The phone's version of the desktop app's mstream: proxy. The player's audio element loads
 * https://localhost/_soncle/stream/VIDEO_ID; this answers those requests (with byte ranges, so
 * seeking works) by fetching the song from googlevideo in 1 MB pieces with the headers the stream
 * client needs. The JS side registers each song's stream URL first (SonclePlugin.registerStream)
 * and hands over a fresh URL when one expires.
 */
final class SoncleStreams {
    static final String PREFIX = "/_soncle/stream/";
    private static final int CHUNK = 1 << 20;

    static final class Entry {
        final String url, mime, error;
        final Map<String, String> headers;
        final long length;
        Entry(String url, Map<String, String> headers, long length, String mime, String error) {
            this.url = url; this.headers = headers; this.length = length; this.mime = mime; this.error = error;
        }
    }

    interface Expired { void onExpired(String id); }

    private static final Map<String, Entry> entries = new ConcurrentHashMap<>();
    private static final Object lock = new Object();
    static Expired expired;

    static void register(String id, Entry e) {
        synchronized (lock) {
            entries.put(id, e);
            lock.notifyAll();
        }
    }

    static void forget(String id) { entries.remove(id); }

    /** Waits up to `ms` for a registration of `id` that is different from `old`. */
    private static Entry await(String id, Entry old, long ms) {
        long until = System.currentTimeMillis() + ms;
        synchronized (lock) {
            Entry e = entries.get(id);
            while ((e == null || e == old) && System.currentTimeMillis() < until) {
                try { lock.wait(Math.max(1, until - System.currentTimeMillis())); } catch (InterruptedException ie) { return null; }
                e = entries.get(id);
            }
            return e == old ? null : e;
        }
    }

    static WebResourceResponse intercept(WebResourceRequest req) {
        String path = req.getUrl().getPath();
        if (path == null || !path.startsWith(PREFIX)) return null;
        String id = path.substring(PREFIX.length());
        Entry e = await(id, null, 30000);
        if (e == null) return error(504, "Stream not ready");
        if (e.error != null) return error(502, e.error);

        long start = 0, end = e.length - 1;
        String range = null;
        for (Map.Entry<String, String> h : req.getRequestHeaders().entrySet()) if ("range".equalsIgnoreCase(h.getKey())) range = h.getValue();
        boolean partial = range != null && range.startsWith("bytes=");
        if (partial) {
            String[] p = range.substring(6).split("-", -1);
            try {
                if (!p[0].isEmpty()) start = Long.parseLong(p[0].trim());
                if (p.length > 1 && !p[1].trim().isEmpty()) end = Math.min(end, Long.parseLong(p[1].trim()));
            } catch (NumberFormatException ignored) { start = 0; }
        }
        if (start >= e.length || start > end) {
            Map<String, String> h = baseHeaders();
            h.put("Content-Range", "bytes */" + e.length);
            return new WebResourceResponse(mime(e), null, 416, "Range Not Satisfiable", h, new ByteArrayInputStream(new byte[0]));
        }
        Map<String, String> h = baseHeaders();
        h.put("Content-Length", String.valueOf(end - start + 1));
        if (partial) h.put("Content-Range", "bytes " + start + "-" + end + "/" + e.length);
        return new WebResourceResponse(mime(e), null, partial ? 206 : 200, partial ? "Partial Content" : "OK", h, new Chunks(id, e, start, end));
    }

    private static Map<String, String> baseHeaders() {
        Map<String, String> h = new HashMap<>();
        h.put("Accept-Ranges", "bytes");
        h.put("Access-Control-Allow-Origin", "*");
        h.put("Cache-Control", "no-store");
        return h;
    }

    private static String mime(Entry e) {
        String m = e.mime == null ? "audio/webm" : e.mime;
        int i = m.indexOf(';');
        return (i > 0 ? m.substring(0, i) : m).trim();
    }

    private static WebResourceResponse error(int status, String msg) {
        return new WebResourceResponse("text/plain", "utf-8", status, msg.length() > 60 ? msg.substring(0, 60) : msg, baseHeaders(), new ByteArrayInputStream(msg.getBytes()));
    }

    /** Streams [start, end] of the song, fetching 1 MB at a time. */
    private static final class Chunks extends InputStream {
        private final String id;
        private Entry e;
        private long pos;
        private final long end;
        private byte[] buf = new byte[0];
        private int bi;

        Chunks(String id, Entry e, long start, long end) { this.id = id; this.e = e; this.pos = start; this.end = end; }

        private boolean fill() throws IOException {
            if (bi < buf.length) return true;
            if (pos > end) return false;
            long to = Math.min(end, pos + CHUNK - 1);
            IOException last = null;
            for (int attempt = 0; attempt < 4; attempt++) {
                try {
                    buf = fetch(e, pos, to);
                    if (buf.length == 0) throw new IOException("empty response");
                    bi = 0;
                    pos += buf.length;
                    return true;
                } catch (Expired403 x) {
                    // ask the app for a fresh URL to the same file, then carry on where we were
                    Entry old = e;
                    if (expired != null) expired.onExpired(id);
                    Entry fresh = await(id, old, 20000);
                    if (fresh == null || fresh.error != null || fresh.length != old.length) throw new IOException("stream expired");
                    e = fresh;
                } catch (IOException x) {
                    last = x;
                    try { Thread.sleep(500L * (attempt + 1)); } catch (InterruptedException ie) { throw new IOException("interrupted"); }
                }
            }
            throw last != null ? last : new IOException("stream failed");
        }

        @Override public int read() throws IOException { return fill() ? (buf[bi++] & 0xff) : -1; }

        @Override public int read(byte[] b, int off, int len) throws IOException {
            if (!fill()) return -1;
            int n = Math.min(len, buf.length - bi);
            System.arraycopy(buf, bi, b, off, n);
            bi += n;
            return n;
        }

        @Override public int available() { return buf.length - bi; }
    }

    private static final class Expired403 extends IOException { Expired403() { super("HTTP 403"); } }

    private static byte[] fetch(Entry e, long from, long to) throws IOException {
        HttpURLConnection c = (HttpURLConnection) new URL(e.url + (e.url.contains("?") ? "&" : "?") + "range=" + from + "-" + to).openConnection();
        try {
            c.setConnectTimeout(15000);
            c.setReadTimeout(20000);
            for (Map.Entry<String, String> h : e.headers.entrySet()) c.setRequestProperty(h.getKey(), h.getValue());
            int status = c.getResponseCode();
            if (status == 403) throw new Expired403();
            if (status >= 400) throw new IOException("HTTP " + status);
            try (InputStream in = c.getInputStream()) {
                ByteArrayOutputStream out = new ByteArrayOutputStream((int) (to - from + 1));
                byte[] b = new byte[1 << 16];
                int n;
                while ((n = in.read(b)) > 0) out.write(b, 0, n);
                return out.toByteArray();
            }
        } finally {
            c.disconnect();
        }
    }
}
