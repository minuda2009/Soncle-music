// Soncle · by minuda2009 (https://github.com/minuda2009) · GPL-3.0-or-later
package com.minuda2009.soncle;

import android.app.Notification;
import android.app.NotificationChannel;
import android.app.NotificationManager;
import android.app.PendingIntent;
import android.content.Intent;
import android.content.pm.ServiceInfo;
import android.graphics.Bitmap;
import android.os.Build;
import android.os.Bundle;
import android.support.v4.media.MediaBrowserCompat;
import android.support.v4.media.MediaMetadataCompat;
import android.support.v4.media.session.MediaSessionCompat;
import android.support.v4.media.session.PlaybackStateCompat;

import androidx.annotation.NonNull;
import androidx.annotation.Nullable;
import androidx.core.app.NotificationCompat;
import androidx.core.app.NotificationManagerCompat;
import androidx.core.app.ServiceCompat;
import androidx.media.MediaBrowserServiceCompat;
import androidx.media.session.MediaButtonReceiver;

import java.util.ArrayList;
import java.util.List;

/**
 * Soncle's media session and playback notification.
 *
 * It is a MediaBrowserService, which is what lets other apps find and control Soncle:
 *  - Google Maps (Settings → Navigation → Show media playback controls → Soncle), so the song and
 *    play/pause/skip appear at the bottom of the navigation screen;
 *  - the lock screen, the notification shade, Bluetooth headsets and car head units, smartwatches.
 * While music plays it is a foreground service, so Android keeps the app alive in the background.
 */
public class SoncleMediaService extends MediaBrowserServiceCompat {
    static final String CHANNEL = "playback";
    static final int NOTE_ID = 7;

    /** What the app last reported; applied when the service starts. */
    static class State {
        String title = "", artist = "", album = "";
        Bitmap art;
        boolean playing;
        long positionMs, durationMs;
        float rate = 1f;
    }

    interface Listener { void onAction(String action, double seekTime); }

    static SoncleMediaService instance;
    static final State state = new State();
    static Listener listener;

    private MediaSessionCompat session;
    private boolean foreground;
    private String shownKey = "";

    private static void emit(String action, double seek) {
        if (listener != null) listener.onAction(action, seek);
    }

    @Override
    public void onCreate() {
        super.onCreate();
        instance = this;
        session = new MediaSessionCompat(this, "Soncle");
        session.setCallback(new MediaSessionCompat.Callback() {
            @Override public void onPlay() { emit("play", -1); }
            @Override public void onPause() { emit("pause", -1); }
            @Override public void onSkipToNext() { emit("nexttrack", -1); }
            @Override public void onSkipToPrevious() { emit("previoustrack", -1); }
            @Override public void onSeekTo(long pos) { emit("seekto", pos / 1000.0); }
            @Override public void onFastForward() { emit("seekforward", -1); }
            @Override public void onRewind() { emit("seekbackward", -1); }
            @Override public void onStop() {
                emit("pause", -1);
                if (!state.playing) shutDown();
            }
        });
        Intent open = getPackageManager().getLaunchIntentForPackage(getPackageName());
        if (open != null) {
            session.setSessionActivity(PendingIntent.getActivity(this, 0, open, PendingIntent.FLAG_IMMUTABLE | PendingIntent.FLAG_UPDATE_CURRENT));
        }
        session.setActive(true);
        setSessionToken(session.getSessionToken());
        if (Build.VERSION.SDK_INT >= 26) {
            NotificationChannel ch = new NotificationChannel(CHANNEL, "Playback", NotificationManager.IMPORTANCE_LOW);
            ch.setShowBadge(false);
            getSystemService(NotificationManager.class).createNotificationChannel(ch);
        }
        apply();
    }

    @Override
    public int onStartCommand(Intent intent, int flags, int startId) {
        MediaButtonReceiver.handleIntent(session, intent);
        apply();
        return START_NOT_STICKY;
    }

    // ---------- browsing (Google Maps, Android Auto and others connect here) ----------
    @Nullable
    @Override
    public BrowserRoot onGetRoot(@NonNull String clientPackageName, int clientUid, @Nullable Bundle rootHints) {
        return new BrowserRoot("soncle_root", null);
    }

    @Override
    public void onLoadChildren(@NonNull String parentId, @NonNull Result<List<MediaBrowserCompat.MediaItem>> result) {
        result.sendResult(new ArrayList<>());
    }

