// Soncle · by minuda2009 (https://github.com/minuda2009) · GPL-3.0-or-later
package com.minuda2009.soncle;

import android.annotation.SuppressLint;
import android.app.Dialog;
import android.bluetooth.BluetoothAdapter;
import android.bluetooth.BluetoothClass;
import android.bluetooth.BluetoothDevice;
import android.bluetooth.BluetoothManager;
import android.content.BroadcastReceiver;
import android.content.Context;
import android.content.Intent;
import android.content.IntentFilter;
import android.graphics.Bitmap;
import android.graphics.BitmapFactory;
import android.media.AudioAttributes;
import android.media.AudioDeviceCallback;
import android.media.AudioDeviceInfo;
import android.media.AudioManager;
import android.os.Build;
import android.os.Handler;
import android.os.Looper;
import android.util.Base64;
import android.webkit.CookieManager;
import android.webkit.JavascriptInterface;
import android.webkit.WebSettings;
import android.webkit.WebView;
import android.webkit.WebViewClient;

import com.getcapacitor.JSArray;
import com.getcapacitor.JSObject;
import com.getcapacitor.PermissionState;
import com.getcapacitor.Plugin;
import com.getcapacitor.PluginCall;
import com.getcapacitor.PluginMethod;
import com.getcapacitor.annotation.CapacitorPlugin;
import com.getcapacitor.annotation.Permission;
import com.getcapacitor.annotation.PermissionCallback;

import java.io.ByteArrayOutputStream;
import java.io.InputStream;
import java.net.HttpURLConnection;
import java.net.URL;
import java.util.ArrayList;
import java.util.Iterator;
import java.util.List;
import androidx.core.content.ContextCompat;
import java.util.Map;
import java.util.concurrent.ConcurrentHashMap;
import java.util.concurrent.ExecutorService;
import java.util.concurrent.Executors;
import java.util.concurrent.atomic.AtomicInteger;

/**
 * The few things the Android app needs native code for:
 *  - a hidden WebView with a youtube.com origin that runs BotGuard (po_token.html) for PO tokens,
 *    the same approach as the desktop app's hidden window;
 *  - byte-range requests to googlevideo with headers a web page may not set (Origin, Referer,
 *    User-Agent);
 *  - Google sign-in in a full-screen WebView, then reading the YouTube cookies;
 *  - the media session and playback notification (SoncleMediaService), which is also what
 *    Google Maps, the lock screen, headsets and cars talk to;
 *  - which output the sound is going to (speaker, wired, USB, Bluetooth, car), so the app can
 *    switch sound profiles like the desktop does.
 */
@CapacitorPlugin(name = "Soncle", permissions = {
    @Permission(alias = "bluetooth", strings = { "android.permission.BLUETOOTH_CONNECT" })
})
public class SonclePlugin extends Plugin {
    private static final String YT = "https://www.youtube.com/";
    private WebView bg;
    private PluginCall bgLoading;
    private final Map<String, PluginCall> pending = new ConcurrentHashMap<>();
    private final AtomicInteger seq = new AtomicInteger();
    private final ExecutorService io = Executors.newFixedThreadPool(4);

    private final Handler main = new Handler(Looper.getMainLooper());
    private String artUrl = "";

    private AudioDeviceCallback deviceCallback;
    private BroadcastReceiver noisyReceiver;
    private String lastOutputKey = null;
    private boolean noisyGuard = false;

    @Override
    public void load() {
        SoncleMediaService.listener = (action, seekTime) -> {
            JSObject d = new JSObject();
            d.put("action", action);
            if (seekTime >= 0) d.put("seekTime", seekTime);
            notifyListeners("mediaAction", d, true);
        };
        SoncleStreams.init(getContext().getCacheDir());
        SoncleStreams.expired = (key) -> {
            JSObject d = new JSObject();
            d.put("key", key);
            notifyListeners("streamExpired", d);
        };
        startOutputWatch();
    }

    // ---------- audio output ----------
    private AudioManager audio() {
        return (AudioManager) getContext().getSystemService(Context.AUDIO_SERVICE);
    }

