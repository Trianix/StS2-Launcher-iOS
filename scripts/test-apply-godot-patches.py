import contextlib
import difflib
import importlib.util
import io
from pathlib import Path
import subprocess
import tempfile
import unittest

spec = importlib.util.spec_from_file_location("godot_patches", Path(__file__).with_name("apply-godot-patches.py"))
module = importlib.util.module_from_spec(spec)
spec.loader.exec_module(module)


class GodotPatchStackTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.addCleanup(self.temporary.cleanup)
        root = Path(self.temporary.name)
        self.source = root / "source with spaces"
        self.source.mkdir()
        self.patches = root / "patches"
        self.patches.mkdir()
        self.file = self.source / "core.cpp"
        self.base = "before\nproperty getter\nafter\n"
        self.first = "before\nproperty getter with trace\nafter\n"
        self.final = "before\ncallback audit\nproperty getter with trace\nafter\n"
        self.file.write_text(self.base, encoding="utf-8", newline="\n")
        self.git("init", "-q")
        self.git("config", "core.autocrlf", "false")
        self.git("add", "core.cpp")
        self.git("-c", "user.name=Patch Tests", "-c", "user.email=tests@example.invalid",
                 "-c", "commit.gpgsign=false", "commit", "-qm", "baseline")
        self.write_patch("01-trace.patch", self.base, self.first)
        self.write_patch("02-audit.patch", self.first, self.final)

    def git(self, *args):
        return subprocess.check_output(["git", "-C", str(self.source), *args], stderr=subprocess.PIPE)

    def write_patch(self, name, before, after):
        patch = "".join(difflib.unified_diff(before.splitlines(True), after.splitlines(True),
                                           fromfile="a/core.cpp", tofile="b/core.cpp"))
        (self.patches / name).write_text(patch, encoding="utf-8", newline="\n")

    def apply(self):
        with contextlib.redirect_stdout(io.StringIO()):
            module.apply_patches(self.source, self.patches)

    def test_overlapping_stack_survives_setup_native_and_api_build_calls(self):
        index_before = self.git("ls-files", "--stage")
        generated = self.source / "build-output.gen.cpp"
        generated.write_text("keep generated glue")
        self.apply()
        # This is the old failure: a later patch invalidates an earlier patch's
        # reverse-check context even though the complete stack is correct.
        reverse = subprocess.run(["git", "-C", str(self.source), "apply", "--reverse", "--check",
                                  str(self.patches / "01-trace.patch")], capture_output=True)
        self.assertNotEqual(0, reverse.returncode)
        self.apply()
        self.apply()
        self.assertEqual(self.final, self.file.read_text())
        self.assertEqual(index_before, self.git("ls-files", "--stage"))
        self.assertEqual("keep generated glue", generated.read_text())

    def test_partial_prefix_resumes(self):
        self.git("apply", str(self.patches / "01-trace.patch"))
        self.apply()
        self.assertEqual(self.final, self.file.read_text())

    def test_windows_checkout_text_filters_are_respected(self):
        self.git("config", "core.autocrlf", "true")
        self.file.write_bytes(self.base.replace("\n", "\r\n").encode())
        self.apply()
        self.apply()
        self.assertEqual(self.final, self.file.read_text())

    def test_source_drift_is_rejected_without_overwriting_edits(self):
        self.apply()
        self.file.write_text(self.final + "local edit\n")
        with self.assertRaisesRegex(RuntimeError, "not a valid prefix"):
            self.apply()
        self.assertEqual(self.final + "local edit\n", self.file.read_text())

    def test_changed_recipe_cannot_silently_reuse_old_source(self):
        self.apply()
        self.write_patch("02-audit.patch", self.first, self.final.replace("audit", "new audit"))
        with self.assertRaisesRegex(RuntimeError, "not a valid prefix"):
            self.apply()
        self.assertEqual(self.final, self.file.read_text())

    def test_invalid_late_patch_does_not_partially_modify_source(self):
        self.write_patch("02-audit.patch", "wrong baseline\n", "wrong output\n")
        with self.assertRaisesRegex(RuntimeError, "does not apply to HEAD"):
            self.apply()
        self.assertEqual(self.base, self.file.read_text())

    def test_deleted_and_new_files_are_verified_on_repeated_calls(self):
        (self.patches / "03-files.patch").write_text(
            "--- /dev/null\n+++ b/new.cpp\n@@ -0,0 +1 @@\n+new file\n"
            "--- a/core.cpp\n+++ /dev/null\n@@ -1,4 +0,0 @@\n"
            "-before\n-callback audit\n-property getter with trace\n-after\n", newline="\n")
        self.apply()
        self.apply()
        self.assertFalse(self.file.exists())
        self.assertEqual("new file\n", (self.source / "new.cpp").read_text())


if __name__ == "__main__":
    unittest.main()
