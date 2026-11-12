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
BUILD_REVISION="$(git -C "$ROOT" rev-parse HEAD)"

# 0. Stage Reference Assemblies & iOS .NET 9.0.7 Runtime BCL
UPSTREAM_PUBLISH_DIR="$ROOT/upstream/godot-export/.godot/mono/publish/arm64"
DEPS_CACHE_DIR="$ROOT/tmp/ios-deps"
IOS_BCL_ASSETS_DIR="$IOS_DIR/assets/dotnet_bcl"
IOS_BUNDLE_BCL_DIR="$IOS_DIR/STS2Launcher/dotnet_bcl"
IOS_FRAMEWORKS_DIR="$IOS_DIR/STS2Launcher/Frameworks"
GODOT_SOURCE_DIR="${GODOT_DIR:-$ROOT/vendor/godot}"

mkdir -p "$UPSTREAM_PUBLISH_DIR" "$DEPS_CACHE_DIR" "$IOS_BCL_ASSETS_DIR" "$IOS_BUNDLE_BCL_DIR" "$IOS_FRAMEWORKS_DIR"
rm -f "$IOS_BCL_ASSETS_DIR/"*.dll

if [ ! -f "$UPSTREAM_PUBLISH_DIR/sts2.dll" ] && [ -f "$IOS_DIR/refs/sts2.dll.gz" ]; then
    echo "Decompressing metadata-only reference assembly sts2.dll..."
    gzip -dc "$IOS_DIR/refs/sts2.dll.gz" > "$UPSTREAM_PUBLISH_DIR/sts2.dll"
fi

REF_APK="$DEPS_CACHE_DIR/StS2Launcher-v0.2.436-arm64-v8a.apk"
if [ ! -f "$REF_APK" ]; then
    echo "Downloading pinned upstream release bundle for Harmony & GodotSharp assemblies..."
    if ! curl -fSL --retry 3 --retry-delay 2 --retry-all-errors "https://github.com/SocialHummingbird/StS2-Launcher-Overhaul/releases/download/v0.2.436/StS2Launcher-v0.2.436-arm64-v8a.apk" -o "$REF_APK"; then
        echo "Direct release download failed (e.g. 504); falling back to GitHub API asset stream..."
        curl -fSL --retry 3 --retry-delay 2 --retry-all-errors -H "Accept: application/octet-stream" "https://api.github.com/repos/SocialHummingbird/StS2-Launcher-Overhaul/releases/assets/677299765" -o "$REF_APK"
    fi
fi

REF_EXTRACT_DIR="$DEPS_CACHE_DIR/ref-apk"
if [ ! -d "$REF_EXTRACT_DIR/assets/dotnet_bcl" ]; then
    mkdir -p "$REF_EXTRACT_DIR"
    unzip -q -o "$REF_APK" "assets/dotnet_bcl/*" -d "$REF_EXTRACT_DIR"
fi

# Copy only necessary reference assemblies from ref-apk, avoiding Android BCL contamination
cp -f "$REF_EXTRACT_DIR/assets/dotnet_bcl/0Harmony.dll" "$UPSTREAM_PUBLISH_DIR/"
GODOTSHARP_SOURCE="$REF_EXTRACT_DIR/assets/dotnet_bcl/GodotSharp.dll"
if [ -f "$GODOT_SOURCE_DIR/bin/GodotSharp/Api/Release/GodotSharp.dll" ]; then
    GODOTSHARP_SOURCE="$GODOT_SOURCE_DIR/bin/GodotSharp/Api/Release/GodotSharp.dll"
elif [ "${STS2_REQUIRE_PATCHED_GODOTSHARP:-0}" = "1" ]; then
    echo "ERROR: Patched GodotSharp assembly is required but was not built"
    exit 1
fi
if [ "${STS2_REQUIRE_PATCHED_GODOTSHARP:-0}" = "1" ] && ! grep -aFq '[STS2ManagedPropertyTrace]' "$GODOTSHARP_SOURCE"; then
    echo "ERROR: Staged GodotSharp assembly is missing the managed property trace marker"
    exit 1
