// Soncle · by minuda2009 (https://github.com/minuda2009) · GPL-3.0-or-later
package com.minuda2009.soncle;

import android.os.Bundle;
import android.webkit.WebResourceRequest;
import android.webkit.WebResourceResponse;
import android.webkit.WebSettings;
import android.webkit.WebView;
import com.getcapacitor.BridgeActivity;
import com.getcapacitor.BridgeWebViewClient;

public class MainActivity extends BridgeActivity {
    @Override
    public void onCreate(Bundle savedInstanceState) {
        registerPlugin(SonclePlugin.class);
        super.onCreate(savedInstanceState);
        WebView web = bridge != null ? bridge.getWebView() : null;
        if (web != null) {
            WebSettings s = web.getSettings();
            // The next song starts on its own (no tap) when the screen is off or the app is closed.
            s.setMediaPlaybackRequiresUserGesture(false);
        }
        if (bridge != null) {
            // Songs play from https://localhost/_soncle/stream/... (see SoncleStreams), the phone's
            // version of the desktop app's mstream: proxy. Everything else is Capacitor's as usual.
            bridge.setWebViewClient(new BridgeWebViewClient(bridge) {
                @Override
                public WebResourceResponse shouldInterceptRequest(WebView view, WebResourceRequest request) {
                    WebResourceResponse r = SoncleStreams.intercept(request);
                    return r != null ? r : super.shouldInterceptRequest(view, request);
                }
            });
        }
    }

    // Music keeps playing with the app in the background: the media notification runs a
    // foreground service, and the page's timers (crossfades, the next song) must keep running.
    @Override
    public void onPause() {
        super.onPause();
        keepRunning();
    }

    @Override
    public void onStop() {
        super.onStop();
        keepRunning();
    }

    private void keepRunning() {
        WebView web = bridge != null ? bridge.getWebView() : null;
        if (web != null) web.resumeTimers();
    }
}
