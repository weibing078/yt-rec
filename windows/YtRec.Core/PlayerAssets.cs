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
        // Keep rendering while occluded/backgrounded, and disable the hardware video overlay (MPO) so the
        // video composites into the window's surface that WGC captures.
        "--disable-features=CalculateNativeWinOcclusion,DirectCompositionVideoOverlays,UseSurfaceLayerForVideo " +
        "--disable-direct-composition-video-overlays " +
        "--disable-backgrounding-occluded-windows " +
        "--disable-renderer-backgrounding " +
        "--disable-background-timer-throttling";

    /// <summary>Watch-page URL for a video id (the full page has movie_player, needed for seek/DVR rewind).</summary>
    public static string WatchUrl(string videoId) => $"https://www.youtube.com/watch?v={videoId}";

    /// <summary>Resolve any YouTube URL to its watch-page URL, or null if it isn't a recognizable video.</summary>
    public static string? WatchUrlFrom(string rawUrl) =>
        YtUrl.VideoId(rawUrl) is string id ? WatchUrl(id) : null;

    /// <summary>Injected on NavigationCompleted: force playback, unmute so audio decodes, and report the
    /// stream ending back to the host via window.chrome.webview.postMessage({type:'ytrec', state:'ended'}).
    /// Polls player state too, since YouTube doesn't reliably fire video 'ended' on live state changes.</summary>
    public const string ForcePlayAndReportScript = """
        (function () {
          function player() { return document.getElementById('movie_player'); }
          function start() {
            var p = player(), v = document.querySelector('video');
            try { if (p && p.unMute) { p.unMute(); p.setVolume(100); } else if (v) { v.muted = false; v.volume = 1.0; } } catch (e) {}
            try { if (v && v.play) v.play().catch(function(){}); } catch (e) {}
            try { if (p && p.playVideo) p.playVideo(); } catch (e) {}
          }
          function report(state) {
            try { window.chrome.webview.postMessage({ type: 'ytrec', state: state }); } catch (e) {}
          }
          start(); setTimeout(start, 1000); setTimeout(start, 3000);
          var v = document.querySelector('video');
          if (v) v.addEventListener('ended', function () { report('ended'); });
          setInterval(function () {
            var p = player();
            if (p && p.getPlayerState && p.getPlayerState() === 0) report('ended'); // 0 = ENDED
          }, 2000);
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
