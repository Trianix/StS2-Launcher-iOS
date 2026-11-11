# iOS gameplay stabilization plan — 2026-11-11

Status: foundation fixes, shared probes, opt-in room initialization fixtures and build preflight implemented. Desktop checks pass; device gameplay and the shared iOS property bridge remain unverified. See [implementation and diagnostic instructions](ios-gameplay-diagnostics.md).

## Recommendation

Change the unit of progress from one repaired control to a verified gameplay route. First repair and test the shared property/tween and scene-initialization contracts, then exercise independent screens and gameplay checkpoints in one diagnostic IPA. Keep rapid managed-code iterations separate from native-engine rebuilds.

The next milestone should be: Standard run → character selection → embark → opening event/map → combat → reward → next room → save/relaunch/continue. A screen opening, a patch applying, or an exception being caught is insufficient evidence of success.

## Evidence and limits

Reviewed `session-handover-2026-11-11.md`, `C:\Users\jerem\Downloads\app_output(22).log`, current patches, tests, build workflow, and selected game IL. The handover describes `b6878e0`; the checkout is at `c3886a0`, with additional gameplay/submenu patches whose messages appear in the log. The exact IPA commit is not independently established by this log.

The log contains 1,113 lines beginning `ERROR:`, but only 20 occur before managed unloading starts at line 5169. Another 1,081 are repeated node-path errors during shutdown. Caught exceptions and `[WARN]` messages are additional evidence, not included in that 20. There is no evidence here of a completed embark or combat. Backgrounding immediately precedes unloading; the cause of application termination is not established.

| Priority | Finding | Evidence / consequence |
| --- | --- | --- |
| P0 | Generic property/tween failures persist | Lines 4553 and 4625: Back/Confirm button `position` fails with Nil/Vector2, inside our existing patches. Lines 4893, 4927, 5008, 5041: Ascension animation fails with Nil/float. The local IL uses `modulate:a` and `position:y`; chaining `From` on the failed tweener throws. |
| P0 | Root scene dependencies remain unresolved | Lines 856–894: `%RootSceneContainer`, `%ReactionWheel`, `%MultiplayerTimeoutOverlay` lookups fail in `NGame._EnterTree`. Lines 4181 and 4280–4285: `%ContinueRunInfo` lookup and main-menu `RefreshButtons` fail, including the recovery attempt. |
| P0 | Character initialization fails, then continues | Lines 4622 and 4969: `AfterInitialized` throws and a finalizer suppresses it. The local IL initializes cursor, reaction, and timeout services here. The missing timeout overlay is a strong candidate; the precise null still needs a runtime field snapshot. |
| P0 | A failed transition is attempted twice | Lines 4961–5080 show the patched transition failing, then the original path running. `SingleplayerSubmenuOpenCharacterSelectPrefix` returns `true` after an exception, even if it has already initialized/pushed a screen. |
| P1 | Singleplayer input hits incomplete shared services | Lines 5125–5162: three `NRemoteMouseCursorContainer._Input` null exceptions. Local IL accesses `NGame.ReactionWheel.Visible` on mouse motion, consistent with the earlier unresolved wheel. Confirm on the installed runtime. |
| P1 | Save/runtime mismatch and Cloud failure | Lines 2322–2360: progress schema 24 versus supported 21, with 36 progress validation warnings. Lines 1122–1127: Cloud upload/abort failure and HTTP 400; automatic Push is paused. Local play proceeds. No data loss is proven. |
| P1 | Diagnostics cannot persist heartbeat | Lines 4358 and 4598: heartbeat writes target the read-only application bundle. |
| P2 | Rendering and lifecycle follow-up | Texture conversions and cursor warnings occur during use; leak reports follow unloading. Measure memory growth during repeated routes before attributing a live leak to those reports. |

A read-only Cecil inventory of `tmp/ref_extracted/assets/dotnet_bcl/sts2.dll` found **1,086 TweenProperty call sites in 550 methods across 295 types**, including compiler-generated types. Examples include map points, End Turn, Proceed, card inspection, combat piles and event options. This is an audit scope, not a count of broken animations. DLL SHA-256: `554096F7790D340999A37E2239AFF81CED701B58E857EC628DF27C7798F5BF31`. Match it to the installed runtime before using this IL as an exact patch contract.

