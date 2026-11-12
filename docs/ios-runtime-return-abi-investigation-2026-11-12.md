# iOS interpreter return ABI: consolidated device evidence

## Candidate and provenance

Reviewed `20261112-093204-1e633319.zip`, SHA-256
`073638440d1922b00159b6de5b060869b3af30c75853d08d91bf69f1230707ef`.
The archive completed normally and includes the report, app log, manifest and
screenshot. Candidate revision: `2845721edf4d3fd8fa5723a6e046ae8bb24b14fa`.
Device reports iPhone14,3 and iOS 26.6.2. Native startup confirms interpreter mode
and no debugger; the earlier generic host JIT readiness message is not active-mode
evidence. Game and GodotSharp hashes are recorded in the supplied manifest.

## Observations

Three of five required groups pass; two fail. This is diagnostic evidence, not
release qualification.

| Area | Device evidence |
| --- | --- |
| Live callback layout | Table size 296, Get offset 208, Set offset 200; managed/native addresses and Variant size match. |
| Plain Control | All 13 property/tween cases pass. |
| C# controls | Empty subclass and game subclasses share inherited generic/indexed property and tween failures. Typed properties work. |
| Packaged scene | Export serialization/access, native serialized position, ownership, unique names, method callbacks and lifecycle pass. Inherited generic properties/tweens fail here too. |
| Independent scalar callbacks | Eight existing cases pass, including structs, pointer/GC and contained exceptions. |
| Harmony | All 14 fixture behavior/cleanup checks pass. This does not establish arbitrary gameplay patch coverage. |

The raw callback audit distinguishes a handled Nil export from an unhandled
property. On later repeated calls, unhandled properties return x0 `0x200` and
handled properties return `0x201`. Their low bytes are correct. The original
diagnostic assertions examined only that byte and therefore passed those cases.
Managed property tracing reports false/unhandled before the native production
caller reports true/handled and skips native-property fallback. Early empty-node
calls also return zero: the result depends on prior register contents.

The ten-second idle measurement records 1,201 frames, p95 8.496 ms, p99 8.558 ms,
and maximum 11.291 ms. Physical memory footprint is unchanged at 206,750,688 bytes.
This is neither combat performance nor sustained thermal qualification. The
reported zero managed allocation counter is not proof of zero allocations.

## Shared mechanism and repair

In pinned Mono `3c298d9f00936d651cc47d221762474e25277672`,
`mono_arch_set_native_call_context_ret` copies the managed return value directly
into the saved native register slot. `stackval_to_data` writes only one byte for
the byte-backed Godot boolean enum. Incoming argument bits survive above that
byte, and the iOS native-to-interpreter trampoline restores the whole register.

Darwin ARM64 extends narrow integer returns, including enum underlying types.
This is explicit in Clang's
[AArch64 return classification](https://github.com/llvm/llvm-project/blob/llvmorg-20.1.8/clang/lib/CodeGen/Targets/AArch64.cpp#L448-L474).
The observed raw register and production fallback failure agree with that source
mechanism. The native audit and production trace are separate invocations;
device verification of the repair is still required.

The separate iOS-only Mono patch extends signed/unsigned byte and short scalar
returns through w0 after copying the managed value. It resolves enum underlying
types and excludes byrefs, structs, pointers, other widths, floating-point and
indirect returns. Neither the Android source recipe nor its patch is changed.
There is no Godot property workaround and no universal register mask.

## Verification and decision gate

The build compiles the actual patched return-packing function with a controlled
frame/register fixture. It exhausts byte/short values and enum cases, checks
unaffected return categories, and verifies unchanged non-iOS behavior. The pinned
unpatched source is a negative control that must reproduce the contaminated false
return. This is a packing regression test, not a substitute for real Mono calls.

The device suite now checks the low 32 bits of live Get/Set returns. Additional
UnmanagedCallersOnly callbacks receive deliberately contaminated full-width
arguments, exercise signed/unsigned narrow returns and boolean enums across GC,
and verify that full-width integer/pointer results survive. Existing scalar,
scene, Harmony and property/tween groups continue to run independently.

Repeat the complete investigation using the packaged marker and send its dated
ZIP. All five required core groups must pass before moving to actual scene/top-bar
regressions and representative combat measurement. If this repair fails to explain
the remaining boundary, apply the two-round review checkpoint in `PLAN.md` rather
than expanding individual gameplay workarounds. Keep existing workarounds until
their real-game regressions pass. This evidence supports continuing the current
port; it does not yet justify a rewrite, JIT migration or a 60 fps promise.

Local validation for the repair: the pinned-source negative control reproduced
the fault; the patched function passed the exhaustive C fixture with both iOS
and non-iOS defines. All 235 managed tests passed. Desktop Godot reported 65
control assertions, 19 packaged-scene assertions and 14 Harmony assertions
passing, with zero diagnostic errors. Required native checks remain visibly
blocked on desktop. Godot also reported the sandbox's root-certificate-store
warning. The complete repaired runtime still needs the physical-device rerun.
