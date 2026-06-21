# Parity Matrix (living tracker)

Feature-by-feature status across platforms. Update as the Windows port lands.
Legend: ✅ done & verified · 🟡 partial / unverified · 🚧 in progress · ⬜ not started · ➖ N/A

| Feature | macOS | Windows | Notes |
|---|:---:|:---:|---|
| **Track A — download (yt-dlp)** | ✅ | ✅ | **Runtime-verified on real Windows** (629 KB MP4 downloaded via YtRec.Cli over SSH); 62 tests green on Mac + Windows. |
| Headless runner `YtRec.Cli` | ✅ | ✅ | cross-platform; real download verified on Mac + Windows. |
| Main window (control center) | ✅ | 🟡 | WinUI 3 MainWindow **compiles on Windows CI** (windows-build); GUI/runtime test pending. |
| Probe + strategy ordering | ✅ | ✅ | C# port; ProcessRunner + YtDlpEngine. |
| 30 s polling / 6 h give-up | ✅ | ✅ | polling implemented (configurable); stops on cancel/terminal/marathon. |
| VOD section download | ✅ | ✅ | `YtDlpEngine.DownloadSectionAsync` (--download-sections), tested. |
| **Track B — single-window video** | ✅ | ✅ | win: **full A/V verified end-to-end on real Win11** — App records a real YouTube video (WebView2) → 1280×720/30fps H.264 + isolated AAC audio MP4, frame-verified (actual video playing), decode 0 errors. Win32-hosted WebView2 + WGC monitor-capture-crop + `RecordingSession`. |
| Video encode → MP4 | ✅ | ✅ | win: **real-time raw-BGRA-pipe verified on real Win11** — `--record` 10 s live window → H.264/yuv420p 1114×628 MP4, duration 10.00 s, full-decode 0 errors. Found+fixed: odd-dim crop + CFR pacing. |
| **Per-window audio isolation** | ✅ | ✅ | win: WASAPI process-loopback **verified on real Win11** (`AudioLoopbackCapture`). Captured exactly the target PID's audio; the hand-rolled `ActivateAudioInterfaceAsync` + completion-handler path works. |
| Audio isolation bleed test passes | ✅ | ✅ | **Win11 verified (mac §1 method):** target 440 Hz captured at −24.1 dB while another process's 1000 Hz (playing at −24.1 dB per system-loopback control) was suppressed to −57.4 dB → **33 dB isolation**, at the noise floor. |
| fMP4 crash-resistant segments | ✅ | ✅ | win: ffmpeg HLS fMP4 segmenter (`seg_init.mp4` + `seg_%05d.m4s`) → reassemble + mux, verified producing a clean MP4 on Win11. Kill-9 recovery scan: logic+tests, runtime pending. |
| Session-gate (first-audio anchor) | ✅ | ✅ | `SessionGate` + tests; in the verified A/V recording, video anchors to the first audio sample (gate on "audio started", since the audio QPC ≠ WGC SystemRelativeTime epoch). |
| Camera/viewfinder quality decoupling | ✅ | 🟡 | off-screen full-res `PlayerWindow` (camera) + throttled mirror in `MonitorWindow` (viewfinder). CI-compile + runtime pending. |
| Rewind recording | ✅ | 🟡 | engine hook only: `PlayerWindow.SeekAsync` / `ProgressStateAsync` via player API. Rewind UI not built yet. |
| Monitor window (move/resize/pin/shrink) | ✅ | 🟡 | `MonitorWindow` built (always-on-top, drag, shrink-to-background, live preview, REC/elapsed/stop). CI-compile + runtime pending. |
| Disaster recovery on launch | ✅ | 🟡 | `RecoveryPlan` + `SegmentReassembler` (logic + L2 tests green); wired to launch in `MainWindow`. Runtime verify pending. |
| Disk guard (8/10/15 GB) | ✅ | ✅ | ported & tested (Phase 1); now polled during recording by the guard timer. |
| Duration cap (3/6/12/∞) | ✅ | 🟡 | `DurationCap` + tests (green); settings UI + guard timer auto-finalize. CI-compile + runtime pending. |
| Marathon detect (>4 h) | ✅ | ✅ | ported & tested (Phase 1). |
| Sleep prevention during record | ✅ | 🟡 | `SleepPrevention` (SetThreadExecutionState) held for the session. Runtime verify pending. |
| Permission UX + self-check | ✅ | 🟡 | WGC consent model; no mic permission needed. Notice text updated. |
| System notifications | ✅ | 🟡 | in-app status only (auto-finalize / recovery messages). Native toast deferred (unpackaged app needs COM-activator registration). |
| Drag output → Premiere | ✅ | 🟡 | `DragItemsStarting` → deferred StorageItems provider. CI-compile + runtime pending. |
| Preview/confirm player | ✅ | ⬜ | mac: AVPlayerView confirm-before-record. Not built on win (the off-screen player is the capture source, not a confirm step). |
| Bilingual zh-TW + English | 🟡 | 🟡 | win: resw en-US + zh-Hant-TW; new Track-B strings added. Secondary windows/dialogs are inline bilingual. Runtime locale test pending. |
| True silent capture | ➖ | ➖ | deferred (virtual audio device) on both. |
| Distribution / installer | ✅ (DMG) | ⬜ | win: GitHub Releases, unsigned (ADR-0005). |