## 1. Establish a repeatable baseline and useful diagnostics

Implement these alongside the first repair batch:

- Record app commit/build, patch assembly hash, game DLL hash, existing PCK identity, Godot/Mono identity, device, renderer and test scenario in one report header.
- Capture a stable writable diagnostic directory on the engine thread and use it for heartbeat/report writes. Avoid relative paths and avoid engine API calls from timer threads.
- Group errors by normalized message and top relevant frame. Preserve the first full stack and occurrence count, with distinct startup, gameplay and shutdown phases.
- Report patch steps as applied, intentionally skipped, failed or behavior-verified. The current log reports 19/19 gameplay steps applied even though event layout and a settings patch report themselves disabled.
- Use a disposable local test profile and fixed seed with cloud synchronization disabled for that profile. Preserve real saves; separately test save compatibility and Cloud transport. The observed schema mismatch makes the current profile an unsuitable clean gameplay baseline.

Acceptance: one report identifies the exact build and fixtures, persists successfully, and lists independent failures and the last successful gameplay checkpoint.

## 2. Fix property access as a shared contract

Create a small engine-hosted probe using a plain `Control`, a minimal custom C# control, and actual game controls (`NBackButton`, `NConfirmButton`, `NAscensionPanel`). Compare typed getters/setters with `Get`, `Set`, `GetIndexed`, `SetIndexed` and tween execution for position, scale, modulate, alpha and component paths. Include scene-loaded and newly created nodes.

Check both reads and writes, property-list visibility, nested paths, chained tween settings, completion signals, callbacks and waits. A non-null tweener alone does not prove that animation or gameplay sequencing completes.

Use the probe to choose the repair layer:

- If generic access fails broadly, investigate the native/managed property bridge, callback return/validity handling, binding versions and Variant conversion. Repair the shared boundary and run the same probe on iOS.
- If failure is confined to particular node/property combinations, use a centralized, explicitly scoped compatibility implementation and apply it to the audited gameplay call sites.
- For the immediate menu route, finish the known Back/Confirm and Ascension fixes together. Existing Back/Confirm prefixes already assign `showPos` directly, then attempt a redundant tween which can remain empty. Preserve and verify that final position, remove the failing animation attempt when using this fallback, and clean up the tween. Ascension additionally needs its final alpha/position and completion behavior restored.

Time-box the shared diagnosis to one investigation pass and one consolidated device probe; if it remains unresolved, ship bounded fallbacks for the critical route with the probe included. Do not make every future gameplay experiment wait for an unlimited engine rewrite.

Do not globally suppress tween errors or replace every tween with immediate assignment. Combat may depend on completion, callbacks and animation timing. Preserve those contracts and verify them before removing old workarounds.

Acceptance: the probe passes and the audited navigation/combat route produces no Nil mismatch, empty tween or stranded completion wait.

## 3. Repair initialization and transitions as complete operations

Audit the real scene hierarchy, owner relationships and the fields required by `_EnterTree`, `_Ready`, initialization and opening. Start with NGame root services, ContinueRunInfo, character selection, then NRun/global UI and the first room.

- Verify whether failures originate in scene metadata/loading, ownership changes or lifecycle timing. `%` lookups already fail in `_EnterTree`; repairing them only after `_Ready` can be too late.
- Preserve nested scene ownership. Use explicit typed paths/contracts and targeted unique-name registration; broad recursive name registration can select the wrong duplicate child or change another scene's ownership.
- Complete initialization and signal connection before enabling interaction. Recovery must prove the required postconditions; filling a few fields after `_Ready` aborts does not execute its remaining setup.
- Track transition state. After mutation begins, either complete the transition or restore a known state; do not fall through to the original method and repeat it. Apply the same review to Standard, Daily and Custom transition prefixes.
- Repair shared cursor/reaction/timeout dependencies. Guard their input lifecycle where appropriate, including teardown. Do not disable all multiplayer services merely because the visible mode is singleplayer.
- Review forced Daily/Custom enabling and the hard-coded `exordium` fallback against the active model/availability data. They are potential future failures found in code, not demonstrated failures in this session.

Acceptance: repeated enter/back/re-enter cycles leave one correct active screen, one initialized lobby, one action per tap and no required-field or swallowed initialization errors.

## 4. Add a gameplay harness that discovers several failures per IPA

