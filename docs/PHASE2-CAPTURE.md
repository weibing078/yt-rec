# Phase 2 — Windows screen-capture engine (design)

The hard core: record one YouTube stream's **video + only its audio**, while the
user keeps working. Grounded in API research + adversarial verification (2026-06).
Mirrors the macOS Track B; see [VERIFIED-BEHAVIOR.md](VERIFIED-BEHAVIOR.md) for the
hard-won macOS lessons that carry over.

## 1. Capture pipeline

```
 WebView2 (off-screen, IsVisible=true, anti-occlusion flags)  ← plays the YouTube stream
        │  its own msedgewebview2.exe process tree
        ├── video ──►  Windows.Graphics.Capture (WGC, CreateForWindow on the WebView2 HWND)
        │                 → D3D11 BGRA frames (occluded-capture OK)
        └── audio ──►  WASAPI process-loopback (target = WebView2 browser PID, INCLUDE tree)
                          → PCM (only that tree's audio; Win11)
                                   │
                 session-gate (anchor to first audio sample) + QPC timestamps
                                   │
                 pipe BGRA + PCM ──► bundled ffmpeg ──► fragmented MP4 segments (.work/segments)
                                   │
                 on stop / disaster-recovery ──► ffmpeg -c copy ──► clean MP4 (Premiere-friendly)
```

The visible **monitor window** is a separate small mirror of the captured frames
(camera/viewfinder split, like macOS) — draggable, resizable, always-on-top,
"shrink to background". The captured WebView2 host is **never** what the user
interacts with.

## 2. Components (exact APIs, min build, C# access)

### 2a. Video — Windows.Graphics.Capture
- `IGraphicsCaptureItemInterop::CreateForWindow(hwnd)` → `GraphicsCaptureItem` →
  `Direct3D11CaptureFramePool.CreateFreeThreaded` → `GraphicsCaptureSession.StartCapture`;
  frames on `FrameArrived` (BGRA `IDirect3DSurface`, `SystemRelativeTime` = QPC).
- **Min build:** Win10 **1903** (CreateForWindow). `CreateFreeThreaded` = 2004.
- **C#:** WinRT projection (already targeting `net8.0-windows10.0.19041.0`) +
  `[ComImport]` `IGraphicsCaptureItemInterop` (IUnknown-based, works on .NET 8) +
  a D3D11 device wrapped as `IDirect3DDevice` (Vortice.Direct3D11/DXGI). Reference:
  `robmikh/Win32CaptureSample`, `microsoft/Windows.UI.Composition-Win32-Samples`.
- **Occluded = OK; minimized = NOT.** WGC captures a covered window's real content,
  but a *minimized* window stops rendering (frozen/1×1). → the capture window must
  never minimize (see §3).
- **Yellow border:** drawn around the captured window *on screen*, not in the
  frames. Since our capture target is off-screen, the border is invisible and not
  recorded — so the (Win11-only, MSIX-only) border-removal API is **not needed**.

### 2b. Audio — WASAPI process-loopback  ⚠️ Windows 11 only
- `ActivateAudioInterfaceAsync(VIRTUAL_AUDIO_DEVICE_PROCESS_LOOPBACK, IID_IAudioClient,
  AUDIOCLIENT_ACTIVATION_PARAMS{ PROCESS_LOOPBACK, TargetProcessId = <WebView2 browser PID>,
  INCLUDE_TARGET_PROCESS_TREE })` → completion handler → `IAudioClient.Initialize`
  (`LOOPBACK | EVENTCALLBACK | AUTOCONVERTPCM`) → `IAudioCaptureClient`.
- **Min build:** documented 20348 = **effectively Windows 11**. Consumer Win10
  (≤19045 / 22H2) does **not** have it (verified). → see §3 fallback.
- **Isolation is by PROCESS TREE, not PID.** Target the WebView2 **browser** PID
  (root); the audio-service child is included. Because WebView2 runs its own
  process tree (own user-data-folder), this excludes our app's own sounds and every
  other app — *provided no other audio-rendering process shares that tree*.
- **C#:** no managed wrapper (NAudio lacks it). Use **CsWin32** (`NativeMethods.txt`:
  `ActivateAudioInterfaceAsync`, `AUDIOCLIENT_ACTIVATION_PARAMS`, `PROCESS_LOOPBACK_MODE`,
  `IAudioClient`, `IAudioCaptureClient`, `IActivateAudioInterfaceCompletionHandler`,
  `VIRTUAL_AUDIO_DEVICE_PROCESS_LOOPBACK`) or hand-rolled COM, mirroring Microsoft's
  **ApplicationLoopback** sample. `GetMixFormat` returns `E_NOTIMPL` → **hardcode**
  the `WAVEFORMATEX` (48 kHz, 2 ch; float or 16-bit PCM). The completion handler must
  be an agile COM object kept rooted (GCHandle) until it fires.

