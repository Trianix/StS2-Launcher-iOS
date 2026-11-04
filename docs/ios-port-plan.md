# Slay the Spire 2 iOS Port — Implementation Plan & Milestones

This document defines the step-by-step implementation plan and milestones for completing the iOS port of the Slay the Spire 2 mobile launcher (`sts2-ios`).

---

## 1. Target Architecture Overview

```mermaid
flowchart TD
    subgraph Boot["1. Native iOS Startup (AppDelegate + Godot Entry)"]
        CheckMarker{"pending_game_launch.json<br/>exists & PCK valid?"}
        LauncherMode["Launcher Mode<br/>STS2_LAUNCHER_BOOTSTRAP=1<br/>--main-pack bundle/bootstrap.pck"]
        GameMode["Game Mode<br/>STS2_LAUNCHER_BOOTSTRAP=0<br/>STS2_AUTO_LAUNCH_GAME=1<br/>--main-pack SlayTheSpire2.pck"]
        CheckMarker -->|No| LauncherMode
        CheckMarker -->|Yes (consume marker)| GameMode
    end

    subgraph Runtime["2. Godot 4.5.1 + Mono (.NET 9 ios-arm64)"]
        Bootstrapper["IOSAssemblyBootstrapper<br/>Stages BCL + STS2Mobile.dll into<br/>Library/Application Support/.../.godot/mono/publish/arm64"]
        MonoInit["Mono Runtime Init<br/>+ real libmono-component-marshal-ilgen-static.a<br/>+ iOS Harmony/__Internal icache flush"]
        ModEntry["STS2Mobile.ModEntry.Initialize()"]
        Bootstrapper --> MonoInit --> ModEntry
    end

    subgraph Modes["3. Execution Flow"]
        LauncherUI["StandaloneLauncherScene<br/>- SteamKit2 Login (Encrypted Token Store)<br/>- Depot Download & PCK Patching<br/>- RuntimeAssemblyCachePreparer (stages sts2.dll)<br/>- Writes pending_game_launch.json & exits"]
        GameBoot["Slay the Spire 2 Game Boot<br/>- ModEntry.Apply() (Harmony Startup Patches)<br/>- FMOD & Spine GDExtensions<br/>- Mobile SaveManager & Steam Cloud Sync"]
    end

    LauncherMode --> Bootstrapper
    GameMode --> Bootstrapper
    ModEntry -->|STS2_LAUNCHER_BOOTSTRAP=1| LauncherUI
    ModEntry -->|STS2_LAUNCHER_BOOTSTRAP=0| GameBoot
    LauncherUI -.->|"Next App Launch"| CheckMarker
```

---

## 2. Completed Foundation (Phases 1–4)

- [x] **Godot 4.5.1 iOS Engine Build (`scripts/build-godot-ios.sh`)**: Static archive `libgodot.ios.template_release.arm64.a` built with `module_mono_enabled=yes` and `patches/godot/sts2-android-runtime.patch`.
- [x] **Mono .NET 9 iOS Build (`scripts/build-ios-mono.sh`)**: Static archive `libmonosgen-2.0.a` compiled from `dotnet/runtime` `v9.0.7`.
- [x] **FMOD & Spine GDExtensions**: Static FMOD Studio libraries (`libfmod_iphoneos.a`, `libfmodstudio_iphoneos.a`), `libGodotFmod.ios.template_release.universal.framework`, and `libspine_godot.ios.template_release.framework` linked and registered in `AppDelegate.mm`.
- [x] **Godot iOS Lifecycle Integration (`AppDelegate.mm`)**: `AppDelegate` subclasses `GDTApplicationDelegate` via `+load` method swizzling so Godot's `main()` drives the Metal render loop while running custom startup hooks.
- [x] **On-Device File Diagnostics**: `boot_diag.log` and `godot_stdout.log` written to `~/Documents` (`UIFileSharingEnabled = true`) for inspection without Xcode console attachment.

---

## 3. Active Implementation Milestones

### Milestone 1: Fix the Mono Execution & Patching Foundation