fi
echo "Staging GodotSharp from $GODOTSHARP_SOURCE"
cp -f "$GODOTSHARP_SOURCE" "$UPSTREAM_PUBLISH_DIR/GodotSharp.dll"
cp -f "$REF_EXTRACT_DIR/assets/dotnet_bcl/MonoMod."*.dll "$UPSTREAM_PUBLISH_DIR/" 2>/dev/null || true
cp -f "$REF_EXTRACT_DIR/assets/dotnet_bcl/0Harmony.dll" "$IOS_BCL_ASSETS_DIR/"
cp -f "$GODOTSHARP_SOURCE" "$IOS_BCL_ASSETS_DIR/GodotSharp.dll"
cp -f "$REF_EXTRACT_DIR/assets/dotnet_bcl/MonoMod."*.dll "$IOS_BCL_ASSETS_DIR/" 2>/dev/null || true
if [ -f "$REF_EXTRACT_DIR/assets/dotnet_bcl/System.Runtime.CompilerServices.Unsafe.dll" ]; then
    cp -f "$REF_EXTRACT_DIR/assets/dotnet_bcl/System.Runtime.CompilerServices.Unsafe.dll" "$UPSTREAM_PUBLISH_DIR/"
    cp -f "$REF_EXTRACT_DIR/assets/dotnet_bcl/System.Runtime.CompilerServices.Unsafe.dll" "$IOS_BCL_ASSETS_DIR/"
fi

echo "Checking managed behavior before native packaging..."
dotnet build "$ROOT/tests/STS2Mobile.GameIdentityTests/STS2Mobile.GameIdentityTests.csproj" -c Release -p:SourceRevisionId="$BUILD_REVISION"
dotnet "$ROOT/tests/STS2Mobile.GameIdentityTests/bin/Release/net9.0/STS2Mobile.GameIdentityTests.dll"

echo "Patching MonoMod.Core for iOS..."
dotnet run --project "$ROOT/tools/SteamKitAndroidPatch/SteamKitAndroidPatch.csproj" -c Release -- --patch-monomod-ios "$IOS_BCL_ASSETS_DIR/MonoMod.Core.dll"
cp -f "$IOS_BCL_ASSETS_DIR/MonoMod.Core.dll" "$UPSTREAM_PUBLISH_DIR/MonoMod.Core.dll" 2>/dev/null || true

IOS_RUNTIME_NUPKG="$DEPS_CACHE_DIR/microsoft.netcore.app.runtime.mono.ios-arm64.9.0.7.nupkg"
if [ ! -f "$IOS_RUNTIME_NUPKG" ]; then
    echo "Downloading Microsoft.NETCore.App.Runtime.Mono.ios-arm64 9.0.7..."
    curl -fSL --retry 3 --retry-delay 2 --retry-all-errors "https://api.nuget.org/v3-flatcontainer/microsoft.netcore.app.runtime.mono.ios-arm64/9.0.7/microsoft.netcore.app.runtime.mono.ios-arm64.9.0.7.nupkg" -o "$IOS_RUNTIME_NUPKG"
fi

IOS_RUNTIME_EXTRACT="$DEPS_CACHE_DIR/ios-runtime"
if [ ! -d "$IOS_RUNTIME_EXTRACT/runtimes" ]; then
    mkdir -p "$IOS_RUNTIME_EXTRACT"
    unzip -q -o "$IOS_RUNTIME_NUPKG" "runtimes/ios-arm64/*" -d "$IOS_RUNTIME_EXTRACT"
fi

