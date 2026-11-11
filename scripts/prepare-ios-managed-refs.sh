#!/usr/bin/env bash
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
REFS="$ROOT/upstream/godot-export/.godot/mono/publish/arm64"
CACHE="$ROOT/tmp/ios-deps"
APK="${STS2_REFERENCE_APK:-$CACHE/StS2Launcher-v0.2.436-arm64-v8a.apk}"
mkdir -p "$REFS" "$CACHE"
if [ ! -f "$APK" ]; then
    curl -fSL --retry 3 --retry-delay 2 --retry-all-errors \
        "https://github.com/SocialHummingbird/StS2-Launcher-Overhaul/releases/download/v0.2.436/StS2Launcher-v0.2.436-arm64-v8a.apk" -o "$APK.tmp"
    mv "$APK.tmp" "$APK"
fi
python3 - "$ROOT" "$APK" <<'PY'
import gzip, pathlib, sys, zipfile
root, apk = pathlib.Path(sys.argv[1]), pathlib.Path(sys.argv[2])
refs = root / 'upstream/godot-export/.godot/mono/publish/arm64'
runtime = root / 'tmp/ios-deps/gameplay-runtime'
runtime.mkdir(parents=True, exist_ok=True)
android = root / 'android/assets/dotnet_bcl'
android.mkdir(parents=True, exist_ok=True)
with zipfile.ZipFile(apk) as archive:
    for name in archive.namelist():
        leaf = pathlib.PurePosixPath(name).name
        if name.startswith('assets/dotnet_bcl/') and leaf.endswith('.dll'):
            (runtime / leaf).write_bytes(archive.read(name))
            if leaf == 'Mono.Cecil.dll' or leaf.startswith('MonoMod.'):
                (android / leaf).write_bytes(archive.read(name))
        if name.startswith('assets/dotnet_bcl/') and (leaf in ('0Harmony.dll', 'GodotSharp.dll', 'Steamworks.NET.dll', 'SmartFormat.dll', 'SmartFormat.ZString.dll') or leaf.startswith('MonoMod.')) and leaf.endswith('.dll'):
            (refs / leaf).write_bytes(archive.read(name))
if not (refs / 'sts2.dll').exists():
    (refs / 'sts2.dll').write_bytes(gzip.decompress((root / 'ios/refs/sts2.dll.gz').read_bytes()))
PY
