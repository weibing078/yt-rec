#!/bin/bash
# 從向量母檔產出 App 圖示與選單列 template。
# 需要 rsvg-convert（brew install librsvg）與 iconutil（macOS 內建）。
# 改了 shared/branding/*.svg 後重跑本腳本即可，產物會被 package-app.sh 拷進 .app。
set -euo pipefail
cd "$(dirname "$0")/.."

APP_SVG="shared/branding/YTRec-AppIcon-1024.svg"
MENU_SVG="shared/branding/YTRec-MenuBar.svg"
ICONSET="shared/branding/AppIcon.iconset"

command -v rsvg-convert >/dev/null || { echo "缺 rsvg-convert：brew install librsvg"; exit 1; }

echo "▶ 產出 App 圖示 iconset（直接從 SVG 各尺寸渲染，最銳利）"
rm -rf "$ICONSET"; mkdir -p "$ICONSET"
render() { rsvg-convert -w "$1" -h "$1" "$APP_SVG" -o "$ICONSET/$2"; }
render 16   icon_16x16.png
render 32   icon_16x16@2x.png
render 32   icon_32x32.png
render 64   icon_32x32@2x.png
render 128  icon_128x128.png
render 256  icon_128x128@2x.png
render 256  icon_256x256.png
render 512  icon_256x256@2x.png
render 512  icon_512x512.png
render 1024 icon_512x512@2x.png

echo "▶ 打包 AppIcon.icns"
iconutil -c icns "$ICONSET" -o shared/branding/AppIcon.icns

echo "▶ 產出選單列 template（PDF 向量，任何螢幕都銳利 + 參考用 PNG）"
rsvg-convert -f pdf -w 36 -h 36 "$MENU_SVG" -o shared/branding/MenuBarIcon.pdf
rsvg-convert -w 18 -h 18 "$MENU_SVG" -o shared/branding/MenuBarIcon.png
rsvg-convert -w 36 -h 36 "$MENU_SVG" -o shared/branding/MenuBarIcon@2x.png

echo "▶ 產出文件／App Store／README 用 PNG（透明底）"
mkdir -p shared/branding/png
for s in 1024 512 256 128; do
  rsvg-convert -w "$s" -h "$s" "$APP_SVG" -o "shared/branding/png/YTRec-${s}.png"
done

echo "✅ 完成：shared/branding/AppIcon.icns、shared/branding/MenuBarIcon.pdf、shared/branding/png/*"
ls -la shared/branding/AppIcon.icns shared/branding/MenuBarIcon.pdf shared/branding/png/
