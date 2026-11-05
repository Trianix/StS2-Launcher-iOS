# iOS Runtime Findings & Investigation Log

Updated: 2026-11-03  
Target Device: iPhone 13 Pro Max (`iPhone14,3` / A15 Bionic), iOS 26.6.2 (build 23G90, arm64e)  
App Bundle: `com.trianix.sts2launcher.D692498JD2` (`STS2Launcher`)  
Deployment / Sideloading: SideStore / StikDebug (DDI auto-mounted via TSS service)

---

## 1. Runtime Boundary & Target Architecture

- **Engine:** Godot 4.5.1-stable compiled for iOS (`platform=ios arch=arm64 target=template_release metal=yes vulkan=no`).
- **CLR / Mono:** Pinned .NET 9.0.7 Mono runtime source (commit `3c298d9f00936d651cc47d221762474e25277672`) statically compiled into `libmonosgen-2.0.a`.
- **Packaging:** Native Xcode host shell (`ios/STS2Launcher`) with Godot and Mono statically force-loaded (`-Wl,-force_load`), packaged into an `.ipa` via GitHub Actions (`.github/workflows/build-ios.yml`).
- **Managed Layer:** `STS2Mobile.dll` (GodotSharp / Harmony entrypoints, SteamKit2, DepotDownloader, LauncherUI).

---

## 2. Chronological Investigation & Root Cause Analysis

### Issue A: Python / USB Toolchain Conflicts
- **Symptom:** `ModuleNotFoundError: No module named 'ipsw_parser.img4'` when running `SideJITServer --pair` or `pymobiledevice3`. Upgrading `pymobiledevice3` to 11.x broke `TunneldRunner`.
- **Root Cause:** Newer `ipsw-parser` 1.9.0 removed internal modules expected by `pymobiledevice3` 4.18.5.
- **Decision / Fix:**
  - Pinned `ipsw-parser==1.3.9` locally (`python -m pip install --user ipsw-parser==1.3.9`).
  - Successfully auto-mounted DeveloperDiskImage via `pymobiledevice3 mounter auto-mount`.
  - Exported pairing file `00008110-000C4D143C8A801E.mobiledevicepairing` directly for on-device StikDebug / SideStore operation.

---

### Issue B: Silent Godot Grey Screen on Initial Launch
- **Symptom:** The sideloaded app started, but remained on an unresponsive solid grey screen (the default clear color of `bootstrap.tscn`).
- **Root Causes:**
  1. **Assembly Search Directory Mismatch:** Godot's `OS::get_user_data_dir()` maps to `Documents/` on iOS. `GodotSharpDirs::get_api_assemblies_dir()` looked for assemblies in `Documents/.godot/mono/publish/arm64`, but `IOSAssemblyBootstrapper.mm` originally staged them only to `Library/Application Support/Godot/app_userdata/sts2/`.
  2. **Mach-O Static Symbol Visibility (`dlsym` returning `nullptr`):** In `patches/godot/sts2-android-runtime.patch`, `load_coreclr` called `dlopen(nullptr, RTLD_NOW)` and `dlsym(lib, "coreclr_initialize")`. In Apple's Mach-O runtime, symbols from statically linked archives (`libmonosgen-2.0.a`) are not added to the dynamic export trie searched by `dlsym` by default.
  3. **Silent stdout/stderr:** iOS SpringBoard launches route `stdout` and `stderr` to `/dev/null`, suppressing engine logs.
- **Decisions / Fixes (Commit `5dcc025`):**
  - **Stdout Redirection:** Added `freopen` in `AppDelegate.mm` (`+load`) streaming unbuffered `stdout`/`stderr` to `Documents/app_output.log` (accessible via Files app and USB HouseArrest service).
  - **Assembly Staging:** Updated `IOSAssemblyBootstrapper.mm` to copy BCL and managed assemblies to both `Documents/.godot/mono/publish/arm64` and `Application Support/...`.
  - **Dynamic Symbol Export:** Added `-rdynamic` to `OTHER_LDFLAGS` in `project.pbxproj` for Debug and Release configurations.
  - **Direct C Linkage:** Updated `patches/godot/sts2-android-runtime.patch` to declare `mono_native` C-linkage entrypoints (`coreclr_initialize`, `coreclr_create_delegate`, etc.) and bind directly if `dlsym` is null.
  - **Launcher Autostart:** Added `setenv("STS2_LAUNCHER_BOOTSTRAP", "1", 0)` in `AppDelegate.mm` to instruct `STS2Mobile.ModEntry.Apply` to show the standalone launcher UI immediately.

