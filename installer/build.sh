#!/usr/bin/env bash
# Baut dist/SchleimigeScreenshots-Setup-<Version>.exe auf Linux/macOS (Docker + Inno Setup unter Wine).
# Voraussetzung: dist/SchleimigeScreenshots.exe  (dotnet publish src/App -c Release -o dist)
set -euo pipefail
cd "$(dirname "$0")/.."

version=$(sed -n 's:.*<Version>\(.*\)</Version>.*:\1:p' Directory.Build.props | head -1)
[ -f dist/SchleimigeScreenshots.exe ] || { echo "dist/SchleimigeScreenshots.exe fehlt - erst: dotnet publish src/App -c Release -o dist" >&2; exit 1; }

docker image inspect schleimige-iscc >/dev/null 2>&1 || docker build -t schleimige-iscc installer/docker
docker run --rm -v "$PWD":/work -w /work schleimige-iscc "/DAppVersion=$version" installer/SchleimigeScreenshots.iss
echo "Fertig: dist/SchleimigeScreenshots-Setup-$version.exe"
