#!/bin/bash
set -x -e

OS="$(uname -s)"

case "$OS" in
    Linux*)
        STS_DLL="$HOME/.steam/steam/steamapps/common/Slay the Spire 2/data_sts2_linuxbsd_x86_64/sts2.dll"
        GODOT="../Godot_v4.5.1-stable_mono_linux_x86_64/Godot_v4.5.1-stable_mono_linux.x86_64"
        ;;
    Darwin*)
        STS_DLL="$HOME/Library/Application Support/steam/steamapps/common/Slay the Spire 2/SlayTheSpire2.app/Contents/Resources/data_sts2_macos_arm64/sts2.dll"
        GODOT="/Applications/Godot_mono.app/Contents/MacOS/Godot"
        ;;
    *)
        echo "Unknown operating system: $OS"
        exit 1
        ;;
esac

cp "$STS_DLL" .
$GODOT --build-solutions --quit --headless --verbose
rm -rf dist
mkdir -p dist
cp ./.godot/mono/temp/bin/Debug/RouteSuggest.dll dist/
$GODOT --export-pack "Windows Desktop" dist/RouteSuggest.pck --headless
cp RouteSuggest.json dist/RouteSuggest.json

VERSION=$(jq -r ".version" RouteSuggest.json)
rm -f RouteSuggest-v$VERSION.zip
cd dist && zip -r ../RouteSuggest-v$VERSION.zip .
