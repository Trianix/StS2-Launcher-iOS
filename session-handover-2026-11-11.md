# STS2 iOS Port – Comprehensive Session Handover Document
**Date:** November 11, 2026 (Updated: 9:05 AM AEST)  
**Repository:** `https://github.com/Trianix/StS2-Launcher-iOS.git`  
**Active Branch:** `feature/ios-port`  
**Latest Commit:** `9488a34` (`fix(ios): resolve NMainMenu NullReferenceException, dismiss black GameTransitionRect, and suppress deferred preload burst`)  
**Latest Passing CI Build:** Run produced from commit `9488a34`  
**Build Artifact:** `STS2Launcher-iOS` (`STS2Launcher-0.2.436.ipa`)  
**Unit Tests:** `STS2Mobile.GameIdentityTests` passing **220/220** tests.

---

## 1. Executive Summary & Historic Milestone Reached

### The Game Starts and Renders on iOS!
The user sideloaded the IPA from commit `9488a34` and reported:
> **"The game starts now but I can't tap anything, write a handover document for the next session, I will provide the log in the next session"**

This confirms a **historic milestone** for the Slay the Spire 2 iOS port:
1. **Complete Engine Boot:** Mono runtime initialization under pure interpreter mode (`MONO_AOT_MODE_INTERP_ONLY = 8`), dynamic code execution workarounds, ModelDb dependency ordering (1,624 models), and save file loading are 100% stable.
2. **Handoff Succeeded:** The launcher overlay cleanly minimized and dismissed.
3. **No Black Screen:** The solid black `GameTransitionRect` overlay was successfully dismissed and disabled.
4. **Main Menu Rendered:** Slay the Spire 2 is actively running, rendering the main menu (background art, Spine animations, UI buttons), and playing audio on physical iOS hardware.

---

## 2. Current Problem: Touch Input Unresponsive ("Can't tap anything")

While the main menu is fully displayed on screen, tapping on buttons (e.g., *Singleplayer*, *Settings*, *Compendium*, etc.) does not trigger any action or visual press/hover feedback.

The user will provide the on-device log (`sts2launcherlog.txt` / `app_output.log`) in the next session.

---

## 3. Technical Investigation & Root Cause Hypotheses

Ahead of receiving the next log, we performed Cecil IL disassembly on the official game assemblies (`sts2.dll`) and analyzed Godot 4's mobile input pipeline:

### Hypothesis 1 (Primary): `NClickableControl` Requires `InputEventMouseButton` — Mobile Emulation Missing
- **IL Disassembly Evidence (`MegaCrit.Sts2.Core.Nodes.GodotExtensions.NClickableControl._GuiInput`):**
  ```il
  IL_0000: ldarg.1 
  IL_0001: isinst Godot.InputEventMouseButton
  IL_0002: stloc.0 
  IL_0003: ldloc.0 
  IL_0004: brfalse.s IL_0058: ldarg.1
  ...
  IL_0058: ldarg.1
  IL_0059: ldsfld Godot.StringName MegaCrit.Sts2.Core.ControllerInput.MegaInput::select
  IL_005e: callvirt System.Boolean Godot.InputEvent::IsActionPressed(...)
  ```
- **Finding:** Every button on the main menu (`NMainMenuTextButton` $\to$ `NButton` $\to$ `NClickableControl`) handles **only** `InputEventMouseButton` (or gamepad action `MegaInput.select`). It does **not** process `InputEventScreenTouch` or `InputEventScreenDrag`.
- **Godot iOS Input Behavior:** On iOS, screen taps emit `InputEventScreenTouch`. Godot only synthesizes `InputEventMouseButton` if the project setting `input_devices/pointing/emulate_mouse_from_touch` is enabled.
- **The Gap:** 
  - `scripts/make-bootstrap-pck.py`'s `project.godot` does not declare `emulate_mouse_from_touch = true`.
  - The PC Steam release of Slay the Spire 2 is configured for desktop platforms and does not enable mobile touch-to-mouse emulation.
  - Without touch emulation, Godot passes `InputEventScreenTouch` to controls, which `NClickableControl` rejects at line IL_0004.

