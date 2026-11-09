# STS2 iOS Port – Session Handover Document
**Date:** November 10, 2026  
**Repository:** `https://github.com/Trianix/StS2-Launcher-iOS.git`  
**Active Branch:** `feature/ios-port`  

---

## 1. Executive Summary & Context

Following the previous session fixes (AMFI SIGKILL bypass, shader warmup Jetsam OOM bypass, Mono interp `MINT_LDSTR_DYNAMIC -> MINT_LDSTR` opcode rewriting, and ISO 639-1 fast locale mapping), the latest test run was captured in `app_output(12).log`.

Analysis of `app_output(12).log` identified two critical bugs:
1. **AutoLaunch Failure (Line 782):**
   `InvalidOperationException: No valid unconsumed Android restart request is available.`
   Prevented the launcher from proceeding automatically into the game scene, leaving the app waiting at the launcher screen until the user manually tapped the launch button.
2. **Globalization-Invariant Mode Crash During Game Startup (Lines 2089–2093):**
   Game startup halted during localization initialization (`LocManager.Initialize()`) right as `SmartFormat.ZString.dll` was loaded. Under `DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1`, calls to `CultureInfo.GetCultureInfo(name)` throw `CultureNotFoundException: Only the invariant culture is supported in globalization-invariant mode.` This prevented `LocManager.CultureInfoFromThreeLetterCode`, `LocManager..cctor`, and `SmartFormat`'s `PluralLocalizationFormatter` from operating.

---

## 2. Issues Investigated & Resolved

### 1. AutoLaunch Blocking on iOS (`app_output(12).log`, line 782)
- **Symptom:**
  ```text
  [Launcher] Launch blocked: launch setup failed (InvalidOperationException: No valid unconsumed Android restart request is available.)
  ```
- **Root Cause:**
  `LauncherUI.AutoLaunch.cs` detects `STS2_AUTO_LAUNCH_GAME=1` set by `AppDelegate.mm` when an authorized runtime slot is present. It triggers `_controller.AutoLaunchRequested(safeLaunch)`, setting `plan.Source = LauncherLaunchSource.AutoLaunch`.
  In `LauncherLaunchCoordinator.Attempt.cs`:
  ```csharp
  var restoringPendingAttempt = string.Equals(plan.Source, LauncherLaunchSource.AutoLaunch, StringComparison.Ordinal);
  var restartRequest = restoringPendingAttempt ? LauncherRestartRequest.ConsumeRestored() : null;
  ```
  On Android, AutoLaunch restores a serialized restart request passed across process recreation via `AndroidGodotAppBridge`. On iOS, apps run in-process and cannot restart themselves; AutoLaunch is an initial direct launch rather than an Android process restoration. Calling `AndroidGodotAppBridge.ConsumeLaunchRestartRequest()` returned an empty payload, throwing `InvalidOperationException`.
- **Fix:**
  - In `src/STS2Mobile/Launcher/LauncherLaunchCoordinator.Attempt.cs`, guarded `restoringPendingAttempt` with `!OperatingSystem.IsIOS()`:
    ```csharp
    var restoringPendingAttempt = !OperatingSystem.IsIOS() && string.Equals(
        plan.Source,
        LauncherLaunchSource.AutoLaunch,
        StringComparison.Ordinal
    );
    ```
  - In `src/STS2Mobile/Launcher/LauncherRestartRequest.cs`, guarded `ConsumeRestored()` with `if (OperatingSystem.IsIOS()) return null;`.
  - On iOS, `attemptId` is now freshly allocated, `LauncherHandoffStateOwner.Shared.Begin(attemptId)` accepts it cleanly, and `TrySignalInProcessLaunch` starts the game automatically.

