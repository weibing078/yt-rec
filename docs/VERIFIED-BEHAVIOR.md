# Verified Behavior & Hard-Won Lessons

Durable knowledge salvaged from the v1/v2 macOS development sessions (HANDOFF
logs, test results, test plans — since removed). This is the institutional
memory that should guide the Windows port and any future macOS work. Items are
marked **[VERIFIED]** when proven on real hardware, **[PITFALL]** for traps,
and **[PARITY]** for things the Windows port must replicate.

---

## 1. Audio isolation — the core lifeline

- **[VERIFIED]** On macOS, a single-window ScreenCaptureKit filter
  (`desktopIndependentWindow` + `capturesAudio` + `excludesCurrentProcessAudio=false`)
  records **only the owning window's app audio**. Tested by injecting other-app
  sound during recording:
  - A 15 s 1 kHz tone (afplay) during capture left the file **bit-identical** to
    the no-injection baseline (1 kHz band at −42.0 dB; a manually mixed control
    raised it to −35.5 dB, proving a real leak *would* show).
  - Three simultaneous independent sources (14k/16k/18k Hz) left only
    −61.6 / −65.4 / −70.2 dB residual (noise floor, 30–40 dB below the leakage
    reference) while the intended audio recorded normally at ~−24 dB.
- **[VERIFIED]** The user's *own* separate browser (Safari/Chrome) watching the
  same stream is a different app and is **not** recorded — single-window capture
  excludes it by construction.
- **[PARITY]** Replicate with WASAPI process-loopback on Windows and re-run the
  **same multi-source bleed test** (inject YouTube Music + a notification sound,
  confirm the file has only the stream audio).

## 2. The fMP4 audio-timeline root cause (most important lesson)

- **[PITFALL]** Side-recording corruption *and* dropped audio were the **same**
  root cause: the **audio track's timeline**, not the assembly/mux method.
  YouTube starts playback late (~6–7 s); if the first audio sample lands mid-stream
  it **poisons that fMP4 fragment**, and *every* reader (ffmpeg AND AVFoundation)
  then truncates the file and drops the audio track.
- Controlled A/B proof: with audio disabled, both concat methods produced full
  15.89 s files, 0 decode errors. With audio enabled, the file broke at 8 s.
  Swapping ffmpeg flags or m3u8-vs-concat never helped — the poison is upstream.
- **[VERIFIED] Fix:** anchor the writer session start to the **first audio
  sample**; drop any video frame that arrives before it. After the fix: decode
  errors 3,625 → 0; readable length 8 s → full; both tracks present.
  Trade-off: the recording loses the opening ~7 s of buffering (Track A covers
  the true start). This is enforced by the `SessionGate` / `retime` logic.
- **[PARITY]** Any Windows port must solve the identical "first audio sample
  arrives mid-stream poisons the fragment" problem. Mirror the session-gate rule.

## 3. Renderer audio is in a child process

- **[VERIFIED]** The embedded browser decodes audio in a **separate OS process**
  (macOS: `com.apple.WebKit.GPU`, PID changes each run, PPID=1). A tap on the
  app's own PID captures the app's own audio engine but **0 samples** from the
  player.
- **[PITFALL]** The private API `responsibility_get_responsible_for_pid` (to
  attribute the child to its owner) **SIGSEGVs under Developer ID + hardened
  runtime** though it works unsigned. It was judged unshippable and reverted.
  Lesson: do not ship a private-API audio route on a reliability-critical tool.
- **[PARITY]** WebView2's renderer is likewise a child process. "Silently capture
  this stream's audio" must target the renderer/process tree (process-loopback),
  not the host PID.

## 4. Hidden vs. visible player (audio decode)

- **[PITFALL]** A fully **hidden/offscreen** WebView decoded **zero** audio
  (`webkitAudioDecodedByteCount = 0`) — YouTube throttles background/offscreen
  audio decoding. This nearly killed the whole feature.
- **[VERIFIED]** A **visible (but occludable)** window *does* decode audio
  (measured mean −22.7 dB / peak −1.2 dB). v2's design — a real on-screen monitor
  window that other windows can cover — is what makes audio capture work.
- **[PARITY]** Verify audio actually decodes for the chosen Windows capture
  target. Keep the player technically foreground/mapped (not minimized).

## 5. Capture / quality decoupling ("camera vs. viewfinder")

- **[VERIFIED]** Output quality is set by a hidden full-size capture window
  ("camera"); the small visible monitor ("viewfinder") is just a mirror. A ~270 px
  preview still produced **1920×1080** output (measured).
