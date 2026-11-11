# iOS gameplay diagnostics and stabilization

Updated: 2026-11-11

The stabilization changes preserve the game's lifecycle and unlock rules, resolve required scene nodes by type without changing ownership, restore a failed submenu push, and remove the known Back/Confirm and Ascension animation failures. They also add shared desktop/device probes and an isolated room initialization sweep.

## Local verification

Use .NET 9 or a compatible SDK/runtime, Godot 4.5.1 Mono, the staged managed dependencies, and an executable game DLL. The compressed iOS `sts2.dll` reference supports compilation and reflection checks; its method bodies cannot execute gameplay or `ISaveStore` calls.

```powershell
dotnet build tests/STS2Mobile.GameIdentityTests/STS2Mobile.GameIdentityTests.csproj -c Release
dotnet tests/STS2Mobile.GameIdentityTests/bin/Release/net9.0/STS2Mobile.GameIdentityTests.dll
./scripts/test-local-gameplay-save-safety.ps1 -GameAssemblyPath <runtime>/sts2.dll
python scripts/test-ios-native-fingerprint.py
./scripts/test-gameplay-stabilization.ps1 `
  -GodotPath <godot-mono-executable> `
  -ManagedRuntimeDirectory <runtime-directory> `
  -GameAssemblyPath <runtime>/sts2.dll
```

Supply `-BaseGamePckPath <matching-game.pck>` to include scene-loaded controls. With no PCK, those cases explicitly report `blocked`. The runner stages the executable game DLL only in its generated test output and restores the compile reference afterwards. It writes `artifacts/gameplay-stabilization/engine.log`, `godot.log` and `last_gameplay_diagnostics.json`. A process timeout or failed behavior assertion fails the command.

The desktop baseline on this checkout passed 230 managed tests, 13 save/synchronization scenarios, 3 native-cache tests and 59 Godot-hosted behavior cases. The two scene-loaded cases were blocked because no gameplay PCK was supplied. This establishes desktop contracts; the iOS bridge, actual room fixtures and full gameplay route still require device evidence.

## One device session with broad coverage

The app writes `last_gameplay_diagnostics.json` in its Documents directory. It includes build/module hashes, the selected PCK/runtime identity, lifecycle checkpoints and grouped managed errors. Checkpoints have `observed` status; they do not certify a completed battle. Opt-in failures also capture node-field snapshots and screenshots when rendering is available.

Prepare these plain files in the app's Documents directory, then fully restart the app:

| File | Contents / behavior |
| --- | --- |
| `gameplay-diagnostics.enabled` | Empty file enables independent property/tween probes. |
| `gameplay-test-session.txt` | A session ID, for example `ironclad-001`. Selects separate saves and a fixed `STABILIZE1` seed. This also enables property probes. |
| `gameplay-fixtures.enabled` | Empty file enables fresh combat, shop, rest-site, event and map initialization fixtures through the game's bootstrap API. Requires the isolated session file. |

Keep the app in the foreground during the sweep. Each room gets a fresh run and a 30-second bootstrap bound. A failed completed operation can be followed by another clean fixture; a timeout or failed reset blocks later fixtures to avoid concurrent mutation. The runner returns to the main menu after a completed sweep. These fixtures test initialization independently of menu navigation, rather than claiming to reproduce the normal map route.

Test saves live beneath the app's private data directory at `gameplay-test-sessions/<id>/saves`. They never notify the normal automatic upload service, and pre-load Cloud synchronization is skipped while that session is selected. The normal profile is not copied into the fixture. Keep the same ID for relaunch/continue testing. Remove the fixtures marker before playing a manual route; remove the session file and restart to return to the normal profile. Test data remains available for inspection.

Equivalent environment variables are `STS2_GAMEPLAY_DIAGNOSTICS=1`, `STS2_GAMEPLAY_TEST_SESSION=<id>` and `STS2_GAMEPLAY_ROOM_FIXTURES=1`. Invalid session IDs fail before save construction instead of falling back to the normal profile.

After the sweep, exercise Standard → character → embark → opening event/map → combat → reward → next room → relaunch/continue. Include attack/skill targeting, cancelled drags, End Turn, reward selection/skipping, shop/rest/event interactions and background/resume. Actual touch, rendering, gameplay state changes and persistence must be verified on the device; a ready scene is insufficient.

## Build workflow

The iOS workflow runs managed, save-safety, native-cache and desktop Godot checks before macOS packaging. It cancels superseded runs and uploads commit-labelled probe and IPA artifacts. Native cache fingerprints include build recipes, patches, toolchain and output hashes; `STS2_FORCE_NATIVE_REBUILD=1` bypasses local reuse. The new cache generation requires an initial native rebuild.

Pending investigations are the shared iOS property bridge (use the new device probe to identify its boundary), future-version save preservation and the real Steam Cloud HTTP 400. The normal paused-Push behavior on synchronization failure remains in place. Automatic battle/reward assertions and managed-patch hot updates are not implemented by the initialization sweep.
