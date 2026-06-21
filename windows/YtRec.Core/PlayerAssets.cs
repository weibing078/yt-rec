namespace YtRec.Core;

/// <summary>WebView2 player wiring (mirrors the mac v1 in-page JS). The host plays the YouTube stream in
/// an off-screen but mapped WebView2 (never hidden/minimized — a fully hidden player decodes ZERO audio,
/// mac §4), with anti-occlusion Chromium flags so video frames keep flowing while occluded (§2d). These
/// are host-agnostic strings so they unit-test with no browser.</summary>
public static class PlayerAssets
{
    /// <summary>Chromium flags for the WebView2 environment so an occluded/off-screen player keeps
    /// rendering video frames and decoding audio (passed as AdditionalBrowserArguments).</summary>
    public const string BrowserArguments =
        // Keep rendering while occluded/backgrounded, disable the hardware video overlay (MPO) so the video
        // composites into the window's surface that WGC captures, and allow autoplay WITH sound (no gesture).
        // --disable-gpu is the decisive one for a *full-size* video: with the GPU on, a large/filled video is
        // promoted to a GPU swap-chain/overlay that WGC window-capture can't see (records BLACK while audio
        // still plays — verified on real Win11). Forcing full software rendering draws the video INTO the
        // window's redirection surface that WGC captures. Costs CPU, but it's the only reliable way to capture
        // full-size video via window-capture (Mac's ScreenCaptureKit captures overlays; WGC does not).
        "--disable-features=CalculateNativeWinOcclusion,DirectCompositionVideoOverlays,UseSurfaceLayerForVideo " +
        "--disable-direct-composition-video-overlays " +
        "--disable-gpu " +
        "--autoplay-policy=no-user-gesture-required " +
        "--disable-backgrounding-occluded-windows " +
        "--disable-renderer-backgrounding " +
        "--disable-background-timer-throttling";

    /// <summary>Watch-page URL for a video id (the full page has movie_player, needed for seek/DVR rewind).</summary>
    public static string WatchUrl(string videoId) => $"https://www.youtube.com/watch?v={videoId}";

    /// <summary>Resolve any YouTube URL to its watch-page URL, or null if it isn't a recognizable video.</summary>
    public static string? WatchUrlFrom(string rawUrl) =>
        YtUrl.VideoId(rawUrl) is string id ? WatchUrl(id) : null;

    /// <summary>Embed-player URL — just the video, no page chrome/sidebar/comments — so the captured window
    /// is clean video (mac §5 "you get the video, not the browser"). Hides controls and autoplays.</summary>
    public static string EmbedUrl(string videoId) =>
        $"https://www.youtube.com/embed/{videoId}?autoplay=1&controls=0&playsinline=1&rel=0&modestbranding=1";

    /// <summary>Resolve any YouTube URL to its embed-player URL (the capture target), or null.</summary>
    public static string? EmbedUrlFrom(string rawUrl) =>
        YtUrl.VideoId(rawUrl) is string id ? EmbedUrl(id) : null;

