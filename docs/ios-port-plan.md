# Slay the Spire 2 iOS Port Roadmap (JIT-Enabled Sideloading)

This roadmap documents the architectural strategy, phase breakdown, and execution status for porting the Slay the Spire 2 mobile launcher from Android to iOS.

---

## Architecture Overview

```
+---------------------------------------------------------------------------------+
|                                 Slay the Spire 2                                |
|           (Godot 4.5.1 PCK + C# Managed sts2.dll + Workshop Mods)               |
+---------------------------------------------------------------------------------+
                                      |
                                      v
+---------------------------------------------------------------------------------+
|                       STS2Mobile (Managed C# Layer)                             |
|  - Harmony / MonoMod Runtime Detours (Mobile layout, touch, aspect ratio)       |
|  - SteamKit2 (Steam login, branch depot downloader, Cloud save sync)            |
|  - Standalone Godot C# Launcher UI                                              |
|  - AppPaths (Sandboxed Documents & Application Support)                         |
+---------------------------------------------------------------------------------+
                                      |
                                      v
+---------------------------------------------------------------------------------+
|                          Godot 4.5.1 Engine (iOS)                               |
|  - SCons platform=ios arch=arm64 module_mono_enabled=yes                        |
|  - Metal Rendering Driver (CAMetalLayer)                                        |
|  - Patched modules/mono (hooks ModEntry.Apply on mobile)                        |
|  - FMOD GDExtension (iOS arm64)                                                 |
+---------------------------------------------------------------------------------+
                                      |
                                      v
+---------------------------------------------------------------------------------+
|                    Mono Runtime (.NET 9 ios-arm64)                              |
|  - Pinned commit 3c298d9f00936d651cc47d221762474e25277672                       |
|  - libmonosgen-2.0.a static library                                             |
|  - JIT enabled via get-task-allow entitlement (AltStore / SideStore / TrollStore)|
+---------------------------------------------------------------------------------+
                                      |
                                      v
+---------------------------------------------------------------------------------+
|                       Native iOS Host Shell (Xcode)                             |
|  - AppDelegate.mm (AVAudioSession, lifecycle, staging trigger)                  |
|  - ViewController.mm (Metal surface, landscape layout, home bar auto-hide)      |
|  - IOSAssemblyBootstrapper.mm (Stages BCL & DLLs into Application Support)      |
|  - IOSGodotAppBridge.mm (C-linkage P/Invoke exports for managed C#)             |
|  - Info.plist (UIFileSharingEnabled, LSSupportsOpeningDocumentsInPlace)         |
|  - STS2Launcher.entitlements (get-task-allow, dynamic-codesigning)              |
+---------------------------------------------------------------------------------+
```

---

## Phase Breakdown & Tracking

### Phase 1: Environment, Host Shell & Toolchain Setup `[COMPLETED]`
- [x] Create iOS project directory structure under `ios/STS2Launcher`.
- [x] Create `ios/STS2Launcher.xcodeproj/project.pbxproj` configured for iOS 15.0+ arm64 deployment.
- [x] Implement `AppDelegate.h`/`.mm` with `AVAudioSessionCategoryPlayback` (ambient mix enabled).
- [x] Implement `ViewController.h`/`.mm` with `CAMetalLayer` and auto-hiding home indicator.
- [x] Implement `IOSAssemblyBootstrapper.h`/`.mm` to stage BCL and assemblies into `~/Library/Application Support/.godot/mono/publish/arm64`.
- [x] Implement `IOSGodotAppBridge.h`/`.mm` with C-linkage exports (`ios_share_text_file`, `ios_get_usable_space_bytes`, `ios_get_device_diagnostics`, `ios_set_keep_screen_on`, `ios_get_safe_area_insets`, `ios_get_documents_dir`).
- [x] Configure `Info.plist` with `UIFileSharingEnabled` (Files app integration), `LSSupportsOpeningDocumentsInPlace`, and `NSAllowsArbitraryLoads`.
- [x] Configure `STS2Launcher.entitlements` with `get-task-allow = true` and `dynamic-codesigning = true` for JIT sideloading.
- [x] Update `patches/godot/sts2-android-runtime.patch` to support both `ANDROID_ENABLED` and `IOS_ENABLED`.
- [x] Update `scripts/make-bootstrap-pck.py` to support custom output targets.
- [x] Create iOS build scripts: `scripts/build-godot-ios.sh`, `scripts/build-ios-mono.sh`, and `scripts/build-ios-local.sh`.
- [x] Create `.github/workflows/build-ios.yml` for automated CI on `macos-14` (Apple Silicon).
- [x] Update `.gitignore` for iOS build outputs, `.ipa`, `.xcarchive`, and Xcode schemes.

