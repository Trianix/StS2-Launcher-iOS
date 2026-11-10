# STS2 iOS Port – Comprehensive Session Handover Document
**Date:** November 10, 2026 (Updated: 3:45 PM AEST)  
**Repository:** `https://github.com/Trianix/StS2-Launcher-iOS.git`  
**Active Branch:** `feature/ios-port`  
**Latest Commit:** [`08da61b`](https://github.com/Trianix/StS2-Launcher-iOS/commit/08da61b84c8a2b5e2d67451e037bc8be68f7b587) (`fix(mono): provide static ARM64 exception routines, add startup exception self-tests, and cut back error-swallowing`)  
**Latest CI Build:** [Run #42752802764](https://github.com/Trianix/StS2-Launcher-iOS/actions/runs/42752802764) — **Success**  
**Build Artifact:** `STS2Launcher-iOS` (`STS2Launcher-0.2.436.ipa`)  

---

## 1. Executive Summary & Session Trajectory

During this session, we transitioned from early launcher boot issues to advancing game engine initialization deep into Slay the Spire 2's core systems. Through four successive device test cycles (`app_output(12).log` through `app_output(16).log`), we identified and resolved cascading issues across iOS runtime limitations, .NET globalization invariance, Mono interpreter quirks, and game model dependency order:

1. **AutoLaunch In-Process Transition (`app_output(12).log`):**  
   Fixed `InvalidOperationException` where Android-specific restart restoration was erroneously invoked on iOS.
2. **Globalization-Invariant Mode Crash (`app_output(12).log`):**  
   Redirected all `CultureInfo.GetCultureInfo` calls to `CultureInfo.InvariantCulture` to prevent `CultureNotFoundException` on iOS.
3. **Missing `System.Runtime.CompilerServices.Unsafe` Facade (`app_output(13).log`):**  
   Bundled `Unsafe.dll` (v4.0.4.1) for `SmartFormat.ZString.dll` into the IPA packaging script (`build-ios-local.sh`).
4. **Mono Interpreter Dynamic Method Filter Failure (`app_output(14).log`):**  
   Mono pure interpreter mode (`MONO_AOT_MODE_INTERP_ONLY = 8`) does not support IL exception filters (`MINT_MONO_FILTER`). Removed the Harmony detour on `SmartFormat` and replaced it with a resilient C# reflection formatter initializer (`LoadLocFormattersPrefix`).
5. **Silent Termination at `LocManager.Initialize()` (`app_output(15).log`):**  
   Patched `LocManager.Initialize` with safe directory creation, isolated `ModelDb.Init` and `ModelDb.InitIds`, and installed an alternate signal stack (`sigaltstack`) in `AppDelegate.mm`.
6. **Sudden Abort in `ModelDb.Init` after 500 Models (`app_output(16).log`):**  
   Identified that `BowlbugsNormal` (index 655) and other `EncounterModel`/`EventModel` subclasses execute `ModelDb.Monster<...>()` and `ModelDb.Relic<...>()` in static field initializers before those prerequisite types are loaded into `_contentById`. Reordered type loading into dependency tiers with a two-pass retry mechanism, skipped JIT prewarming (`PrewarmJit`), enabled stream `AutoFlush`, and installed native Objective-C/C++ crash hooks.

---

## 2. Deep Dive: The `ModelDb.Init` Crash (`app_output(16).log`)

### The Symptom
In `app_output(16).log` (PID 11954) and `app_output_prev(2).log` (PID 11951), the game advanced past all previous roadblocks:
- `OneTimeInitialization.ExecuteEssential` entered cleanly.
- `AtlasManager.LoadEssentialAtlases` loaded `ui_atlas` (102 sprites) and `compressed` (45 sprites).
- `LocManager.Initialize` completed successfully (`eng` locale loaded, formatters registered).
- `ModelDb.Init` began registering 1,624 subtypes:
  ```text
  2026-11-10 14:20:01.188 STS2Launcher[11954:530739] [STS2Mobile] [ModelDb.Init] Progress: 250/1624 instantiated (250 ok, 0 failed).
  2026-11-10 14:20:01.220 STS2Launcher[11954:530739] [STS2Mobile] [ModelDb.Init] Progress: 500/1624 instantiated (500 ok, 0 failed).
  ```
- The process abruptly died between models 500 and 750 without logging an error.

### Root Cause Analysis
1. **Source-Generated Array Order vs. Static Constructor Dependencies:**
   - The engine iterates through `AbstractModelSubtypes.All` (1,613 types generated in alphabetical namespace order):
     - `Achievements` (0..8)
     - `Acts` (9..13)
     - `Afflictions` (14..24)
     - `CardPools` (25..34)
     - `Cards` (35..639) — `Shroud` is at index 500
     - `Enchantments` (640..652)
     - **`Encounters` (653..747)** — `AxebotsNormal` at 653, `BowlbugsNormal` at 655, `CultistsNormal` at 665
     - **`Events` (748..816)** — `ColossalFlower` at 756, `Darv` at 758, `DollRoom` at 762, `FakeMerchant` at 766
     - **`Monsters` (840..965)** — `BowlbugEgg` at 842, `CalcifiedCultist` at 850
     - `Orbs`, `Potions` (1000+), `Powers`, **`Relics` (1300+)**
2. **Circular Lookup During Static Field Initialization:**
   - Decompilation of `BowlbugsNormal` revealed:
     ```csharp
     public sealed class BowlbugsNormal : EncounterModel
     {
         private static readonly Dictionary<MonsterModel, int> _workerValidCounts = new Dictionary<MonsterModel, int>
         {
             { ModelDb.Monster<BowlbugEgg>(), 1 },
             { ModelDb.Monster<BowlbugSilk>(), 1 },
             { ModelDb.Monster<BowlbugNectar>(), 1 }
         };
     ...
     ```
   - When `Activator.CreateInstance(typeof(BowlbugsNormal))` is called at index 655, the CLR executes its static constructor (`.cctor`).
   - `ModelDb.Monster<BowlbugEgg>()` calls `(BowlbugEgg)_contentById[GetId<BowlbugEgg>()]`.
   - Because `BowlbugEgg` is at index 842, it is **not yet in `_contentById`**.
   - `_contentById[id]` throws `KeyNotFoundException`, which wraps into a `TypeInitializationException`.
   - The same circular dependency exists in `CultistsNormal` (`CalcifiedCultist`), `FlyconidNormal` (`LeafSlimeM`), `RubyRaidersNormal` (`AxeRubyRaider`), `SlimesWeak` (`LeafSlimeS`), `Darv` (`Astrolabe`, `BlackStar`), `DollRoom` (`DaughterOfTheWind`), and `FakeMerchant` (`FakeAnchor`).
3. **Mono Interpreter Abort on Static Initializer Exception:**
   - In pure Mono interpreter mode, unhandled exceptions inside `.cctor` during `Activator.CreateInstance` can lead to native interpreter assertion failures (`g_assert` / `mono_interp_exec_method`), abruptly aborting the process.
4. **Log Truncation from Missing Stream Flushing:**
   - In .NET, `Console.Error` is a buffered `TextWriter`. Without `AutoFlush = true` or explicit `Flush()`, entries between index 500 and index 655 sat in memory buffers and were lost upon process termination.
   - Mono runtime initialization had also replaced the initial crash signal handlers installed in `+load`.

---

## 3. Comprehensive Fixes Applied in Commit `2b18fe0`

### A. Dependency-Tiered Model Registration (`PlatformPatches.Prefixes.cs`)
Implemented `GetModelDependencyPriority(Type type)` to sort all subtypes before registration:
- **Tier 0 (Prerequisites / Zero Cross-Dependencies):** `MonsterModel`, `PotionModel`, `PowerModel`, `AfflictionModel`, `EnchantmentModel`, `CardModel`, `CardPoolModel`, `OrbModel`, `CharacterModel`, `ActModel`, `AchievementModel`.
- **Tier 1 (Relics):** `RelicModel` (e.g. `Cauldron`, which queries `PotionModel`).
- **Tier 2 (Encounters & Events):** `EncounterModel` and `EventModel` (which query `MonsterModel` and `RelicModel`).
- **Tier 3:** All remaining types.

### B. Two-Pass Resilient Loading
- **Pass 1:** Instantiates models in dependency order. Any model that throws is safely captured and added to a `deferred` list instead of halting execution.
- **Pass 2:** Retries deferred models once all other models are registered in `_contentById`. Any model that still fails is logged with its full `InnerException` and skipped so the game engine proceeds unaffected.

### C. JIT Prewarm Suppression on iOS
- Patched `OneTimeInitialization.PrewarmJit` to return `false` on iOS. In pure interpreter mode (`MONO_AOT_MODE_INTERP_ONLY = 8`), `RuntimeHelpers.PrepareMethod` has no JIT compiler to prewarm and risks emitting unmapped trampolines.

### D. Diagnostics & Crash Telemetry
- Enabled `AutoFlush` on `Console.Error` and `Console.Out` in `BootstrapTrace` static constructor, with explicit `Console.Error.Flush()` on every iOS log call.
- Re-installed native crash signal handlers (`sts2_reinstall_crash_handlers`) immediately before `ExecuteEssential` and `ModelDb.Init` so they remain active over Mono's runtime defaults.
- Added `NSSetUncaughtExceptionHandler` and `std::terminate` handlers in `AppDelegate.mm`.

---

## 4. Root Cause Deep Dive: Mono Exception Handling Routines on Stock iOS

### The Fundamental Obstacle
In pure Mono interpreter mode (`MONO_AOT_MODE_INTERP_ONLY = 8`), JIT compilation is disabled. However, by default Mono initializes its runtime exception-handling routines (`restore_context`, `call_filter`, `throw_exception`, `rethrow_exception`, `rethrow_preserve_exception`, `throw_corlib_exception`) by dynamically emitting ARM64 machine code into heap memory via `mono_global_codeman_reserve` during `mono_exceptions_init()`.

On stock iOS without an attached JIT debugger (no JIT entitlement, AMFI / W^X enforced):
- Executing code located in dynamically allocated heap memory triggers an immediate, uncatchable hardware termination (`EXC_BAD_ACCESS (KERN_PROTECTION_FAILURE)` / `SIGBUS` / AMFI SIGKILL).
- Whenever an exception unwound into non-interpreter code (`interp_throw` calling `mono_restore_context`), evaluated an exception filter (`call_filter`), or traversed native-to-interpreter boundaries, jumping to these codeman addresses abruptly terminated the process before managed catch blocks or signal handlers could run.

### The Solution (Commit `08da61b`)
1. **Pre-compiled Static ARM64 `.text` Trampolines (`patches/mono/android-net9.patch`):**
   Added static, position-independent assembly implementations in `src/mono/mono/mini/exceptions-arm64.c`:
   - `mono_restore_context_arm64`: Restores 32 SIMD `q0..q31` registers and integer registers directly from `MonoContext`, restores `sp`, and branches to `pc`.
   - `mono_call_filter_arm64`: Sets up stack frame, restores callee-saved registers from `MonoContext`, executes the filter at `handler_ip`, saves results, restores caller registers, and returns.
   - `mono_throw_exception_arm64`, `mono_rethrow_exception_arm64`, `mono_rethrow_preserve_exception_arm64`, `mono_throw_corlib_exception_arm64`: Construct standard frames and invoke `mono_arm_throw_exception`.
   - `mono_arch_get_*`: Directly return the static `.text` function pointers without reserving codeman heap memory.
2. **Startup Exception Diagnostic Self-Test Suite (`STS2Mobile.Diagnostics.ExceptionSelfTest`):**
   Runs automatically after patch orchestration:
   - **Test 1:** Plain throw/catch.
   - **Test 2:** `Activator.CreateInstance` on a type whose static constructor (`.cctor`) fails.
   - **Test 3:** `MethodInfo.Invoke` on a method that throws.
   - **Test 4:** Harmony-detoured methods with both plain `try/catch` and `when (...)` exception filters.
   - **Test 5:** Native-to-managed callback round-trip and internal exception handling via `sts2_test_native_callback_roundtrip`.
3. **Cut Back Error-Swallowing Prefixes (`PlatformPatches.Prefixes.cs`):**
   - In `ModelDb.Init` Pass 1: Immediately log any exception and inner exception details.
   - In `ModelDb.Init` Pass 2: Log full exception details on any permanent failure.
   - In `LocManager.Initialize`, `ModelDb.Init`, and `ModelDb.InitIds`: Rethrow unexpected errors rather than swallowing them.

---

## 5. Current Repository State

- **Branch:** `feature/ios-port` (clean working tree).
- **Core Files Modified:**
  - `patches/mono/android-net9.patch`: Pre-compiled static ARM64 exception routines in `.text`.
  - `src/STS2Mobile/Diagnostics/ExceptionSelfTest.cs`: Diagnostic self-test suite.
  - `src/STS2Mobile/ModEntry.Apply.cs`: Wired `ExceptionSelfTest.Run(harmony)`.
  - `src/STS2Mobile/Patches/PlatformPatches.Prefixes.cs`: Removed error-swallowing, added detailed Pass 1 logging.
  - `ios/STS2Launcher/IOSGodotAppBridge.h` & `.mm`: Added `sts2_test_native_callback_roundtrip`.
- **CI Workflow:** `.github/workflows/build-ios.yml` running on `macos-15`.
- **Unit Tests:** `STS2Mobile.GameIdentityTests` passing **220/220** tests.

---

## 6. Next Steps for Next Session

1. **Monitor CI Build:**
   Check GitHub Actions run for commit [`08da61b`](https://github.com/Trianix/StS2-Launcher-iOS/commit/08da61b84c8a2b5e2d67451e037bc8be68f7b587) (cache key invalidated for `android-net9.patch`, rebuilding Mono with static exception trampolines).
2. **Device Verification:**
   - Sideload the new IPA onto test device.
   - Observe `[ExceptionSelfTest]` output at startup to verify all 5 test categories pass.
   - Confirm `ModelDb.Init` completes registering all 1,624 models and transitions cleanly into `OneTimeInitialization.ExecuteDeferred()` and the main menu.
   - Because `AutoFlush` and native signal handlers are now fully armed, `app_output.log` will contain the exact failing line and/or full ARM64 register state and backtrace symbols.