---

### Issue C: CI Patch Application & Compiler Errors
- **Symptom 1:** GitHub Actions failed on `Apply Godot patches` with `error: corrupt patch at line 123`.
  - **Cause:** Hunk header added-line count mismatch (`+311,55` vs `+311,56`) in `patches/godot/sts2-android-runtime.patch`.
  - **Fix (Commit `a7237e6`):** Corrected hunk line counts; verified clean apply and reverse against Godot 4.5.1 sources.
- **Symptom 2:** GitHub Actions failed in `Build Godot Engine for iOS` with:
  ```text
  modules/mono/mono_gd/gd_mono.cpp:386:8: error: redefinition of 'lib'
          void *lib = r_coreclr_dll_handle;
  ```
  - **Cause:** The `#if defined(IOS_ENABLED)` block declared `void *lib = dlopen(...)` without an enclosing block scope, colliding with `void *lib = r_coreclr_dll_handle;` lower in the same function.
  - **Fix (Commit `962cf74`):** Renamed variable to `ios_lib` and wrapped the block in `{ ... }` for strict scope isolation.

---

### Issue D: JIT Crash on iOS 26 / A15 Bionic (`EXC_BAD_ACCESS / KERN_PROTECTION_FAILURE`)
- **Symptom:** App booted Mono, staged assemblies, reached `monovm_create_delegate` -> `mini_init` -> `mono_runtime_init_checked` -> `mono_jit_runtime_invoke`, and then crashed instantly with `EXC_BAD_ACCESS (KERN_PROTECTION_FAILURE at 0x000000011...)` / `SIGABRT` / `SIGSEGV`.
- **Crash Log Analysis:**
  - `codeSigningFlags`: `0x32002005` (`CS_DEBUGGED` and `CS_GET_TASK_ALLOW` both active).
  - The faulting instruction pointer was valid generated ARM64 code (`fd 7b b9 a9 ...` - standard function prologue), but executing the page triggered a hardware protection fault.
- **Diagnostic JIT Probe (Commit `65eebd0`):**
  Added `sts2_run_jit_probe()` in `AppDelegate.mm` testing three code execution strategies using `sigsetjmp`:
  ```text
  [JITProbe] pid=5145 P_TRACED(debugger attached)=0
  [JITProbe] plain RWX mmap: FAULT signal 10 (errno=3)
  [JITProbe] RW mmap + mprotect(RX): FAULT signal 10 (errno=3)
  [JITProbe] MAP_JIT + pthread_jit_write_protect_np: pthread_jit_write_protect_np not found
  [JITProbe] MAP_JIT + pthread_jit_write_protect_np: mmap FAILED errno=1 (Operation not permitted)
  ```
- **Finding:**
  On A15+ devices running iOS 26, the kernel enforces TXM (Trusted Execution Monitor) / SPTM hardware W^X policies. Attaching a standard debugger flag (`CS_DEBUGGED`) no longer allows execution of arbitrary `mmap` pages. Sideloaded apps requiring JIT must implement an interactive `brk #0xf00d` region preparation handshake with StikDebug's automation script (`universal.js`).

---

### Issue E: Decision to Transition to Mono Interpreter Mode
- **Decision (Commit `1da7786`):**
  Rather than undertaking an experimental, unverified `brk #0xf00d` custom memory allocator rewrite in the Mono runtime, configure Mono to run in **interpreter-only mode** (`MONO_AOT_MODE_INTERP_ONLY`).
- **Implementation in `patches/godot/sts2-android-runtime.patch`:**
  ```cpp
  // iOS 26 (TXM) refuses executable JIT memory even with a debugger attached, so run Mono in
  // interpreter-only mode unless STS2_MONO_JIT is set. 8 == MONO_AOT_MODE_INTERP_ONLY.
  if (getenv("STS2_MONO_JIT") == nullptr) {
      mono_native::mono_jit_set_aot_mode(8);
  }
  ```
