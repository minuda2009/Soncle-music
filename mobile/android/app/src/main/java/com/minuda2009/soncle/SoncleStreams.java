// Soncle · by minuda2009 (https://github.com/minuda2009) · GPL-3.0-or-later
package com.minuda2009.soncle;

import android.webkit.WebResourceRequest;
import android.webkit.WebResourceResponse;

import java.io.ByteArrayInputStream;
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

    /**
     * Streams [start, end] of the song as the bytes arrive: googlevideo is asked for at most 1 MB
     * per request (some stream clients refuse more), but each piece is passed on while it
     * downloads, so playback starts after the first few KB instead of after a whole megabyte. The
     * first piece is small so the player has something to decode almost at once. Connections are
     * kept alive between pieces.
     */
    private static final class Chunks extends InputStream {
        private static final int FIRST = 256 * 1024;
        private final String id;
        private Entry e;
        private long pos;
        private final long start, end;
        private InputStream cur;
        private int failures;

        Chunks(String id, Entry e, long start, long end) { this.id = id; this.e = e; this.start = start; this.pos = start; this.end = end; }

        private boolean open() throws IOException {
            if (cur != null) return true;
            if (pos > end) return false;
            long to = Math.min(end, pos + (pos == start ? FIRST : CHUNK) - 1);
            while (true) {
                try {
                    cur = connect(e, pos, to);
                    return true;
                } catch (Expired403 x) {
                    // ask the app for a fresh URL to the same file, then carry on where we were
                    Entry old = e;
                    if (expired != null) expired.onExpired(id);
                    Entry fresh = await(id, old, 20000);
                    if (fresh == null || fresh.error != null || fresh.length != old.length) throw new IOException("stream expired");
                    e = fresh;
                } catch (IOException x) {
                    if (++failures > 4) throw x;
                    pause(failures);
                }
            }
        }

        private void close(InputStream in) { try { in.close(); } catch (IOException ignored) { } }

        private void pause(int n) throws IOException {
            try { Thread.sleep(400L * n); } catch (InterruptedException ie) { throw new IOException("interrupted"); }
        }

        @Override public int read(byte[] b, int off, int len) throws IOException {
            while (true) {
                if (!open()) return -1;
                int n;
                try {
                    n = cur.read(b, off, (int) Math.min(len, end - pos + 1));
                } catch (IOException x) {
                    close(cur);
                    cur = null;
                    if (++failures > 4) throw x;
                    pause(failures);
                    continue;          // reopen from the current position
                }
                if (n < 0) {           // this piece is done; the next open() continues at pos
                    close(cur);
                    cur = null;
                    continue;
                }
                if (n > 0) { pos += n; failures = 0; return n; }
            }
        }

        @Override public int read() throws IOException {
            byte[] one = new byte[1];
            return read(one, 0, 1) < 0 ? -1 : one[0] & 0xff;
        }

        @Override public void close() { if (cur != null) close(cur); cur = null; }
    }

    private static final class Expired403 extends IOException { Expired403() { super("HTTP 403"); } }

    /** Opens [from, to] of the stream; the caller reads (and closes) the body. */
    private static InputStream connect(Entry e, long from, long to) throws IOException {
        HttpURLConnection c = (HttpURLConnection) new URL(e.url + (e.url.contains("?") ? "&" : "?") + "range=" + from + "-" + to).openConnection();
        c.setConnectTimeout(15000);
        c.setReadTimeout(20000);
        for (Map.Entry<String, String> h : e.headers.entrySet()) c.setRequestProperty(h.getKey(), h.getValue());
        int status = c.getResponseCode();
        if (status == 403) { c.disconnect(); throw new Expired403(); }
        if (status >= 400) { c.disconnect(); throw new IOException("HTTP " + status); }
        return c.getInputStream();
    }
}
