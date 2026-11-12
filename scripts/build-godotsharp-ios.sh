#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
GODOT_DIR="${GODOT_DIR:-$ROOT/vendor/godot}"
IOS_BCL_ASSETS_DIR="$ROOT/ios/assets/dotnet_bcl"
UPSTREAM_PUBLISH_DIR="$ROOT/upstream/godot-export/.godot/mono/publish/arm64"

if [ ! -d "$GODOT_DIR/.git" ]; then
    echo "ERROR: Expected the pinned Godot source at $GODOT_DIR"
    exit 1
fi

source "$ROOT/scripts/godot-source-utils.sh"
apply_godot_patches "$GODOT_DIR" "$ROOT"
python3 "$ROOT/scripts/verify-godot-callback-contract.py" "$GODOT_DIR" "$GODOT_DIR/bin/sts2-callback-contract.json"

if [ ! -f "$ROOT/venv/bin/activate" ]; then
    echo "ERROR: Expected the Godot build virtualenv at $ROOT/venv"
    exit 1
fi

source "$ROOT/venv/bin/activate"
host_arch="$(uname -m)"
case "$host_arch" in
    arm64|aarch64) scons_arch="arm64" ;;
    x86_64|amd64) scons_arch="x86_64" ;;
    *) echo "ERROR: Unsupported macOS build host architecture: $host_arch" >&2; exit 1 ;;
esac

jobs="$(sysctl -n hw.ncpu 2>/dev/null || echo 4)"
CCACHE_OPTS=""
if command -v ccache >/dev/null 2>&1; then
    echo "ccache detected: $(ccache --version | head -n 1)"
    CCACHE_OPTS="c_compiler_launcher=ccache cpp_compiler_launcher=ccache"
fi

echo "Building Godot macOS editor for glue generation (arch=$scons_arch)..."
(cd "$GODOT_DIR" && scons platform=macos arch="$scons_arch" target=editor module_mono_enabled=yes vulkan=no $CCACHE_OPTS -j"$jobs")

editor="$GODOT_DIR/bin/godot.macos.editor.$scons_arch.mono"
if [ ! -x "$editor" ]; then
    echo "ERROR: Expected Mono-enabled Godot editor not found at $editor"
    exit 1
fi

echo "Generating Godot C# glue from the pinned engine API..."
(cd "$GODOT_DIR" && "$editor" --headless --generate-mono-glue modules/mono/glue)

echo "Building GodotSharp from the patched source..."
(cd "$GODOT_DIR" && python3 modules/mono/build_scripts/build_assemblies.py --godot-output-dir=./bin --godot-platform=macos)

godotsharp="$GODOT_DIR/bin/GodotSharp/Api/Release/GodotSharp.dll"
if [ ! -f "$godotsharp" ]; then
    echo "ERROR: Patched GodotSharp assembly was not produced at $godotsharp"
    exit 1
fi
if ! python3 -c 'import pathlib, sys; marker = "[STS2ManagedPropertyTrace]".encode("utf-16le"); sys.exit(0 if marker in pathlib.Path(sys.argv[1]).read_bytes() else 1)' "$godotsharp"; then
    echo "ERROR: Built GodotSharp assembly does not contain the managed property trace marker"
    exit 1
fi

mkdir -p "$IOS_BCL_ASSETS_DIR" "$UPSTREAM_PUBLISH_DIR"
cp -f "$godotsharp" "$IOS_BCL_ASSETS_DIR/GodotSharp.dll"
cp -f "$godotsharp" "$UPSTREAM_PUBLISH_DIR/GodotSharp.dll"
echo "Staged traced GodotSharp assembly: $godotsharp"
