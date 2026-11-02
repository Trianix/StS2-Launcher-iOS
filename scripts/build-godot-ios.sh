#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
GODOT_DIR="${GODOT_DIR:-$ROOT/vendor/godot}"
IOS_LIBS="$ROOT/ios/libs"
TARGET="${GODOT_TARGET:-template_release}"
EXPECTED_GODOT_VERSION="${EXPECTED_GODOT_VERSION:-4.5.1-stable}"

source "$ROOT/scripts/godot-source-utils.sh"

if [ ! -d "$GODOT_DIR" ]; then
    echo "ERROR: Expected Godot source checkout at $GODOT_DIR"
    echo "Run scripts/setup-godot-source.sh first."
    exit 1
fi

apply_godot_patches "$GODOT_DIR" "$ROOT"

if [ ! -f "$ROOT/venv/bin/activate" ]; then
    echo "ERROR: Expected Python virtualenv with SCons at $ROOT/venv"
    exit 1
fi

source "$ROOT/venv/bin/activate"
jobs="$(sysctl -n hw.ncpu 2>/dev/null || nproc 2>/dev/null || echo 4)"

echo "Building Godot for iOS (platform=ios arch=arm64 target=$TARGET module_mono_enabled=yes)..."
cd "$GODOT_DIR"
scons platform=ios arch=arm64 target="$TARGET" module_mono_enabled=yes -j"$jobs"

BUILT_LIB="$GODOT_DIR/bin/libgodot.ios.$TARGET.arm64.a"
if [ ! -f "$BUILT_LIB" ]; then
    BUILT_LIB="$(find "$GODOT_DIR/bin" -name "libgodot.ios*.a" | head -n 1)"
fi
if [ -z "$BUILT_LIB" ] || [ ! -f "$BUILT_LIB" ]; then
    echo "ERROR: Expected Godot library not found in $GODOT_DIR/bin"
    exit 1
fi

mkdir -p "$IOS_LIBS"
cp "$BUILT_LIB" "$IOS_LIBS/libgodot.ios.template_release.arm64.a"
echo "Staged Godot iOS library at $IOS_LIBS/libgodot.ios.template_release.arm64.a"
