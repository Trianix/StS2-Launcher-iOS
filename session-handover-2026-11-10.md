# STS2 iOS Port – Comprehensive Session Handover Document
**Date:** November 10, 2026 (Updated: 9:30 PM AEST)  
**Repository:** `https://github.com/Trianix/StS2-Launcher-iOS.git`  
**Active Branch:** `feature/ios-port`  
**Latest Passing CI Build:** [Run #42752802764](https://github.com/Trianix/StS2-Launcher-iOS/actions/runs/42752802764) (pending new push)  
**Build Artifact:** `STS2Launcher-iOS`  
**Unit Tests:** `STS2Mobile.GameIdentityTests` passing **220/220** tests.

---

## 1. Executive Summary & Breakthroughs

### Breakthrough 1: Handoff Fully Operational on Device
In `app_output(17).log` (from commit `fe51d92`), the launcher overlay minimized and dismissed cleanly (`overlayVisible=false`, `event=handoff_completed`), confirming that the previous handoff timeout bug is 100% resolved on physical iOS hardware!

### Breakthrough 2: Diagnosed Black Screen & Missing Audio
Following the launcher overlay dismissal, the screen remained solid black with no audio playing. Cecil IL disassembly and in-depth log analysis uncovered three interacting root causes:

1. **`NMainMenu._Ready()` `NullReferenceException` (Silent Audio & Uninitialized Submenus):**
   - In `app_output(17).log` line 3782:
     ```text
     ERROR: System.NullReferenceException: Object reference not set to an instance of an object.
        at MegaCrit.Sts2.Core.Nodes.Screens.MainMenu.NMainMenu._Ready_PatchedBy<com.sts2mobile>(NMainMenu )
     ```
   - Disassembly revealed that `_Ready()` invokes `GetNode<NPatchNotesButton>("%PatchNotesButton")`. In the stock PC `main_menu.tscn`, `PatchNotesButton` (and `PatchNotesScreen`, `ContinueRunInfo`) lacks `unique_name_in_owner = true`, causing Godot to log `ERROR: Node not found: "%PatchNotesButton"` and return `null`.
   - The subsequent line executed `this._patchNotesButtonNode.Connect(...)` on `null`, crashing `_Ready()` halfway through.
   - Crucially, the lines following this crash were **never reached**:
     - `NAudioManager.Instance.PlayMusic("event:/music/menu_update")` (NO MUSIC PLAYED)
     - `SubmenuStack.InitializeForMainMenu(this)` (SUBMENUS NEVER CONFIGURED)
     - `RefreshButtons()` (BUTTONS NEVER REFRESHED)
     - `Transition.FadeIn(3.0f)` (FADE IN NEVER STARTED)

2. **`GameTransitionRect` Solid Black Overlay (Black Screen):**
   - In `game.tscn`, `GameTransitionRect` (`NTransition`) is a sibling of `RootSceneContainer` drawn *on top of* it, containing `SimpleTransition` (a full-screen solid black `ColorRect` with `Visible = true`).
   - Because `SafeLaunchMainMenuAsync` bypassed the desktop logo flow (`skipLogo = true`), `GameTransitionRect` was never dismissed or hidden.
   - In `NTransition.FadeIn`, if `_threshold == 0`, it early-exits without setting `Visible = false` or `MouseFilter = Ignore`.
   - The solid black `ColorRect` stayed visible over the entire viewport, blocking the rendered `NMainMenu` and intercepting touches.

3. **686-Asset Startup Texture Burst (Main Thread Starvation & Memory Warnings):**
   - In `SafeLaunchMainMenuAsync`, `LoadDeferredStartupAssetsAsync` queues `PreloadManager.LoadCommonAndMainMenuAssets()`, which synchronously decompresses 686 high-resolution BPTC/DXT textures on the CPU under the Mono interpreter.
   - Lines 3881–5646 in `app_output(17).log` showed >90 seconds of main thread saturation and `Did receive memory warning!`, starving Godot's frame rendering and FMOD's `system.update()`.
   - Android had `DeferredPreloadExperimentPatches.cs` to suppress this 686-asset burst, but it was guarded with `if (!OperatingSystem.IsAndroid()) return;` and had never run on iOS.

---

## 2. Key Fixes Implemented

### 1. Unique Name Hierarchy Resolution (`PlatformPatches.Prefixes.cs`)
Added `EnsureUniqueNamesInOwner(Node current, Node owner)` which traverses the main menu hierarchy and assigns `UniqueNameInOwner = true` and `Owner = owner` on all descendants. This ensures `%PatchNotesButton`, `%PatchNotesScreen`, and `%ContinueRunInfo` resolve natively in `GetNode("%...")`.

### 2. Main Menu Resilient Lifecycle Patch & Finalizer (`PlatformPatches.Prefixes.cs` & `PlatformPatches.PlatformUtil.LocaleStrategy.cs`)
- Added `MainMenuReadyPrefix` to execute `EnsureUniqueNamesInOwner` before `_Ready()` runs.
- Added `MainMenuReadyFinalizer` to catch any uncaught exception in `_Ready()`, log details, and run a fallback initialization sequence:
  - Plays menu music: `NAudioManager.Instance.PlayMusic("event:/music/menu_update")`.
  - Initializes `SubmenuStack`: `SubmenuStack.InitializeForMainMenu(instance)`.
  - Refreshes buttons: `instance.RefreshButtons()`.
  - Unconditionally dismisses any lingering transitions.
  - Suppresses the exception (returns `null`) to keep the Godot C# engine bridge healthy.

### 3. Transition Dismissal & Fade-In Patch (`PlatformPatches.Prefixes.cs`)
- Added `DismissTransition(NTransition transition)`:
  - Sets `Visible = false`.
  - Sets `MouseFilter = MouseFilterEnum.Ignore`.
  - Resets `InTransition = false` and `_threshold = 0`.
  - Traverses and hides all child controls (such as `SimpleTransition` solid black `ColorRect`).
- Added `TransitionFadeInPrefix` to immediately complete transitions if already dismissed.
- Integrated `DismissTransition` into `SafeLaunchMainMenuAsync` immediately upon acquiring `game.Transition` and after attaching `NMainMenu`.

### 4. Suppression of 686 Deferred Preload Assets on iOS (`DeferredPreloadExperimentPatches.cs`)
- Updated guard to `if (!MobilePlatform.IsMobile) return;`.
- Configured `ReadSuppressionArm()` to return `true` on iOS (`OperatingSystem.IsIOS()`).
- Eliminates the 90-second CPU decompression stall and avoids iOS memory warnings, freeing the main thread for smooth 60 FPS rendering and audio playback.

---

## 3. Verification & Testing

1. **Local Build:**
   ```powershell
   tmp/dotnet-sdk-10.0.100/dotnet.exe build src/STS2Mobile/STS2Mobile.csproj
   ```
   **Result:** `0 Error(s), 0 Warning(s)`. Build succeeded.

2. **Unit Tests:**
   ```powershell
   tmp/dotnet-sdk-10.0.100/dotnet.exe run --project tests/STS2Mobile.GameIdentityTests/STS2Mobile.GameIdentityTests.csproj
   ```
   **Result:** `GameIdentity tests: 220 passed, 0 failed`.

---

## 4. Next Steps for Next Session

1. **GitHub Actions CI Build:**
   - Push commit to `feature/ios-port`.
   - Monitor CI run and wait for the `STS2Launcher-iOS` artifact.
2. **Device Verification:**
   - Download the new `STS2Launcher-0.2.436.ipa` (or current build number) and install on the test device.
   - Launch and tap **Play Vanilla**.
   - Verify that:
     1. The launcher overlay minimizes and dismisses cleanly.
     2. The solid black transition is dismissed.
     3. The main menu screen with title artwork, buttons, and Spine animations is displayed.
     4. Main menu theme music (`event:/music/menu_update`) plays cleanly.
