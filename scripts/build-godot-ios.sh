#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
GODOT_DIR="${GODOT_DIR:-$ROOT/vendor/godot}"
IOS_LIBS="$ROOT/ios/libs"
TARGET="${GODOT_TARGET:-template_release}"
EXPECTED_GODOT_VERSION="${EXPECTED_GODOT_VERSION:-4.5.1-stable}"

mkdir -p "$IOS_LIBS"

if python3 "$ROOT/scripts/ios-native-fingerprint.py" check godot "$IOS_LIBS/libgodot.ios.template_release.arm64.a"; then
    echo "Godot iOS library matches source recipe and toolchain; reusing."
    exit 0
fi

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

CCACHE_OPTS=""
if command -v ccache >/dev/null 2>&1; then
    echo "ccache detected: $(ccache --version | head -n 1)"
    CCACHE_OPTS="c_compiler_launcher=ccache cpp_compiler_launcher=ccache"
fi

echo "Building Godot for iOS (platform=ios arch=arm64 target=$TARGET module_mono_enabled=yes vulkan=no metal=yes $CCACHE_OPTS)..."
cd "$GODOT_DIR"
scons platform=ios arch=arm64 target="$TARGET" module_mono_enabled=yes vulkan=no metal=yes $CCACHE_OPTS -j"$jobs"

BUILT_LIB="$GODOT_DIR/bin/libgodot.ios.$TARGET.arm64.a"
if [ -z "$BUILT_LIB" ] || [ ! -f "$BUILT_LIB" ]; then
    echo "ERROR: Expected Godot library not found in $GODOT_DIR/bin"
    exit 1
fi

cp "$BUILT_LIB" "$IOS_LIBS/libgodot.ios.template_release.arm64.a"
python3 "$ROOT/scripts/ios-native-fingerprint.py" record godot "$IOS_LIBS/libgodot.ios.template_release.arm64.a"
echo "Staged Godot iOS library at $IOS_LIBS/libgodot.ios.template_release.arm64.a"