**Goal:** Ensure Mono can initialize `System.Private.CoreLib`, execute P/Invoke marshaling wrappers, and run Harmony (`MonoMod.Core`) method detours on iOS without crashing on `KERN_PROTECTION_FAILURE` or missing IL generation stubs.

#### Execution Mode Decision Gate
- **Path A (Primary — Sideloaded JIT via StikDebug / `CS_DEBUGGED`):** Run Mono in JIT mode when a debugger/JIT enabler is attached, allowing Harmony (`0Harmony.dll` / `MonoMod.Core`) to compile `DynamicMethod` detours at runtime just like Android.
- **Path B (Fallback — Zero-JIT Interpreter + Offline `Mono.Cecil` Rewriting):** If running without StikDebug/JIT permissions, Mono must run in `MONO_AOT_MODE_INTERP_ONLY` **and** all Harmony patches must be rewritten to patch `sts2.dll` on disk via `Mono.Cecil` during `RuntimePackWriter` staging.

#### Implementation Steps
- [x] **Step 1.1: Link the real `marshal-ilgen` component in `scripts/build-ios-mono.sh`**
  - Currently, `-DDISABLE_COMPONENTS=1` in `scripts/build-ios-mono.sh` links `libmono-component-marshal-ilgen-stub-static.a`. Without the real component, Mono cannot generate P/Invoke or Reverse P/Invoke IL wrappers at runtime.
  - Update `scripts/build-ios-mono.sh` to build and archive `libmono-component-marshal-ilgen-static.a` (instead of `-stub-static.a`) into `libmonosgen-2.0.a`.
- [x] **Step 1.2: Fix `patches/mono/android-net9.patch` (`mini-runtime.c` & iOS JIT code allocation)**
  - In `patches/mono/android-net9.patch` (`get_ftnptr_for_method` in `mini-runtime.c`), remove the hardcoded `jit_only = TRUE` override (`mono_jit_compile_method_with_policy(method, TRUE, TRUE, error)`) when interpreter mode is active (`mono_use_interpreter`), otherwise Mono forcibly invokes the JIT compiler even when configured for interpreter mode.
  - For Path A (JIT on iOS): Ensure Mono's `mono-codeman.c` / `mono-mmap.c` does not fall back to non-executable `dlmalloc` (`rw-`) heap allocations when `TARGET_IOS` is defined; ensure code chunks are allocated via `mmap` and transitioned to `PROT_READ | PROT_EXEC` (`mprotect` + `sys_icache_invalidate`).
- [x] **Step 1.3: Move `sts2_run_jit_probe()` in `ios/STS2Launcher/AppDelegate.mm` out of `+load`**
  - `+[AppDelegate load]` runs during `dyld` image loading *before* `main()` and before StikDebug attaches (`P_TRACED=0`).
  - Move `sts2_run_jit_probe()` into `application:didFinishLaunchingWithOptions:` so it accurately detects `CS_DEBUGGED` after debugger attachment and sets an environment variable (`STS2_IOS_JIT_AVAILABLE=1` or `0`) consumed by `gdmono.cpp`.
- [x] **Step 1.4: Update `patches/harmony/android-net9.patch` for iOS (`MonoMod.Core`)**
  - On Android, `patches/harmony/android-net9.patch` adds `AndroidSystem.cs` which P/Invokes `libmonosgen-2.0.so!mono_arch_flush_icache`.
  - On iOS, `libmonosgen-2.0.a` is statically linked into the main executable (`__Internal`), and `OperatingSystem.IsIOS()` is not matched by `OperatingSystem.IsAndroid()`.
  - Update the Harmony patch so `MonoMod.Core` supports `OperatingSystem.IsIOS()` and resolves `mono_arch_flush_icache` via `[DllImport("__Internal")]`.

**Milestone 1 Exit Criteria:**
- `godot_stdout.log` shows `[STS2GodotPatch] initialize_GDMono_step: mono_jit_init_version RETURNED` and `STS2Mobile.ModEntry.Initialize` starts executing in managed C#.

---

### Milestone 2: Unify iOS Paths & Clean Up `dotnet_bcl` Packaging

