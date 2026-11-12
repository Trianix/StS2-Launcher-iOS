#!/usr/bin/env python3
"""Write reproducible runtime provenance beside the iOS bootstrap assemblies."""
import hashlib
import json
from pathlib import Path
import subprocess
import sys


def sha256(path):
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def main():
    root, bcl_dir, assemblies_dir, bootstrap_pck = map(Path, sys.argv[1:5])
    native = root / "ios" / "libs"
    try:
        revision = subprocess.check_output(["git", "-C", str(root), "rev-parse", "HEAD"], text=True).strip()
        diff = subprocess.check_output(["git", "-C", str(root), "diff", "--binary", "HEAD"])
    except (OSError, subprocess.CalledProcessError) as exc:
        raise SystemExit(f"Cannot record source revision/diff: {exc}")

    files = {}
    for label, path in {
        "godotArchive": native / "libgodot.ios.template_release.arm64.a",
        "monoArchive": native / "libmonosgen-2.0.a",
        "bootstrapPck": bootstrap_pck,
        "monoRuntimeRecipe": root / "patches" / "mono" / "runtime.json",
        "monoPatch": root / "patches" / "mono" / "android-net9.patch",
        "monoIosReturnPatch": root / "patches" / "mono" / "ios-arm64-interpreter-returns.patch",
        "godotPatch": root / "patches" / "godot" / "sts2-android-runtime.patch",
        "godotBuildScript": root / "scripts" / "build-godot-ios.sh",
        "monoBuildScript": root / "scripts" / "build-ios-mono.sh",
        "nativeFingerprintRecipe": root / "scripts" / "ios-native-fingerprint.py",
    }.items():
        if not path.is_file():
            raise SystemExit(f"Required runtime provenance input is missing: {path}")
        files[label] = sha256(path)

    framework_root = root / "ios" / "STS2Launcher" / "Frameworks"
    native_files = {}
    for path in sorted(framework_root.rglob("*")):
        if path.is_file():
            native_files[path.relative_to(framework_root).as_posix()] = sha256(path)
    if not native_files:
        raise SystemExit(f"No native framework outputs were found under {framework_root}")

    library_files = {}
    for path in sorted(native.glob("*")):
        if path.is_file():
            library_files[path.name] = sha256(path)

    downloaded = {}
    dependency_cache = root / "tmp" / "ios-deps"
    for path in sorted(dependency_cache.glob("*")):
        if path.is_file() and path.suffix.lower() in {".apk", ".nupkg", ".zip"}:
            downloaded[path.name] = sha256(path)

    assemblies = {}
    for directory in (assemblies_dir, bcl_dir):
        if not directory.is_dir():
            raise SystemExit(f"Required assembly directory is missing: {directory}")
        for path in sorted(directory.glob("*.dll")):
            assemblies[f"{directory.name}/{path.name}"] = sha256(path)

    recipe = json.loads((root / "patches" / "mono" / "runtime.json").read_text(encoding="utf-8"))
    manifest = {
        "schema": 1,
        "sourceRevision": revision,
        "sourceDiffSha256": hashlib.sha256(diff).hexdigest(),
        "sourceTreeDirty": bool(diff),
        "godotVersion": "4.5.1-stable",
        "godotSourceCommit": "f62fdbde15035c5576dad93e586201f4d41ef0cb",
        "monoVersion": recipe["version"],
        "monoSourceCommit": recipe["commit"],
        "target": "ios-arm64",
        "executionModePolicy": "JIT only when the native launch capability check confirms permission; otherwise force interpreter",
        "files": files,
        "nativeLibraryArtifacts": library_files,
        "nativeFrameworkFiles": native_files,
        "downloadedDependencyArchives": downloaded,
        "assemblies": assemblies,
        "godotCallbackContract": json.loads((bcl_dir / "godot-callback-contract.json").read_text(encoding="utf-8"))
            if (bcl_dir / "godot-callback-contract.json").is_file() else {"status": "unavailable"},
    }
    output = bcl_dir / "runtime-provenance.json"
    output.write_text(json.dumps(manifest, indent=2, sort_keys=True) + "\n", encoding="utf-8")
    print(f"Wrote iOS runtime provenance to {output}")


if __name__ == "__main__":
    main()
