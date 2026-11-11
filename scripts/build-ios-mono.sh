#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
RECIPE_PATH="$ROOT/patches/mono/runtime.json"
PATCH_PATH="$ROOT/patches/mono/android-net9.patch"
IOS_LIBS="$ROOT/ios/libs"
CACHE_DIR="$ROOT/tmp/ios-mono"
# A changed patch must never reuse an already-patched source tree.
COMMIT="3c298d9f00936d651cc47d221762474e25277672"
ARCHIVE_URL="https://codeload.github.com/dotnet/runtime/tar.gz/$COMMIT"
PATCH_ID="$(shasum -a 256 "$PATCH_PATH" | cut -c1-16)"
SOURCE_DIR="$CACHE_DIR/source-$COMMIT-$PATCH_ID"
BUILD_DIR="$CACHE_DIR/build-arm64-$COMMIT-$PATCH_ID"

mkdir -p "$CACHE_DIR" "$IOS_LIBS"

if python3 "$ROOT/scripts/ios-native-fingerprint.py" check mono "$IOS_LIBS/libmonosgen-2.0.a"; then
    echo "Mono iOS library matches source recipe and toolchain; reusing."
    exit 0
fi

if [ ! -d "$SOURCE_DIR/.git" ]; then
    echo "Downloading pinned .NET runtime source ($COMMIT)..."
    ARCHIVE_FILE="$CACHE_DIR/runtime-$COMMIT.tar.gz"
    if [ ! -f "$ARCHIVE_FILE" ]; then
        curl -fSL "$ARCHIVE_URL" -o "$ARCHIVE_FILE"
    fi
    
    mkdir -p "$SOURCE_DIR"
    tar -xzf "$ARCHIVE_FILE" -C "$SOURCE_DIR" --strip-components=1
    
    echo "Initializing git index and applying patches..."
    git -C "$SOURCE_DIR" init --quiet
    git -C "$SOURCE_DIR" config core.autocrlf false
    git -C "$SOURCE_DIR" add -f -- .
    git -C "$SOURCE_DIR" apply "$PATCH_PATH"
fi

echo "Configuring Mono CMake for iOS arm64..."
mkdir -p "$BUILD_DIR"
cmake -S "$SOURCE_DIR/src/mono" -B "$BUILD_DIR" -G Ninja \
    -DCMAKE_SYSTEM_NAME=iOS \
    -DCMAKE_OSX_SYSROOT=iphoneos \
    -DCMAKE_OSX_ARCHITECTURES=arm64 \
    -DCLR_CMAKE_HOST_ARCH=arm64 \
    -DMONO_COMPONENTS_RID=ios-arm64 \
    -DCMAKE_BUILD_TYPE=Release \
    -DGC_SUSPEND=hybrid \
    -DENABLE_MINIMAL=ssa,logging \
    -DCMAKE_INSTALL_LIBDIR=lib

echo "Building Mono and marshal-ilgen component for iOS..."
cmake --build "$BUILD_DIR" --target monosgen-static mono-component-marshal-ilgen-static mono-component-debugger-stub-static mono-component-diagnostics_tracing-stub-static mono-component-hot_reload-stub-static --parallel "$(sysctl -n hw.ncpu 2>/dev/null || echo 4)"

OUTPUT_LIB="$BUILD_DIR/mono/mini/libmonosgen-2.0.a"
if [ ! -f "$OUTPUT_LIB" ]; then
    OUTPUT_LIB="$(find "$BUILD_DIR" -name "libmonosgen*.a" | head -n 1)"
fi

if [ -f "$OUTPUT_LIB" ]; then
    EXTRA_LIBS=()
    CONTAINERS_LIB="$(find "$BUILD_DIR" -name "libdn-containers*.a" | head -n 1 || true)"
    if [ -n "$CONTAINERS_LIB" ] && [ -f "$CONTAINERS_LIB" ]; then
        EXTRA_LIBS+=("$CONTAINERS_LIB")
    fi
    for comp in "marshal-ilgen-static" "debugger-stub-static" "diagnostics_tracing-stub-static" "hot_reload-stub-static"; do
        COMP_LIB="$(find "$BUILD_DIR" -name "libmono-component-$comp*.a" | head -n 1 || true)"
        if [ -n "$COMP_LIB" ] && [ -f "$COMP_LIB" ]; then
            echo "Found Mono component: $COMP_LIB"
            EXTRA_LIBS+=("$COMP_LIB")
        fi
    done

    if [ ${#EXTRA_LIBS[@]} -gt 0 ]; then
        echo "Combining libmonosgen, libdn-containers, and Mono components with libtool..."
        libtool -static -o "$IOS_LIBS/libmonosgen-2.0.a" "$OUTPUT_LIB" "${EXTRA_LIBS[@]}"
    else
        cp "$OUTPUT_LIB" "$IOS_LIBS/libmonosgen-2.0.a"
    fi
    echo "Staged libmonosgen-2.0.a at $IOS_LIBS/libmonosgen-2.0.a"
    python3 "$ROOT/scripts/ios-native-fingerprint.py" record mono "$IOS_LIBS/libmonosgen-2.0.a"
else
    echo "ERROR: libmonosgen library not found in $BUILD_DIR"
    exit 1
fi
