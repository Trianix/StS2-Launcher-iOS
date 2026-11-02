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

# 1. Build Managed Assembly
echo "Building STS2Mobile managed assembly..."
dotnet build "$ROOT/src/STS2Mobile/STS2Mobile.csproj" -c Release

# 2. Generate Bootstrap PCK
echo "Generating bootstrap.pck..."
python3 "$ROOT/scripts/make-bootstrap-pck.py" "$IOS_DIR/STS2Launcher/bootstrap.pck"

# 3. Stage Managed Assemblies into App Bundle Resources
ASSEMBLIES_DIR="$IOS_DIR/STS2Launcher/assemblies"
mkdir -p "$ASSEMBLIES_DIR"

MANAGED_BIN="$ROOT/src/STS2Mobile/bin/Release/net9.0"
cp "$MANAGED_BIN/STS2Mobile.dll" "$ASSEMBLIES_DIR/"
if [ -f "$MANAGED_BIN/SteamKit2.dll" ]; then cp "$MANAGED_BIN/SteamKit2.dll" "$ASSEMBLIES_DIR/"; fi
if [ -f "$MANAGED_BIN/0Harmony.dll" ]; then cp "$MANAGED_BIN/0Harmony.dll" "$ASSEMBLIES_DIR/"; fi
if [ -f "$MANAGED_BIN/MonoMod.Core.dll" ]; then cp "$MANAGED_BIN/MonoMod."*.dll "$ASSEMBLIES_DIR/"; fi
if [ -f "$MANAGED_BIN/Mono.Cecil.dll" ]; then cp "$MANAGED_BIN/Mono.Cecil.dll" "$ASSEMBLIES_DIR/"; fi

# Also stage from android assets if dotnet_bcl is staged there
if [ -d "$ROOT/android/assets/dotnet_bcl" ]; then
    mkdir -p "$IOS_DIR/STS2Launcher/dotnet_bcl"
    cp -R "$ROOT/android/assets/dotnet_bcl/"* "$IOS_DIR/STS2Launcher/dotnet_bcl/"
fi

# 4. Build with Xcode
mkdir -p "$ARTIFACTS_DIR"
ARCHIVE_PATH="$ARTIFACTS_DIR/STS2Launcher.xcarchive"
IPA_PATH="$ARTIFACTS_DIR/STS2Launcher-$VERSION_NAME.ipa"

echo "Running xcodebuild archive..."
xcodebuild archive \
    -project "$XCODEPROJ" \
    -scheme "$SCHEME" \
    -configuration "$CONFIGURATION" \
    -archivePath "$ARCHIVE_PATH" \
    -destination "generic/platform=iOS" \
    CODE_SIGN_IDENTITY="-" \
    CODE_SIGNING_REQUIRED=NO \
    CODE_SIGNING_ALLOWED=NO

# 5. Package unsigned / ad-hoc IPA for AltStore / SideStore / Sideloadly
echo "Packaging IPA..."
APP_DIR="$ARCHIVE_PATH/Products/Applications/STS2Launcher.app"
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
