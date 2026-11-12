#!/usr/bin/env python3
"""Compile the real Mono return-packing function with a poisoned register fixture.

--download verifies/applies the iOS patch to pinned upstream source, including a
negative control. --source tests the actual prepared source before building Mono.
The fixture replaces frame access and ABI metadata, not the function under test;
physical-device UCO and live Godot callback tests remain required.
"""
import argparse
import hashlib
import json
import os
from pathlib import Path
import shlex
import shutil
import subprocess
import tempfile
import urllib.request

ROOT = Path(__file__).resolve().parent.parent
PINNED_SOURCE_SHA256 = "6e18c1368e521071b56e9528f4e6e8b88bdf9cb980f9e55aa41da891507cd19e"
RELATIVE_SOURCE = Path("src/mono/mono/mini/mini-arm64.c")


def return_function(source):
    start = source.index("void\nmono_arch_set_native_call_context_ret (")
    end = source.index("\n/* Gets the arguments from ccontext", start)
    return source[start:end]


HARNESS = r'''
#include <assert.h>
#include <stdint.h>
#include <stdio.h>
#include <string.h>
#ifdef _MSC_VER
#include <malloc.h>
#define alloca _alloca
#else
#include <alloca.h>
#endif
typedef void *gpointer;
typedef void *MonoInterpFrameHandle;
typedef uint64_t host_mgreg_t;
typedef int8_t gint8;
typedef int16_t gint16;
typedef int32_t gint32;
typedef uint8_t guint8;
typedef uint16_t guint16;
typedef uint32_t guint32;
#define g_assert assert
enum { MONO_TYPE_VOID, MONO_TYPE_I1, MONO_TYPE_U1, MONO_TYPE_I2,
       MONO_TYPE_U2, MONO_TYPE_BOOLEAN, MONO_TYPE_CHAR, MONO_TYPE_I4,
       MONO_TYPE_I8, MONO_TYPE_PTR, MONO_TYPE_VALUETYPE, MONO_TYPE_R8 };
enum { ArgInIReg, ArgVtypeInIRegs, ArgInFReg, ArgHFA, ArgVtypeByRef };
typedef struct MonoType { int type, byref; struct MonoType *underlying; } MonoType;
typedef struct { MonoType *ret; } MonoMethodSignature;
typedef struct { int storage, reg; } ArgInfo;
typedef struct { ArgInfo ret; } CallInfo;
typedef struct { host_mgreg_t gregs[10], fregs[16]; } CallContext;
typedef struct { unsigned char bytes[32]; size_t size; } Frame;
typedef struct {
    void (*frame_arg_to_data)(MonoInterpFrameHandle, MonoMethodSignature *, int, gpointer);
} MonoEECallbacks;
static MonoType *mini_get_underlying_type(MonoType *type) {
    return type->underlying ? type->underlying : type;
}
static void copy_frame(MonoInterpFrameHandle frame, MonoMethodSignature *sig, int index, gpointer data) {
    Frame *f = frame;
    assert(index == -1);
    (void)sig;
    memcpy(data, f->bytes, f->size);
}
static const MonoEECallbacks *mini_get_interp_callbacks(void) {
    static const MonoEECallbacks callbacks = { copy_frame };
    return &callbacks;
}
static int arg_need_temp(ArgInfo *arg) { return arg->storage == ArgHFA ? 16 : 0; }
static gpointer arg_get_storage(CallContext *context, ArgInfo *arg) {
    return arg->storage == ArgInFReg ? &context->fregs[arg->reg] : &context->gregs[arg->reg];
}
static void arg_set_val(CallContext *context, ArgInfo *arg, gpointer value) {
    assert(arg->storage == ArgHFA);
    memcpy(&context->fregs[arg->reg], value, 16);
}
/* PRODUCTION_FUNCTION */
static int failures;
static void scalar_case(const char *name, MonoType type, size_t width, uint64_t bits,
                        uint32_t extended, int storage, int normalize) {
    const uint64_t poison = UINT64_C(0x123456789abcde00);
    CallContext context;
    memset(&context, 0xa5, sizeof(context));
    context.gregs[1] = poison;
    Frame frame = {{0}, width};
    memcpy(frame.bytes, &bits, width);
    MonoMethodSignature sig = { &type };
    CallInfo info = { {storage, 1} };
    mono_arch_set_native_call_context_ret(&context, &frame, &sig, &info, NULL);
    uint64_t expected = poison;
    memcpy(&expected, &bits, width);
#if defined(TARGET_IOS)
    if (normalize) expected = extended;
#else
    (void)normalize; (void)extended;
#endif
    if (context.gregs[1] != expected || context.gregs[0] != UINT64_C(0xa5a5a5a5a5a5a5a5) ||
        context.gregs[2] != UINT64_C(0xa5a5a5a5a5a5a5a5)) {
        if (failures < 12 || strcmp(name, "false") == 0)
            fprintf(stderr, "%s: actual=0x%016llx expected=0x%016llx\n", name,
                    (unsigned long long)context.gregs[1], (unsigned long long)expected);
        failures++;
    }
}
int main(void) {
    MonoType i1={MONO_TYPE_I1,0,NULL}, u1={MONO_TYPE_U1,0,NULL};
    MonoType i2={MONO_TYPE_I2,0,NULL}, u2={MONO_TYPE_U2,0,NULL};
    assert(mini_get_underlying_type(&u1) == &u1);
    for (unsigned bits=0; bits<256; bits++) {
        scalar_case("sbyte", i1, 1, bits, (uint32_t)(int32_t)(int8_t)bits, ArgInIReg, 1);
        scalar_case("byte", u1, 1, bits, bits, ArgInIReg, 1);
        scalar_case("byte-enum", (MonoType){MONO_TYPE_VALUETYPE,0,&u1}, 1, bits, bits, ArgInIReg, 1);
    }
    for (unsigned bits=0; bits<65536; bits++) {
        scalar_case("short", i2, 2, bits, (uint32_t)(int32_t)(int16_t)bits, ArgInIReg, 1);
        scalar_case("ushort", u2, 2, bits, bits, ArgInIReg, 1);
        scalar_case("short-enum", (MonoType){MONO_TYPE_VALUETYPE,0,&i2}, 2, bits,
                    (uint32_t)(int32_t)(int16_t)bits, ArgInIReg, 1);
    }
    scalar_case("false", (MonoType){MONO_TYPE_BOOLEAN,0,NULL}, 1, 0, 0, ArgInIReg, 1);
    scalar_case("true", (MonoType){MONO_TYPE_BOOLEAN,0,NULL}, 1, 1, 1, ArgInIReg, 1);
    scalar_case("char", (MonoType){MONO_TYPE_CHAR,0,NULL}, 2, 65535, 65535, ArgInIReg, 1);
    scalar_case("int32", (MonoType){MONO_TYPE_I4,0,NULL}, 4, 0x87654321, 0, ArgInIReg, 0);
    scalar_case("int64", (MonoType){MONO_TYPE_I8,0,NULL}, 8, UINT64_C(0xfedcba9876543210), 0, ArgInIReg, 0);
    scalar_case("pointer", (MonoType){MONO_TYPE_PTR,0,NULL}, 8, UINT64_C(0x123456789abcdef0), 0, ArgInIReg, 0);
    scalar_case("byref", (MonoType){MONO_TYPE_U1,1,NULL}, 8, UINT64_C(0x123456789abcdef0), 0, ArgInIReg, 0);
    scalar_case("tiny-aggregate", (MonoType){MONO_TYPE_VALUETYPE,0,NULL}, 1, 7, 0, ArgVtypeInIRegs, 0);
    scalar_case("aggregate", (MonoType){MONO_TYPE_VALUETYPE,0,NULL}, 8, UINT64_C(0x123456789abcdef0), 0, ArgVtypeInIRegs, 0);
    CallContext context, original;
    memset(&context, 0xa5, sizeof(context)); original = context;
    Frame frame = {{0}, 16};
    for (unsigned i=0; i<sizeof(frame.bytes); i++) frame.bytes[i]=(unsigned char)i;
    MonoType type = {MONO_TYPE_R8,0,NULL};
    MonoMethodSignature sig = {&type};
    CallInfo info = {{ArgInFReg,1}};
    frame.size=8;
    mono_arch_set_native_call_context_ret(&context, &frame, &sig, &info, NULL);
    assert(memcmp(&context.fregs[1], frame.bytes, 8)==0);
    assert(memcmp(context.gregs, original.gregs, sizeof(context.gregs))==0);
    frame.size=16; info.ret.storage=ArgHFA; type.type=MONO_TYPE_VALUETYPE;
    mono_arch_set_native_call_context_ret(&context, &frame, &sig, &info, NULL);
    assert(memcmp(&context.fregs[1], frame.bytes, 16)==0);
    assert(memcmp(context.gregs, original.gregs, sizeof(context.gregs))==0);
    unsigned char indirect[32];
    frame.size=32; info.ret.storage=ArgVtypeByRef;
    mono_arch_set_native_call_context_ret(&context, &frame, &sig, &info, indirect);
    assert(memcmp(indirect, frame.bytes, 32)==0);
    original=context; type.type=MONO_TYPE_VOID;
    mono_arch_set_native_call_context_ret(&context, &frame, &sig, &info, NULL);
    assert(memcmp(&context, &original, sizeof(context))==0);
    if (!failures) puts("PASS: exhaustive narrow scalars/enums; full-width/byref/aggregate/float/HFA/indirect/void preserved");
    return failures ? 1 : 0;
}
'''