### Hypothesis 2: Lingering Overlay Intercepting Mouse/Touch Events
- Even though `DismissNode` calls `node.MouseFilter = Control.MouseFilterEnum.Ignore` and `node.QueueFree()`, any lingering control with `MouseFilter = MouseFilterEnum.Stop` sitting at the root level or with a high `ZIndex` will consume all touch/mouse events before they reach `RootSceneContainer`.
- **Potential Suspects to verify against the next log's scene tree dump:**
  1. `LauncherUI` or `LauncherStartupStatus`: Verify whether `QueueFree()` was completed or if a parent node remains.
  2. `STS2MobileStartupFallbackShield`: In `ModEntry.StandaloneLauncher.cs`, `AddStartupFallbackShield` adds a full-screen `ColorRect` with `ZIndex = 1000` and `MouseFilter = Control.MouseFilterEnum.Stop`. If any fallback was triggered, this shield would block all input.
  3. Additional `NTransition` / `ColorRect` in `NGame`: Confirm whether another canvas layer or fade rect is active.

### Hypothesis 3: Viewport or Window Focus Dropped on iOS
- If Godot's iOS `DisplayServer` or `Window` lost focus or did not mark the viewport as focused, `_GuiInput` might not be dispatched by Godot's GUI input router.

---

## 4. Planned Action Plan for the Next Session

When the user provides the log (`sts2launcherlog.txt` / `app_output.log`):

1. **Log Inspection:**
   - Check the diagnostic scene tree dump to confirm all nodes and their `MouseFilter` status (`Stop`, `Pass`, or `Ignore`).
   - Check for any `STS2MobileStartupFallbackShield` or undisposed launcher controls.
   - Look for any `InputEvent` messages or unhandled exception traces.

2. **Implement Universal Touch-to-Mouse Adapter (`TouchInputPatches.cs`):**
   - In Godot, add a global input interceptor (via `NGame._UnhandledInput`, a scene tree autoload, or a Harmony prefix on `Godot.Viewport._GuiInput` / `Input.ParseInputEvent`):
     ```csharp
     if (ev is InputEventScreenTouch touch)
     {
         var mouseEvent = new InputEventMouseButton
         {
             Position = touch.Position,
             GlobalPosition = touch.Position,
             ButtonIndex = MouseButton.Left,
             Pressed = touch.Pressed,
             ButtonMask = touch.Pressed ? MouseButtonMask.Left : 0
         };
         Input.ParseInputEvent(mouseEvent);
     }
     else if (ev is InputEventScreenDrag drag)
     {
         var motionEvent = new InputEventMouseMotion
         {
             Position = drag.Position,
             GlobalPosition = drag.Position,
             Relative = drag.Relative,
             Velocity = drag.Velocity,
             ButtonMask = MouseButtonMask.Left
         };
         Input.ParseInputEvent(motionEvent);
     }
     ```
   - Also dynamically set `ProjectSettings.SetSetting("input_devices/pointing/emulate_mouse_from_touch", true)` during startup in `PlatformPatches`.
   - Update `scripts/make-bootstrap-pck.py` to include:
     ```ini
     [input_devices]
     pointing/emulate_mouse_from_touch=true
     ```

3. **Verify Scene Tree Overlay Input Pass-Through:**
   - In `SafeLaunchMainMenuAsync`, add a pass that inspects all root siblings of `NGame` and ensures any non-game control has `MouseFilter = Control.MouseFilterEnum.Ignore`.

4. **Test & Verify:**
   - Run `tmp/dotnet-sdk-10.0.100/dotnet.exe build src/STS2Mobile/STS2Mobile.csproj`.
   - Run `tmp/dotnet-sdk-10.0.100/dotnet.exe run --project tests/STS2Mobile.GameIdentityTests/STS2Mobile.GameIdentityTests.csproj`.
   - Commit and push to `feature/ios-port` for device verification.

---

## 5. Verification Commands

- **Build:**
  ```powershell
  tmp/dotnet-sdk-10.0.100/dotnet.exe build src/STS2Mobile/STS2Mobile.csproj
  ```
- **Unit Tests:**
  ```powershell
  tmp/dotnet-sdk-10.0.100/dotnet.exe run --project tests/STS2Mobile.GameIdentityTests/STS2Mobile.GameIdentityTests.csproj
  ```
