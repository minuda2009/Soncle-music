// Soncle · by minuda2009 (https://github.com/minuda2009) · GPL-3.0-or-later
package com.minuda2009.soncle;

import android.os.Bundle;
import android.webkit.WebSettings;
import android.webkit.WebView;
import com.getcapacitor.BridgeActivity;

public class MainActivity extends BridgeActivity {
    @Override
    public void onCreate(Bundle savedInstanceState) {
        super.onCreate(savedInstanceState);
        WebView web = bridge != null ? bridge.getWebView() : null;
        if (web != null) {
            WebSettings s = web.getSettings();
            // The next song starts on its own (no tap) when the screen is off or the app is closed.
            s.setMediaPlaybackRequiresUserGesture(false);
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