**Goal:** Eliminate the directory split-brain between `~/Documents` and `~/Library/Application Support`, remove Android-contaminated BCL assemblies from the iOS bundle, and remove dead host shell files.

#### Implementation Steps
- [x] **Step 2.1: Unify internal launcher/runtime paths under `AppPaths.AppPrivateDataDir`**
  - On iOS, Godot's `OS.GetDataDir()` returns `~/Documents`, while `IOSGodotAppBridge.GetUserDataDir()` returns `~/Library/Application Support/Godot/app_userdata/sts2`.
  - Update `LauncherUI.ResolveLauncherDataDirectory()` in `src/STS2Mobile/Launcher/LauncherUI.Paths.cs` so `OperatingSystem.IsIOS()` returns `AppPaths.AppPrivateDataDir` (matching Android).
  - Update `ModEntry.RuntimeDataDirectory` in `src/STS2Mobile/ModEntry.RuntimeFiles.cs` so `OperatingSystem.IsIOS()` returns `AppPaths.AppPrivateDataDir`.
  - Update `ios/STS2Launcher/IOSAssemblyBootstrapper.mm` so `.godot/mono/publish/arm64` is staged only under `~/Library/Application Support/Godot/app_userdata/sts2/.godot/mono/publish/arm64`, keeping `~/Documents` clean for user-visible files (`StS2Launcher/Mods`, imported `.pck` files, and diagnostic logs).
  - Verify `patches/godot/sts2-android-runtime.patch` resolves the `.godot/mono/publish/arm64` assembly directory from the same `Application Support` path on `IOS_ENABLED`.
- [x] **Step 2.2: Build a clean iOS `dotnet_bcl` in `scripts/build-ios-local.sh` and `.github/workflows/build-ios.yml`**
  - Stop extracting `assets/dotnet_bcl/*` from `tmp/ref.apk` (which contaminates the iOS app bundle with Android-patched `SteamKit2.dll` / `System.Net.WebSockets.Client.dll` and a stale 9 MB `sts2.dll`).
  - Populate `ios/STS2Launcher/dotnet_bcl` strictly from:
    1. `Microsoft.NETCore.App.Runtime.Mono.ios-arm64` (`9.0.7`) managed BCL assemblies + `System.Private.CoreLib.dll`.
    2. `dotnet publish` output of `src/STS2Mobile/STS2Mobile.csproj` (`STS2Mobile.dll`, iOS-patched `0Harmony.dll`, `Mono.Cecil*.dll`, `SteamKit2.dll`, `protobuf-net*.dll`, `QRCoder.dll`, `Backbone.dll`, `GodotSharp.dll`, `GodotPlugins.dll`).
    3. If `SteamKit2.dll` / `System.Net.WebSockets.Client.dll` require `SteamKitAndroidPatch` for Mono SHA-1/crypto workarounds on iOS, run `SteamKitAndroidPatch` explicitly against the clean NuGet assemblies and ensure `AndroidJavaCrypto` uses its managed/Apple crypto path on iOS.
- [x] **Step 2.3: Remove dead code `ViewController.h` / `ViewController.mm`**
  - Delete `ios/STS2Launcher/ViewController.h` and `ios/STS2Launcher/ViewController.mm` (unused because `GDTApplicationDelegate` creates `GDTViewController`) and remove their build references from `ios/STS2Launcher.xcodeproj/project.pbxproj`.

**Milestone 2 Exit Criteria:**
- All launcher config, runtime slots, and `.godot/mono/publish/arm64` assemblies live under `Library/Application Support/Godot/app_userdata/sts2`, while `~/Documents` only contains user-facing folders and logs.
- `dotnet_bcl` builds cleanly in CI without requiring `ref.apk`.

---

### Milestone 3: Boot `bootstrap.pck` (Launcher UI) & Fix Steam Login Persistence [COMPLETED]

**Goal:** Render the standalone Launcher UI (`StandaloneLauncherScene.tscn`) on the iPhone screen, log into Steam, persist encrypted login credentials on iOS, and download/patch `SlayTheSpire2.pck`.