Extend the existing Godot-hosted test approach in `tools/LauncherUiPreview`; share scenario/assertion code with an opt-in iOS diagnostic runner. Use real matching PCK/DLL assets. Desktop runs can validate scenes and behavior quickly but cannot certify the iOS Mono/Metal bridge.

Provide both an end-to-end route and independent scenarios. Independent scenarios allow rewards, shops or combat UI to be exercised when a menu is broken; they do not substitute for verifying the real transition into that screen. Use actual game setup APIs and versioned fixtures, not arbitrary attachment of uninitialized nodes.

| Scenario | Required observations |
| --- | --- |
| Menu and character selection | Standard/Back, valid character selection, ascension controls, embark exactly once; Settings/Compendium/Timeline as independent checks. |
| Run start, event and map | Run and player created, opening choice completes, map opens, one room transition, no overlay intercepting input. |
| Combat | Hand/energy/intent render; attack and skill play; targeting; cancel drag; invalid target; End Turn; enemy turn; damage/block/energy assertions. |
| Rewards | Gold/card/relic/potion where applicable; choose/skip; close; next room remains reachable. |
| Room coverage | Shop purchase/remove, rest/upgrade and event choice on prepared fixtures; scroll and inspect controls work. |
| Persistence and lifecycle | Save/relaunch/continue; background/resume in menu and combat; correct pause/audio/input state; no duplicated action. |

Add a timeout, screenshot, scene/field snapshot and structured result per scenario. Stop a failed dependent chain and record later steps as blocked; reset cleanly before independent scenarios. Keep the player's normal profile untouched. Physical touch testing must still verify drag targeting, cancellation, scrolling and safe-area layout; invoking handlers alone cannot establish input correctness.

The current button test checks patch method existence, return type and parameter count; the submenu test checks target presence. Retain these as compatibility checks and add behavior tests for the actual failure modes. The handover's 221 passing tests are historical evidence, not gameplay validation or a fresh test run from this review.

## 5. Shorten the build/test cycle

The current iOS workflow already caches Mono and Godot libraries. Its visible job builds/packages/uploads without running the gameplay behavior suite. Therefore the first improvement is stronger pre-IPA checks and more coverage per install, not simply another cache.

- Run managed build, compatibility checks and supported engine-hosted scenarios before the macOS packaging job. Run save-safety checks when changing persistence and platform paths.
- Separate engine/runtime jobs from managed patch packaging. Reuse native libraries for managed-only changes, keyed by all relevant source revisions, patches, build options and toolchain inputs.
- Replace local library-exists-only reuse with a fingerprint check or explicit force-rebuild path. `build-godot-ios.sh` currently returns immediately when its output library exists.
- Add stale-run cancellation and unique commit-labelled artifacts/reports. Measure queue, build, packaging, install and test durations before choosing further optimizations.
- After the harness works, investigate a development-only managed-patch update path using the existing staging infrastructure, with hash validation, restart and rollback. This is a feasibility item, not a promise of hot reload; native changes still need an IPA and updates must not overwrite the runtime game DLL.

## Proposed batches and completion gates

1. **Foundation plus diagnostic runner:** writable reports/build identity; root/character initialization and transition repair; known Back/Confirm/Ascension corrections; shared property probe; isolated test profile. One device session should return all independent probe results, even if embark remains blocked.
2. **Core gameplay route:** resolve the shared property defect or implement verified scoped compatibility, then clear run-start/map/combat/reward failures found by the runner. Gate on a complete battle, reward and next-room transition, followed by relaunch/continue.
3. **Coverage expansion:** remaining characters and representative mechanics, shop/rest/events, other menus, then Daily/Custom and multiplayer readiness; lifecycle, memory/performance and Cloud verification get their own measurable results.

These are implementation batches, not a guaranteed number of builds. Put independently verified fixes into each batch, rerun affected scenarios locally, and spend device builds on platform validation and broad discovery. Avoid sending another IPA whose sole acceptance criterion is that one more button opens.

Cloud follow-up must diagnose the failing HTTP stage using sanitized request/response metadata and verify upload/commit/read-back with disposable data. Retain the existing paused-Push behavior on failure. Before resuming synchronization for a future-version profile, establish compatibility and preservation of unknown data; do not treat successful recovery parsing as proof of a lossless round trip.
