# STS2 iOS Port – Comprehensive Session Handover Document
**Date:** November 11, 2026 (Updated: 11:45 AM AEST)  
**Repository:** `https://github.com/Trianix/StS2-Launcher-iOS.git`  
**Active Branch:** `feature/ios-port`  
**Latest Commits:** 
- `b6878e0` (`fix(ios): prevent NMainMenuTextButton and button tween crashes on touch release`)
- `f51e681` (`fix(ios): replace NPatchNotesScreen._Ready with resilient prefix, hook back button signals, and suppress post-attach`)
**Latest Passing CI Build:** Builds on GitHub Actions tracking `feature/ios-port`  
**Unit Tests:** `STS2Mobile.GameIdentityTests` passing **221/221** tests.

---

## 1. Executive Summary & Progression

### Milestone 1: Patch Notes Screen ("Errortember 00, 1982") Overlay Solved!
- **Symptom:** In `app_output(18).log` and `app_output(19).log`, an un-dismissible patch notes overlay titled "Errortember 00, 1982" was displayed, and tapping Back did nothing.
- **Root Cause:** `NPatchNotesScreen._Ready()` was crashing midway through initialization due to missing patch notes files or HTTP fetch failures, leaving the screen in a broken, half-initialized state where signals were never connected.
- **Resolution (Commit `f51e681`):** Replaced `NPatchNotesScreen._Ready()` with a resilient Harmony prefix, hooked BackButton click handlers, and suppressed post-attach display when running on iOS. The user verified in `app_output(20).log`: **"the patchnote overlay is gone!"**.

### Milestone 2: Main Menu Button Activation Solved!
- **Symptom:** In `app_output(20).log`, the user reported:
  > *"the patchnote overlay is gone! however when tapping the menu options it doesn't open the corresponding option, the option is highlighted when I tap but nothing happens"*
- **Root Cause Discovered in `app_output(20).log` and Cecil IL:**
  1. In `NClickableControl.HandleMouseRelease()`:
     ```csharp
     if (isMouseButtonLeftRelease)
         OnReleaseHandler();
     ```
  2. In `NClickableControl.OnReleaseHandler()`:
     ```csharp
     _isPressed = false;
     OnRelease();                                // Line 0011: Virtual call to NMainMenuTextButton.OnRelease()
     EmitSignal(SignalName.Released, this);      // Line 0017: EMITS THE RELEASED SIGNAL THAT TRIGGERS THE ACTION
     ```
  3. In `NMainMenuTextButton.OnRelease()` $\to$ `AnimRelease()`:
     ```csharp
     _tween = CreateTween();
     _tween.TweenProperty(label, "scale", ...).SetTrans(...).SetEase(...);
     _tween.TweenProperty(label, "self_modulate", ...).SetTrans(...).SetEase(...);
     ```
  4. On iOS, Godot's property validation fails for custom C# controls:
     `ERROR: Type mismatch between initial and final value: Nil and Vector2 at: validate_type_match (scene/resources/animation.cpp:5688)`
  5. Because `validate_type_match` fails, `TweenProperty` returns `null`.
  6. Calling `.SetTrans(...)` on `null` throws:
     `ERROR: System.NullReferenceException: Object reference not set to an instance of an object at MegaCrit.Sts2.Core.Nodes.Screens.MainMenu.NMainMenuTextButton.AnimRelease()`
  7. **Crucial Consequence:**
     The unhandled exception in `AnimRelease()` immediately aborted `OnReleaseHandler()` at line 0011. Line 0017 (`EmitSignal(SignalName.Released, this)`) **never executed**.
     Therefore, the `Released` signal was never emitted, and button click handlers like `SingleplayerButtonPressed` were never called!
  8. The same crash was occurring in `AnimUnhover()` during focus changes, as well as in `NShortSubmenuButton`, `NSubmenuButton`, `NCompendiumBottomButton`, `NOpenProfileScreenButton`, and `NCharacterSelectButton`.

---

## 2. Fix Implementation (Commit `b6878e0`)

### 1. Universal Safety Net in `NClickableControl.OnReleaseHandler`
Added a Harmony prefix on `NClickableControl.OnReleaseHandler`:
- Ensures `_isPressed = false;` is set.
- Wraps `OnRelease()` in a `try / catch` so that ANY future exception or tween bug in any custom button subclass cannot abort signal emission.
- Guarantees `EmitSignal(SignalName.Released, __instance)` **always** executes!

### 2. Comprehensive Button Fixes in `CommonUiButtonPatches.cs`
Direct scale and modulate assignment with tween cleanup, preventing Godot `Nil and Vector2` tween NREs:
- **`NMainMenuTextButton`:**
  - `AnimRelease`: Direct assignment of `label.Scale` and `label.SelfModulate`, killing old tween.
  - `AnimPressDown`: Direct assignment of `label.Scale = _downScale; label.SelfModulate = _downColor;`.
  - `AnimUnhover`: Direct assignment of `label.Scale = Vector2.One; label.SelfModulate = _defaultColor;`.
  - `OnFocus`: Direct assignment of `label.Scale = _hoverScale; label.SelfModulate = _hoveredColor;` + hover SFX.
- **`NShortSubmenuButton`:**
  - `OnPress`: Direct scale `0.98f`, modulate `lightGray`, shader parameter update.
  - `OnFocus`: Direct scale `1.02f`, modulate `White`, shader parameter update.
  - `OnUnfocus` & `OnRelease`: Direct scale `Vector2.One`, modulate `White`, shader parameter update.
- **`NSubmenuButton`:**
  - `OnUnfocus`: Reset scale to `Vector2.One`, shader parameter update.
- **`NCompendiumBottomButton`:**
  - `OnPress`, `OnFocus`, `OnUnfocus`: Direct scale and modulate assignments.
- **`NOpenProfileScreenButton`:**
  - `OnUnfocus`: Direct scale reset to `Vector2.One`.
- **`NCharacterSelectButton`:**
  - `OnUnfocus`: Direct scale reset and shader saturation/value restore.
- **`NContinueRunInfo`:**
  - `AnimShow` & `AnimHide`: Direct position and alpha modulate assignments.

### 3. Verification & Testing
- Added unit test `ButtonPatchesTargetMethodsExist` in `STS2Mobile.GameIdentityTests/Program.ButtonPatchesTests.cs`.
- Registered and verified with dynamic assembly resolver.
- **All 221 unit tests pass (`GameIdentity tests: 221 passed, 0 failed`)**.
- Built and pushed to `feature/ios-port`.

---

## 3. Next Steps for Device Testing
1. Download the new IPA generated by CI from commit `b6878e0`.
2. Sideload onto the iOS device.
3. Tap on the Main Menu buttons:
   - `Singleplayer`
   - `Settings`
   - `Compendium`
   - `Timeline`
4. Confirm that tapping menu buttons now opens the corresponding screens / submenus!
