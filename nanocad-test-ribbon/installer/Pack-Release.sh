#!/usr/bin/env bash
# Pack slim plugin zip (+ optional separate GDAL runtime asset).
# Usage:
#   ./Pack-Release.sh 1.2.49
#   ./Pack-Release.sh 1.2.49 --with-gdal-runtime
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
REPO="$(cd "$ROOT/.." && pwd)"
VER="${1:-}"
if [[ -z "$VER" ]]; then
  echo "usage: $0 <version> [--with-gdal-runtime]" >&2
  exit 2
fi
WITH_GDAL=0
[[ "${2:-}" == "--with-gdal-runtime" ]] && WITH_GDAL=1

SLIM_OUT="$REPO/nanocad-test-ribbon-v${VER}.zip"
RUNTIME_OUT="$REPO/nanocad-gdal-runtime-3.12.1.zip"
TMP="$(mktemp -d)"
trap 'rm -rf "$TMP"' EXIT

echo "Packing slim plugin → $SLIM_OUT"
mkdir -p "$TMP/plugin"
# Copy tree then drop heavy/native build artifacts.
cp -a "$ROOT/." "$TMP/plugin/"
rm -rf \
  "$TMP/plugin/NcTestRibbon/bin" \
  "$TMP/plugin/NcTestRibbon/obj" \
  "$TMP/plugin/tools/NcGdalGeo/bin" \
  "$TMP/plugin/tools/NcGdalGeo/obj" \
  "$TMP/plugin/installer/gdal"

cat > "$TMP/plugin/GDAL-RUNTIME.txt" <<EOF
Slim plugin zip (~plugin + deps + NcGdalGeo tool). GDAL natives (~132MB) are separate.

Reuse LoadFile from v1.2.48 (or later):
  %LOCALAPPDATA%\\ASDC\\LoadFile\\NANOCAD\\gdal\\x64\\…

Or install natives once:
  nanocad-gdal-runtime-3.12.1.zip
  https://github.com/rublikdimas1989/Nanocad-Ribbon/releases/tag/gdal-runtime-3.12.1

INSTALL.bat copies plugin deps + refreshes tools\\NcGdalGeo; reuses existing gdal\\ if package has none.
Then: dotnet build → installer\\INSTALL.bat
EOF

rm -f "$SLIM_OUT"
( cd "$TMP/plugin" && zip -r -q "$SLIM_OUT" . )
echo "Slim size: $(du -h "$SLIM_OUT" | awk '{print $1}')"

if [[ "$WITH_GDAL" -eq 1 ]]; then
  echo "Packing GDAL runtime → $RUNTIME_OUT"
  mkdir -p "$TMP/runtime/installer/gdal" "$TMP/runtime/installer/tools"
  cp -a "$ROOT/installer/gdal/." "$TMP/runtime/installer/gdal/"
  cp -a "$ROOT/installer/tools/." "$TMP/runtime/installer/tools/"
  cat > "$TMP/runtime/INSTALL-GDAL-RUNTIME.ps1" <<'PSEOF'
$ErrorActionPreference = 'Stop'
$src = Split-Path -Parent $MyInvocation.MyCommand.Path
$sync = Join-Path $env:LOCALAPPDATA 'ASDC\LoadFile\NANOCAD'
New-Item -ItemType Directory -Path $sync -Force | Out-Null
$gdalSrc = Join-Path $src 'installer\gdal'
$toolSrc = Join-Path $src 'installer\tools\NcGdalGeo'
if (-not (Test-Path (Join-Path $gdalSrc 'x64\gdal_wrap.dll'))) { throw 'installer\gdal missing' }
if (-not (Test-Path (Join-Path $toolSrc 'NcGdalGeo.exe'))) { throw 'NcGdalGeo missing' }
$gdalDest = Join-Path $sync 'gdal'
$toolDest = Join-Path $sync 'tools\NcGdalGeo'
if (Test-Path $gdalDest) { Remove-Item $gdalDest -Recurse -Force }
if (Test-Path $toolDest) { Remove-Item $toolDest -Recurse -Force }
New-Item -ItemType Directory -Path $gdalDest -Force | Out-Null
New-Item -ItemType Directory -Path $toolDest -Force | Out-Null
Copy-Item (Join-Path $gdalSrc '*') $gdalDest -Recurse -Force
Copy-Item (Join-Path $toolSrc '*') $toolDest -Recurse -Force
Write-Host "GDAL runtime installed to $sync"
PSEOF
  cat > "$TMP/runtime/INSTALL-GDAL-RUNTIME.bat" <<'BEOF'
@echo off
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0INSTALL-GDAL-RUNTIME.ps1"
if errorlevel 1 pause
BEOF
  cat > "$TMP/runtime/README.txt" <<EOF
nanocad-gdal-runtime-3.12.1
Matched GDAL / GDAL.Native / GDAL.Plugins 3.12.1 + NcGdalGeo (geo + TIF translate).

Install once (nanoCAD closed):
  INSTALL-GDAL-RUNTIME.bat

Leaves:
  %LOCALAPPDATA%\\ASDC\\LoadFile\\NANOCAD\\gdal\\
  %LOCALAPPDATA%\\ASDC\\LoadFile\\NANOCAD\\tools\\NcGdalGeo\\

Slim plugin zips (v1.2.49+) reuse this automatically.
EOF
  rm -f "$RUNTIME_OUT"
  ( cd "$TMP/runtime" && zip -r -q "$RUNTIME_OUT" . )
  echo "Runtime size: $(du -h "$RUNTIME_OUT" | awk '{print $1}')"
fi

echo "Done."
