# Parity Matrix (living tracker)

Feature-by-feature status across platforms. Update as the Windows port lands.
Legend: ✅ done & verified · 🟡 partial / unverified · 🚧 in progress · ⬜ not started · ➖ N/A

| Feature | macOS | Windows | Notes |
|---|:---:|:---:|---|
| **Track A — download (yt-dlp)** | ✅ | 🟡 | Pure logic ported & tested in `YtRec.Core`; subprocess runner + UI pending. |
| Probe + strategy ordering | ✅ | ✅ | C# port, 42 tests green (Phase 1). |
| 30 s polling / 6 h give-up | ✅ | ⬜ | needs the process runner. |
| VOD section download | ✅ | 🟡 | Timecode/section arg logic ported & tested; runner pending. |
| **Track B — single-window video** | ✅ | ⬜ | mac: SCK; win: Windows.Graphics.Capture. |
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
| Bilingual zh-TW + English | 🟡 | ⬜ | mac currently zh-TW only; add i18n both sides (ADR-0005). |
| True silent capture | ➖ | ➖ | deferred (virtual audio device) on both. |
| Distribution / installer | ✅ (DMG) | ⬜ | win: GitHub Releases, unsigned (ADR-0005). |