- **[VERIFIED]** Recording becomes visible within ~2 s of start; output target is
  1080p H.264 + 48 kHz AAC stereo.
- **[PARITY]** On Windows, capture a hidden full-resolution render surface
  decoupled from the visible preview, so shrinking the preview never degrades output.

## 6. Rewind recording (live "start from a past point")

- **[VERIFIED]** For continuous lives you can't download a past section. Instead:
  start *monitoring* (capture running, **not yet writing**) → rewind to a position
  → *begin recording from there*. Proven on a parliament stream: rewound to 09:57,
  began recording, output was the **past** footage (1080p h264 + 48 kHz aac).
- **[PITFALL]** For live streams, raw `video.currentTime` is unreliable for
  seeking — use the player API (`movie_player.getProgressState` / `seekTo`).
- **[PITFALL]** Rewind is real-time 1× (re-recording N rewound minutes takes N
  minutes). Rewind range = the channel's DVR window (~12–14 h for parliament;
  depends on the player's DVR, not the 30 s HLS manifest). If DVR is off, no rewind.
- **[PARITY]** Decouple capture (preview) from writing (`beginWriting`). Use the
  player's own time/seek API.

## 7. Crash resistance & disaster recovery

- **[VERIFIED]** fMP4 2-second segments (`seg_init` + `seg_%05d`) into
  `.work/segments/`; already-flushed segments are always valid.
