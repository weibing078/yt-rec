#!/bin/bash
# Fetch yt-dlp + ffmpeg into mac/Vendor/bin (not committed to git).
# Idempotent: skips tools already present unless --force.
#   tools/setup-binaries.sh [--force]
set -euo pipefail
cd "$(dirname "$0")/.."          # repo root
BIN="mac/Vendor/bin"
mkdir -p "$BIN"

FORCE="${1:-}"
have() { [[ -x "$BIN/$1" && "$FORCE" != "--force" ]]; }

# yt-dlp — official universal2 standalone for macOS
if have yt-dlp; then
  echo "✓ yt-dlp present (use --force to refresh)"
else
  echo "▶ downloading yt-dlp (macos)"
  curl -fsSL "https://github.com/yt-dlp/yt-dlp/releases/latest/download/yt-dlp_macos" -o "$BIN/yt-dlp"
  chmod +x "$BIN/yt-dlp"
fi

# ffmpeg — static arm64 build (martin-riedl.de). If this URL ever changes, grab a
# static macOS ffmpeg manually and drop it at mac/Vendor/bin/ffmpeg.
if have ffmpeg; then
  echo "✓ ffmpeg present (use --force to refresh)"
else
  echo "▶ downloading ffmpeg (macos arm64 static)"
  TMP="$(mktemp -d)"
  if curl -fsSL "https://ffmpeg.martin-riedl.de/redirect/latest/macos/arm64/release/ffmpeg.zip" -o "$TMP/ffmpeg.zip"; then
    unzip -o -q "$TMP/ffmpeg.zip" -d "$TMP"
    mv "$TMP/ffmpeg" "$BIN/ffmpeg"
    chmod +x "$BIN/ffmpeg"
  else
    echo "✗ ffmpeg download failed — fetch a static macOS ffmpeg manually into $BIN/ffmpeg" >&2
    rm -rf "$TMP"; exit 1
  fi
  rm -rf "$TMP"
fi

echo "✅ binaries ready in $BIN:"
"$BIN/yt-dlp" --version 2>/dev/null | head -1 | sed 's/^/   yt-dlp /'
"$BIN/ffmpeg" -version 2>/dev/null | head -1 | sed 's/^/   /'
