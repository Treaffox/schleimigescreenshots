#!/usr/bin/env bash
# Installiert bei Bedarf das .NET 10 SDK (ohne Root, nach ~/.dotnet), führt die Tests aus und
# baut dist/SchleimigeScreenshots.exe (Windows, self-contained) – auch auf Linux.
# Den Installer baut danach installer/build.sh (Docker + Inno Setup).
set -euo pipefail
cd "$(dirname "$0")/.."

export DOTNET_ROOT="${DOTNET_ROOT:-$HOME/.dotnet}"
export PATH="$DOTNET_ROOT:$PATH"
export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1

if ! dotnet --list-sdks 2>/dev/null | grep -q '^10\.'; then
    echo ">> .NET 10 SDK wird installiert nach $DOTNET_ROOT"
    curl -fsSL https://dot.net/v1/dotnet-install.sh -o "${TMPDIR:-/tmp}/dotnet-install.sh"
    bash "${TMPDIR:-/tmp}/dotnet-install.sh" --channel 10.0 --install-dir "$DOTNET_ROOT"
fi

echo ">> Tests"
dotnet test tests -c Release

echo ">> Publish (win-x64)"
dotnet publish src/App -c Release -o dist
cp install.ps1 dist/

version=$(sed -n 's:.*<Version>\(.*\)</Version>.*:\1:p' Directory.Build.props | head -1)
echo "Fertig: dist/SchleimigeScreenshots.exe (Version $version)"
echo "Installer (optional, braucht Docker): installer/build.sh"
