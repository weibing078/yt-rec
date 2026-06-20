# ADR 0003 — Open-source under Apache-2.0 (+ brand reservation)

**Status:** Accepted (2026-06-20)

## Context
The project was previously proprietary ("All rights reserved", Resona Frame
CO., LTD.). It is being opened up for a free public release. A license had to be
chosen, and the choice was delegated to the engineering recommendation.

## Decision
License the source under **Apache-2.0**. Reserve the "YT Rec" name, logo, and
icon as trademarks (see [NOTICE](../../NOTICE)). Include an acceptable-use note
(record only content the user is entitled to).

## Rationale
- Permissive (encourages adoption/contribution) **with** an explicit patent
  grant — appropriate for a company-owned project.
- The app invokes `ffmpeg`/`yt-dlp` as **separate subprocesses** (never statically
  linked), so FFmpeg's (L)GPL does **not** infect our code — a permissive license
  is clean.
- Trademark reservation lets others fork/use the code while protecting the brand.

## Consequences
- `LICENSE` replaced with the canonical Apache-2.0 text; copyright + attribution
  moved to `NOTICE`.
- Bundled binaries are **not** committed; redistributors of a packaged build must
  comply with FFmpeg/yt-dlp licenses themselves (noted in `NOTICE`).
