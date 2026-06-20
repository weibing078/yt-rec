#!/bin/bash
# 啟動 YT Rec。
#   ./scripts/run.sh            一般啟動（開已打包好的 App）
#   ./scripts/run.sh --rebuild  改過程式碼後：重新打包再開
#
# 一律開「dist/YT Rec.app」（Developer ID 簽名版，螢幕錄製授權才黏得住）。
# 不要直接跑 .build/release 的裸檔——簽章身分不同，權限永遠對不上。
set -euo pipefail
cd "$(dirname "$0")/.."
APP="dist/YT Rec.app"

if [[ ! -d "$APP" || "${1:-}" == "--rebuild" ]]; then
    echo "▶ 打包中（自動採用 Developer ID 憑證）…"
    ./scripts/package-app.sh >/dev/null
fi

echo "▶ 啟動 YT Rec"
open "$APP"