---

### Phase 2: Engine & Native Runtime Compilation `[READY TO RUN]`
- [x] SCons iOS build script (`scripts/build-godot-ios.sh`) prepared.
- [x] CMake Mono `ios-arm64` build script (`scripts/build-ios-mono.sh`) prepared.
- [ ] Run Godot 4.5.1 iOS arm64 build and verify `libgodot.ios.template_release.arm64.a`.
- [ ] Run Mono .NET 9 iOS arm64 build and verify `libmonosgen-2.0.a`.
- [ ] Link `libgodot.a` and `libmonosgen-2.0.a` into `ios/libs/`.

---

### Phase 3: Native Host Shell & Lifecycle `[COMPLETED]`
- [x] Assembly staging logic from bundle resources to sandbox user directory.
- [x] Documents directory preparation (`Documents/StS2Launcher/Mods`) for direct file management via iOS Files app.
- [x] Native share sheet integration (`UIActivityViewController`).
- [x] Screen wake lock controller (`[UIApplication sharedApplication].idleTimerDisabled`).

---

### Phase 4: Audio Middleware (FMOD for iOS) `[PENDING]`
- [ ] Obtain FMOD Studio Engine for iOS (`fmod.xcframework`).
- [ ] Link FMOD frameworks into `ios/libs/` and Xcode project.
- [ ] Build/stage `fmod_gdextension` for iOS (`fmod_gdextension.ios.template_release.arm64.dylib` or static link).
- [ ] Confirm PCK patcher rewrites FMOD bank paths to `user://fmod_banks/` (functional within iOS sandbox).

---

### Phase 5: Managed Layer Adaptations (`STS2Mobile` C#) `[NEXT]`
- [ ] **`AppPaths.cs`**:
  - Add `OperatingSystem.IsIOS()` detection.
  - Map `ExternalModsDir` to `Environment.GetFolderPath(SpecialFolder.MyDocuments)/StS2Launcher/Mods`.
  - Bypass Android storage permissions checks (`HasStoragePermission()` returns `true`).
- [ ] **Bridge Integration**:
  - Implement `IOSGodotAppBridge.cs` in managed C# using `[DllImport("__Internal")]` to bind to the native C functions.
  - Provide unified bridge abstraction (`AppBridge`) that delegates to `AndroidGodotAppBridge` on Android and `IOSGodotAppBridge` on iOS.
- [ ] **Safe Area & Display Insets**:
  - Update `UiScalePatches.cs` and `MobileLayoutPatches.cs` to query `DisplayServer.GetDisplaySafeArea()`.
  - Add margin padding for iPhone notch, Dynamic Island, and bottom home bar.
- [ ] **Managed Build Manifests**:
  - Stage `mono-runtime-build.json` and `harmony-runtime-build.json` for iOS so `STS2Mobile.csproj` builds without Android dependencies.

---

### Phase 6: Steam Download & PCK Patching Pipeline `[PENDING]`
- [ ] Test `SteamKit2` depot download on iOS (TLS / WebSocket socket performance).
- [ ] Ensure `ios_set_keep_screen_on(true)` is activated during depot downloads to avoid iOS background suspension.
- [ ] Verify `DepotDownloader.PckPatch.cs` patches `SlayTheSpire2.pck` cleanly on iOS.
- [ ] Test Steam Cloud save upload/download over mobile connection.

---

### Phase 7: Sideloading Packaging & Device Acceptance `[PENDING]`
- [ ] Generate `.ipa` package using `scripts/build-ios-local.sh` or GitHub Actions.
- [ ] Sideload onto iPhone via AltStore (enable AltJIT) or SideStore (enable SideJIT).
- [ ] Verify standalone launcher startup on physical device.
- [ ] Verify Steam login, depot download, and PCK patch execution.
- [ ] Verify game boot, touch input, audio playback (FMOD), and Cloud save sync.
