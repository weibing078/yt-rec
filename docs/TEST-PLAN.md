# Test Plan (cross-platform)

The single source of truth for "working". Both the macOS and Windows apps must
pass this matrix. Detailed engineering rationale lives in
[VERIFIED-BEHAVIOR.md](VERIFIED-BEHAVIOR.md); the canonical logic rules are in
[`shared/spec/`](../shared/spec/).

## Test pyramid

| Layer | What | Where it runs |
|---|---|---|
| **L1** Pure logic | URL parse, strategy order, marathon/disk thresholds, session-gate, retime, parsing | CI, no deps (ms). Implemented twice (Swift + C#), same cases. |
| **L2** Component / filesystem | segment store, newest-file pick, recovery idempotency, history | CI, temp dir (s) |
| **L3** Real-binary integration | real `ffmpeg`/`yt-dlp` remux/probe/poll (stubbable via bin-dir env) | gated (network: opt-in flag) |
| **L4** Real-hardware acceptance | the T-matrix below | manual runbook, signed/real build |

macOS baseline: **163 automated tests green** (L1–L3). Windows must build an
equivalent L1–L3 suite around `YtRec.Core`.

## L4 acceptance matrix

| # | Scenario | Expected |
|---|---|---|
| **T1** | Live in progress · side-record | Monitor window shows the stream **with audio** within ~2 s; recording indicator on; window draggable / resizable / pin-on-top. |
| **T2** | **No other-app audio (CORE)** | While recording, play Premiere preview / music / a notification sound → finished file contains **only** the stream's audio, zero bleed (verify via tone-injection + spectral baseline). |
| **T3** | **No other-window video** | Stack other windows over the monitor window → finished video shows **only** the stream, not the overlay. |
| **T4** | Work while recording | Operate other apps normally during capture; no interference, no stutter. |
| **T5** | Confirm content | Monitor window shows live frames; user can tell it's the right stream and actually playing. |
| **T6** | Very long stream | Hits duration cap → auto-finalize + save + notify; the 3-stage disk guard fires correctly. |
| **T7** | Rewind recording | From monitoring, rewind to a past point → "record from here" → output is the **past** footage, 1080p h264 + 48 kHz aac. |
| **T8** | Quality decoupling | Shrink the monitor window to minimum while recording 1080p → output is still 1080p, no black bars. |
| **T9** | Ended video (VOD) | Detected as ended → download track grabs it (or `--download-sections` for a range); output plays. |
| **T10** | Disaster recovery | Kill the app mid-record → relaunch auto-reassembles a valid MP4 + notifies. |
| **T11** | Drag into Premiere | An output row drags into a Premiere project panel and imports/scrubs cleanly. |
| **T12** | Permission not granted | Clear warning + deep link to OS settings; **no crash**; download track may still run. |

## Corruption-check standard (every recorded output)

- Full decode (`ffmpeg -f null`, no seek) with ~**0** NAL/decode errors.
- Native framework (AVFoundation / Media Foundation) reads **full length** with
  the **correct track count** (video + audio).
- Opens and scrubs cleanly in QuickTime/Windows player **and** Premiere.
- Do **not** judge integrity by OS metadata indexing (unreliable on temp files).

## Key L1 cases (must exist in both Swift & C#)

- **URL**: all YouTube forms (`watch` / `youtu.be` / `/live` / `shorts` / `embed`
  / `v`) → same 11-char ID; 10 or 12 chars → reject; `-`/`_` legal; whitespace
  trimmed; `&t=`/`?si=` ignored; non-YouTube & playlist-only → friendly reject.
- **Disk guard**: `<8 GB` refuse, `8–15 GB` warn, `>15 GB` ok; lookup failure →
  `Int64.max` (don't block); during-record `<10 GB` → auto-finalize.
- **Marathon**: exactly 4 h → true; 3 h 59 m → false; ended → false; no
  timestamp → false.
- **Download decision**: default-off → side-record only; auto →
  `is_live`/`is_upcoming`/probe-fail → don't download, `post_live`/`was_live`/
  `not_live` → download; always-try + `>4 h` open → marathon intercept.
- **Session gate**: first audio sample sets session start; video before it is
  dropped; `retime` → PTS from 0, never negative.
- **Start-job guard precedence**: empty → ignore; **active-job check takes
  priority over** non-YouTube check; valid → start.
- **Stop notifications**: durationLimit / lowDisk / playerEnded / userStopped →
  distinct messages; nativeSucceeded → none; empty result → "no output".