#### Implementation Steps
- [x] **Step 3.1: Verify standalone Launcher UI rendering on device & unify directories**
  - Boot `bootstrap.pck` with `STS2_LAUNCHER_BOOTSTRAP=1`.
  - Confirm `ModEntry.Initialize` loads `StandaloneLauncherScene.tscn`, applies safe-area insets (`IOSGodotAppBridge.GetSafeAreaInsets`), and accepts touch input. Unified data directories to `AppPaths.AppPrivateDataDir` (`~/Library/Application Support/Godot/app_userdata/sts2`).
- [x] **Step 3.2: Implement iOS credential encryption for `SteamCredentialStore`**
  - Add native iOS Keychain key generation (`ios_get_or_create_device_key`) in `IOSGodotAppBridge.h`/`.mm` with `kSecClassGenericPassword` and `kSecAttrAccessibleAfterFirstUnlock`.
  - Added P/Invoke binding in `src/STS2Mobile/IOSGodotAppBridge.cs`.
  - Implemented AES-256-GCM encryption/decryption in `src/STS2Mobile/Steam/AndroidEncryptedJsonFile.cs` matching Android wire format `[iv_len (1)] [iv (12)] [ciphertext] [tag (16)]` with Keychain key and persistent fallback key in `AppPaths.AppPrivateDataDir/.device_credential.key`.
- [x] **Step 3.3: Enable mobile Steam connection settings & verify depot download on iOS**
  - Updated `src/STS2Mobile/Steam/SteamConnectionConfigurationFactory.cs` and `MachineId.cs` so `OperatingSystem.IsIOS()` uses `ProtocolTypes.Tcp`, seeds the mobile machine-ID cache, and configures `IMachineInfoProvider`.
  - Verified compilation and passed all 220 `GameIdentity` tests.

**Milestone 3 Exit Criteria:**
- Launcher UI renders on device, Steam login succeeds and persists across restarts, and `SlayTheSpire2.pck` + `sts2.dll` are downloaded and patched into the active runtime slot.

---

### Milestone 4: Implement the "Launcher → Game" Handoff on iOS [COMPLETED]

**Goal:** Port the dual-mode launch state machine from `android/src/com/game/sts2launcher/GodotApp.java` to iOS so clicking **Play** stages the active slot's `sts2.dll` and boots `SlayTheSpire2.pck` on the next launch.

#### Implementation Steps
- [x] **Step 4.1: Enable `RuntimeAssemblyCachePreparer` on iOS**
  - In `src/STS2Mobile/Launcher/RuntimeAssemblyCachePreparer.cs` (`PrepareForLaunch`), enabled `PrepareIOS` for `OperatingSystem.IsIOS()`.
  - Promotes the active branch's `sts2.dll` from the validated runtime pack and support DLLs (`Steamworks.NET.dll`, `Sentry.dll`, etc.) from the game directory into `~/Library/Application Support/Godot/app_userdata/sts2/.godot/mono/publish/arm64`.
- [x] **Step 4.2: Implement `RequestGameLaunchAndRestart` in `IOSGodotAppBridge`**
  - Added `ios_request_game_launch_and_restart(const char* pck_path, const char* source)` to `ios/STS2Launcher/IOSGodotAppBridge.h`/`.mm` and bound it in `src/STS2Mobile/IOSGodotAppBridge.cs`.
  - Writes a durable launch marker file (`~/Library/Application Support/Godot/app_userdata/sts2/pending_game_launch.json`) storing `pck_path`, `source`, and timestamp.
  - Updated `src/STS2Mobile/Launcher/LauncherModel.Launch.cs` to call `IOSGodotAppBridge.RequestGameLaunchAndRestart(readiness.RuntimeSlot.PckPath, "launcher_play")`.
