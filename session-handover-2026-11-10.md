# STS2 iOS Port – Comprehensive Session Handover Document
**Date:** November 10, 2026 (Updated: 8:40 PM AEST)  
**Repository:** `https://github.com/Trianix/StS2-Launcher-iOS.git`  
**Active Branch:** `feature/ios-port`  
**Latest Passing CI Build:** [Run #42752802764](https://github.com/Trianix/StS2-Launcher-iOS/actions/runs/42752802764)  
**Build Artifact:** `STS2Launcher-iOS`  
**Unit Tests:** `STS2Mobile.GameIdentityTests` passing **220/220** tests.

---

## 1. Executive Summary & Major Breakthrough

During this session, we analyzed the on-device log (`sts2launcherlog.txt`) from the test device running commit `9f179de`. The log provided conclusive proof of a **massive breakthrough**:

1. **THE GAME ENGINE & MAIN MENU ARE 100% OPERATIONAL ON DEVICE:**
   - `Fabricator` Level 1 dependency tiering: **Pass 1 registered 1,624 / 1,624 models with 0 deferred and 0 failed!**
   - `ModelDb.InitIds` finished with 1,624 initialized, 0 failed.
   - `ExecuteEssential` completed.
   - Saves (`settings.save`, `profile.save`, `profile1/saves/progress.save`, `profile1/saves/prefs.save`) loaded cleanly.
   - `SafeLaunchMainMenuAsync` executed without any `NullReferenceException`:
     - `game.RootSceneContainer` was resolved (`<Control#40533755414>`).
     - `PreloadManager.LoadMainMenuEssentials` completed.
     - `NMainMenu.Create` succeeded (`<Control#147773721112>`).
     - `NMainMenu` was attached to `RootSceneContainer` successfully.
     - `Main menu reached. Godot ticks: 10,547ms`.
     - `AtlasManager.LoadAllAtlases` skipped, `PrewarmJit` skipped on iOS.
     - `Resilient GameStartup finished successfully`.
2. **THE MAIN MENU IS FULLY MOUNTED IN THE SCENE TREE:**
   - The diagnostic snapshot captured the entire scene tree alive on iOS:
     ```text
     RootSceneContainer children=1 visible=True visibleInTree=True
       NMainMenu name=MainMenu children=13 visible=True visibleInTree=True
         NMainMenuBg name=MainMenuBg (SpineMesh2D, GpuParticles2D stars/clouds, Logo)
         MainMenuTextButtons (ContinueButton, AbandonRunButton, SingleplayerButton, MultiplayerButton, TimelineButton, SettingsButton, CompendiumButton, QuitButton)
         ChangeProfileButton, PatchNotesButton, PatchNotesScreen, Submenus, SettingsScreen...
     ```
3. **IDENTIFIED THE FINAL HANDOFF BLOCKER: 15-Second Timeout in `EnsureMainMenuReadyAsync`:**
   - 15 seconds after `SafeLaunchMainMenuAsync` finished, the launcher reported:
     ```text
     2026-11-10T09:09:23.6993650Z [Startup] Waiting for the game menu...
     2026-11-10T09:09:38.7076430Z Game startup failed: System.TimeoutException: Game loading stopped responding. Return to the launcher and retry in a fresh process.
        at STS2Mobile.Launcher.LauncherStartupOperation.WaitAsync(Task task, LauncherAsyncSignalWaiter waiter, LauncherMonotonicDeadline deadline)
        at STS2Mobile.Launcher.LauncherGameStartupRecovery.EnsureMainMenuReadyAsync(Object game, Node gameNode, Label startupStatus, String attemptId)
     ```
   - **Root Cause:**
     `EnsureMainMenuReadyAsync` calls `LauncherGameSceneReadiness.GetCurrentReadyScene(game, attemptId)`, which required `Binding.IsReady(game, attemptId, current)`.
     The binding relied exclusively on Harmony postfix detours on `NMainMenu..ctor()` and `NMainMenu._Ready()`. Under pure Mono interpreter mode (`interp.c`), Harmony detours on constructors and Godot C++ engine lifecycle callbacks do not reliably trigger.
     Because `SafeLaunchMainMenuAsync` instantiated `NMainMenu` directly, it did not explicitly register the scene with `LauncherGameSceneReadiness`.
     Additionally, `LauncherHandoffVisibility.Capture()` lacked an `OperatingSystem.IsIOS()` branch, relying on desktop-oriented `DisplayServer.WindowIsFocused()`.
   - **Fix Applied:**
     - Explicitly registered `LauncherGameSceneReadiness.BindScene(mainMenu)` and `LauncherGameSceneReadiness.RecordReady(mainMenu, game)` in `SafeLaunchMainMenuAsync`.
     - Added an automatic binding/readiness fallback in `LauncherGameSceneReadiness.GetCurrentReadyScene` whenever an active `NMainMenu` is detected in the tree.
     - Added `OperatingSystem.IsIOS()` in `LauncherHandoffVisibility.Capture()` ensuring mobile foreground/focus confirmation.
     - Added diagnostic trace logging to `EnsureMainMenuReadyAsync`.

---

## 2. On-Device Log Evidence (`sts2launcherlog.txt`)

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

### Evidence 2: Flawless Execution of `SafeLaunchMainMenuAsync` (Lines 2903–2920)
```text
2026-11-10T09:09:22.9701370Z [GameStartup] Transitioning to main menu via resilient SafeLaunchMainMenuAsync...
2026-11-10T09:09:22.9707250Z [LaunchMainMenu] Starting resilient SafeLaunchMainMenuAsync (skipLogo=True)...
2026-11-10T09:09:22.9709880Z [LaunchMainMenu] NGame node ready confirmed.
2026-11-10T09:09:22.9712470Z [LaunchMainMenu] game.RootSceneContainer is null, locating in scene tree...
2026-11-10T09:09:22.9716750Z [LaunchMainMenu] RootSceneContainer resolved: <Control#40533755414>
2026-11-10T09:09:22.9719330Z [LaunchMainMenu] Preloading MainMenuEssentials...
2026-11-10T09:09:23.3809750Z [LaunchMainMenu] PreloadManager.LoadMainMenuEssentials completed.
2026-11-10T09:09:23.3813310Z [LaunchMainMenu] Creating NMainMenu instance...
2026-11-10T09:09:23.3921520Z [LaunchMainMenu] NMainMenu.Create succeeded: <Control#147773721112>
2026-11-10T09:09:23.3924740Z [LaunchMainMenu] Attaching NMainMenu to RootSceneContainer...
2026-11-10T09:09:23.5331170Z [LaunchMainMenu] NMainMenu attached to RootSceneContainer successfully.
2026-11-10T09:09:23.5336150Z [LaunchMainMenu] Main menu reached. Godot ticks: 10,547ms
2026-11-10T09:09:23.5344990Z [LaunchMainMenu] Initiating LoadDeferredStartupAssetsAsync in background...
2026-11-10T09:09:23.6962550Z [LaunchMainMenu] SafeLaunchMainMenuAsync completed successfully!
2026-11-10T09:09:23.6990860Z [PostStartupEvidence] phase=NGame.GameStartup completed before main-menu guard mainMenu=True scene=MegaCrit.Sts2.Core.Nodes.Screens.MainMenu.NMainMenu name=MainMenu fullTrace=False
2026-11-10T09:09:23.6993650Z [Startup] Waiting for the game menu...
```

### Evidence 3: Live Main Menu Scene Tree Dump (Lines 968–1075)
```text
RootSceneContainer children=1 visible=True visibleInTree=True
  NMainMenu name=MainMenu children=13 visible=True visibleInTree=True
    NMainMenuBg name=MainMenuBg (SpineMesh2D, GpuParticles2D stars/clouds, Logo)
    MainMenuTextButtons (ContinueButton, AbandonRunButton, SingleplayerButton, MultiplayerButton, TimelineButton, SettingsButton, CompendiumButton, QuitButton)
    ChangeProfileButton, PatchNotesButton, PatchNotesScreen, Submenus, SettingsScreen...
```

---

## 3. Key Fixes Implemented in this Commit

### 1. Explicit Scene Readiness Registration (`PlatformPatches.Prefixes.cs`)
Immediately upon attaching `mainMenu` to `rootSceneContainer`, `SafeLaunchMainMenuAsync` now explicitly calls:
```csharp
LauncherGameSceneReadiness.BindScene(mainMenu);
LauncherGameSceneReadiness.RecordReady(mainMenu, game);
```
This guarantees the active startup attempt binds the scene instance without relying on Mono interpreter Harmony hooks.

### 2. Autonomous Scene Readiness Fallback (`LauncherGameSceneReadiness.cs`)
In `LauncherGameSceneReadiness.GetCurrentReadyScene`:
- Accepts an optional `game` parameter in `RecordReady(object scene, object game = null)` so readiness can be recorded directly.
- Inspects `container.CurrentScene`. If it is an `NMainMenu` inside the tree and node-ready, but not yet marked in `Binding.IsReady`, it automatically invokes `Binding.Bind(current)` and `Binding.MarkReady(current, game)`.

### 3. iOS Handoff Visibility Confirmation (`LauncherHandoffVisibility.cs`)
In `LauncherHandoffVisibility.Capture`:
- Added `OperatingSystem.IsIOS()` support returning `activityForeground: true`, `windowFocused: true`.
- Allows `PromoteWhenVisibleLocked` to immediately dismiss the launcher overlay (`_overlay.Dismiss()`) and transition to `LauncherHandoffState.GameVisible`.

### 4. Diagnostic Startup Recovery Logging (`LauncherGameStartupRecovery.cs`)
Added detailed logging to `EnsureMainMenuReadyAsync`:
- Logs confirmation of ready scene detection.
- Logs post-draw frame progression.
- Logs exact outcome of `TryConfirmRendered`.

---

## 4. Verification & Testing

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

## 5. Next Steps

1. **Commit and Push:**
   Push to `feature/ios-port` to trigger GitHub Actions CI build for the updated IPA.
2. **Install IPA on Test Device:**
   Download the new `STS2Launcher-iOS` artifact from GitHub Actions and sideload it.
3. **Launch and Verify:**
   - Tap "Play Vanilla".
   - Confirm in log:
     - `[StartupRecovery] Ready scene confirmed: NMainMenu.`
     - `[StartupRecovery] Main menu scene confirmed rendered for attemptId=...`
     - The launcher overlay is dismissed, and the fully rendered Slay the Spire 2 main menu appears on screen!