    private void startOutputWatch() {
        AudioManager am = audio();
        if (am == null) return;
        deviceCallback = new AudioDeviceCallback() {
            @Override
            public void onAudioDevicesAdded(AudioDeviceInfo[] added) {
                maybeOutputChanged();
            }

            @Override
            public void onAudioDevicesRemoved(AudioDeviceInfo[] removed) {
                maybeOutputChanged();
            }
        };
        am.registerAudioDeviceCallback(deviceCallback, main);
        noisyReceiver = new BroadcastReceiver() {
            @Override
            public void onReceive(Context context, Intent intent) {
                if (AudioManager.ACTION_AUDIO_BECOMING_NOISY.equals(intent.getAction())) notifyNoisy();
            }
        };
        try {
            getContext().registerReceiver(noisyReceiver, new IntentFilter(AudioManager.ACTION_AUDIO_BECOMING_NOISY));
        } catch (Exception e) {
            noisyReceiver = null;   // becoming-noisy is a bonus; never let it break the plugin
        }
    }

    private void stopOutputWatch() {
        AudioManager am = audio();
        if (am != null && deviceCallback != null) {
            try { am.unregisterAudioDeviceCallback(deviceCallback); } catch (Exception ignored) { }
        }
        deviceCallback = null;
        if (noisyReceiver != null) {
            try { getContext().unregisterReceiver(noisyReceiver); } catch (Exception ignored) { }
        }
        noisyReceiver = null;
    }

    private void maybeOutputChanged() {
        // Routing can lag the device callback (LE Audio takes longer to settle), so re-read a few
        // times; otherwise a quick connect could still look like the old output and the profile
        // would not switch.
        checkOutput();
        main.postDelayed(this::checkOutput, 300);
        main.postDelayed(this::checkOutput, 900);
        main.postDelayed(this::checkOutput, 1800);
    }

    private void checkOutput() {
        JSObject o = readOutput();
        String key = o.getString("id", "") + "|" + o.getString("type", "") + "|" + o.getString("name", "");
        if (key.equals(lastOutputKey)) return;
        lastOutputKey = key;
        notifyListeners("outputChanged", o);
    }

    private void notifyNoisy() {
        JSObject d = new JSObject();
        d.put("noisy", true);
        notifyListeners("outputChanged", d);
        // "becoming noisy" often fires just before the device list settles; re-read so the switch to
        // the phone speaker (and its profile) is not missed. The guard stops a second pause toast.
        noisyGuard = true;
        main.postDelayed(() -> { noisyGuard = false; checkOutput(); }, 700);
    }