- **Architectural Implications & Trade-offs:**
  1. **No JIT Code Pages Needed:** Interpreter interprets CIL bytecode directly, bypassing W^X kernel memory execution faults on iOS 26.
  2. **Performance:** Execution of managed code will be slower than native JIT compilation. For the launcher UI (SteamKit2 download, file checks, Godot control widgets), overhead is negligible.
  3. **Harmony Compatibility:** Full game runtime patching via Harmony (`0Harmony.dll` / MonoMod) normally generates runtime dynamic trampolines (`DynamicMethod`). In interpreter mode, runtime IL hooks must use interpreter-compatible hooks or ahead-of-time patched assemblies.
  4. **Emergency Override:** Setting environment variable `STS2_MONO_JIT=1` re-enables full JIT mode if run on pre-TXM devices or if a `brk #0xf00d` allocator is implemented later.

---

### Issue F: Dynamic Heap Trampoline W^X Crash (`coreclr_create_delegate` & `sts2mobile_probe()`)
- **Symptom:** App booted Godot, initialized Metal 3.1, initialized CoreCLR domain `domain_id=0`, loaded `STS2Mobile.dll`, and crashed immediately with `SIGSEGV` when invoking `sts2mobile_probe()` or any delegate returned by `coreclr_create_delegate`.
- **Root Cause:**
  - Even in Interpreter mode (`MONO_AOT_MODE_INTERP_ONLY = 8`), `coreclr_create_delegate` calls `mono_create_ftnptr` which generates dynamic machine-code stubs (`tramp-arm64.c`) on the heap (`mov x15, ...; mov x16, ...; br x16`).
  - Without an attached debugger (`P_TRACED=0`), iOS kernel W^X forbids execution of heap memory pages. Jumping to the delegate function pointer triggered hardware `SIGSEGV` at `0x112d76c00`.
- **Decision & Fix (Commit `6b1b506`):**
  - Managed initializers on iOS must never be called through dynamic heap function pointer trampolines.
  - Implemented `ios_invoke_godot_plugin_init` and `ios_invoke_sts2mobile_apply` using Mono's precompiled static embedding API `mono_runtime_invoke`.
  - `mono_runtime_invoke` executes methods directly inside the interpreter loop (`interp.c`) within the pre-signed binary `__TEXT` segment with zero W^X violations.
  - Scoped `sts2mobile_probe` and `sts2mobile_harmony_probe` to `ANDROID_ENABLED` only.
  - Set `godot_plugins_initialize = reinterpret_cast<godot_plugins_initialize_fn>(1)` on iOS to satisfy engine null checks while routing actual invocation through `mono_runtime_invoke`.

---

### Issue G: StikDebug Wait Screen Auto-Start Race Condition
- **Symptom:** The wait screen had an arbitrary 3.2-second timeout (`sPollCount >= 8`) that auto-started the engine in Interpreter mode before StikDebug could attach.
- **Decision & Fix (Commit `c15fa1c`):**
  - Removed the auto-start timeout from `sts2_pollJitStatus:`.
  - Wait screen remains active displaying live PID, `P_TRACED`, and `CS_DEBUGGED` status.
  - Automatically launches Godot in JIT mode as soon as StikDebug attaches (`sts2_ios_is_jit_ready` returns true).
  - Provides a manual fallback button *"Start in Interpreter Mode (No JIT)"* if the user prefers to run without JIT.
  - Rate-limited the diagnostic probe in `AppDelegate.mm` to prevent log flooding while waiting.

---

