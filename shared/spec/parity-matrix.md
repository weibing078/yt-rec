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
| **Track B — single-window video** | ✅ | 🟡 | mac: SCK. win: WGC **runtime-verified on real Win10** — real window captured to PNG (1938×1048, real content, not black). Encode pipeline (2c) next. |
| **Per-window audio isolation** | ✅ | ⬜ | mac: SCK single-window; win: WASAPI process-loopback. **Hardest item.** |
| Audio isolation bleed test passes | ✅ | ⬜ | tone-injection + spectral baseline. |
| fMP4 crash-resistant segments | ✅ | ⬜ | win: MF fragmented-MP4 or ffmpeg segmenter. |
| Session-gate (first-audio anchor) | ✅ | ⬜ | prevents fragment corruption. |
| Camera/viewfinder quality decoupling | ✅ | ⬜ | hidden full-res capture + small mirror. |
| Rewind recording | ✅ | ⬜ | player seek API; needs DVR window. |
| Monitor window (move/resize/pin/shrink) | ✅ | ⬜ | win: never truly minimize. |
| Disaster recovery on launch | ✅ | ⬜ | scan segments, reassemble, idempotent. |
| Disk guard (8/10/15 GB) | ✅ | ✅ | ported & tested (Phase 1). |
| Duration cap (3/6/12/∞) | ✅ | ⬜ | |
| Marathon detect (>4 h) | ✅ | ✅ | ported & tested (Phase 1). |
| Sleep prevention during record | ✅ | ⬜ | mac: beginActivity; win: SetThreadExecutionState. |
| Permission UX + self-check | ✅ | 🟡 | win consent model differs; no mic permission needed. |
| System notifications | ✅ | ⬜ | |
| Drag output → Premiere | ✅ | ⬜ | |
| Preview/confirm player | ✅ | ⬜ | mac: AVPlayerView; win: MediaPlayerElement. |
| Bilingual zh-TW + English | 🟡 | 🟡 | win: resw en-US + zh-Hant-TW, locale-based, PRI compiles on CI; runtime locale test pending. mac still zh-TW only. |
| True silent capture | ➖ | ➖ | deferred (virtual audio device) on both. |
| Distribution / installer | ✅ (DMG) | ⬜ | win: GitHub Releases, unsigned (ADR-0005). |
