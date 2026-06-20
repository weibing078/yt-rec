# ADR 0001 — Cross-platform monorepo, both platforms maintained & synced

**Status:** Accepted (2026-06-20)

## Context
YT Rec ships on macOS (v1.0). A Windows build is needed for a Windows teammate
now, and likely a free open-source release later. The macOS app will keep
evolving alongside Windows — neither is frozen.

## Decision
Keep both platforms in **one repository**, side by side:
`mac/`, `windows/`, with shared `docs/`, `shared/spec/`, `shared/branding/`,
`tools/`. Feature parity is tracked by the shared behavior spec and
[TEST-PLAN.md](../TEST-PLAN.md), not by sharing application code.

## Consequences
- One place for product decisions, brand, and parity criteria.
- Each platform's build stays self-contained under its own folder.
- Contributors can work on one platform without the other's toolchain.
- The macOS app was relocated into `mac/`; `swift build` + 163 tests re-verified
  green after the move.
