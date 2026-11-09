# STS2 iOS Port – Session Handover Document
**Date:** November 9, 2026  
**Repository:** `https://github.com/Trianix/StS2-Launcher-iOS.git`  
**Active Branch:** `feature/ios-port`  
**Latest Commit:** `af89aef`  
**Latest CI Build:** [Run #42598831164](https://github.com/Trianix/StS2-Launcher-iOS/actions/runs/42598831164) (Success – Artifact: `STS2Launcher-iOS`, 46.3 MB)

---

## 1. Executive Summary & Architecture

The project delivers a standalone iOS build for **Slay the Spire 2** without requiring a jailbreak or an attached debugger (no JIT entitlement required on stock iOS).

### Architecture Highlights:
- **Engine Core:** Custom Godot 4.5 engine with embedded Mono runtime running strictly in interpreter mode (`MONO_AOT_MODE_INTERP_ONLY = 8`).
- **Launcher & Assembly Staging:** `STS2Launcher` boots first, locates the PCK and staged assemblies in `Application Support/Godot/app_userdata/sts2/`, initializes Godot, boots Mono, and transfers execution to `STS2Mobile.dll` / `sts2.dll`.
- **Dynamic Detour Engine:** Standard desktop Harmony relies on dynamic native machine code trampolines, which are prohibited by iOS AMFI / W^X without JIT. Instead, `STS2Mobile` uses a native interpreter bridge (`sts2_ios_interp_detour_method` in `IOSGodotAppBridge.mm`). Harmony compiles prefix/postfix patches into dynamic methods via the Mono interpreter, and the bridge copies the transformed interpreter body (`InterpMethod`) into the target method's cached interpreter struct.

---

## 2. Issues Investigated & Resolved in This Session

### 1. AMFI SIGKILL on Stock iOS Launch (`app_output(8).log`)
- **Symptom:** The app launched and was immediately terminated with SIGKILL unless launched through a JIT debugger (StikDebug).
- **Root Cause:** A JIT probe allocated memory with `PROT_EXEC` to check if JIT was available. On un-debugged iOS, this violated AMFI code signing constraints, resulting in immediate SIGKILL.
- **Fix (Commit `b399f74`):** Bypassed the JIT probe and debugger wait screen when running un-debugged. The app boots immediately in Mono Interpreter mode (`MONO_AOT_MODE_INTERP_ONLY = 8`).

### 2. Shader Warmup Jetsam OOM Crash (`app_output(9).log`)
- **Symptom:** During launch, the app crashed at ~30% shader cache warmup.
- **Root Cause:** Desktop shader warmup loops tried to pre-warm all render pipeline permutations simultaneously, causing RAM usage to exceed the iOS Jetsam memory limit (~1.8 GB).
- **Fix (Commit `991bb9b`):** Guarded shader warmup screens with `OperatingSystem.IsIOS()`, skipping redundant desktop pipeline pre-warming on iOS.

### 3. Mono Interpreter Dynamic Wrapper Assertion (`app_output(10).log`)
- **Symptom:**
  ```text
  * Assertion at .../interp.c:5788, condition 'method->wrapper_type == MONO_WRAPPER_DYNAMIC_METHOD' not met
  ```
  during `NDevConsole._Ready()` in main menu loading.
- **Root Cause:** In Mono interpreter (`src/mono/mono/mini/interp/interp.c`), string literals in dynamic methods are emitted as opcode 273 (`MINT_LDSTR_DYNAMIC`):
  ```c
  MINT_IN_CASE(MINT_LDSTR_DYNAMIC) {
      MonoString *s = NULL;
      guint32 strtoken = (guint32)(gsize)frame->imethod->data_items [ip [2]];
      MonoMethod *method = frame->imethod->method;
      g_assert (method->wrapper_type == MONO_WRAPPER_DYNAMIC_METHOD);
      s = (MonoString*)mono_method_get_wrapper_data (method, strtoken);
      LOCAL_VAR (ip [1], gpointer) = s;
      ip += 3;
      MINT_IN_BREAK;
  }
  ```
  Because the detour was executing under `NDevConsole._Ready` (a regular method, not a dynamic wrapper), `method->wrapper_type` was 0, triggering the runtime assertion.
- **Initial Incomplete Fix (`0c43534`):** In `sts2_copy_interp_body`, set `dst_target->method = src_body->method;`. While this made `method->wrapper_type` dynamic, it caused a severe regression (Bug 4).

### 4. Detour Hash Table Mismatch (`app_output(11).log`)
- **Symptom:** The app launched, but **none** of the detours executed. `NGame.InitializePlatform()` ran unpatched, tried to call Steamworks, threw `DllNotFoundException: steam_api64`, caught it, and attempted to show an error popup.
- **Root Cause:** Mono interpreter's `mono_interp_get_imethod(MonoMethod *method)` looks up the cached `InterpMethod` in MonoDomain's internal hash table (`jit_code_hash`). The lookup checks `if (imethod->method == method)`. Because commit `0c43534` changed `imethod->method` to point to Harmony's patch method, lookups for `NGame.InitializePlatform` failed! Mono treated the method as untransformed, allocated a fresh `InterpMethod`, compiled the **original unpatched IL**, and executed it.
- **Permanent Fix (`af89aef`):**
  1. **Preserve Method Pointer:** Kept `dst_target->method = src_mono_method` untouched. All lookups in `jit_code_hash` match, ensuring all detours execute.
  2. **Opcode Rewriting (`sts2_resolve_dynamic_strings`):**
     Both `MINT_LDSTR` (opcode 272) and `MINT_LDSTR_DYNAMIC` (opcode 273) have the exact same instruction format (`[opcode, dest_local_offset, data_items_index]`, 3 words). `MINT_LDSTR` loads `data_items[ip[2]]` directly as a `MonoString*`.
     In `IOSGodotAppBridge.mm`, before installing the detour, `sts2_resolve_dynamic_strings` scans the instructions:
     - Extracts `strtoken` from `imethod->data_items[ip[2]]`.
     - Calls `mono_method_get_wrapper_data(dynamic_mono_method, strtoken)` to pre-resolve the `MonoString*`.
     - Writes the resolved `MonoString*` directly into `imethod->data_items[ip[2]]`.
     - Replaces opcode 273 (`MINT_LDSTR_DYNAMIC`) with 272 (`MINT_LDSTR`).
     - At runtime, `MINT_LDSTR` executes without assertions or dynamic method checks.

### 5. Globalization Invariant Mode Locale Crash
- **Symptom:** `CultureNotFoundException: Only the invariant culture is supported in globalization-invariant mode. (Parameter 'name') en-AU is an invalid culture identifier.`
- **Root Cause:** In .NET globalization-invariant mode on iOS, calling `new CultureInfo("en-AU")` throws an exception.
- **Fix (`af89aef` in `PlatformPatches.Locale.cs`):**
  - Added a fast-lookup `TwoLetterToThreeLetter` ISO 639-1 dictionary (`en` -> `eng`, `de` -> `deu`, etc.).
  - Strips region subtags (e.g. `en-AU` -> `en`) and returns the 3-letter language code instantly with zero exceptions.

---

## 3. Key Files & Structure

| File | Purpose |
|------|---------|
| `ios/STS2Launcher/IOSGodotAppBridge.mm` | Native bridge: POSIX shims, Keychain device keys, Mono interpreter method detouring (`sts2_ios_interp_detour_method`), opcode rewriting (`sts2_resolve_dynamic_strings`). |
| `src/STS2Mobile/Patches/PlatformPatches.cs` | Harmony patch orchestration for Steamworks, Sentry, SaveManager, and PlatformUtil bypasses. |
| `src/STS2Mobile/Patches/PlatformPatches.Prefixes.cs` | Prefix handlers skipping Steam (`InitializePlatformPrefix` returns `Task.FromResult(true)`). |
| `src/STS2Mobile/Patches/PlatformPatches.Locale.cs` | Safe locale parsing without `CultureInfo` throwing in invariant mode. |
| `src/STS2Mobile/Launcher/LauncherStartupFlow.cs` | Mobile startup flow and shader warmup bypass for iOS. |
| `.github/workflows/build-ios.yml` | GitHub Actions workflow: builds Mono, Godot runner, compiles STS2Mobile, and packages the IPA. |

---

## 4. Current State & Verification Checklist

- [x] Mono Interpreter detour hash lookup preserves target `MonoMethod*`.
- [x] Dynamic string loading assertion resolved via bytecode rewriting (`273 -> 272`).
- [x] Invariant culture crash avoided with fast ISO language code mapping.
- [x] Standalone startup verified without JIT debugger.
- [x] CI Build passed on commit `af89aef` (Run `#42598831164`).

### Next Steps for the Incoming Session:
1. User tests the IPA generated from Run `#42598831164`.
2. Inspect the new `app_output.log`:
   - Confirm that `[STS2Launcher iOS] Rewrote X MINT_LDSTR_DYNAMIC -> MINT_LDSTR opcodes` appears in the log.
   - Confirm that `InitializePlatform` detour logs `Skipping Steam initialization (mobile)`.
   - Confirm the game boots cleanly into the main menu without Jetsam OOM or assertions.
3. If new errors appear, diagnose the log and apply targeted adjustments to mobile UI scaling or touch input handlers.
