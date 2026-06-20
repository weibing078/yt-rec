# ADR 0004 — Windows support: Win11 + Win10, with audio graceful degradation

**Status:** Accepted (2026-06-20)

## Context
The killer audio feature ("only that window's audio") relies on Windows
**process-loopback** audio capture, which is reliable on Windows 11 / Win10 build
20348+ and absent on older Windows 10. Both Win11 and Win10 must be supported.

## Decision
- **Single-window video** (Windows.Graphics.Capture, Win10 1903+) and the
  **download track** work on all supported versions.
- **Per-window audio isolation** is used where the OS supports process-loopback.
  On older Win10 it **degrades to system-audio (default-render) loopback**, clearly
  surfaced in the UI so the user knows other sounds may be captured.

## Correction (2026-06-20, after API verification — see PHASE2-CAPTURE.md)
The earlier "Win11 / 20348+" framing was too generous. WASAPI process-loopback
requires build **20348, which NO consumer Windows 10 release ever shipped**
(Windows 10 tops out at 19045 / 22H2; 20348 is the Server 2022 build). So:
- **Per-window audio isolation is effectively Windows 11 ONLY.**
- **All consumer Windows 10** (incl. 22H2) falls back to **system-audio** capture —
  i.e. the signature "won't record other apps' sound" does **not** hold on Win10.
- Video capture + download track still work on Win10 1903+.
The app detects this at runtime and shows the audio mode (isolated vs system).

## Consequences
- Feature parity with macOS is full on Win11; partial (audio isolation) on old
  Win10 — an accepted, documented trade.
- The UI must detect capability at runtime and label the active audio mode.

## Note on minimized windows
Unlike macOS `desktopIndependentWindow`, a **minimized** window on Windows
typically stops rendering (WGC yields no frames). The monitor window must never
truly minimize — use "shrink to background" / off-screen-but-mapped instead.
