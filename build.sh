#!/bin/bash
set -x -e

OS="$(uname -s)"
STS2_PATH=${STS2_PATH:-""}
GODOT_PATH=${GODOT_PATH:-""}

case "$OS" in
    Linux*)
        if [ -z "$STS2_PATH" ]; then
            STS2_PATH="$HOME/.steam/steam/steamapps/common/Slay the Spire 2"
        fi
        STS_DLL="$STS2_PATH/data_sts2_linuxbsd_x86_64/sts2.dll"
        if [ -z "$GODOT_PATH" ]; then
            GODOT_PATH="godot" # Fallback to path
        fi
        ;;
    Darwin*)
        if [ -z "$STS2_PATH" ]; then
            STS2_PATH="$HOME/Library/Application Support/Steam/steamapps/common/Slay the Spire 2"
        fi
        STS_DLL="$STS2_PATH/SlayTheSpire2.app/Contents/Resources/data_sts2_macos_arm64/sts2.dll"
        if [ -z "$GODOT_PATH" ]; then
            GODOT_PATH="/Applications/Godot_mono.app/Contents/MacOS/Godot"
        fi
        ;;
    *)
        echo "Unknown operating system: $OS"
        exit 1
        ;;
esac

if [ ! -f "$STS_DLL" ]; then
    echo "Error: sts2.dll not found at $STS_DLL"
    echo "Please set STS2_PATH environment variable."
    exit 1
fi

cp "$STS_DLL" ./sts2.dll
"$GODOT_PATH" --build-solutions --quit --headless --verbose
rm -rf dist
mkdir -p dist
cp ./.godot/mono/temp/bin/Debug/RouteSuggest.dll dist/
"$GODOT_PATH" --export-pack "Windows Desktop" dist/RouteSuggest.pck --headless
cp RouteSuggest.json dist/RouteSuggest.json
cp RouteSuggestConfig.json dist/RouteSuggestConfig.json

VERSION=$(jq -r ".version" RouteSuggest.json)
rm -f RouteSuggest-v$VERSION.zip
cd dist && zip -r ../RouteSuggest-v$VERSION.zip .