def compile_and_run(source, work, ios, expect_failure=False):
    unit = work / ("returns-ios.c" if ios else "returns-android.c")
    unit.write_text(HARNESS.replace("/* PRODUCTION_FUNCTION */", return_function(source)), encoding="utf-8")
    executable = unit.with_suffix(".exe" if os.name == "nt" else "")
    compiler = shlex.split(os.environ.get("CC", "cc"), posix=os.name != "nt")
    if not shutil.which(compiler[0]):
        raise RuntimeError("A C compiler is required; return-packing verification is incomplete")
    target = "TARGET_IOS=1" if ios else "TARGET_ANDROID=1"
    if Path(compiler[0]).stem.lower() == "cl":
        options = ["/nologo", "/std:c11", "/O2", "/W4", "/WX", "/D" + target, str(unit),
                   "/Fe:" + str(executable), "/Fo:" + str(unit.with_suffix(".obj"))]
    else:
        options = ["-std=c11", "-O2", "-fno-strict-aliasing", "-Wall", "-Wextra", "-Werror",
                   "-D" + target, str(unit), "-o", str(executable)]
    subprocess.run(compiler + options, check=True)
    result = subprocess.run([str(executable)], capture_output=True, text=True)
    if expect_failure:
        if result.returncode != 1 or "false: actual=0x123456789abcde00" not in result.stderr:
            raise RuntimeError("Pinned-source negative control did not reproduce the stale-register failure")
        print("PASS: unpatched pinned source reproduces the false/narrow-return failure")
    else:
        if result.returncode:
            raise RuntimeError(result.stderr)
        print(("iOS: " if ios else "Android (unchanged): ") + result.stdout.strip())


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    group = parser.add_mutually_exclusive_group(required=True)
    group.add_argument("--source", type=Path)
    group.add_argument("--download", action="store_true")
    args = parser.parse_args()
    with tempfile.TemporaryDirectory(prefix="sts2-mono-returns-") as directory:
        work = Path(directory)
        if args.download:
            recipe = json.loads((ROOT / "patches/mono/runtime.json").read_text())
            url = f"https://raw.githubusercontent.com/dotnet/runtime/{recipe['commit']}/{RELATIVE_SOURCE.as_posix()}"
            with urllib.request.urlopen(url, timeout=30) as response:
                original = response.read()
            if hashlib.sha256(original).hexdigest() != PINNED_SOURCE_SHA256:
                raise RuntimeError("Pinned Mono source hash mismatch; review the return ABI before updating this test")
            path = work / RELATIVE_SOURCE
            path.parent.mkdir(parents=True)
            path.write_bytes(original)
            compile_and_run(original.decode(), work, ios=True, expect_failure=True)
            subprocess.run(["git", "-C", str(work), "apply", str(ROOT / "patches/mono/ios-arm64-interpreter-returns.patch")], check=True)
        else:
            path = args.source
        source = path.read_text(encoding="utf-8")
        compile_and_run(source, work, ios=True)
        compile_and_run(source, work, ios=False)


if __name__ == "__main__":
    main()
