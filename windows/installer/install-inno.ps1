<#
  install-inno.ps1 — one-time bootstrap of the Inno Setup 6 compiler on the build box.
  Silently installs ISCC.exe, then drops in the (unofficial) Traditional-Chinese wizard translation so
  the installer can greet the zh-TW audience in Chinese.  Idempotent: skips the download if ISCC exists.

      powershell -NoProfile -ExecutionPolicy Bypass -File windows\installer\install-inno.ps1
#>
param(
  [string]$Url   = "https://jrsoftware.org/download.php/is.exe",
  [string]$ChtIsl = "https://raw.githubusercontent.com/jrsoftware/issrc/main/Files/Languages/Unofficial/ChineseTraditional.isl"
)
$ErrorActionPreference = "Stop"
$iscc = "C:\Program Files (x86)\Inno Setup 6\ISCC.exe"

if (-not (Test-Path $iscc)) {
  $tmp = Join-Path $env:TEMP "innosetup-setup.exe"
  Write-Host "==> Downloading Inno Setup..." -ForegroundColor Cyan
  Invoke-WebRequest -Uri $Url -OutFile $tmp -UseBasicParsing
  Write-Host "==> Installing silently..." -ForegroundColor Cyan
  Start-Process -FilePath $tmp -ArgumentList "/VERYSILENT","/SUPPRESSMSGBOXES","/NORESTART","/SP-" -Wait
  if (-not (Test-Path $iscc)) { throw "ISCC.exe not found after install." }
}
Write-Host "ISCC: $iscc" -ForegroundColor Green

# Best-effort: add the Traditional-Chinese wizard translation (not bundled by default).
try {
  $dst = "C:\Program Files (x86)\Inno Setup 6\Languages\ChineseTraditional.isl"
  if (-not (Test-Path $dst)) {
    Invoke-WebRequest -Uri $ChtIsl -OutFile $dst -UseBasicParsing
    Write-Host "Added Traditional-Chinese wizard translation." -ForegroundColor Green
  }
} catch {
  Write-Host "(Chinese translation skipped: $($_.Exception.Message) — installer will use the English wizard.)" -ForegroundColor Yellow
}