- On launch, scan each task's `.work/segments/`; if an init + ≥2 segments exist
  with no final output → reassemble in the background + notify. Must:
  - **skip the currently-recording job dir** (never delete in-use segments),
  - be **idempotent** (don't re-assemble/re-notify an already-recovered folder),
  - abandon if `seg_init` is missing; skip if only 1 segment.
- **[PITFALL]** Naive binary concat of init+segments can produce thousands of NAL
  errors *if the capture-side audio timeline is wrong* (§2). The real fix is §2,
  then concat + `ffmpeg -c copy` (single-moov, Premiere-friendly). Do **not** add
  `+faststart` for local files (avoids a full rewrite).
- **[PITFALL]** Disaster recovery must run **off the main thread** — synchronous
  8 MB-chunk concat on the UI thread froze the launch spinner.
- **[PITFALL]** Zero-padded `seg_%05d` sorts lexicographically; past ~99,999
  segments (~55 h) order breaks. Safe within the 6 h cap; use numeric sort if
  unbounded.
- **[PARITY]** Directly portable (same ffmpeg approach + same edge cases).

## 8. Numeric constants (carry verbatim to Windows)

- **Disk guard:** pre-start free space `< 8 GB` → refuse side-record;
  `8–15 GB` → warn (confirm to continue); `> 15 GB` → ok. During recording, check
  **every 60 s**; `< 10 GB` → auto-finalize + save + notify.
  Invariant: `stopGB(10) > refuseGB(8)`. `freeBytes` lookup failure → treat as
  `Int64.max` (don't falsely block network/external drives).
- **Duration cap:** options 3 h / **6 h (default)** / 12 h / unlimited →
  auto-finalize + save + notify at the cap.
- **Marathon detect:** already-running-and-still-live `> 4 h` → side-record only
  (don't start a "from-start" download of days of content). Boundary: exactly 4 h
  → marathon; 3 h 59 m → not.
- **Health check:** ~8 s (ticks ≥ 4) with 0 complete frames → warn (window
  minimized?); ~12 s (ticks ≥ 6) with frames but no audio → `audioDisabled`
  video-only survival mode.
- **Audio-decision window:** start ~4 s after the player "playing" event, 16 s
  hard cap; select as soon as the first audio sample arrives.

## 9. yt-dlp / Track A (cross-platform, reuse verbatim)

- **[VERIFIED]** Working download invocation:
  `yt-dlp --newline --no-colors --no-playlist --ignore-config --retries 10
  --fragment-retries 60 -N 4 --merge-output-format mp4 --ffmpeg-location <bin>
  -S "res:1080,vcodec:h264,acodec:m4a" -o "<dir>/%(title).70B [%(id)s].%(ext)s" <URL>`
  (add `--live-from-start` for live). The `h264 + m4a` sort makes the file drop
  straight into Premiere with no transcode.
- **[PITFALL]** `--newline` is required for progress parsing. Cancel with **SIGINT**
  (lets yt-dlp finalize the `.part`). **Don't** use `--print` to locate output (it
  forces quiet+simulate) — instead scan the folder for the newest video file.
  Probe with `--print "%(id)s\t%(title)s\t%(live_status)s\t%(release_timestamp)s"
  --skip-download`.
- **[PITFALL]** VOD `--download-sections` works **only on ended videos**; an
  ongoing live returns "cannot be partially downloaded" (DASH and HLS).
- **[PITFALL]** Strategy order keyed on `live_status`:
  `is_live`/`is_upcoming` → `[--live-from-start, normal, 720p-degrade]`;
  `post_live`/`was_live`/`not_live` → `[normal, --live-from-start, 720p-degrade]`.
  Whole round fails → poll every 30 s; give up after 6 h.
- **[PITFALL]** Permanent failures (stop polling): private / unavailable / removed
  / members-only / age-restricted ("sign in to confirm your age") / account
  terminated / geo-blocked. Everything else (404/5xx, "live event has ended",
  network) is retryable. Any terminal failure must surface a clear localized
  reason, not the generic "download failed".
- **[PITFALL]** yt-dlp needs a JS runtime (prefers `deno`) for some YouTube
  parsing; a packaged app's PATH may not see system `node`/`deno`. The
  `android_vr` client currently yields HD (1080p60 H.264) without JS, but that
  fallback can close. Plan to bundle a JS runtime and keep yt-dlp updatable.

## 10. Verification methodology (reuse on Windows)

- **[PITFALL]** Judge file integrity by **full-decode error count** (`ffmpeg
  -f null`, no seek) + native-framework full read (AVFoundation / Media
  Foundation) and correct **track count**. Do **not** trust OS metadata indexing
  (macOS `mdls`/Spotlight reports null on un-indexed temp files — a red herring).
- Functional permission self-check: play a known test tone and confirm the
  capture path receives the **exact amplitude** (e.g. peak 0.300), rather than
  only reading the OS permission flag.
- Test pyramid used: **L1** pure logic (CI) · **L2** filesystem/temp (CI) · **L3**
  real ffmpeg/yt-dlp (gated) · **L4** real-hardware manual runbook. Pattern:
  extract orchestration decisions into pure "enum-in → enum-out" functions so the
  hard logic gets L1 coverage with no device. macOS reached **163 green tests**.

## 10b. Windows capture — verified on real Win11 (build 26200)

- **[VERIFIED]** The real-time recorder (`ContinuousRecorder`: WGC frame → `IDirect3DDxgiInterfaceAccess`
  → ID3D11 staging-texture readback → BGRA piped to ffmpeg `-f rawvideo` → MP4) works end to end on
  real hardware. A 10 s capture of a live window → H.264/yuv420p MP4, full-decode 0 errors.
- **[PITFALL]** A captured window can be **odd-sized** (seen: 1115×628). `libx264`/yuv420p require even
  dimensions → "width not divisible by 2" and the encode aborts (0-byte file). **Fix:** ffmpeg
  `-vf crop=trunc(iw/2)*2:trunc(ih/2)*2` (drops ≤1 px). Applied to every encode path.
- **[PITFALL]** WGC delivers frames at the window's **variable** render rate (on-change), but rawvideo is
  fed to ffmpeg at a fixed `-framerate`. Writing every WGC frame makes the file play at the wrong speed
  (seen: 356 frames over 10 s muxed as 15 fps → a 23.7 s slow-motion file). **Fix:** real-time **CFR
  pacing** — emit the latest frame as many times as wall-clock says are due (duplicate when idle, drop
  when faster than fps). After the fix a 10 s capture is exactly 10.00 s / 150 frames @ 15 fps.
- **[PARITY]** Both fixes live in `ContinuousRecorder` + `RecordingSession`; any new encode path must keep
  them. (The live A/V `RecordingSession` adds the §2 session-gate + WASAPI audio on top.)

- **[VERIFIED]** WASAPI loopback (`AudioLoopbackCapture`, hand-rolled COM) works on real Win11: system
  loopback and **per-process loopback** (`ActivateAudioInterfaceAsync` + completion handler, targeting a
  PID's tree) both capture 48 kHz/2ch/16-bit PCM. The completion-handler-kept-rooted pattern is correct.
- **[VERIFIED] Per-process audio isolation (the killer feature)** — mac §1 method on Win11: target process
  played 440 Hz, another process played 1000 Hz (system-loopback control confirmed BOTH at −24.1 dB).
  Process-loopback on the target's PID captured 440 Hz at −24.1 dB but the other process's 1000 Hz at only
  **−57.4 dB → 33 dB of isolation** (noise floor). "Record only this stream's audio" holds on Windows.
- **[PARITY]** Isolation is by **process tree**: target the WebView2 *browser* PID (root); give the player
  its own user-data-folder so no other audio-rendering process shares the tree.

## 10c. Integrated A/V on Win11 — what works + the WebView2 capture wall

- **[VERIFIED]** The full live A/V path runs end to end on real Win11 (App `--autorecord`): WebView2 plays a
  YouTube URL, process-loopback captures its (isolated) audio (mean −18.9 dB — real), and the recorder muxes
  a **valid, decode-clean MP4** (h264 1280×720 30 fps + AAC 48 kHz, duration matches real time). The
  session-gate, CFR pacing, HLS-fMP4 segments, finalize-mux, and disaster recovery all work.
- **[PITFALL]** Every ffmpeg `Process` MUST set `CreateNoWindow = true`. Otherwise it pops a console window
  (Windows Terminal on Win11) that lands over the captured area and gets recorded.
- **[PITFALL]** ffmpeg's HLS muxer writes `-hls_fmp4_init_filename` relative to its **CWD**, not the playlist
  dir → set `WorkingDirectory` to the segments dir and use bare output names, else "Permission denied".
- **[PITFALL]** The audio QPC (IAudioCaptureClient) and WGC `SystemRelativeTime` do **not** share an epoch —
  don't gate video by comparing them (drops every frame). Gate on "first audio sample has arrived" (boolean).
- **[PITFALL]** WASAPI COM objects (IAudioClient) are **not** apartment-agile: acquire+init+run them on a
  dedicated **MTA** thread. Acquiring on the WinUI UI thread (STA) → E_NOINTERFACE on first call.
- **[VERIFIED — RESOLVED] Full Mac parity on Win11.** The WinUI 3 `WebView2` control is visual-hosted
  (composition), so its rendered video never reaches a WGC-capturable surface (window-capture = 1 static
  frame; monitor-capture of its rect = blank/white, independent of every GPU/overlay flag tried). **Fix that
  works:** host the WebView2 in a plain **Win32 window** via `CoreWebView2Environment.CreateCoreWebView2ControllerAsync(
  CoreWebView2ControllerWindowReference.CreateFromWindowHandle(hwnd))` (windowed, not visual). That HWND
  renders normally; the recorder monitor-captures + crops to its rect. End-to-end result on real Win11:
  a 1280×720/30fps H.264 + AAC MP4 of the **actual YouTube video** (frame verified — Rick Astley playing)
  with the **isolated** stream audio (−19 dB), duration matches real time, full-decode 0 errors.
  - **[PITFALL]** The WinAppSDK WebView2 projection differs from the classic .NET SDK: controller creation
    takes a `CoreWebView2ControllerWindowReference` (not `IntPtr`) and `Controller.Bounds` is
    `Windows.Foundation.Rect` (not `System.Drawing.Rectangle`).

## 11. macOS-specific facts (context, not for Windows)

- Permission is the merged **"Screen & System Audio Recording"** (macOS 14+);
  SCK audio needs **only** this one permission — the app is *not* in the
  Microphone list. v2 removed the separate process-tap path and
  `NSAudioCaptureUsageDescription`.
- Ad-hoc signing breaks TCC persistence; a **Developer ID** signature is required
  for screen-recording grants to stick across restarts/repackaging.
- yt-dlp is PyInstaller-packaged → under hardened runtime needs
  `com.apple.security.cs.allow-unsigned-executable-memory` +
  `disable-library-validation`, or it's killed on launch.
- macOS 15+ shows a monthly "continue allowing" screen-recording prompt (no
  workaround — document it).
- App is **not sandboxed** (must run external binaries). Bundle id
  `tw.weibing.ytrec`, display name "YT Rec".

## 12. Deferred / known gaps (both platforms)

- **True silent capture** (record the stream but speakers stay silent) is **not**
  implemented — it needs an embedded virtual audio device (BlackHole on macOS;
  VB-CABLE-style on Windows). v2 ships **audible** side-recording; the owner
  accepted hearing the stream as long as no *other* app audio leaks in.
- In-app clip trimming was **removed** (owner decision): the app is a pure
  recorder; editing happens in Premiere. "You get exactly what you recorded."
- Live ads are recorded as-is (edit out later).
- Stream-end detection: relying on the player `ended` event is unreliable
  (YouTube may switch player state instead of firing `video.ended`); may need
  player-state polling.
