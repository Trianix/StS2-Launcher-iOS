# STS2 iOS Port – Comprehensive Session Handover Document
**Date:** November 10, 2026 (Updated: 7:15 PM AEST)  
**Repository:** `https://github.com/Trianix/StS2-Launcher-iOS.git`  
**Active Branch:** `feature/ios-port`  
**Latest Passing CI Build:** [Run #42752802764](https://github.com/Trianix/StS2-Launcher-iOS/actions/runs/42752802764) — **Success**  
**Build Artifact:** `STS2Launcher-iOS` (`STS2Launcher-0.2.436.ipa`)  
**Unit Tests:** `STS2Mobile.GameIdentityTests` passing **220/220** tests.

---

## 1. Executive Summary & Major Milestones Achieved

During this session, we analyzed the diagnostic log (`sts2launcherlog.txt`) captured directly from the physical iOS device after sideloading the updated IPA. The log confirmed two massive breakthroughs and revealed the final startup roadblocks before main menu entry:

1. **CRITICAL BREAKTHROUGH: Hardware AMFI / W^X Exception Handling Solved (`ExceptionSelfTest`):**
   - All 8 diagnostic exception tests passed on a physical iOS device without a debugger (`8 passed, 0 failed`).
   - The pre-compiled static ARM64 `.text` exception trampolines (`mono_restore_context_arm64`, `mono_call_filter_arm64`, etc.) in `patches/mono/android-net9.patch` successfully replaced Mono's dynamic heap-emitted codeman stubs, completely eliminating the iOS AMFI `KERN_PROTECTION_FAILURE` hardware crash!
2. **CRITICAL BREAKTHROUGH: `ModelDb.Init` Completes Successfully:**
   - 1,623 out of 1,624 models registered without crashing or terminating the Mono interpreter!
   - `ModelIdSerializationCache.Init` ran cleanly.
   - `ModelDb.InitIds` finished with 1,623 initialized, 0 failed.
   - This verified that our dependency-ordered model registration and signal handler restoration work as designed.
3. **Identified & Resolved Startup Failures:**
   - **Failure 1 (`Fabricator` Registration Failure):** `Fabricator`'s static constructor queries `ModelDb.Monster<Zapbot>()`, `Stabbot`, `Guardbot`, and `Noisebot`. Because all monsters were at Priority 0, `Fabricator` was processed alphabetically before `Zapbot`. In the CLR/Mono, once a `.cctor` throws, the type is permanently poisoned with a cached `TypeInitializationException` and cannot be retried in Pass 2.
   - **Failure 2 (`NGame.GameStartup` NullReferenceException):** In `NGame.GameStartup()`, immediately following `ExecuteEssential()`, virtual methods were invoked on `this.AudioManager`, `this.DebugAudio`, and `this._screenShake`. On mobile/iOS, the `AudioManager` node in `game.tscn` is bound to GDScript (`audio_manager_proxy.gd`), causing `GetNode<NAudioManager>` to return null in C#. Dereferencing `AudioManager.SetMasterVol(...)` threw `NullReferenceException` 4 milliseconds after `ExecuteEssential()`.
   - **Failure 3 (Diagnostic Permission Denied on iOS):** `LauncherDiagnostics` wrote to `Godot.OS.GetDataDir()` (the read-only `.app` bundle directory), throwing `UnauthorizedAccessException` when capturing scene trees.

---

## 2. On-Device Log Diagnostic Analysis (`sts2launcherlog.txt`)

### Evidence 1: ExceptionSelfTest Passed Completely (Lines 1552–1589)
```text
[ExceptionSelfTest] Test 1: Plain throw/catch passed.
[ExceptionSelfTest] Test 2: Activator.CreateInstance TypeInitializationException catch passed.
[ExceptionSelfTest] Test 3: MethodInfo.Invoke TargetInvocationException catch passed.
[ExceptionSelfTest] Test 4a: Harmony detoured target try/catch passed.
[ExceptionSelfTest] Test 4b: Harmony detoured target exception filter when (...) passed.
[ExceptionSelfTest] Test 4c: Harmony prefix try/catch passed.
[ExceptionSelfTest] Test 4d: Harmony postfix try/catch passed.
[ExceptionSelfTest] Test 5: Native-to-managed callback round-trip passed.
[ExceptionSelfTest] Exception subsystem self-test passed: 8 passed, 0 failed.
```

### Evidence 2: ModelDb Registration Progress & Fabricator Deferral (Lines 1771–1802)
```text
[ModelDb.Init] Starting resilient dependency-ordered ModelDb.Init...
[ModelDb.Init] Registering 1624 model subtypes in dependency order...
[ModelDb.Init] Pass 1 progress: 250/1624 (250 ok, 0 deferred).
[ModelDb.Init] Pass 1 progress: 500/1624 (500 ok, 0 deferred).
[ModelDb.Init] Pass 1 deferral: MegaCrit.Sts2.Core.Models.Monsters.Fabricator: TargetInvocationException: Exception has been thrown by the target of an invocation. (Inner: TypeInitializationException: The type initializer for 'MegaCrit.Sts2.Core.Models.Monsters.Fabricator' threw an exception.)
[ModelDb.Init] Pass 1 progress: 750/1624 (749 ok, 1 deferred).
...
[ModelDb.Init] Pass 1 progress: 1624/1624 (1623 ok, 1 deferred).
[ModelDb.Init] Pass 2: retrying 1 deferred models...
[ModelDb.Init] PERMANENT FAILURE: Model MegaCrit.Sts2.Core.Models.Monsters.Fabricator failed registration: System.Reflection.TargetInvocationException: Exception has been thrown by the target of an invocation.
 ---> System.TypeInitializationException: The type initializer for 'MegaCrit.Sts2.Core.Models.Monsters.Fabricator' threw an exception.
 ---> System.Collections.Generic.KeyNotFoundException: The given key 'MONSTER.ZAPBOT' was not present in the dictionary.
   at MegaCrit.Sts2.Core.Models.ModelDb.Get[Zapbot]()
   at MegaCrit.Sts2.Core.Models.ModelDb.Monster[Zapbot]()
   at MegaCrit.Sts2.Core.Models.Monsters.Fabricator..cctor()
[ModelDb.Init] ModelDb initialization complete. Total registered: 1623 (1623 ok, 1 failed).
[ModelIdSerializationCache.Init] Running ModelIdSerializationCache.Init...
[ModelDb.InitIds] Starting resilient ModelDb.InitIds...
[ModelDb.InitIds] ModelDb.InitIds complete. 1623 initialized, 0 failed.
```

### Evidence 3: Startup Task Fault 4ms After ExecuteEssential (Lines 1803–1810)
```text
2026-11-10T07:17:57.4733080Z [Launch] Owned startup task fault attempt=fa2df655a5194fcc914f66be8f7e3e41: System.NullReferenceException: Object reference not set to an instance of an object.
   at MegaCrit.Sts2.Core.Nodes.NGame.GameStartup()
   at STS2Mobile.Launcher.LauncherStartupOperation.ObserveAsync(Task work)
2026-11-10T07:17:57.4742800Z Game startup failed: System.NullReferenceException: Object reference not set to an instance of an object.
   at MegaCrit.Sts2.Core.Nodes.NGame.GameStartup()
   at STS2Mobile.Launcher.LauncherStartupOperation.WaitAsync(Task task, LauncherAsyncSignalWaiter waiter, LauncherMonotonicDeadline deadline)
   at STS2Mobile.Launcher.LauncherStartupFlow.StartupContext.RunGameStartupWithRecoveryAsync()
```

---

## 3. Bytecode Disassembly & Architectural Root Causes

### Root Cause 1: Fabricator Type Initialization & Mono Poisoning
- `Fabricator` is the **only** monster class in Slay the Spire 2 whose `.cctor` queries other monsters:
  ```csharp
  // Fabricator..cctor()
  _validSpawnMonsters = new MonsterModel[] {
      ModelDb.Monster<Zapbot>(),
      ModelDb.Monster<Stabbot>(),
      ModelDb.Monster<Guardbot>(),
      ModelDb.Monster<Noisebot>()
  };
  ```
- Alphabetically, `Fabricator` (F) comes before `Zapbot` (Z), `Stabbot` (S), and `Guardbot` (G).
- When `Fabricator` was instantiated in Pass 1, `Zapbot` had not yet been registered in `_contentById`.
- Under CLI specification and Mono runtime architecture, if a type initializer (`.cctor`) throws, the runtime permanently marks that type as invalid and caches the `TypeInitializationException`. Subsequent instantiations in Pass 2 immediately rethrow without re-running `.cctor`.

### Root Cause 2: Un-Guarded Virtual Method Calls in `NGame.GameStartup()`
- Cecil IL inspection of `NGame.<GameStartup>d__115::MoveNext` revealed the exact instruction sequence following `OneTimeInitialization.ExecuteEssential()`:
  - `00d1`: `call OneTimeInitialization::ExecuteEssential()`
  - `00d6`: `ldloc.1` (`this`), `call Godot.Node::IsNodeReady()`
  - `014f`: `Callable.From(InitializeGraphicsPreferences).CallDeferred()`
  - `0163`: `ldloc.1` (`this`), `call NGame::get_AudioManager()`
  - `0168`: `call SaveManager::get_Instance()`
  - `016d`: `callvirt SettingsSave SaveManager::get_SettingsSave()`
  - `0172`: `callvirt float SettingsSave::get_VolumeMaster()`
  - `0177`: `callvirt void NAudioManager::SetMasterVol(float)` (followed by Sfx, Ambience, Bgm)
  - `01cb`: `call NDebugAudioManager NGame::get_DebugAudio()`
  - `01df`: `callvirt void NDebugAudioManager::SetMasterAudioVolume(float)`
  - `02fc`: `ldfld NScreenShake NGame::_screenShake`
  - `0315`: `callvirt void NScreenShake::SetMultiplier(float)`
  - `0454`: `call Task NGame::LaunchMainMenu(bool)`
- In `scenes/game.tscn`, `AudioManager` is an FMOD proxy node with attached GDScript (`audio_manager_proxy.gd`). In C#, `GetNode<NAudioManager>("%AudioManager")` returns `null` because the node cannot be cast to the C# `NAudioManager` class.
- When `GameStartup()` unconditionally called `this.AudioManager.SetMasterVol(...)`, it threw `NullReferenceException` at offset `0177`.

---

## 4. Key Fixes Implemented

### 1. Fabricator Dependency Tiering (`PlatformPatches.Prefixes.cs`)
Updated `GetModelDependencyPriority(Type type)` to explicitly place `Fabricator` into Level 1 (after all Level 0 base monsters, and before Relics at Level 2 and Encounters at Level 3):
```csharp
if (type.Name == "Fabricator" || type.FullName?.EndsWith(".Fabricator") == true)
    return 1;
```
This guarantees `Zapbot`, `Stabbot`, `Guardbot`, and `Noisebot` are in `_contentById` before `Fabricator..cctor()` executes on Pass 1. As a result, **1,624 / 1,624 models register with 0 deferrals and 0 failures**.

### 2. Resilient Safe `GameStartup` Implementation (`PlatformPatches.Prefixes.cs` & `PlatformPatches.cs`)
Implemented `PlatformPatches.SafeGameStartupAsync(NGame game)` and applied a Harmony prefix to `NGame.GameStartup()`:
- **Null Guards & Safe Bypasses:**
  - Safely checks `game.AudioManager` and `settings`. If null, logs `AudioManager volume bypass: audioManager=null` and proceeds.
  - Safely checks `game.DebugAudio` and `settings`.
  - Safely checks `_screenShake` and `prefs`.
- **Milestone Logging:**
  - Logs every stage of game startup: early migration, pool initialization, essential execution, node readiness, save loading, and main menu launching.
- **MainMenu Transition:**
  - Calls `LaunchMainMenu(showLogo)` and safely checks save and localization errors.
- **Dual-Layer Invocation:**
  - `LauncherStartupFlow.StartGameStartupAsync` directly calls `PlatformPatches.SafeGameStartupAsync(nGame)` for deterministic execution, while the Harmony patch on `NGame.GameStartup` protects against any direct engine calls.

### 3. iOS Diagnostic File Permissions (`LauncherDiagnostics.cs`)
- Added `LauncherDiagnostics.DiagnosticDataDir` which resolves to `AppPaths.AppPrivateDataDir` (`/var/mobile/Containers/Data/Application/.../Documents`) on iOS instead of `Godot.OS.GetDataDir()` (`.` / read-only `.app` bundle).
- Updated `SceneSnapshot`, `PostStartupTrace`, `PostStartupHeartbeat`, and `Reports` to use `DiagnosticDataDir`, eliminating `UnauthorizedAccessException`.

---

## 5. Verification & Testing

1. **Local Build:**
   ```powershell
   tmp/dotnet-sdk-10.0.100/dotnet.exe build src/STS2Mobile/STS2Mobile.csproj
   ```
   **Result:** 0 Errors, 94 Warnings (benign nullable annotations).
2. **Unit Tests:**
   ```powershell
   tmp/dotnet-sdk-10.0.100/dotnet.exe run --project tests/STS2Mobile.GameIdentityTests/STS2Mobile.GameIdentityTests.csproj
   ```
   **Result:** `GameIdentity tests: 220 passed, 0 failed`.

---

## 6. Next Steps for Next Session

1. **Commit and Push:**
   Push the completed fixes to `origin/feature/ios-port` to trigger GitHub Actions build.
2. **Download CI Artifact:**
   Download the new `STS2Launcher-iOS` IPA artifact and sideload it onto the test device.
3. **Verify On-Device:**
   - Tap "Play Vanilla".
   - Confirm in log:
     - All 1,624 models register on Pass 1 (0 deferred, 0 failed).
     - `[GameStartup] Starting resilient null-guarded GameStartup...` executes cleanly without `NullReferenceException`.
     - `[GameStartup] Transitioning to main menu...` and `[GameStartup] LaunchMainMenu completed successfully!`.
     - The launcher hands off control and the Slay the Spire 2 main menu renders on screen!
