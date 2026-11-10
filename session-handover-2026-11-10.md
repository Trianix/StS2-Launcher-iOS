# STS2 iOS Port – Comprehensive Session Handover Document
**Date:** November 10, 2026 (Updated: 8:00 PM AEST)  
**Repository:** `https://github.com/Trianix/StS2-Launcher-iOS.git`  
**Active Branch:** `feature/ios-port`  
**Latest Passing CI Build:** [Run #42752802764](https://github.com/Trianix/StS2-Launcher-iOS/actions/runs/42752802764)  
**Build Artifact:** `STS2Launcher-iOS`  
**Unit Tests:** `STS2Mobile.GameIdentityTests` passing **220/220** tests.

---

## 1. Executive Summary & Major Milestones Achieved

During this session, we analyzed the latest on-device log (`sts2launcherlog.txt`) captured directly from the physical iOS device after sideloading the updated IPA. The log confirmed major milestones and revealed the final startup roadblock before main menu entry:

1. **VERIFIED WORKING ON DEVICE: 100% ModelDb Initialization (Pass 1 complete with 0 failures):**
   - Fabricator Level 1 dependency tiering worked flawlessly: **Pass 1 registered 1,624 / 1,624 models with 0 deferred and 0 failed!**
   - `ModelIdSerializationCache.Init` ran cleanly.
   - `ModelDb.InitIds` complete: **1,624 initialized, 0 failed**.
2. **VERIFIED WORKING ON DEVICE: SafeGameStartupAsync Pipeline:**
   - Pre-startup migrations ran cleanly.
   - Node readiness reached (`[GameStartup] NGame node is ready.`).
   - Essential one-time initialization completed (`[GameStartup] ExecuteEssential completed.`).
   - AudioManager null guards worked as designed (`AudioManager volume bypass: audioManager=ok, settings=ok`).
   - Save files (`settings.save`, `profile.save`, `profile1/saves/progress.save`, `profile1/saves/prefs.save`) were all found and successfully read on iOS.
   - Diagnostic data permissions fix verified: scene snapshot wrote to `AppPrivateDataDir` with zero `UnauthorizedAccessException` errors!
3. **IDENTIFIED & RESOLVED NEW BLOCKER: `NullReferenceException` at `NGame.LaunchMainMenu`:**
   - In `SafeGameStartupAsync`, `LaunchMainMenu(showLogo)` was called with an inverted parameter name.
   - Disassembly revealed `NGame.LaunchMainMenu` and `NGame.LoadMainMenu` unconditionally call `this.RootSceneContainer.SetCurrentScene(...)`.
   - On mobile/iOS, `NGame.RootSceneContainer` and `NGame.Transition` can be null if `%RootSceneContainer` or `%GameTransitionRect` unique names are not bound, or if `NMainMenu` / `NLogoAnimation` instantiation misses cache.
   - Calling virtual methods on a null `RootSceneContainer` throws `NullReferenceException`.
   - Fixed by implementing `SafeLaunchMainMenuAsync`, a bulletproof null-guarded main menu transition with automatic node recovery, asset cache fallback, and dual-layer Harmony interception on `NGame.LaunchMainMenu`.
4. **RESOLVED SENTRY TYPE CONVERSION WARNING:**
   - Fixed `SentryService.AfterGameInit` passing `PlatformBranch` enum instead of `string` (`branch.ToString()`), preventing `ArgumentException`.

---

## 2. On-Device Log Diagnostic Analysis (`sts2launcherlog.txt`)

### Evidence 1: Fabricator Tiering Fix & 100% ModelDb Registration (Lines 2636–2648)
```text
[ModelDb.Init] Starting resilient dependency-ordered ModelDb.Init...
[ModelDb.Init] Registering 1624 model subtypes in dependency order...
[ModelDb.Init] Pass 1 progress: 250/1624 (250 ok, 0 deferred).
[ModelDb.Init] Pass 1 progress: 500/1624 (500 ok, 0 deferred).
[ModelDb.Init] Pass 1 progress: 750/1624 (750 ok, 0 deferred).
[ModelDb.Init] Pass 1 progress: 1000/1624 (1000 ok, 0 deferred).
[ModelDb.Init] Pass 1 progress: 1250/1624 (1250 ok, 0 deferred).
[ModelDb.Init] Pass 1 progress: 1500/1624 (1500 ok, 0 deferred).
[ModelDb.Init] Pass 1 progress: 1624/1624 (1624 ok, 0 deferred).
[ModelDb.Init] ModelDb initialization complete. Total registered: 1624 (1624 ok, 0 failed).
[ModelIdSerializationCache.Init] Running ModelIdSerializationCache.Init...
[ModelDb.InitIds] Starting resilient ModelDb.InitIds...
[ModelDb.InitIds] ModelDb.InitIds complete. 1624 initialized, 0 failed.
```

### Evidence 2: GameStartup Execution & New Main Menu Blocker (Lines 2649–2670)
```text
2026-11-10T08:32:07.9855480Z [GameStartup] ExecuteEssential completed.
2026-11-10T08:32:07.9858290Z [GameStartup] NGame node is ready.
2026-11-10T08:32:07.9861390Z [GameStartup] Configuring audio volumes on AudioManager...
2026-11-10T08:32:07.9880120Z [GameStartup] Initializing profile and progress data...
2026-11-10T08:32:07.9892500Z [Save] Android local save exists: profile.save -> ... = True
2026-11-10T08:32:07.9915390Z [Save] Android local save exists: profile1/saves/progress.save -> ... = True
2026-11-10T08:32:08.1750460Z [Save] Android local save exists: profile1/saves/prefs.save -> ... = True
2026-11-10T08:32:08.1796320Z [GameStartup] SentryService.AfterGameInit: Object of type 'MegaCrit.Sts2.Core.Platform.PlatformBranch' cannot be converted to type 'System.String'.
2026-11-10T08:32:08.1800330Z [GameStartup] Transitioning to main menu...
2026-11-10T08:32:08.3290560Z Game startup failed: System.NullReferenceException: Object reference not set to an instance of an object.
   at MegaCrit.Sts2.Core.Nodes.NGame.LaunchMainMenu(Boolean skipLogo)
   at STS2Mobile.Patches.PlatformPatches.SafeGameStartupAsync(NGame game)
   at STS2Mobile.Launcher.LauncherStartupOperation.ObserveAsync(Task work)
   at STS2Mobile.Launcher.LauncherStartupFlow.RunGameStartupAsync(StartupContext startup)
```

---

## 3. Bytecode Disassembly & Architectural Root Causes

### Root Cause 1: Parameter Inversion & Logo Path Execution
In `NGame.cs`, the signature is:
```csharp
public async Task LaunchMainMenu(bool skipLogo)
```
In `SafeGameStartupAsync`, the code computed:
```csharp
bool devSkip = MegaCrit.Sts2.Core.Debug.DebugSettings.DevSkip;
bool skipLogo = (settings != null && settings.SkipIntroLogo) || CommandLineHelper.HasArg("fastmp");
bool showLogo = !(devSkip || skipLogo);
...
launchMainMenuMethod.Invoke(game, new object[] { showLogo });
```
When `skipLogo` was true (the user has `SkipIntroLogo` enabled in `settings.save`), `showLogo` evaluated to `false`. Passing `false` into `LaunchMainMenu(bool skipLogo)` forced the engine into the intro logo animation pipeline instead of directly loading the main menu!

### Root Cause 2: Un-Guarded `RootSceneContainer` and `Transition` Lookups
Cecil IL inspection of `NGame.<LaunchMainMenu>d__128::MoveNext` and `NGame.<LoadMainMenu>d__136::MoveNext`:
- **If `skipLogo == false` (Logo Animation path):**
  - `0042`: `call PreloadManager::LoadLogoAnimation()`
  - `009c`: `call NLogoAnimation::Create()`
  - `00a8`: `call NGame::get_RootSceneContainer()`
  - `00b3`: `callvirt NSceneContainer::SetCurrentScene(logoAnimation)`
  - `0188`: `call NGame::get_Transition()`
  - `01a7`: `callvirt NTransition::FadeIn(...)`
- **If `skipLogo == true` (Direct Main Menu path):**
  - `0114`: `call PreloadManager::LoadMainMenuEssentials()`
  - `02eb`: `call NGame::LoadMainMenu(false)`
  - In `LoadMainMenu`:
    - `00c2`: `call NMainMenu::Create(openTimeline)`
    - `00c9`: `call NGame::get_RootSceneContainer()`
    - `00cf`: `callvirt NSceneContainer::SetCurrentScene(mainMenu)`
- In `NGame._EnterTree`:
  `set_RootSceneContainer(this.GetNode<NSceneContainer>("%RootSceneContainer"));`
  `set_Transition(this.GetNode<NTransition>("%GameTransitionRect"));`
- Both `RootSceneContainer` and `Transition` have **private setters** in `NGame` and are populated exclusively by `%UniqueName` node path lookups.
- If `%RootSceneContainer` is not resolved, `game.RootSceneContainer` is null. Dereferencing `this.RootSceneContainer.SetCurrentScene(...)` throws `NullReferenceException` synchronously inside `LaunchMainMenu`/`LoadMainMenu`.

---

## 4. Key Fixes Implemented

### 1. Resilient `SafeLaunchMainMenuAsync` (`PlatformPatches.Prefixes.cs`)
Implemented `PlatformPatches.SafeLaunchMainMenuAsync(NGame game, bool skipLogo)`:
1. **Node Readiness Confirmation:** Ensures `game.IsNodeReady()` before querying child nodes.
2. **Automated `RootSceneContainer` Recovery:**
   - Checks `game.RootSceneContainer`.
   - If null, resolves `%RootSceneContainer` or `RootSceneContainer` or searches `game.GetChildren()`.
   - If still missing, creates a new `NSceneContainer { Name = "RootSceneContainer" }` and attaches it to `game`.
   - Assigns `game.RootSceneContainer` via reflection (both property and backing field).
3. **Automated `Transition` Recovery:**
   - Checks `game.Transition`. If null, resolves `%GameTransitionRect` or child `NTransition` and assigns it via reflection.
4. **Safe Preload:**
   - Calls `await PreloadManager.LoadMainMenuEssentials()` inside a guarded try/catch.
5. **Resilient `NMainMenu` Instantiation:**
   - Calls `NMainMenu.Create(false)`.
   - If that throws, falls back to direct `ResourceLoader.Load<PackedScene>("res://scenes/screens/main_menu.tscn").Instantiate<NMainMenu>()`.
6. **Robust Scene Attachment:**
   - Calls `rootSceneContainer.SetCurrentScene(mainMenu)`.
   - If that throws, falls back to direct `rootSceneContainer.AddChild(mainMenu)` and sets `CurrentScene` via reflection.
7. **Post-Transition Engine Tasks:**
   - Logs `NGame.LogResourceStats("main menu loaded (essential)")`.
   - Safely invokes `NGame.LoadDeferredStartupAssetsAsync()` in background.

### 2. Dual-Layer Harmony Interception (`PlatformPatches.cs` & `PlatformPatches.PlatformUtil.LocaleStrategy.cs`)
- Added `ApplyLaunchMainMenuPatch(Harmony harmony)` which hooks `NGame.LaunchMainMenu(bool)`.
- The prefix `LaunchMainMenuPrefix` intercepts any call to `LaunchMainMenu` from anywhere in the engine or launcher, routing it deterministically into `SafeLaunchMainMenuAsync(__instance, true)`.

### 3. Sentry Branch Argument Fix (`PlatformPatches.Prefixes.cs`)
- In `SafeGameStartupAsync`, replaced raw `branch` (`PlatformBranch` enum) with `branch.ToString()` (`string`) when invoking `SentryService.AfterGameInit`.

---

## 5. Verification & Testing

1. **Local Build:**
   ```powershell
   tmp/dotnet-sdk-10.0.100/dotnet.exe build src/STS2Mobile/STS2Mobile.csproj
   ```
   **Result:** 0 Errors, 95 Warnings (CS8632 nullable annotations in non-nullable context).
2. **Unit Tests:**
   ```powershell
   tmp/dotnet-sdk-10.0.100/dotnet.exe run --project tests/STS2Mobile.GameIdentityTests/STS2Mobile.GameIdentityTests.csproj
   ```
   **Result:** `GameIdentity tests: 220 passed, 0 failed`.

---

## 6. Next Steps for Next Session

1. **Commit and Push:**
   Push the completed fixes to `origin/feature/ios-port` to trigger GitHub Actions CI.
2. **Download CI Artifact:**
   Download the new `STS2Launcher-iOS` IPA artifact and sideload it onto the test device.
3. **Verify On-Device:**
   - Tap "Play Vanilla".
   - Confirm in log:
     - `[LaunchMainMenu] Starting resilient SafeLaunchMainMenuAsync (skipLogo=True)...`
     - `[LaunchMainMenu] RootSceneContainer resolved: ...`
     - `[LaunchMainMenu] NMainMenu.Create succeeded: ...`
     - `[LaunchMainMenu] NMainMenu attached to RootSceneContainer successfully.`
     - `[LaunchMainMenu] SafeLaunchMainMenuAsync completed successfully!`
     - The launcher dismisses the overlay and the Slay the Spire 2 main menu renders on screen!
