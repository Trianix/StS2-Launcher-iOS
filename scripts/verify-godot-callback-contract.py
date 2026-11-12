#!/usr/bin/env python3
"""Audit the full native/managed callback table in the pinned Godot checkout."""
import hashlib
import json
from pathlib import Path
import re
import sys


def audit(root):
    paths = {
        "managed": root / "modules/mono/glue/GodotSharp/GodotSharp/Core/Bridge/ManagedCallbacks.cs",
        "native": root / "modules/mono/mono_gd/gd_mono_cache.h",
        "types": root / "modules/mono/glue/GodotSharp/GodotSharp/Core/NativeInterop/InteropStructs.cs",
    }
    sources = {key: path.read_text(encoding="utf-8") for key, path in paths.items()}
    managed = re.findall(r"public delegate\* unmanaged<[^;]+>\s+(\w+);", sources["managed"])
    native = re.findall(r"^\s+Func\w+\s+(\w+);", sources["native"], re.MULTILINE)
    if not managed or managed != native:
        raise ValueError(f"Managed/native callback table order mismatch:\nmanaged={managed}\nnative={native}")
    if not re.search(r"enum godot_bool\s*:\s*byte", sources["types"]):
        raise ValueError("The expected one-byte managed Godot boolean representation changed")
    for name, method in (("Get", "Get"), ("Set", "Set")):
        if not re.search(rf"delegate\* unmanaged<IntPtr, godot_string_name\*, godot_variant\*, godot_bool> CSharpInstanceBridge_{name};", sources["managed"]):
            raise ValueError("Managed property callback signature changed: " + name)
        if not re.search(rf"using FuncCSharpInstanceBridge_{name}\s*=\s*bool\(", sources["native"]):
            raise ValueError("Native property callback return declaration changed: " + name)
        if f"CSharpInstanceBridge_{name} = &CSharpInstanceBridge.{method}" not in sources["managed"]:
            raise ValueError("Managed property callback registration changed: " + name)
    return {
        "schema": 1, "status": "source-declarations-match", "callbackCount": len(managed), "callbackOrder": managed,
        "managedReturn": "godot_bool : byte", "nativeReturn": "bool",
        "limitation": "Declaration audit only; physical iOS must verify layout, live addresses and raw return bytes.",
        "sourceSha256": {key: hashlib.sha256(path.read_bytes()).hexdigest() for key, path in paths.items()},
    }


if __name__ == "__main__":
    result = audit(Path(sys.argv[1]))
    if len(sys.argv) > 2:
        output = Path(sys.argv[2])
        output.parent.mkdir(parents=True, exist_ok=True)
        output.write_text(json.dumps(result, indent=2) + "\n", encoding="utf-8")
    print(f"Godot callback declarations match: {result['callbackCount']} entries; device ABI qualification still required.")
