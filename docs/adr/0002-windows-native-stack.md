# ADR 0002 — Native per platform; Windows = C# / .NET 8 + WinUI 3

**Status:** Accepted (2026-06-20)

## Context
The product's core value is native single-window video + per-window audio
capture. We considered a shared cross-platform UI framework (Electron, Flutter,
.NET MAUI) to share code between macOS and Windows.

## Decision
**No shared-UI framework.** Each platform is native:
- macOS stays Swift / SwiftUI (already shipping and real-hardware verified).
- Windows is **C# / .NET 8 + WinUI 3**.

Parity is maintained via the shared behavior spec + acceptance tests. The small
pure-logic core is implemented twice and unit-tested against the same cases.

## Rationale
- No cross-platform framework cleanly exposes `ScreenCaptureKit` /
  `Windows.Graphics.Capture` or per-process audio loopback; we'd write native
  plugins anyway.
- Rewriting the working, verified Swift app into Electron/Flutter is pure risk
  for zero capture-quality gain.
- C#/.NET reaches the needed WinRT APIs (WGC, WASAPI process-loopback via
  CsWin32/projections) and is the most contributor-friendly choice for an
  open-source Windows app.

## Consequences
- Two UI codebases to maintain — accepted; the spec keeps them honest.
- `YtRec.Core` (the .NET logic library) is plain cross-platform C#, so it builds
  and unit-tests on any OS (including the Mac used for development). The WinUI
  app and capture engine require a real Windows environment to build/test.