### Issue H: CI Build Speed Optimization (Mono/Godot Cache Segregation & ccache)
- **Symptom:** Every patch tweak required an 18-minute rebuild in GitHub Actions because Mono and Godot were lumped into a single cache key.
- **Decision & Fix (Commit `c15fa1c`):**
  - Integrated `ccache` (`hendrikmuhs/ccache-action@v1.2`) with `c_compiler_launcher=ccache cpp_compiler_launcher=ccache` in SCons.
  - Split native cache into `cache-mono-lib` (`ios/libs/libmonosgen-2.0.a`) and `cache-godot-lib` (`ios/libs/libgodot.ios.template_release.arm64.a`).
  - The 3.5-minute Mono build is now restored in 1 second and completely skipped on Godot-only changes.
  - Cached Godot static library enables incremental builds to finish in under 2 minutes.

---

### Issue I: Black Screen Hang & CADisplayLink Rendering Kickoff
- **Symptom:** App displayed StikDebug wait screen, but when tapping *"Start in Interpreter Mode (No JIT)"*, the screen hung black. Terminating or switching apps caused Godot to fail with `ERROR: Condition "!_start_success" is true at: cleanup (main/main.cpp:4950)`.
- **Root Cause:**
  1. Godot's iOS rendering loop is driven by `CADisplayLink`, activated by `[GDTView startRendering]` inside `OS_AppleEmbedded::on_focus_in()`.
  2. Stock Godot invokes `on_focus_in()` from `applicationDidBecomeActive:`. However, because the StikDebug wait screen was presented during `didFinishLaunchingWithOptions:`, iOS had already fired `applicationDidBecomeActive:` at launch.
  3. When Godot was subsequently started via button tap (`sts2_forceStartWithoutJit:`) or JIT polling (`sts2_pollJitStatus:`), `self.window.rootViewController` was updated to `GDTViewController`, but iOS never re-sent `applicationDidBecomeActive:`.
  4. Without calling `[viewController.godotView startRendering]` or `OS_AppleEmbedded::on_focus_in()`, `CADisplayLink` was never added to the runloop. Consequently, `drawView` never executed, `GDTViewRenderer.setupView:` never invoked `Main::setup2()` or `Main::start()`, and the screen hung indefinitely on black.
  5. Furthermore, `sts2_forceStartWithoutJit:` executed `apple_embedded_main` synchronously within UIKit's touch event dispatcher, swapping `rootViewController` mid-touch.
- **Decision & Fix:**
  1. In `drivers/apple_embedded/app_delegate_service.mm` (`patches/godot/sts2-ios-logging.patch`), at the end of `sts2_startGodotEngine`, explicitly invoke:
     ```objc
     [viewController.godotView startRendering];
     if (OS_AppleEmbedded::get_singleton()) {
         OS_AppleEmbedded::get_singleton()->on_focus_in();
     }
     ```
  2. In `sts2_forceStartWithoutJit:`, `sts2_pollJitStatus:`, and `applicationDidBecomeActive:`, decouple engine initialization by dispatching to the next runloop turn:
     ```objc
     dispatch_async(dispatch_get_main_queue(), ^{
         [self sts2_startGodotEngine];
     });
     ```
  3. Verified `CADisplayLink` registers on `[NSRunLoop currentRunLoop]` for `NSRunLoopCommonModes`, ensuring initial frames immediately drive `Main::setup2()`, managed `.NET` startup, and `Main::start()`.

---

## 3. Verification & Diagnostic Commands

- **Read Device App Output Log:**
  ```powershell
  python -c "from pymobiledevice3.lockdown import create_using_usbmux; from pymobiledevice3.services.house_arrest import HouseArrestService; ld = create_using_usbmux(); ha = HouseArrestService(ld, bundle_id='com.trianix.sts2launcher.D692498JD2'); print(ha.get_file_contents('/Documents/app_output.log').decode(errors='replace'))"
  ```
- **Pull & Inspect iOS Crash Logs:**
  ```powershell
  python -m pymobiledevice3 crash pull tmp\crashes
  ```
- **Inspect App Container Filesystem:**
  ```powershell
  python -c "from pymobiledevice3.lockdown import create_using_usbmux; from pymobiledevice3.services.house_arrest import HouseArrestService; ld = create_using_usbmux(); ha = HouseArrestService(ld, bundle_id='com.trianix.sts2launcher.D692498JD2'); print('Documents:', ha.listdir('/Documents')); print('Publish:', ha.listdir('/Documents/.godot/mono/publish/arm64'))"
  ```
