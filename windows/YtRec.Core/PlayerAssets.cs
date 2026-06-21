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
        // --disable-gpu-compositing is the decisive one for a *full-size* video: without it a large/filled
        // video is promoted to a GPU swap-chain/overlay that WGC window-capture can't see (records black) —
        // forcing software compositing draws the video INTO the window's redirection surface WGC captures.
        "--disable-features=CalculateNativeWinOcclusion,DirectCompositionVideoOverlays,UseSurfaceLayerForVideo " +
        "--disable-direct-composition-video-overlays " +
        "--disable-gpu-compositing " +
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

    /// <summary>Injected on NavigationCompleted (mirrors mac <c>playerTakeoverJS</c>). CSS-stretches the entire
    /// YouTube player container chain to fill the window (100vw/100vh) so the captured surface is the video
    /// edge-to-edge — no page chrome, no crop needed (the whole window IS the video). Forces playback + unmute
    /// (audio must decode), pins quality to <c>hd1080</c>, and reports the source video's pixel dimensions
    /// (<c>dims:[videoWidth, videoHeight]</c> → host picks landscape/portrait output) and stream end
    /// (<c>state:'ended'</c>) via <c>window.chrome.webview.postMessage</c>. A non-16:9 source is
    /// <c>object-fit:contain</c> letterboxed inside the window — never distorted.</summary>
    public const string FillPlayAndReportScript = """
        (function () {
          var fill = 'visibility:visible!important;position:fixed!important;left:0!important;top:0!important;' +
            'right:0!important;bottom:0!important;width:100vw!important;height:100vh!important;' +
            'min-width:0!important;min-height:0!important;max-width:none!important;max-height:none!important;' +
            'margin:0!important;padding:0!important;transform:none!important;overflow:visible!important;background:#000!important';
          var css = 'html,body{background:#000!important;overflow:hidden!important;margin:0!important;padding:0!important}' +
            'body *{visibility:hidden!important}' +
            'ytd-app,#content,#page-manager,ytd-watch-flexy,#full-bleed-container,#player-full-bleed-container,' +
            '#player-theater-container,#columns,#primary,#primary-inner,#player,#player-container,' +
            '#player-container-inner,#player-api,#movie_player,.html5-video-player,.html5-video-container{' + fill + '}' +
            'video{visibility:visible!important;position:absolute!important;left:0!important;top:0!important;' +
            'width:100%!important;height:100%!important;object-fit:contain!important;' +
            'z-index:2147483647!important;background:#000!important;transform:none!important}';
          var style = document.createElement('style');
          style.textContent = css;
          (document.documentElement || document.body).appendChild(style);
          function post(o) { try { window.chrome.webview.postMessage(o); } catch (e) {} }
          var lastW = 0, lastH = 0;
          function tick() {
            try {
              var p = document.getElementById('movie_player');
              var v = document.querySelector('video');
              if (v) {
                if (v.muted) v.muted = false;
                if (v.volume < 1) v.volume = 1.0;
                if (v.paused && !v.ended) { var pr = v.play(); if (pr && pr.catch) pr.catch(function () {}); }
                if (v.videoWidth > 0 && (v.videoWidth !== lastW || v.videoHeight !== lastH)) {
                  lastW = v.videoWidth; lastH = v.videoHeight;
                  post({ type: 'ytrec', dims: [v.videoWidth, v.videoHeight] }); // host → landscape/portrait output
                }
                if (!v.__ytrecHooked) {
                  v.__ytrecHooked = true;
                  v.addEventListener('ended', function () { post({ type: 'ytrec', state: 'ended' }); });
                }
              }
              if (p) {
                if (p.unMute) p.unMute();
                if (p.setPlaybackQualityRange) p.setPlaybackQualityRange('hd1080', 'hd1080'); // pin 1080p
                if (p.setSize) { try { p.setSize(window.innerWidth, window.innerHeight); } catch (e) {} }
                if (p.getPlayerState && p.getPlayerState() === 0) post({ type: 'ytrec', state: 'ended' }); // 0 = ENDED
              }
              try { window.dispatchEvent(new Event('resize')); } catch (e) {}
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
