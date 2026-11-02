#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
VERSION_NAME="${1:-0.2.436}"
VERSION_CODE="${2:-436009}"
SCHEME="STS2Launcher"
CONFIGURATION="Release"
ARTIFACTS_DIR="$ROOT/artifacts/ios"
IOS_DIR="$ROOT/ios"
XCODEPROJ="$IOS_DIR/STS2Launcher.xcodeproj"

echo "=== Building STS2Launcher iOS ($VERSION_NAME / $VERSION_CODE) ==="

# 0. Stage Reference Assemblies & iOS .NET 9.0.7 Runtime BCL
UPSTREAM_PUBLISH_DIR="$ROOT/upstream/godot-export/.godot/mono/publish/arm64"
DEPS_CACHE_DIR="$ROOT/tmp/ios-deps"
IOS_BCL_ASSETS_DIR="$IOS_DIR/assets/dotnet_bcl"
IOS_BUNDLE_BCL_DIR="$IOS_DIR/STS2Launcher/dotnet_bcl"
IOS_FRAMEWORKS_DIR="$IOS_DIR/STS2Launcher/Frameworks"

mkdir -p "$UPSTREAM_PUBLISH_DIR" "$DEPS_CACHE_DIR" "$IOS_BCL_ASSETS_DIR" "$IOS_BUNDLE_BCL_DIR" "$IOS_FRAMEWORKS_DIR"

if [ ! -f "$UPSTREAM_PUBLISH_DIR/sts2.dll" ] && [ -f "$IOS_DIR/refs/sts2.dll.gz" ]; then
    echo "Decompressing metadata-only reference assembly sts2.dll..."
    gzip -dc "$IOS_DIR/refs/sts2.dll.gz" > "$UPSTREAM_PUBLISH_DIR/sts2.dll"
fi

REF_APK="$DEPS_CACHE_DIR/StS2Launcher-v0.2.436-arm64-v8a.apk"
if [ ! -f "$REF_APK" ]; then
    echo "Downloading pinned upstream release bundle for Harmony & GodotSharp assemblies..."
    curl -fSL "https://github.com/SocialHummingbird/StS2-Launcher-Overhaul/releases/download/v0.2.436/StS2Launcher-v0.2.436-arm64-v8a.apk" -o "$REF_APK"
fi

REF_EXTRACT_DIR="$DEPS_CACHE_DIR/ref-apk"
if [ ! -d "$REF_EXTRACT_DIR/assets/dotnet_bcl" ]; then
    mkdir -p "$REF_EXTRACT_DIR"
    unzip -q -o "$REF_APK" "assets/dotnet_bcl/*" -d "$REF_EXTRACT_DIR"
fi

cp -f "$REF_EXTRACT_DIR/assets/dotnet_bcl/"* "$IOS_BCL_ASSETS_DIR/"
cp -f "$REF_EXTRACT_DIR/assets/dotnet_bcl/"* "$IOS_BUNDLE_BCL_DIR/"
cp -f "$REF_EXTRACT_DIR/assets/dotnet_bcl/0Harmony.dll" "$UPSTREAM_PUBLISH_DIR/"
cp -f "$REF_EXTRACT_DIR/assets/dotnet_bcl/GodotSharp.dll" "$UPSTREAM_PUBLISH_DIR/"
cp -f "$REF_EXTRACT_DIR/assets/dotnet_bcl/MonoMod."*.dll "$UPSTREAM_PUBLISH_DIR/" 2>/dev/null || true

IOS_RUNTIME_NUPKG="$DEPS_CACHE_DIR/microsoft.netcore.app.runtime.mono.ios-arm64.9.0.7.nupkg"
if [ ! -f "$IOS_RUNTIME_NUPKG" ]; then
    echo "Downloading Microsoft.NETCore.App.Runtime.Mono.ios-arm64 9.0.7..."
    curl -fSL "https://api.nuget.org/v3-flatcontainer/microsoft.netcore.app.runtime.mono.ios-arm64/9.0.7/microsoft.netcore.app.runtime.mono.ios-arm64.9.0.7.nupkg" -o "$IOS_RUNTIME_NUPKG"
fi

IOS_RUNTIME_EXTRACT="$DEPS_CACHE_DIR/ios-runtime"
if [ ! -d "$IOS_RUNTIME_EXTRACT/runtimes" ]; then
    mkdir -p "$IOS_RUNTIME_EXTRACT"
    unzip -q -o "$IOS_RUNTIME_NUPKG" "runtimes/ios-arm64/*" -d "$IOS_RUNTIME_EXTRACT"
fi

echo "Staging iOS-specific .NET 9.0.7 BCL and native interop libraries..."
cp -f "$IOS_RUNTIME_EXTRACT/runtimes/ios-arm64/lib/net9.0/"*.dll "$IOS_BUNDLE_BCL_DIR/"
cp -f "$IOS_RUNTIME_EXTRACT/runtimes/ios-arm64/native/System.Private.CoreLib.dll" "$IOS_BUNDLE_BCL_DIR/"
cp -f "$IOS_RUNTIME_EXTRACT/runtimes/ios-arm64/native/libSystem."*.dylib "$IOS_FRAMEWORKS_DIR/"