### 2c. Recording — pipe to bundled ffmpeg (fragmented MP4)
- Spawn bundled `ffmpeg.exe`; feed BGRA via `-f rawvideo` on one named pipe and PCM
  via `-f s16le`/`-f f32le` on a second named pipe (stdin carries only one raw
  stream). Mux to fragmented MP4 with `-movflags +frag_keyframe+empty_moov+default_base_moof`
  → each flushed fragment is independently decodable (kill-9 loses only the last
  fragment). Hardware encode via `h264_qsv` / `h264_nvenc` / `h264_amf` when present,
  else `libx264`.
- **Why ffmpeg, not Media Foundation SinkWriter:** MF is heavyweight COM (BGRA→NV12
  conversion, IMFSample timestamping, vendor-MFT quirks); the ffmpeg path is plain
  BCL (`Process` + `NamedPipeServerStream`), reuses the binary we already bundle, and
  is the same fragmented-MP4 crash-resistance mechanism. (`MF_MPEG4SINK_MOOV_BEFORE_MDAT`
  is a known trap — not fragmentation; avoid.)
- **Finalize:** on stop / disaster-recovery, `ffmpeg -c copy` the fragmented file to a
  clean single-moov MP4 (Premiere-friendly), per OBS's "soft remux" practice. No
  `+faststart` for local files.

### 2d. The player — WebView2
- Audio **keeps decoding** when occluded/off-screen (the macOS killer-bug is absent on
  Windows) — confirmed; the only break is OS app *suspend*, which an **unpackaged Win32
  host never triggers** (we're unpackaged ✅).
- Video frames **stop** when occluded by default → create the environment with
  `AdditionalBrowserArguments = "--disable-features=CalculateNativeWinOcclusion
  --disable-backgrounding-occluded-windows --disable-renderer-backgrounding
  --disable-background-timer-throttling"`, and keep the host **`IsVisible=true`**,
  positioned off-screen/behind — **never** `IsVisible=false`, **never** minimized.
- Find the browser PID for loopback via `CoreWebView2Environment.BrowserProcessId` /
  `GetProcessInfos`. Inject the v1 JS (force play, report `ended`) as on macOS.
- WebView2 Runtime: preinstalled Win11 & most Win10; bundle fixed-version only for bare machines.

## 3. Hard constraints & how we handle them

| Constraint | Handling |
|---|---|
| Minimized window → no frames | Monitor "shrink to background" = move off-screen / behind, **not** minimize; keep `IsVisible=true`. |
| Occluded WebView2 → no video frames (default) | Chromium anti-occlusion flags (§2d). |
| **Per-window audio = Win11 only** | Win11: process-loopback (zero-bleed). **Win10: fall back to system/default-render loopback** (records all system audio) — surfaced in the UI as a clear "this PC can't isolate audio" notice (ADR-0004 updated). |
| Audio-timeline fragment corruption | Port the macOS **session-gate**: anchor session start to the first audio sample; drop earlier video. Single QPC clock for both streams. |
| Process-tree audio bleed | WebView2 gets its own user-data-folder/tree; our app plays no audio through it. Verify with the bleed test. |
| Kill-9 mid-record | Fragmented MP4 + on-launch segment scan → ffmpeg reassemble (port macOS disaster recovery). |

## 4. Runtime verification (MUST run on real Windows — CI can't)

1. **Audio isolation (core, Win11):** record while playing other-app audio (music +
   a notification + Premiere preview) → finished file has **only** the stream audio.
   Use the macOS 1 kHz tone-injection + spectral-baseline method.
2. **Occluded capture:** cover/move-off-screen the WebView2 → video frames keep
   flowing (flags work) and audio keeps decoding.
3. **Win10 fallback:** on Win10 22H2, confirm process-loopback fails cleanly and the
   system-audio fallback + UI notice engage.
4. **Kill-9 recovery:** TerminateProcess mid-record → relaunch reassembles a playable MP4.
5. **A/V sync** over a 30+ min recording; **disk/duration guards**; **drag to Premiere**.

## 5. Staged sub-plan

- **2a** — WGC video PoC: capture our own WebView2 window → mirror to monitor window; verify occluded capture. (CI compiles; you runtime-verify.)
- **2b** — Process-loopback audio (Win11) + Win10 system-audio fallback + capability detection.
- **2c** — ffmpeg fragmented-MP4 sink (2 pipes, session-gate, QPC) + finalize remux.
- **2d** — Monitor window (move/resize/always-on-top/shrink-to-background) + recording banner; rewind via player seek API.
- **2e** — Disaster recovery, disk/duration guards (reuse `YtRec.Core` logic), notifications, permissions/settings, i18n strings.

Each stage: I write it → CI compile-checks on Windows → you runtime-verify the items in §4.

## 6. Open decision
The per-window audio isolation being **Win11-only** is stricter than ADR-0004 assumed.
Confirm the target machine is Windows 11 for the full experience; Win10 users get
video capture + download + **system-audio** side-recording (not isolated). See
[adr/0004-windows-support-floor.md](adr/0004-windows-support-floor.md).
