# Fetch yt-dlp.exe + ffmpeg.exe into windows\vendor\bin (not committed to git).
# Idempotent: skips tools already present unless -Force.
#   tools\setup-binaries.ps1 [-Force]
param([switch]$Force)
$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $PSScriptRoot
$bin  = Join-Path $root "windows\vendor\bin"
New-Item -ItemType Directory -Force -Path $bin | Out-Null

function Have($name) { (Test-Path (Join-Path $bin $name)) -and (-not $Force) }

# yt-dlp — official Windows standalone
if (Have "yt-dlp.exe") {
  Write-Host "OK  yt-dlp.exe present (use -Force to refresh)"
} else {
  Write-Host ">> downloading yt-dlp.exe"
  Invoke-WebRequest -Uri "https://github.com/yt-dlp/yt-dlp/releases/latest/download/yt-dlp.exe" `
    -OutFile (Join-Path $bin "yt-dlp.exe")
}

# ffmpeg — BtbN static win64 build. If the asset name changes, grab a static
# ffmpeg.exe manually and drop it at windows\vendor\bin\ffmpeg.exe.
if (Have "ffmpeg.exe") {
  Write-Host "OK  ffmpeg.exe present (use -Force to refresh)"
} else {
  Write-Host ">> downloading ffmpeg (win64 static)"
  $tmp = New-Item -ItemType Directory -Force -Path (Join-Path $env:TEMP ("ytrec-ff-" + [guid]::NewGuid()))
  $zip = Join-Path $tmp "ffmpeg.zip"
  Invoke-WebRequest -Uri "https://github.com/BtbN/FFmpeg-Builds/releases/download/latest/ffmpeg-master-latest-win64-gpl.zip" -OutFile $zip
  Expand-Archive -Path $zip -DestinationPath $tmp -Force
  $exe = Get-ChildItem -Path $tmp -Recurse -Filter "ffmpeg.exe" | Select-Object -First 1
  Copy-Item $exe.FullName (Join-Path $bin "ffmpeg.exe") -Force
  Remove-Item $tmp -Recurse -Force
}

Write-Host "Done. Binaries in $bin"
& (Join-Path $bin "yt-dlp.exe") --version
& (Join-Path $bin "ffmpeg.exe") -version | Select-Object -First 1