    /// <summary>Injected on NavigationCompleted. Keeps the YouTube player INLINE (never fullscreen-filled) —
    /// a full-size/fullscreen video is promoted to a GPU overlay that WGC window-capture renders BLACK, while
    /// an inline video composites into the capturable window surface (verified on real Win11). Switches the
    /// watch page to THEATER mode to make that inline player as large as possible (toward 1080p), forces
    /// playback + unmute and pins <c>hd1080</c>, and reports: the source pixel dims (<c>dims:[w,h]</c> →
    /// landscape/portrait output), the video element's rect as window fractions (<c>rect:[x,y,w,h]</c> → the
    /// host crops to the video only, dropping page chrome) and stream end (<c>state:'ended'</c>).</summary>
    public const string FillPlayAndReportScript = """
        (function () {
          function post(o) { try { window.chrome.webview.postMessage(o); } catch (e) {} }
          function player() { return document.getElementById('movie_player'); }
          // Guarantee a clean recording = the VIDEO only, never the YouTube UI. Hide every player overlay so a
          // progress bar / title / end-screen card / spinner / watermark can't land in a frame at any moment.
          try {
            var st = document.createElement('style');
            st.textContent = '.ytp-chrome-bottom,.ytp-chrome-top,.ytp-chrome-controls,.ytp-gradient-bottom,' +
              '.ytp-gradient-top,.ytp-ce-element,.ytp-endscreen-content,.ytp-pause-overlay,.ytp-watermark,' +
              '.ytp-cards-teaser,.ytp-ce-covering-overlay,.iv-branding,.annotation,.ytp-spinner,.ytp-tooltip,' +
              '.ytp-bezel,.ytp-doubletap-ui-legacy,.ytp-show-cards-title,.ytp-paid-content-overlay' +
              '{display:none!important;opacity:0!important}';
            (document.documentElement || document.body).appendChild(st);
          } catch (e) {}
          function ensureTheater() {
            try {
              var flexy = document.querySelector('ytd-watch-flexy');
              if (flexy && !flexy.hasAttribute('theater')) {
                var b = document.querySelector('.ytp-size-button');
                if (b) b.click(); // enlarge the inline player (stays inline → still WGC-capturable)
              }
            } catch (e) {}
          }
          var lastW = 0, lastH = 0;
          function tick() {
            try {
              var p = player(), v = document.querySelector('video');
              if (v) {
                if (v.muted) v.muted = false;
                if (v.volume < 1) v.volume = 1.0;
                if (v.paused && !v.ended) { var pr = v.play(); if (pr && pr.catch) pr.catch(function () {}); }
                if (v.videoWidth > 0 && (v.videoWidth !== lastW || v.videoHeight !== lastH)) {
                  lastW = v.videoWidth; lastH = v.videoHeight;
                  post({ type: 'ytrec', dims: [v.videoWidth, v.videoHeight] });
                }
                if (!v.__ytrecHooked) {
                  v.__ytrecHooked = true;
                  v.addEventListener('ended', function () { post({ type: 'ytrec', state: 'ended' }); });
                }
                // Crop to the actual PICTURE, not the player box: a vertical video sits letterboxed inside a
                // wider player element, so cropping the element gives big black pillars. Compute the
                // object-fit:contain picture rect from the source aspect so the output fills the frame.
                var er = v.getBoundingClientRect(), W = window.innerWidth, H = window.innerHeight;
                var vw = v.videoWidth, vh = v.videoHeight;
                if (er.width > 80 && er.height > 80 && W > 0 && H > 0 && vw > 0 && vh > 0) {
                  var sc = Math.min(er.width / vw, er.height / vh);
                  var pw = vw * sc, ph = vh * sc;
                  var px = er.left + (er.width - pw) / 2, py = er.top + (er.height - ph) / 2;
                  post({ type: 'ytrec', rect: [px / W, py / H, pw / W, ph / H] });
                }
              }
              if (p) {
                if (p.unMute) p.unMute();
                if (p.setPlaybackQualityRange) p.setPlaybackQualityRange('hd1080', 'hd1080'); // pin 1080p
                if (p.getPlayerState && p.getPlayerState() === 0) post({ type: 'ytrec', state: 'ended' }); // 0=ENDED
              }
              ensureTheater();
            } catch (e) {}
          }
          tick(); setTimeout(tick, 1000); setTimeout(tick, 3000); setInterval(tick, 2000);
        })();
        """;

    /// <summary>Script that returns the player's progress state JSON (current/seekable range) for the
    /// rewind UI — current, duration and the DVR seekable window for live streams.</summary>
    public const string ProgressStateScript =
        "(function(){var p=document.getElementById('movie_player');" +
        "return (p&&p.getProgressState)?JSON.stringify(p.getProgressState()):'null';})();";

    /// <summary>Script that seeks the player to <paramref name="seconds"/> (rewind), using the player API
    /// since raw video.currentTime is unreliable for live seeking (mac §6).</summary>
    public static string SeekScript(double seconds) =>
        $"(function(){{var p=document.getElementById('movie_player');" +
        $"if(p&&p.seekTo)p.seekTo({seconds.ToString(System.Globalization.CultureInfo.InvariantCulture)}, true);}})();";
}
