<#
  build-installer.ps1 — publish the self-contained WinUI app, then compile it into a single
  YT-Rec-Setup.exe with Inno Setup.  Run on the Windows build box:

      powershell -NoProfile -ExecutionPolicy Bypass -File windows\installer\build-installer.ps1 -Version 1.1.1

  Output: windows\dist\YT-Rec-Setup.exe  (per-user installer, self-contained, unsigned).
  -SkipBuild reuses the existing publish folder (just re-runs Inno Setup).
#>
param(
  [string]$Version = "1.1.1",
  [switch]$SkipBuild
)
$ErrorActionPreference = "Stop"

# The WinUI app needs the .NET 8 SDK (global.json pins 8.0.x), which lives in the per-user .dotnet on the box
# — not the system dotnet (9.x). Put it first on PATH so VS MSBuild's SDK resolver picks 8.0.x; force English
# tool output so logs are parseable.
$env:DOTNET_CLI_UI_LANGUAGE = "en"
$env:DOTNET_ROOT = "$env:USERPROFILE\.dotnet"
$env:Path = "$env:USERPROFILE\.dotnet;" + $env:Path

$installerDir = $PSScriptRoot
$windowsDir   = Split-Path $installerDir -Parent
$appProj      = Join-Path $windowsDir "YtRec.App\YtRec.App.csproj"
$distDir      = Join-Path $windowsDir "dist"
New-Item -ItemType Directory -Force -Path $distDir | Out-Null

function Find-MSBuild {
  $known = "C:\Program Files (x86)\Microsoft Visual Studio\2022\BuildTools\MSBuild\Current\Bin\MSBuild.exe"
  if (Test-Path $known) { return $known }
  $vswhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
  if (Test-Path $vswhere) {
    $p = & $vswhere -latest -requires Microsoft.Component.MSBuild -find "MSBuild\**\Bin\MSBuild.exe" | Select-Object -First 1
    if ($p -and (Test-Path $p)) { return $p }
  }
  throw "MSBuild (VS 2022 Build Tools) not found — the WinUI app needs VS-grade MSBuild, not the bare dotnet SDK."
}

function Find-ISCC {
  foreach ($p in @("C:\Program Files (x86)\Inno Setup 6\ISCC.exe", "C:\Program Files\Inno Setup 6\ISCC.exe")) {
    if (Test-Path $p) { return $p }
  }
  throw "Inno Setup 6 (ISCC.exe) not found. Install it first (see install-inno.ps1)."
}

if (-not $SkipBuild) {
  $msbuild = Find-MSBuild
  Write-Host "==> Publishing $appProj (self-contained win-x64, v$Version)" -ForegroundColor Cyan
  & $msbuild $appProj /restore /t:Publish /p:Configuration=Release /p:Platform=x64 `
      /p:RuntimeIdentifier=win-x64 /p:SelfContained=true /p:WindowsAppSDKSelfContained=true `
      /p:Version=$Version /v:minimal /nologo
  if ($LASTEXITCODE -ne 0) { throw "MSBuild publish failed ($LASTEXITCODE)" }
}

# Pick the freshest publish\YtRec.exe — avoids the stale older-TFM folder (e.g. ...19041...) shadowing the build.
$exe = Get-ChildItem (Join-Path $windowsDir "YtRec.App\bin\x64\Release") -Recurse -Filter YtRec.exe -ErrorAction SilentlyContinue |
       Where-Object { $_.FullName -like "*\win-x64\publish\YtRec.exe" } |
       Sort-Object LastWriteTime -Descending | Select-Object -First 1
if (-not $exe) { throw "No published YtRec.exe found — did the build run?" }
$publishDir = $exe.DirectoryName
Write-Host "==> Publish folder: $publishDir" -ForegroundColor Cyan

$iscc = Find-ISCC
Write-Host "==> Compiling installer with $iscc" -ForegroundColor Cyan
& $iscc "/DAppVersion=$Version" "/DPublishDir=$publishDir" "/DOutputDir=$distDir" (Join-Path $installerDir "YtRec.iss")
if ($LASTEXITCODE -ne 0) { throw "Inno Setup compile failed ($LASTEXITCODE)" }

$setup = Join-Path $distDir "YT-Rec-Setup.exe"
if (-not (Test-Path $setup)) { throw "Expected $setup was not produced." }
$mb = [math]::Round((Get-Item $setup).Length / 1MB, 1)
$sha = (Get-FileHash $setup -Algorithm SHA256).Hash
Write-Host "`n✅ $setup  ($mb MB)" -ForegroundColor Green
Write-Host "   SHA256 $sha" -ForegroundColor Green
