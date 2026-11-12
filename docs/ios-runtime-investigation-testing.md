# Consolidated iOS runtime investigation

This candidate repairs narrow integer returns in the iOS ARM64 Mono interpreter,
following the `20261112-093204-1e633319` device investigation. It reruns the same
consolidated investigation before more gameplay changes. It does not claim the
iOS port is qualified. The repair is in Mono's shared return packing; the Godot
property callbacks retain their existing behavior.

## On the iPhone

1. Download the new workflow artifact. It contains an IPA, its checksum, this guide,
   and `gameplay-property-probe.enabled`. Use the IPA from the same artifact.
2. Install it over the existing app using your usual signing identity. Keep the
   existing installation and its data. Close the app completely after installation.
3. In Files, copy `gameplay-property-probe.enabled` into **On My iPhone → STS2Launcher**
   (the app's Documents folder, beside `app_output.log`). Use that exact filename,
   without `.txt`. This is the same marker used by the previous property probe.
4. Open the app normally from its icon, without attaching a debugger or enabling JIT.
5. Leave it in the foreground. A status screen should show each investigation group.
   The complete run normally takes under two minutes, including a twelve-second idle
   measurement. It runs without starting the launcher, opening a game save, or syncing
   Cloud. No Play button or normal run is needed for this investigation.
6. Wait for **Investigation complete**. `FAILED` is a useful diagnostic result, and
   `INCOMPLETE` means a required contract was blocked. Neither means release-ready.
7. In Files, open **STS2Launcher → runtime-investigations** and send the newest dated
   `.zip`. It contains the per-run JSON, app log snapshot, runtime manifest and a
   screenshot when available. Also describe what the status screen showed.
8. If the app crashes, freezes, or never shows completion after two minutes, close
   it and collect `app_output.log` plus the newest dated folder's
   `runtime-investigation.json` **before reopening it**. If no dated folder exists,
   send `app_output.log` and `last_gameplay_diagnostics.json`. A native crash can prevent
   ZIP creation. Do not keep retrying the same candidate.
9. Completion removes the one-shot marker. After a crash, remove the marker manually
   before a normal launch. Dated evidence survives subsequent launches. Keep the ZIP
   you exported; a normal launch can overwrite the top-level app log and JSON.

Some tests deliberately throw an exception whose message starts `EXPECTED runtime
fixture getter exception`. The surrounding assertion checks that Godot contains
it. Other exceptions remain failures. The screen can pause briefly during GC tests.

## Questions this candidate must answer

| Question | Evidence |
| --- | --- |
| Do every native/managed callback field and their order agree in pinned source? | Embedded `godotCallbackContract` declaration audit. |
| Does the live device table match the managed registration, sizes and offsets? | `callback:layout` and `callback:layout-evidence`. |
| Do actual Get/Set callbacks return canonical 0/1 for handled and unhandled properties? | `callback:get:*`, `callback:set:*`, `[STS2CallbackAudit]` raw bytes and full x0 register. Assertions now require zero extension through w0 (low 32 bits), rather than just the low byte. A handled Nil export is distinct from an unhandled property. |
| Do native-property fallback, indexed access, interpolation, callbacks, Finished awaiting and cancellation work across controls? | `runtime:properties` and `scene:RuntimeProbeFixture:*`. |
| Do generated exports, scene serialization, ownership, unique names and lifecycle work without the game PCK? | `scene:*` and `runtime:packaged-scene`. |
| Do byte, signed integer, floating point, HFA/mixed structs, pointers, contained exceptions and GC survive the native boundary? | `runtime:native-scalars`. |
| Does the repair cover signed/unsigned bytes and shorts and the byte enum used for booleans, without truncating full-width returns? | `native-scalars:uco-*` calls real UnmanagedCallersOnly entries with deliberately contaminated argument registers and repeated GC; it inspects raw returns before a typed C caller can mask them. |
| Do Harmony prefixes, postfixes, transpilers, replacement, reverse patches, unpatching, generics, cached callers, exceptions and GC behave correctly? | `runtime:harmony`, individual fixture assertions and cleanup results. Patch installation alone cannot pass. |

The idle measurement records frame intervals, allocations, GC, managed memory,
physical memory footprint and thermal state. It does **not** qualify combat or
the 60 fps release target. Actual game scene regressions, Android/desktop baseline
equivalence, and sustained combat remain visibly incomplete.

## Review checkpoint

The previous bundle established the return-register discrepancy; this is the
shared repair and verification round. All five core runtime groups must pass,
including generic/indexed inherited properties and the tween cases on every
control and the packaged fixture. The idle frame measurement remains separate
from combat qualification, and release qualification remains incomplete.

If the repair does not clear that boundary, review the complete evidence and the
runtime feasibility checkpoint in `PLAN.md` before adding further patches. Once
correctness is qualified, verify the top-bar/real-scene regressions and measure
representative combat promptly; expanding full gameplay coverage depends on that
result. No additional normal gameplay test is needed for this investigation ZIP.

## Desktop preflight

The existing runner supports `scripts/test-gameplay-stabilization.ps1
-RuntimeInvestigation` with its normal Godot/runtime/game DLL arguments. It mounts
the same generated fixture PCK. Desktop can verify shared scene/property behavior;
native iOS checks remain blocked and the aggregate stays incomplete. Linux also
reports the unsupported mobile Harmony detour backend as blocked.
