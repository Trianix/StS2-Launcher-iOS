import importlib.util
import json
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch

spec = importlib.util.spec_from_file_location("native_fingerprint", Path(__file__).with_name("ios-native-fingerprint.py"))
module = importlib.util.module_from_spec(spec)
spec.loader.exec_module(module)


class NativeFingerprintTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        for relative in ("scripts/ios-native-fingerprint.py", "scripts/build-godot-ios.sh", "scripts/build-ios-mono.sh",
                         "scripts/setup-godot-source.sh", "scripts/godot-source-utils.sh", "scripts/apply-godot-patches.py", "scripts/requirements-godot-build.txt",
                         "patches/godot/runtime.patch", "patches/mono/runtime.json"):
            target = self.root / relative
            target.parent.mkdir(parents=True, exist_ok=True)
            target.write_text("first")
        self.patchers = [patch.object(module, "ROOT", self.root),
                         patch.object(module, "__file__", str(self.root / "scripts/ios-native-fingerprint.py")),
                         patch.object(module.subprocess, "check_output", return_value="Xcode test")]
        for patcher in self.patchers:
            patcher.start()
            self.addCleanup(patcher.stop)

    def test_patch_and_toolchain_changes_invalidate_native_reuse(self):
        first = module.fingerprint("godot")
        self.assertEqual(first, module.fingerprint("godot"))
        (self.root / "patches/godot/runtime.patch").write_text("changed")
        second = module.fingerprint("godot")
        self.assertNotEqual(first, second)
        with patch.object(module.subprocess, "check_output", return_value="Xcode newer"):
            self.assertNotEqual(second, module.fingerprint("godot"))

    def test_managed_changes_preserve_native_reuse(self):
        first = module.fingerprint("godot")
        managed = self.root / "src/STS2Mobile/Patches/Fix.cs"
        managed.parent.mkdir(parents=True)
        managed.write_text("managed fix")
        self.assertEqual(first, module.fingerprint("godot"))

    def test_patch_applicator_changes_invalidate_native_reuse(self):
        first = module.fingerprint("godot")
        (self.root / "scripts/apply-godot-patches.py").write_text("new stack validation")
        self.assertNotEqual(first, module.fingerprint("godot"))

    def test_mono_runtime_manifest_changes_invalidate_native_reuse(self):
        first = module.fingerprint("mono")
        (self.root / "patches/mono/runtime.json").write_text("updated pinned runtime recipe")
        self.assertNotEqual(first, module.fingerprint("mono"))

    def test_ios_return_patch_invalidates_mono_only(self):
        mono = module.fingerprint("mono")
        godot = module.fingerprint("godot")
        target = self.root / "patches/mono/ios-arm64-interpreter-returns.patch"
        target.write_text("iOS return packing repair")
        self.assertNotEqual(mono, module.fingerprint("mono"))
        self.assertEqual(godot, module.fingerprint("godot"))
        previous = module.fingerprint("mono")
        target.write_text("revised iOS return packing")
        self.assertNotEqual(previous, module.fingerprint("mono"))

    def test_record_rejects_changed_or_unfingerprinted_library(self):
        library = self.root / "libgodot.a"
        library.write_bytes(b"native archive")
        with patch("sys.argv", ["fingerprint", "check", "godot", str(library)]):
            self.assertEqual(1, module.main())
        with patch("sys.argv", ["fingerprint", "record", "godot", str(library)]):
            self.assertEqual(0, module.main())
        with patch("sys.argv", ["fingerprint", "check", "godot", str(library)]):
            self.assertEqual(0, module.main())
            library.write_bytes(b"stale or damaged")
            self.assertEqual(1, module.main())


if __name__ == "__main__":
    unittest.main()