    // ---------- state → session, notification, foreground ----------
    void apply() {
        State s = state;
        MediaMetadataCompat.Builder m = new MediaMetadataCompat.Builder()
                .putString(MediaMetadataCompat.METADATA_KEY_TITLE, s.title)
                .putString(MediaMetadataCompat.METADATA_KEY_ARTIST, s.artist)
                .putString(MediaMetadataCompat.METADATA_KEY_ALBUM, s.album)
                .putLong(MediaMetadataCompat.METADATA_KEY_DURATION, s.durationMs);
        if (s.art != null) {
            m.putBitmap(MediaMetadataCompat.METADATA_KEY_ALBUM_ART, s.art);
            m.putBitmap(MediaMetadataCompat.METADATA_KEY_ART, s.art);
        }
        session.setMetadata(m.build());
        long actions = PlaybackStateCompat.ACTION_PLAY | PlaybackStateCompat.ACTION_PAUSE | PlaybackStateCompat.ACTION_PLAY_PAUSE
                | PlaybackStateCompat.ACTION_SKIP_TO_NEXT | PlaybackStateCompat.ACTION_SKIP_TO_PREVIOUS
                | PlaybackStateCompat.ACTION_SEEK_TO | PlaybackStateCompat.ACTION_STOP;
        session.setPlaybackState(new PlaybackStateCompat.Builder()
                .setActions(actions)
                .setState(s.playing ? PlaybackStateCompat.STATE_PLAYING : PlaybackStateCompat.STATE_PAUSED, s.positionMs, s.playing ? s.rate : 0f)
                .build());

        // Rebuild the notification only when what it shows changes (not for position updates).
        String key = s.title + '|' + s.artist + '|' + s.playing + '|' + (s.art != null ? s.art.getGenerationId() : 0);
        if (key.equals(shownKey) && foreground == s.playing) return;
        shownKey = key;
        Notification n = notification(s);
        if (s.playing) {
            ServiceCompat.startForeground(this, NOTE_ID, n, Build.VERSION.SDK_INT >= 29 ? ServiceInfo.FOREGROUND_SERVICE_TYPE_MEDIA_PLAYBACK : 0);
            foreground = true;
        } else {
            if (foreground) ServiceCompat.stopForeground(this, ServiceCompat.STOP_FOREGROUND_DETACH);
            foreground = false;
            try { NotificationManagerCompat.from(this).notify(NOTE_ID, n); } catch (SecurityException ignored) { /* notifications turned off */ }
        }
    }

    private Notification notification(State s) {
        NotificationCompat.Action prev = new NotificationCompat.Action(android.R.drawable.ic_media_previous, "Previous",
                MediaButtonReceiver.buildMediaButtonPendingIntent(this, PlaybackStateCompat.ACTION_SKIP_TO_PREVIOUS));
        NotificationCompat.Action toggle = s.playing
                ? new NotificationCompat.Action(android.R.drawable.ic_media_pause, "Pause", MediaButtonReceiver.buildMediaButtonPendingIntent(this, PlaybackStateCompat.ACTION_PAUSE))
                : new NotificationCompat.Action(android.R.drawable.ic_media_play, "Play", MediaButtonReceiver.buildMediaButtonPendingIntent(this, PlaybackStateCompat.ACTION_PLAY));
        NotificationCompat.Action next = new NotificationCompat.Action(android.R.drawable.ic_media_next, "Next",
                MediaButtonReceiver.buildMediaButtonPendingIntent(this, PlaybackStateCompat.ACTION_SKIP_TO_NEXT));
        return new NotificationCompat.Builder(this, CHANNEL)
                .setSmallIcon(R.drawable.ic_stat_soncle)
                .setContentTitle(s.title)
                .setContentText(s.artist)
                .setLargeIcon(s.art)
                .setContentIntent(session.getController().getSessionActivity())
                .setDeleteIntent(MediaButtonReceiver.buildMediaButtonPendingIntent(this, PlaybackStateCompat.ACTION_STOP))
                .setVisibility(NotificationCompat.VISIBILITY_PUBLIC)
                .setOnlyAlertOnce(true)
                .setShowWhen(false)
                .setOngoing(s.playing)
                .addAction(prev).addAction(toggle).addAction(next)
                .setStyle(new androidx.media.app.NotificationCompat.MediaStyle()
                        .setMediaSession(session.getSessionToken())
                        .setShowActionsInCompactView(0, 1, 2))
                .build();
    }

    void shutDown() {
        ServiceCompat.stopForeground(this, ServiceCompat.STOP_FOREGROUND_REMOVE);
        foreground = false;
        shownKey = "";
        NotificationManagerCompat.from(this).cancel(NOTE_ID);
        stopSelf();
    }

    @Override
    public void onDestroy() {
        instance = null;
        if (session != null) {
            session.setActive(false);
            session.release();
        }
        super.onDestroy();
    }
}