# 1. Build Managed Assembly
echo "Publishing STS2Mobile managed assembly..."
dotnet publish "$ROOT/src/STS2Mobile/STS2Mobile.csproj" -c Release

# 2. Generate Bootstrap PCK
echo "Generating bootstrap.pck..."
python3 "$ROOT/scripts/make-bootstrap-pck.py" "$IOS_DIR/STS2Launcher/bootstrap.pck"
cp -f "$IOS_DIR/STS2Launcher/bootstrap.pck" "$IOS_DIR/STS2Launcher/STS2Launcher.pck"

# 3. Stage Managed Assemblies into App Bundle Resources
ASSEMBLIES_DIR="$IOS_DIR/STS2Launcher/assemblies"
mkdir -p "$ASSEMBLIES_DIR"

MANAGED_PUBLISH="$ROOT/src/STS2Mobile/bin/Release/net9.0/publish"
if [ ! -d "$MANAGED_PUBLISH" ]; then
    MANAGED_PUBLISH="$ROOT/src/STS2Mobile/bin/Release/net9.0"
fi

cp -f "$MANAGED_PUBLISH/STS2Mobile.dll" "$ASSEMBLIES_DIR/"
cp -f "$MANAGED_PUBLISH/STS2Mobile.dll" "$IOS_BUNDLE_BCL_DIR/"

for dll in SteamKit2.dll protobuf-net.dll protobuf-net.Core.dll System.IO.Hashing.dll ZstdSharp.dll Mono.Cecil.dll 0Harmony.dll GodotSharp.dll; do
    if [ -f "$MANAGED_PUBLISH/$dll" ]; then
        cp -f "$MANAGED_PUBLISH/$dll" "$ASSEMBLIES_DIR/"
        cp -f "$MANAGED_PUBLISH/$dll" "$IOS_BUNDLE_BCL_DIR/"
    elif [ -f "$IOS_BCL_ASSETS_DIR/$dll" ]; then
        cp -f "$IOS_BCL_ASSETS_DIR/$dll" "$ASSEMBLIES_DIR/"
    fi
done
cp -f "$IOS_BCL_ASSETS_DIR/MonoMod."*.dll "$ASSEMBLIES_DIR/" 2>/dev/null || true

# 4. Build with Xcode
mkdir -p "$ARTIFACTS_DIR"
IPA_PATH="$ARTIFACTS_DIR/STS2Launcher-$VERSION_NAME.ipa"

echo "Running xcodebuild..."
xcodebuild build \
    -project "$XCODEPROJ" \
    -target "$SCHEME" \
    -configuration "$CONFIGURATION" \
    -sdk iphoneos \
    SYMROOT="$IOS_DIR/build" \
    MARKETING_VERSION="$VERSION_NAME" \
    CURRENT_PROJECT_VERSION="$VERSION_CODE" \
    CODE_SIGN_IDENTITY="-" \
    CODE_SIGNING_REQUIRED=NO \
    CODE_SIGNING_ALLOWED=NO

# 5. Package unsigned / ad-hoc IPA for AltStore / SideStore / Sideloadly
echo "Packaging IPA..."
APP_DIR="$IOS_DIR/build/$CONFIGURATION-iphoneos/STS2Launcher.app"
if [ ! -d "$APP_DIR" ]; then
    echo "ERROR: Built STS2Launcher.app not found at $APP_DIR"
    exit 1
fi

# Ensure all runtime assets and native interop dylibs are embedded into the .app bundle
cp -f "$IOS_DIR/STS2Launcher/bootstrap.pck" "$APP_DIR/"
cp -f "$IOS_DIR/STS2Launcher/bootstrap.pck" "$APP_DIR/STS2Launcher.pck"
mkdir -p "$APP_DIR/assemblies"
cp -R "$ASSEMBLIES_DIR/"* "$APP_DIR/assemblies/"
mkdir -p "$APP_DIR/dotnet_bcl"
cp -R "$IOS_BUNDLE_BCL_DIR/"* "$APP_DIR/dotnet_bcl/"
mkdir -p "$APP_DIR/Frameworks"
cp -f "$IOS_FRAMEWORKS_DIR/"*.dylib "$APP_DIR/Frameworks/"
cp -f "$IOS_FRAMEWORKS_DIR/"*.dylib "$APP_DIR/"

# Ad-hoc sign dylibs and app bundle with JIT entitlements
for dylib in "$APP_DIR/Frameworks/"*.dylib "$APP_DIR/"*.dylib; do
    if [ -f "$dylib" ]; then
        codesign --force --sign - "$dylib"
    fi
done
codesign --force --sign - --entitlements "$IOS_DIR/STS2Launcher/STS2Launcher.entitlements" "$APP_DIR"

TMP_PAYLOAD="$(mktemp -d)"
mkdir -p "$TMP_PAYLOAD/Payload"
cp -R "$APP_DIR" "$TMP_PAYLOAD/Payload/"

(cd "$TMP_PAYLOAD" && zip -qr "$IPA_PATH" Payload)
rm -rf "$TMP_PAYLOAD"

# Generate SHA256 checksum
shasum -a 256 "$IPA_PATH" > "$IPA_PATH.sha256"

echo "=== Build Complete! ==="
echo "IPA created at: $IPA_PATH"
echo "Checksum: $(cat "$IPA_PATH.sha256")"