### 2. Globalization-Invariant Mode Crash During Startup (`app_output(12).log`, lines 2089–2093)
- **Symptom:**
  Execution halted during `LocManager.Initialize()`:
  ```text
  [WARN] Running in .NET globalization-invariant mode. Locale-specific formatting will be disabled.
  [INFO] Initializing language for the first time from platform locale: en_AU -> eng
  [INFO] Loading locale path=res://localization/eng
  .NET: Loading assembly 'SmartFormat.ZString.dll' ...
  ```
- **Root Cause:**
  - `AppDelegate.mm` enables `DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1` on iOS to prevent ICU data dependencies and missing native symbols.
  - In globalization-invariant mode in .NET, `CultureInfo.GetCultureInfo(name)` or `new CultureInfo(name)` for any non-empty name throws:
    `CultureNotFoundException: Only the invariant culture is supported in globalization-invariant mode. (Parameter 'name') en is an invalid culture identifier.`
  - In `LocManager.SetLanguageInternal`, it calls `LocManager.CultureInfoFromThreeLetterCode("eng")`, which executes `CultureInfo.GetCultureInfo("en")`.
  - In `LocManager..cctor`, static initialization calls `CultureInfo.GetCultureInfo("en")` to populate `_englishCultureInfo`.
  - In `SmartFormat.Extensions.PluralLocalizationFormatter`, CLDR pluralization rule resolution checks `if (cultureInfo.Equals(CultureInfo.InvariantCulture)) cultureInfo = CultureInfo.GetCultureInfo("en");`.
- **Fix:**
  - In `src/STS2Mobile/Patches/PlatformPatches.PlatformUtil.LocaleStrategy.cs`:
    - Added `ApplyLocManagerCulturePatch`: Harmony prefix for `LocManager.CultureInfoFromThreeLetterCode` returning `CultureInfo.InvariantCulture`.
    - Added `ApplyCultureInfoGetCultureInfoPatch`: Harmony prefix for `System.Globalization.CultureInfo.GetCultureInfo(string)` returning `CultureInfo.InvariantCulture`.
  - In `src/STS2Mobile/Patches/PlatformPatches.Prefixes.cs`:
    - Implemented `CultureInfoFromThreeLetterCodePrefix` and `GetCultureInfoPrefix` returning `CultureInfo.InvariantCulture`.
  - In `src/STS2Mobile/Patches/PlatformPatches.cs`:
    - Registered both patches during startup orchestration under `Platform compatibility`.
  - This ensures all culture queries throughout the game engine, localization tables, and string formatters safely receive `CultureInfo.InvariantCulture` without unhandled exceptions.

---

## 3. Modified Files

| File | Change |
|------|--------|
| `src/STS2Mobile/Launcher/LauncherLaunchCoordinator.Attempt.cs` | Guarded `restoringPendingAttempt` with `!OperatingSystem.IsIOS()` for in-process auto-launch. |
| `src/STS2Mobile/Launcher/LauncherRestartRequest.cs` | Guarded `ConsumeRestored()` with `if (OperatingSystem.IsIOS()) return null;`. |
| `src/STS2Mobile/Patches/PlatformPatches.PlatformUtil.LocaleStrategy.cs` | Added `ApplyLocManagerCulturePatch` and `ApplyCultureInfoGetCultureInfoPatch`. |
| `src/STS2Mobile/Patches/PlatformPatches.Prefixes.cs` | Added `CultureInfoFromThreeLetterCodePrefix` and `GetCultureInfoPrefix` handlers. |
| `src/STS2Mobile/Patches/PlatformPatches.cs` | Registered `ApplyLocManagerCulturePatch` and `ApplyCultureInfoGetCultureInfoPatch`. |

---

## 4. Verification

- **Compilation:** `STS2Mobile.csproj` builds cleanly with 0 errors.
- **Unit Tests:** `STS2Mobile.GameIdentityTests` executed: **220 passed, 0 failed**.
- **Detour Compatibility:** Tested Harmony patching on `CultureInfo.GetCultureInfo(string)` under globalization-invariant mode; verified clean redirection to `CultureInfo.InvariantCulture`.
