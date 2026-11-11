"""Bind native library reuse to the recipe, platform options and Xcode SDK."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import subprocess

ROOT = Path(__file__).resolve().parent.parent


def fingerprint(kind):
    inputs = [Path(__file__), ROOT / f"scripts/build-{kind}-ios.sh"] if kind == "godot" else [Path(__file__), ROOT / "scripts/build-ios-mono.sh"]
    inputs += sorted((ROOT / "patches" / ("godot" if kind == "godot" else "mono")).rglob("*"))
    if kind == "godot":
        inputs += [ROOT / "scripts/setup-godot-source.sh", ROOT / "scripts/godot-source-utils.sh", ROOT / "scripts/requirements-godot-build.txt"]
    hashes = {str(p.relative_to(ROOT)): hashlib.sha256(p.read_bytes()).hexdigest() for p in inputs if p.is_file()}
    tools = {}
    for tool in (["xcodebuild", "-version"], ["xcrun", "--sdk", "iphoneos", "--show-sdk-version"]):
        tools[tool[0]] = subprocess.check_output(tool, text=True).strip()
    payload = {"kind": kind, "files": hashes, "toolchain": tools, "target": os.environ.get("GODOT_TARGET", "template_release"),
               "expectedGodotVersion": os.environ.get("EXPECTED_GODOT_VERSION", "4.5.1-stable")}
    return hashlib.sha256(json.dumps(payload, sort_keys=True).encode()).hexdigest()


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("action", choices=("check", "record"))
    parser.add_argument("kind", choices=("godot", "mono"))
    parser.add_argument("library", type=Path)
    args = parser.parse_args()
    marker = args.library.with_suffix(args.library.suffix + ".fingerprint.json")
    expected = fingerprint(args.kind)
    if args.action == "record":
        marker.write_text(json.dumps({"fingerprint": expected, "librarySha256": hashlib.sha256(args.library.read_bytes()).hexdigest()}, indent=2))
        return 0
    if os.environ.get("STS2_FORCE_NATIVE_REBUILD") == "1" or not args.library.is_file() or not marker.is_file():
        return 1
    try:
        saved = json.loads(marker.read_text())
        return 0 if saved["fingerprint"] == expected and saved["librarySha256"] == hashlib.sha256(args.library.read_bytes()).hexdigest() else 1
    except (OSError, ValueError, KeyError):
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
