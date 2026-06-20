# ADR 0005 — Distribution (GitHub Releases, unsigned) & bilingual UI

**Status:** Accepted (2026-06-20)

## Context
The Windows build goes to a non-technical teammate and, later, public
open-source users. Code signing removes the SmartScreen warning but costs money.
The current UI is Traditional Chinese only; open-source users need English.

## Decision
- **Distribution:** GitHub Releases, **unsigned**, with an illustrated install
  guide that walks a non-technical user through the SmartScreen "More info → Run
  anyway" step. Leave a documented hook to add cheap signing (e.g. Azure Trusted
  Signing) later without rework.
- **i18n:** ship **bilingual zh-TW + English** from day one (proper resource-based
  localization), default to the OS language.

## Consequences
- No recurring signing cost now; first-run friction mitigated by the guide.
- All user-facing strings live in resource files (both platforms) — no hardcoded
  UI text.
- Distribution packaging is a Phase 3 task (after the capture engine works).
