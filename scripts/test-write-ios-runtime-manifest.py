import importlib.util
import json
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch

spec = importlib.util.spec_from_file_location("runtime_manifest", Path(__file__).with_name("write-ios-runtime-manifest.py"))
module = importlib.util.module_from_spec(spec)
spec.loader.exec_module(module)


class RuntimeManifestTests(unittest.TestCase):
    def test_manifest_records_build_recipe_and_bundled_hashes(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            bcl = root / "dotnet_bcl"
            assemblies = root / "assemblies"
            native = root / "ios/libs"
            frameworks = root / "ios/STS2Launcher/Frameworks"
            recipe = root / "patches/mono/runtime.json"
            for directory in (bcl, assemblies, native, frameworks, recipe.parent, root / "patches/godot", root / "scripts"):
                directory.mkdir(parents=True, exist_ok=True)
            (native / "libgodot.ios.template_release.arm64.a").write_bytes(b"godot")
            (native / "libmonosgen-2.0.a").write_bytes(b"mono")
            (frameworks / "libsample.framework/libsample").parent.mkdir(parents=True)
            (frameworks / "libsample.framework/libsample").write_bytes(b"framework")
            (root / "bootstrap.pck").write_bytes(b"pck")
            recipe.write_text(json.dumps({"version": "9.0.7", "commit": "pinned-commit"}))
            for relative in ("patches/mono/android-net9.patch", "patches/mono/ios-arm64-interpreter-returns.patch", "patches/godot/sts2-android-runtime.patch",
                             "scripts/build-godot-ios.sh", "scripts/build-ios-mono.sh", "scripts/ios-native-fingerprint.py"):
                (root / relative).write_text(relative)
            (bcl / "GodotSharp.dll").write_bytes(b"godotsharp")
            (assemblies / "STS2Mobile.dll").write_bytes(b"mobile")
            argv = ["manifest", str(root), str(bcl), str(assemblies), str(root / "bootstrap.pck")]
            with patch.object(module.sys, "argv", argv), patch.object(
                module.subprocess, "check_output", side_effect=["revision", b"working diff"]
            ):
                module.main()

            manifest = json.loads((bcl / "runtime-provenance.json").read_text())
            self.assertEqual("revision", manifest["sourceRevision"])
            self.assertTrue(manifest["sourceTreeDirty"])
            self.assertEqual("pinned-commit", manifest["monoSourceCommit"])
            self.assertEqual(module.sha256(root / "patches/mono/ios-arm64-interpreter-returns.patch"), manifest["files"]["monoIosReturnPatch"])
            self.assertEqual(module.sha256(native / "libgodot.ios.template_release.arm64.a"), manifest["files"]["godotArchive"])
            self.assertEqual(module.sha256(bcl / "GodotSharp.dll"), manifest["assemblies"]["dotnet_bcl/GodotSharp.dll"])
            self.assertEqual(module.sha256(assemblies / "STS2Mobile.dll"), manifest["assemblies"]["assemblies/STS2Mobile.dll"])


if __name__ == "__main__":
    unittest.main()
