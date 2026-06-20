# Architecture

How YT Rec is structured across macOS and Windows, and why.

## 1. Guiding principle: native per platform, synced by spec

The product *is* native screen + audio capture. No cross-platform UI framework
(Electron / Flutter / .NET MAUI) exposes the required capabilities cleanly:

- **Single-window video capture** that keeps recording even when the window is
  occluded by other windows.
- **Per-window / per-process audio capture** that records *only* the target
  window's audio and excludes every other app.

So each platform is built with its own native stack, and parity is maintained
**not by sharing code but by sharing a behavior spec + acceptance tests**:

- [`shared/spec/`](../shared/spec/) — the language-neutral canonical behavior
  (strategy ordering, thresholds, session-gating, parsing rules).
- [`docs/TEST-PLAN.md`](TEST-PLAN.md) — the acceptance matrix both apps must pass.
- The small pure-logic core is implemented twice (Swift + C#) and both
  implementations are unit-tested against the same cases.

## 2. The two-track capture model

Both platforms implement the same dual-track design:

- **Track A — Download** (`yt-dlp` + `ffmpeg`, run as subprocesses). Probes
  `live_status`, then tries ordered strategies on a polling loop. Best for
  already-ended VODs (faster, higher quality). Cross-platform by nature.
- **Track B — Side-recording** (the core). Plays the stream in an embedded
  browser surface and captures **that window's** video + audio, segmenting to
  crash-resistant fMP4. This is the killer feature and the hard part to port.

The download track is **secondary / default-off** in v2; screen-side recording
is the primary capture method (see [PRD](PRD.md) D2).

## 3. Stack mapping (macOS ↔ Windows)

| Concern | macOS (shipping) | Windows (target) |
|---|---|---|
| Language / UI | Swift · SwiftUI · AppKit | C# / .NET 8 · WinUI 3 |
| Single-window video capture | `ScreenCaptureKit` `SCContentFilter(desktopIndependentWindow:)` | `Windows.Graphics.Capture` (per-window) |
| Per-window/app audio isolation | `SCStreamConfiguration.capturesAudio` on a single-window filter | **WASAPI process-loopback** (`ActivateAudioInterfaceAsync` + `PROCESS_LOOPBACK`, Win 10 20348+/Win 11) |
| Hardware video encode | VideoToolbox H.264 | Media Foundation HW encoder, or pipe frames → bundled `ffmpeg` |
| Crash-resistant container | `AVAssetWriter` fMP4 (`.mpeg4AppleHLS`, 2s segments) | MF fragmented-MP4 sink writer, **or** feed `ffmpeg` a 2s-segment muxer |
| Embedded stream player | `WKWebView` | WebView2 (Edge/Chromium) |
| Download / remux | bundled `yt-dlp` + `ffmpeg` | bundled `yt-dlp.exe` + `ffmpeg.exe` (identical CLI) |
| Disaster recovery / disk guard | pure Swift logic | pure C# logic (port from spec) |

## 4. Known hard problems carried into the Windows port

These are documented in detail in [VERIFIED-BEHAVIOR.md](VERIFIED-BEHAVIOR.md);
the highlights that shape the Windows design:

1. **Audio timeline alignment.** The fMP4 fragment is corrupted if a video frame
   lands before the first audio sample. The session start **must** be anchored to
   the first audio sample (drop earlier video). This was the single root cause of
   both file corruption and dropped-audio on macOS. Windows must solve the same
   "first audio arrives mid-stream" problem.
2. **Renderer audio lives in a child process.** The browser decodes audio in a
   separate process (macOS: `com.apple.WebKit.GPU`; Windows: the WebView2
   renderer). Capturing the host app's PID gets **zero** stream audio — the
   capture must target the renderer/process tree.
3. **Hidden/offscreen players may decode no audio.** Background tabs get
   throttled. macOS v2 solved this by capturing a single visible-but-occludable
   window. Windows must verify audio actually decodes for its capture target.
4. **Minimized windows stop rendering on Windows.** `Windows.Graphics.Capture`
   captures occluded windows but a *minimized* window typically yields no frames
   — a real gap vs. macOS `desktopIndependentWindow`. Design the monitor window
   to never be truly minimized (use "shrink to background" / off-screen-but-mapped).
5. **PTS / VFR jumps inflate duration.** Capture frames can carry a forward-jumped
   timestamp, inflating container duration (up to ~50% on static content). Clamp
   PTS jumps and frame durations on both platforms.

## 5. OS-version policy (Windows)

- **Windows 11 / Win10 build 20348+**: full per-window audio isolation via
  process-loopback.
- **Older Windows 10**: process-loopback unavailable → **graceful degradation**
  to default-render loopback (system audio), surfaced clearly in the UI. The
  download track and single-window *video* capture still work everywhere
  (WGC since Win10 1903).
