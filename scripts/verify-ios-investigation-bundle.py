#!/usr/bin/env python3
"""Reject IPAs missing the executable fixtures, native audit API or PCK files."""
import hashlib
import json
from pathlib import Path
import struct
import sys
import zipfile


def pck_files(data):
    if len(data) < 104 or struct.unpack_from("<II", data) != (0x43504447, 3):
        raise ValueError("Expected a Godot v3 bootstrap PCK")
    file_base, directory = struct.unpack_from("<QQ", data, 24)
    count, = struct.unpack_from("<I", data, directory)
    cursor = directory + 4
    files = {}
    for _ in range(count):
        length, = struct.unpack_from("<I", data, cursor)
        cursor += 4
        name = data[cursor:cursor + length].rstrip(b"\0").decode("utf-8")
        cursor += length
        offset, size = struct.unpack_from("<QQ", data, cursor)
        digest = data[cursor + 16:cursor + 32]
        cursor += 36
        payload = data[file_base + offset:file_base + offset + size]
        if len(payload) != size or hashlib.md5(payload).digest() != digest:
            raise ValueError("PCK payload is missing or corrupt: " + name)
        if name in files:
            raise ValueError("Duplicate PCK path: " + name)
        files[name] = payload
    return files


def verify(ipa):
    with zipfile.ZipFile(ipa) as archive:
        roots = [name[:-len("Info.plist")] for name in archive.namelist()
                 if name.startswith("Payload/") and name.count("/") == 2 and name.endswith(".app/Info.plist")]
        if len(roots) != 1:
            raise ValueError("Expected exactly one app in IPA")
        root = roots[0]
        def read(name):
            return archive.read(root + name)
        manifest = json.loads(read("dotnet_bcl/runtime-provenance.json"))
        if manifest.get("godotCallbackContract", {}).get("status") != "source-declarations-match":
            raise ValueError("Pinned Godot callback declaration audit is missing")
        for name in ("STS2Mobile.dll", "RuntimeContractFixtures.dll", "GodotSharp.dll"):
            for folder in ("assemblies", "dotnet_bcl"):
                path = folder + "/" + name
                if hashlib.sha256(read(path)).hexdigest() != manifest["assemblies"][path]:
                    raise ValueError("Bundled assembly does not match runtime manifest: " + path)
            if read("assemblies/" + name) != read("dotnet_bcl/" + name):
                raise ValueError("Assembly copies disagree: " + name)
        files = pck_files(read("bootstrap.pck"))
        for name in ("res://RuntimeProbeFixture.cs", "res://runtime_probe.tscn"):
            if name not in files:
                raise ValueError("Missing packaged fixture: " + name)
        if read("bootstrap.pck") != read("STS2Launcher.pck"):
            raise ValueError("Bootstrap PCK copies disagree")
        executable = read("STS2Launcher")
        for symbol in (b"sts2_ios_callback_layout_v1", b"sts2_ios_callback_get_v1", b"sts2_ios_callback_set_v1",
                       b"sts2_probe_byte_v1", b"sts2_probe_return_register_v1", b"sts2_probe_large_v1", b"sts2_probe_thermal_v1"):
            if symbol not in executable:
                raise ValueError("Native investigation export missing: " + symbol.decode())
    print("Investigation IPA contents verified: " + str(ipa))


if __name__ == "__main__":
    verify(Path(sys.argv[1]))