echo "Staging clean iOS-specific .NET 9.0.7 BCL and native interop libraries..."
rm -rf "$IOS_BUNDLE_BCL_DIR"/*
cp -f "$IOS_RUNTIME_EXTRACT/runtimes/ios-arm64/lib/net9.0/"*.dll "$IOS_BUNDLE_BCL_DIR/"
cp -f "$IOS_RUNTIME_EXTRACT/runtimes/ios-arm64/native/System.Private.CoreLib.dll" "$IOS_BUNDLE_BCL_DIR/"
rm -f "$IOS_FRAMEWORKS_DIR/libSystem."*.dylib
for dylib_path in "$IOS_RUNTIME_EXTRACT/runtimes/ios-arm64/native/libSystem."*.dylib; do
    if [ -f "$dylib_path" ]; then
        dylib_file="$(basename "$dylib_path")"
        fw_name="${dylib_file%.dylib}"
        fw_dir="$IOS_FRAMEWORKS_DIR/$fw_name.framework"
        rm -rf "$fw_dir"
        mkdir -p "$fw_dir"
        cp -f "$dylib_path" "$fw_dir/$fw_name"
        install_name_tool -id "@rpath/$fw_name.framework/$fw_name" "$fw_dir/$fw_name"
        bundle_id_suffix="$(echo "$fw_name" | tr '_' '-')"
        cat > "$fw_dir/Info.plist" <<EOF
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
	<key>CFBundleExecutable</key>
	<string>$fw_name</string>
	<key>CFBundleIdentifier</key>
	<string>com.trianix.sts2launcher.framework.$bundle_id_suffix</string>
	<key>CFBundleInfoDictionaryVersion</key>
	<string>6.0</string>
	<key>CFBundleName</key>
	<string>$fw_name</string>
	<key>CFBundlePackageType</key>
	<string>FMWK</string>
	<key>CFBundleShortVersionString</key>
	<string>9.0.7</string>
	<key>CFBundleVersion</key>
	<string>9.0.7</string>
	<key>DTPlatformName</key>
	<string>iphoneos</string>
	<key>MinimumOSVersion</key>
	<string>15.0</string>
</dict>
</plist>
EOF
    fi
done
cp -f "$IOS_BCL_ASSETS_DIR/0Harmony.dll" "$IOS_BUNDLE_BCL_DIR/"
cp -f "$IOS_BCL_ASSETS_DIR/GodotSharp.dll" "$IOS_BUNDLE_BCL_DIR/"
cp -f "$IOS_BCL_ASSETS_DIR/MonoMod."*.dll "$IOS_BUNDLE_BCL_DIR/" 2>/dev/null || true

# 0b. Stage FMOD Studio Engine & fmod-gdextension (6.1.0-4.5.0) + spine-godot (4.2 / 4.5.1-stable) for iOS
mkdir -p "$IOS_DIR/libs"

FMOD_ADDONS_ZIP="$DEPS_CACHE_DIR/fmod-gdextension-6.1.0-4.5.0-addons.zip"
if [ ! -f "$FMOD_ADDONS_ZIP" ]; then
    echo "Downloading utopia-rise/fmod-gdextension 6.1.0-4.5.0 for iOS..."
    curl -fSL --retry 3 --retry-delay 2 --retry-all-errors "https://github.com/utopia-rise/fmod-gdextension/releases/download/6.1.0-4.5.0/addons.zip" -o "$FMOD_ADDONS_ZIP"
fi

FMOD_EXTRACT_DIR="$DEPS_CACHE_DIR/fmod-addons"
if [ ! -d "$FMOD_EXTRACT_DIR/fmod/libs/iOS" ]; then
    mkdir -p "$FMOD_EXTRACT_DIR"
    unzip -q -o "$FMOD_ADDONS_ZIP" "fmod/libs/iOS/*" -d "$FMOD_EXTRACT_DIR"
fi

cp -f "$FMOD_EXTRACT_DIR/fmod/libs/iOS/libfmod_iphoneos.a" "$IOS_DIR/libs/"
cp -f "$FMOD_EXTRACT_DIR/fmod/libs/iOS/libfmodstudio_iphoneos.a" "$IOS_DIR/libs/"

FMOD_FW_NAME="libGodotFmod.ios.template_release.universal"
FMOD_FW_DIR="$IOS_FRAMEWORKS_DIR/$FMOD_FW_NAME.framework"
mkdir -p "$FMOD_FW_DIR"
cp -f "$FMOD_EXTRACT_DIR/fmod/libs/iOS/libGodotFmod.ios.template_release.xcframework/ios-arm64/$FMOD_FW_NAME.dylib" "$FMOD_FW_DIR/$FMOD_FW_NAME"
install_name_tool -id "@rpath/$FMOD_FW_NAME.framework/$FMOD_FW_NAME" "$FMOD_FW_DIR/$FMOD_FW_NAME"

cat > "$FMOD_FW_DIR/Info.plist" <<EOF
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
	<key>CFBundleExecutable</key>
	<string>$FMOD_FW_NAME</string>
	<key>CFBundleIdentifier</key>
	<string>com.trianix.sts2launcher.framework.libGodotFmod</string>
	<key>CFBundleInfoDictionaryVersion</key>
	<string>6.0</string>
	<key>CFBundleName</key>
	<string>$FMOD_FW_NAME</string>
	<key>CFBundlePackageType</key>
	<string>FMWK</string>
	<key>CFBundleShortVersionString</key>
	<string>6.1.0</string>
	<key>CFBundleVersion</key>
	<string>6.1.0</string>
	<key>DTPlatformName</key>
	<string>iphoneos</string>
	<key>MinimumOSVersion</key>
	<string>15.0</string>
</dict>
</plist>
EOF

SPINE_ZIP="$DEPS_CACHE_DIR/spine-godot-extension-4.2-4.5.1-stable.zip"
if [ ! -f "$SPINE_ZIP" ]; then
    echo "Downloading spine-godot GDExtension 4.2 for Godot 4.5.1-stable..."
    curl -fSL --retry 3 --retry-delay 2 --retry-all-errors "https://spine-godot.s3.amazonaws.com/4.2/4.5.1-stable/spine-godot-extension-4.2-4.5.1-stable.zip" -o "$SPINE_ZIP"
fi

SPINE_EXTRACT_DIR="$DEPS_CACHE_DIR/spine-godot"
if [ ! -d "$SPINE_EXTRACT_DIR/bin/ios" ]; then
    mkdir -p "$SPINE_EXTRACT_DIR"
    unzip -q -o "$SPINE_ZIP" "bin/ios/*" -d "$SPINE_EXTRACT_DIR"
fi

rm -rf "$IOS_FRAMEWORKS_DIR/libspine_godot.ios.template_release.framework"
cp -R "$SPINE_EXTRACT_DIR/bin/ios/libspine_godot.ios.template_release.framework" "$IOS_FRAMEWORKS_DIR/"

# 1. Build Managed Assembly
echo "Publishing STS2Mobile managed assembly..."
MANAGED_PUBLISH="$ROOT/tmp/ios-managed-publish"
rm -rf "$MANAGED_PUBLISH"
dotnet publish "$ROOT/src/STS2Mobile/STS2Mobile.csproj" -c Release -o "$MANAGED_PUBLISH" -p:SourceRevisionId="$BUILD_REVISION"

echo "Patching SteamKit2 for iOS..."
dotnet run --project "$ROOT/tools/SteamKitAndroidPatch/SteamKitAndroidPatch.csproj" -c Release -- --patch-ios "$MANAGED_PUBLISH/SteamKit2.dll"
if [ -f "$IOS_BUNDLE_BCL_DIR/MonoMod.Core.dll" ]; then
    echo "Patching MonoMod for iOS..."
    dotnet run --project "$ROOT/tools/SteamKitAndroidPatch/SteamKitAndroidPatch.csproj" -c Release -- --patch-monomod-ios "$IOS_BUNDLE_BCL_DIR/MonoMod.Core.dll"
elif [ -f "$MANAGED_PUBLISH/MonoMod.Core.dll" ]; then
    echo "Patching MonoMod for iOS..."
    dotnet run --project "$ROOT/tools/SteamKitAndroidPatch/SteamKitAndroidPatch.csproj" -c Release -- --patch-monomod-ios "$MANAGED_PUBLISH/MonoMod.Core.dll"
fi

# 2. Generate Bootstrap PCK
echo "Generating bootstrap.pck..."
python3 "$ROOT/scripts/make-bootstrap-pck.py" "$IOS_DIR/STS2Launcher/bootstrap.pck"
cp -f "$IOS_DIR/STS2Launcher/bootstrap.pck" "$IOS_DIR/STS2Launcher/STS2Launcher.pck"

# 3. Stage Managed Assemblies into App Bundle Resources
ASSEMBLIES_DIR="$IOS_DIR/STS2Launcher/assemblies"
rm -rf "$ASSEMBLIES_DIR"
mkdir -p "$ASSEMBLIES_DIR"

cp -f "$MANAGED_PUBLISH/STS2Mobile.dll" "$ASSEMBLIES_DIR/"
cp -f "$MANAGED_PUBLISH/STS2Mobile.dll" "$IOS_BUNDLE_BCL_DIR/"

for dll in SteamKit2.dll protobuf-net.dll protobuf-net.Core.dll System.IO.Hashing.dll ZstdSharp.dll Mono.Cecil.dll 0Harmony.dll GodotSharp.dll; do
    if [ -f "$MANAGED_PUBLISH/$dll" ]; then
        cp -f "$MANAGED_PUBLISH/$dll" "$ASSEMBLIES_DIR/"
        cp -f "$MANAGED_PUBLISH/$dll" "$IOS_BUNDLE_BCL_DIR/"
    elif [ -f "$IOS_BUNDLE_BCL_DIR/$dll" ]; then
        cp -f "$IOS_BUNDLE_BCL_DIR/$dll" "$ASSEMBLIES_DIR/"
    elif [ -f "$IOS_BCL_ASSETS_DIR/$dll" ]; then
        cp -f "$IOS_BCL_ASSETS_DIR/$dll" "$ASSEMBLIES_DIR/"
    fi
done
cp -f "$IOS_BUNDLE_BCL_DIR/MonoMod."*.dll "$ASSEMBLIES_DIR/" 2>/dev/null || true
if [ -f "$REF_EXTRACT_DIR/assets/dotnet_bcl/System.Runtime.CompilerServices.Unsafe.dll" ]; then
    cp -f "$REF_EXTRACT_DIR/assets/dotnet_bcl/System.Runtime.CompilerServices.Unsafe.dll" "$ASSEMBLIES_DIR/"
    cp -f "$REF_EXTRACT_DIR/assets/dotnet_bcl/System.Runtime.CompilerServices.Unsafe.dll" "$IOS_BUNDLE_BCL_DIR/"
fi
# Stage metadata-only reference sts2.dll into dotnet_bcl so initial launcher boot can resolve types before any game branch is downloaded.
# Never place sts2.dll in assemblies/ so IOSAssemblyBootstrapper will not overwrite a staged runtime-pack sts2.dll.
if [ -f "$UPSTREAM_PUBLISH_DIR/sts2.dll" ]; then
    cp -f "$UPSTREAM_PUBLISH_DIR/sts2.dll" "$IOS_BUNDLE_BCL_DIR/sts2.dll"
fi
rm -f "$ASSEMBLIES_DIR/sts2.dll"
python3 "$ROOT/scripts/write-ios-runtime-manifest.py" "$ROOT" "$IOS_BUNDLE_BCL_DIR" "$ASSEMBLIES_DIR" "$IOS_DIR/STS2Launcher/bootstrap.pck"

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
rm -f "$APP_DIR/Frameworks/"*.dylib "$APP_DIR/"*.dylib
for fw in "$IOS_FRAMEWORKS_DIR/"*.framework; do
    if [ -d "$fw" ]; then
        rm -rf "$APP_DIR/Frameworks/$(basename "$fw")"
        cp -R "$fw" "$APP_DIR/Frameworks/"
    fi
done

# Ad-hoc sign frameworks, dylibs, and app bundle with JIT entitlements
for fw in "$APP_DIR/Frameworks/"*.framework; do
    if [ -d "$fw" ]; then
        codesign --force --sign - "$fw"
    fi
done
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