    /**
     * The output the sound is actually going to. Android 13+ answers this directly with
     * getAudioDevicesForAttributes (the anticipatory audio routing API); below that there is no
     * public per-app route, so prefer a connected personal output over the built-in speaker.
     */
    private AudioDeviceInfo pickOutput(AudioManager am) {
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.TIRAMISU) {
            try {
                AudioAttributes media = new AudioAttributes.Builder().setUsage(AudioAttributes.USAGE_MEDIA).build();
                List<AudioDeviceInfo> devices = am.getAudioDevicesForAttributes(media);
                if (devices != null && !devices.isEmpty()) return devices.get(0);
            } catch (Exception ignored) { /* fall through to the ordered scan */ }
        }
        AudioDeviceInfo bt = null, usb = null, wired = null, speaker = null;
        for (AudioDeviceInfo d : am.getDevices(AudioManager.GET_DEVICES_OUTPUTS)) {
            switch (d.getType()) {
                case AudioDeviceInfo.TYPE_BLUETOOTH_A2DP:
                case AudioDeviceInfo.TYPE_BLUETOOTH_SCO:
                    if (bt == null) bt = d;
                    break;
                case AudioDeviceInfo.TYPE_USB_DEVICE:
                case AudioDeviceInfo.TYPE_USB_HEADSET:
                case AudioDeviceInfo.TYPE_USB_ACCESSORY:
                    if (usb == null) usb = d;
                    break;
                case AudioDeviceInfo.TYPE_WIRED_HEADPHONES:
                case AudioDeviceInfo.TYPE_WIRED_HEADSET:
                    if (wired == null) wired = d;
                    break;
                case AudioDeviceInfo.TYPE_BUILTIN_SPEAKER:
                    if (speaker == null) speaker = d;
                    break;
                default:
                    if (isBluetooth(d.getType()) && bt == null) bt = d;   // BLE headset/speaker
                    break;
            }
        }
        return bt != null ? bt : usb != null ? usb : wired != null ? wired : speaker;
    }

    private JSObject readOutput() {
        AudioManager am = audio();
        AudioDeviceInfo d = am == null ? null : pickOutput(am);
        JSObject o = new JSObject();
        if (d == null) {
            o.put("id", 0);
            o.put("type", "speaker");
            o.put("name", "");
            o.put("bluetooth", false);
            o.put("noisy", noisyGuard);
            return o;
        }
        String type = isCarDevice(d) ? "car" : outputType(d.getType());
        o.put("id", d.getId());
        o.put("type", type);
        o.put("name", productName(d));
        o.put("bluetooth", isBluetooth(d.getType()));
        o.put("noisy", noisyGuard);
        return o;
    }

    private static boolean isBluetooth(int t) {
        if (t == AudioDeviceInfo.TYPE_BLUETOOTH_A2DP || t == AudioDeviceInfo.TYPE_BLUETOOTH_SCO) return true;
        // BLE audio (LE Audio earbuds) — common on newer Galaxy/Pixel phones — is Bluetooth too.
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.S
                && (t == AudioDeviceInfo.TYPE_BLE_HEADSET || t == AudioDeviceInfo.TYPE_BLE_SPEAKER
                    || t == AudioDeviceInfo.TYPE_BLE_BROADCAST || t == AudioDeviceInfo.TYPE_HEARING_AID)) return true;
        return false;
    }

    private static String outputType(int t) {
        switch (t) {
            case AudioDeviceInfo.TYPE_BUILTIN_SPEAKER:
            case AudioDeviceInfo.TYPE_BUILTIN_EARPIECE:
                return "speaker";
            case AudioDeviceInfo.TYPE_WIRED_HEADPHONES:
            case AudioDeviceInfo.TYPE_WIRED_HEADSET:
                return "wired";
            case AudioDeviceInfo.TYPE_USB_DEVICE:
            case AudioDeviceInfo.TYPE_USB_HEADSET:
            case AudioDeviceInfo.TYPE_USB_ACCESSORY:
                return "usb";
            case AudioDeviceInfo.TYPE_BLUETOOTH_A2DP:
            case AudioDeviceInfo.TYPE_BLUETOOTH_SCO:
                return "bluetooth";
            case AudioDeviceInfo.TYPE_HDMI:
            case AudioDeviceInfo.TYPE_HDMI_ARC:
            case AudioDeviceInfo.TYPE_HDMI_EARC:
                return "hdmi";
            default:
                if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.S
                        && (t == AudioDeviceInfo.TYPE_BLE_HEADSET || t == AudioDeviceInfo.TYPE_BLE_SPEAKER
                            || t == AudioDeviceInfo.TYPE_BLE_BROADCAST || t == AudioDeviceInfo.TYPE_HEARING_AID)) return "bluetooth";
                return "other";
        }
    }

    // The product name of a Bluetooth device needs BLUETOOTH_CONNECT on Android 12+; without it the
    // app still knows the type, so profiles work from the type alone.
    private String productName(AudioDeviceInfo d) {
        if (isBluetooth(d.getType()) && Build.VERSION.SDK_INT >= Build.VERSION_CODES.S
                && getPermissionState("bluetooth") != PermissionState.GRANTED) return "";
        try {
            CharSequence n = d.getProductName();
            return n == null ? "" : n.toString();
        } catch (SecurityException e) {
            return "";
        }
    }

    private boolean bluetoothConnectAllowed() {
        return Build.VERSION.SDK_INT < Build.VERSION_CODES.S
                || getPermissionState("bluetooth") == PermissionState.GRANTED;
    }

    /**
     * A car's Bluetooth class-of-device says "car audio" even when its name is not a car brand
     * (many say "Uconnect", a model number, or just "Bluetooth"). Needs BLUETOOTH_CONNECT to read
     * the device.
     */
    private boolean isCarDevice(AudioDeviceInfo d) {
        if (!isBluetooth(d.getType()) || !bluetoothConnectAllowed()) return false;
        try {
            BluetoothManager bm = (BluetoothManager) getContext().getSystemService(Context.BLUETOOTH_SERVICE);
            BluetoothAdapter adapter = bm == null ? null : bm.getAdapter();
            if (adapter == null) return false;
            BluetoothDevice device = adapter.getRemoteDevice(d.getAddress());
            BluetoothClass cls = device.getBluetoothClass();
            if (cls == null) return false;
            // Cars report the AUDIO_VIDEO major class with the car-audio minor class; there is no
            // separate "car" major class. Headset/speaker minor classes are deliberately excluded.
            return cls.getDeviceClass() == BluetoothClass.Device.AUDIO_VIDEO_CAR_AUDIO;
        } catch (Exception ignored) {
            return false;   /* class unavailable */
        }
    }

    /** The active media output: { id, type, name, bluetooth, noisy }. */
    @PluginMethod
    public void getOutput(PluginCall call) {
        call.resolve(readOutput());
    }

    /** Bluetooth output names the app can see (needs BLUETOOTH_CONNECT on Android 12+). */
    @PluginMethod
    public void getBluetoothDevices(PluginCall call) {
        List<String> seen = new ArrayList<>();
        AudioManager am = audio();
        if (am != null && bluetoothConnectAllowed()) {
            for (AudioDeviceInfo d : am.getDevices(AudioManager.GET_DEVICES_OUTPUTS)) {
                if (!isBluetooth(d.getType())) continue;
                String n = productName(d);
                if (!n.isEmpty() && !seen.contains(n)) seen.add(n);
            }
        }
        JSObject r = new JSObject();
        r.put("names", new JSArray(seen));
        call.resolve(r);
    }

    /** Whether BLUETOOTH_CONNECT is granted (or not needed); the UI asks when it isn't. */
    @PluginMethod
    public void getBluetoothPermission(PluginCall call) {
        JSObject r = new JSObject();
        r.put("granted", bluetoothConnectAllowed());
        call.resolve(r);
    }

    /** Asked when per-device profiles need Bluetooth names and the permission isn't granted yet. */
    @PluginMethod
    public void requestBluetoothPermission(PluginCall call) {
        if (Build.VERSION.SDK_INT < Build.VERSION_CODES.S) {
            JSObject r = new JSObject();
            r.put("granted", true);
            call.resolve(r);
            return;
        }
        if (getPermissionState("bluetooth") == PermissionState.GRANTED) {
            JSObject r = new JSObject();
            r.put("granted", true);
            call.resolve(r);
            return;
        }
        requestPermissionForAlias("bluetooth", call, "bluetoothPermissionCallback");
    }

    @PermissionCallback
    private void bluetoothPermissionCallback(PluginCall call) {
        if (call == null) return;
        JSObject r = new JSObject();
        r.put("granted", getPermissionState("bluetooth") == PermissionState.GRANTED);
        call.resolve(r);
    }

    // ---------- song streams (SoncleStreams) ----------
    /** { key, url, headers, length, mime } or { key, error } */
    @PluginMethod
    public void registerStream(PluginCall call) {
        String key = call.getString("key");
        if (key == null) { call.reject("key required"); return; }
        String error = call.getString("error", null);
        java.util.Map<String, String> headers = new java.util.HashMap<>();
        JSObject h = call.getObject("headers", new JSObject());
        Iterator<String> keys = h.keys();
        while (keys.hasNext()) { String k = keys.next(); headers.put(k, h.getString(k)); }
        Double len = call.getDouble("length", 0.0);
        SoncleStreams.register(key, new SoncleStreams.Entry(call.getString("url", ""), headers, len == null ? 0 : len.longValue(), call.getString("mime", "audio/webm"), error));
        call.resolve();
    }

    @PluginMethod
    public void forgetStream(PluginCall call) {
        String key = call.getString("key");
        if (key != null) SoncleStreams.forget(key);
        call.resolve();
    }

    // ---------- media session ----------
    /** { title, artist, album, artwork (https URL), playing, position (s), duration (s), rate } */
    @PluginMethod
    public void mediaUpdate(PluginCall call) {
        final SoncleMediaService.State s = SoncleMediaService.state;
        final String url = call.getString("artwork", "");
        final boolean playing = Boolean.TRUE.equals(call.getBoolean("playing", false));
        final String title = call.getString("title", ""), artist = call.getString("artist", ""), album = call.getString("album", "");
        final long pos = Math.round(call.getDouble("position", 0.0) * 1000), dur = Math.round(call.getDouble("duration", 0.0) * 1000);
        final float rate = call.getFloat("rate", 1f);
        main.post(() -> {
            if (!title.equals(s.title)) s.art = null;
            s.title = title; s.artist = artist; s.album = album;
            s.playing = playing; s.positionMs = pos; s.durationMs = dur; s.rate = rate;
            SoncleMediaService svc = SoncleMediaService.instance;
            if (svc != null) svc.apply();
            else if (playing) ContextCompat.startForegroundService(getContext(), new Intent(getContext(), SoncleMediaService.class));
            if (url != null && !url.isEmpty() && (!url.equals(artUrl) || s.art == null)) loadArt(url);
            call.resolve();
        });
    }

    private void loadArt(final String url) {
        artUrl = url;
        io.execute(() -> {
            Bitmap bmp = null;
            try (InputStream in = new URL(url).openStream()) { bmp = BitmapFactory.decodeStream(in); } catch (Exception ignored) { }
            final Bitmap art = bmp;
            main.post(() -> {
                if (art == null || !url.equals(artUrl)) return;
                SoncleMediaService.state.art = art;
                SoncleMediaService svc = SoncleMediaService.instance;
                if (svc != null) svc.apply();
            });
        });
    }

    @PluginMethod
    public void mediaStop(PluginCall call) {
        main.post(() -> {
            SoncleMediaService.state.playing = false;
            SoncleMediaService svc = SoncleMediaService.instance;
            if (svc != null) svc.shutDown();
            call.resolve();
        });
    }

    // ---------- BotGuard ----------
    public class Bridge {
        @JavascriptInterface
        public void done(String id, String json) {
            PluginCall c = pending.remove(id);
            if (c == null) return;
            JSObject r = new JSObject();
            r.put("json", json);
            c.resolve(r);
        }

        @JavascriptInterface
        public void fail(String id, String message) {
            PluginCall c = pending.remove(id);
            if (c != null) c.reject(message == null ? "BotGuard error" : message);
        }
    }

    @SuppressLint({"SetJavaScriptEnabled", "AddJavascriptInterface"})
    @PluginMethod
    public void botguardLoad(PluginCall call) {
        final String html = call.getString("html", "");
        final String ua = call.getString("userAgent", null);
        getActivity().runOnUiThread(() -> {
            destroyBotguard();
            bg = new WebView(getContext());
            WebSettings s = bg.getSettings();
            s.setJavaScriptEnabled(true);
            if (ua != null) s.setUserAgentString(ua);
            bg.addJavascriptInterface(new Bridge(), "SoncleBridge");
            bgLoading = call;
            bg.setWebViewClient(new WebViewClient() {
                @Override
                public void onPageFinished(WebView view, String url) {
                    if (bgLoading != null) {
                        bgLoading.resolve();
                        bgLoading = null;
                    }
                }
            });
            bg.loadDataWithBaseURL(YT, html, "text/html", "utf-8", null);
        });
    }

    /** Runs a JS expression (it may return a promise) in the BotGuard page; resolves { json }. */
    @PluginMethod
    public void botguardExec(PluginCall call) {
        final String code = call.getString("code", "null");
        final String id = "c" + seq.incrementAndGet();
        pending.put(id, call);
        final String js = "(function(){try{Promise.resolve(" + code + ").then(function(r){SoncleBridge.done('" + id + "',JSON.stringify(r===undefined?null:r));},"
                + "function(e){SoncleBridge.fail('" + id + "',String(e&&e.message||e));});}catch(e){SoncleBridge.fail('" + id + "',String(e&&e.message||e));}})();";
        getActivity().runOnUiThread(() -> {
            if (bg == null) {
                pending.remove(id);
                call.reject("BotGuard page not loaded");
                return;
            }
            bg.evaluateJavascript(js, null);
        });
    }

    @PluginMethod
    public void botguardReset(PluginCall call) {
        getActivity().runOnUiThread(() -> {
            destroyBotguard();
            call.resolve();
        });
    }

    private void destroyBotguard() {
        for (PluginCall c : pending.values()) c.reject("BotGuard page reset");
        pending.clear();
        if (bgLoading != null) {
            bgLoading.reject("BotGuard page reset");
            bgLoading = null;
        }
        if (bg != null) {
            bg.destroy();
            bg = null;
        }
    }

    // ---------- byte ranges ----------
    /** GET with any headers; resolves { status, data (base64), contentRange }. */
    @PluginMethod
    public void httpBytes(PluginCall call) {
        final String url = call.getString("url");
        final JSObject headers = call.getObject("headers", new JSObject());
        io.execute(() -> {
            HttpURLConnection c = null;
            try {
                c = (HttpURLConnection) new URL(url).openConnection();
                c.setConnectTimeout(15000);
                c.setReadTimeout(20000);
                Iterator<String> keys = headers.keys();
                while (keys.hasNext()) {
                    String k = keys.next();
                    c.setRequestProperty(k, headers.getString(k));
                }
                int status = c.getResponseCode();
                InputStream in = status >= 400 ? c.getErrorStream() : c.getInputStream();
                ByteArrayOutputStream out = new ByteArrayOutputStream(1 << 20);
                if (in != null) {
                    byte[] buf = new byte[1 << 16];
                    int n;
                    while ((n = in.read(buf)) > 0) out.write(buf, 0, n);
                    in.close();
                }
                JSObject r = new JSObject();
                r.put("status", status);
                r.put("data", Base64.encodeToString(out.toByteArray(), Base64.NO_WRAP));
                String range = c.getHeaderField("Content-Range");
                if (range != null) r.put("contentRange", range);
                call.resolve(r);
            } catch (Exception e) {
                call.reject(e.getMessage() == null ? e.toString() : e.getMessage());
            } finally {
                if (c != null) c.disconnect();
            }
        });
    }

    // ---------- Google sign-in ----------
    /** Opens Google's sign-in page full screen; resolves { cookie } once YouTube's session cookies exist. */
    @SuppressLint("SetJavaScriptEnabled")
    @PluginMethod
    public void signIn(PluginCall call) {
        final String url = call.getString("url");
        getActivity().runOnUiThread(() -> {
            final boolean[] finished = {false};
            final Dialog dialog = new Dialog(getActivity(), android.R.style.Theme_DeviceDefault_NoActionBar);
            final WebView web = new WebView(getActivity());
            final CookieManager cookies = CookieManager.getInstance();
            cookies.setAcceptCookie(true);
            cookies.setAcceptThirdPartyCookies(web, true);
            WebSettings s = web.getSettings();
            s.setJavaScriptEnabled(true);
            s.setDomStorageEnabled(true);
            // Google refuses sign-in in pages it identifies as an embedded WebView ("; wv").
            s.setUserAgentString(s.getUserAgentString().replace("; wv", ""));
            web.setWebViewClient(new WebViewClient() {
                @Override
                public void onPageFinished(WebView view, String page) {
                    if (finished[0]) return;
                    String ck = cookies.getCookie("https://music.youtube.com");
                    if (ck != null && (ck.contains("SAPISID=") || ck.contains("__Secure-3PAPISID="))) {
                        finished[0] = true;
                        cookies.flush();
                        JSObject r = new JSObject();
                        r.put("cookie", ck);
                        call.resolve(r);
                        dialog.dismiss();
                    }
                }
            });
            dialog.setOnDismissListener(d -> {
                if (!finished[0]) {
                    finished[0] = true;
                    call.reject("Sign-in was cancelled");
                }
                web.destroy();
            });
            dialog.setContentView(web);
            dialog.show();
            web.loadUrl(url);
        });
    }

    @PluginMethod
    public void signOut(PluginCall call) {
        getActivity().runOnUiThread(() -> CookieManager.getInstance().removeAllCookies(ok -> {
            CookieManager.getInstance().flush();
            call.resolve();
        }));
    }

    @Override
    protected void handleOnDestroy() {
        stopOutputWatch();
        destroyBotguard();
        io.shutdownNow();
    }
}
