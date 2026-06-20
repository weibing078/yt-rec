# ADR 0004 — Windows support: Win11 + Win10, with audio graceful degradation

**Status:** Accepted (2026-06-20)

## Context
The killer audio feature ("only that window's audio") relies on Windows
**process-loopback** audio capture, which is reliable on Windows 11 / Win10 build
20348+ and absent on older Windows 10. Both Win11 and Win10 must be supported.

## Decision
- **Single-window video** (Windows.Graphics.Capture, Win10 1903+) and the
  **download track** work on all supported versions.
- **Per-window audio isolation** is used where the OS supports process-loopback
  (Win11 / 20348+). On older Win10 it **degrades to system-audio (default-render)
  loopback**, clearly surfaced in the UI so the user knows other sounds may be
  captured.

## Consequences
- Feature parity with macOS is full on Win11; partial (audio isolation) on old
  Win10 — an accepted, documented trade.
- The UI must detect capability at runtime and label the active audio mode.

## Note on minimized windows
Unlike macOS `desktopIndependentWindow`, a **minimized** window on Windows
typically stops rendering (WGC yields no frames). The monitor window must never
truly minimize — use "shrink to background" / off-screen-but-mapped instead.