- [x] **Step 4.3: Dynamically switch `--main-pack` and environment variables on iOS startup**
  - Implemented dynamic method swizzling on `NSBundle` (`infoDictionary` and `objectForInfoDictionaryKey:`) in `ios/STS2Launcher/AppDelegate.mm` executed in `+load`.
  - On startup, checks for `pending_game_launch.json`:
    - **If present and `pck_path` exists on disk:** Deletes `pending_game_launch.json` immediately (preventing crash loops), sets `STS2_LAUNCHER_BOOTSTRAP=0`, `STS2_AUTO_LAUNCH_GAME=1`, `STS2_ACTIVE_PCK_PATH=<pck_path>`, and injects `godot_cmdline` = `@[ @"--main-pack", pckPath ]`.
    - **If absent (normal launch):** Sets `STS2_LAUNCHER_BOOTSTRAP=1`, `STS2_AUTO_LAUNCH_GAME=0`, and uses `@[ @"--main-pack", @"bootstrap.pck" ]`.
- [x] **Step 4.4: Add iOS Relaunch Notification UX**
  - Linked `UserNotifications.framework` in `ios/STS2Launcher.xcodeproj/project.pbxproj`.
  - Added local notification scheduling in `ios_request_game_launch_and_restart` alerting the user *"Slay the Spire 2 is ready — Tap to launch game"* with a guaranteed fallback timer before `exit(0)`.

**Milestone 4 Exit Criteria:**
- Clicking **Play** in the Launcher stages `sts2.dll`, writes `pending_game_launch.json`, exits cleanly, and the very next app open mounts `SlayTheSpire2.pck` with `STS2_LAUNCHER_BOOTSTRAP=0`.

---

### Milestone 5: Enable Mobile Gameplay Patches & Cloud Save Sync on iOS

**Goal:** Ensure all mobile Harmony patches, save management, Workshop mod loading, and FMOD/Spine integrations execute properly when `SlayTheSpire2.pck` boots on iOS.

#### Implementation Steps
- [x] **Step 5.1: Update `OperatingSystem.IsAndroid()` guards across `src/STS2Mobile/Patches/`**
  - Introduced `MobilePlatform.IsMobile => OperatingSystem.IsAndroid() || OperatingSystem.IsIOS()` in `src/STS2Mobile/MobilePlatform.cs`.
  - Updated mobile guards in:
    - `src/STS2Mobile/Patches/LauncherPatches.cs`: `StartOnMainMenu`, `ConstructDefault` (SaveManager + AndroidLocalSaveStore), `NormalizeUserDataPath`.
    - `src/STS2Mobile/Patches/ModLoaderPatches.cs`: Mobile mod discovery and Workshop staging.
    - `src/STS2Mobile/Patches/SettingsPatches.MobileDefaults.cs`: Skip intro logo and mobile graphics defaults.
    - `src/STS2Mobile/Patches/AndroidAtlasCompatibilityPatches.cs`: On-demand atlas loading and virtual filesystem fallbacks.
    - `src/STS2Mobile/Patches/AndroidAssetPreloadPatches.cs`: 4ms frame budget on background asset preloading.
    - `src/STS2Mobile/Patches/ControllerInputPatches.cs`: Fallback controller input strategy for iOS Bluetooth/MFi gamepads.
    - `src/STS2Mobile/Patches/SettingsPatches.LocString.cs` & `DevConsolePatches.cs`: Fallback string handling and mobile console suppression.
    - `src/STS2Mobile/Steam/SteamAuth.cs`, `Connection.cs`, `Login.cs`: Mobile auth resilience, retries, and EOSType.IOSUnknown.
  - Verified clean compilation and passed 220 of 220 `GameIdentity` test cases.
- [ ] **Step 5.2: End-to-End Game Verification on Physical Device**
  - Verify `ModEntry.Apply` applies all critical Harmony patches without failure (`patchResult.CriticalFailed == false`).
  - Verify main menu rendering, Spine character skeletons, and FMOD bank audio playback (including recovery after iOS audio interruption/lock screen).
  - Verify starting a run, touch input ergonomics, local save persistence, and Steam Cloud save sync.

**Milestone 5 Exit Criteria:**
- Slay the Spire 2 boots to the main menu, plays audio via FMOD, renders Spine animations, syncs Steam Cloud saves, and plays through combat on iOS.
