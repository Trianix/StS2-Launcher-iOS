import importlib.util
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest

spec = importlib.util.spec_from_file_location("verify_investigation", Path(__file__).with_name("verify-ios-investigation-bundle.py"))
module = importlib.util.module_from_spec(spec)
spec.loader.exec_module(module)


class RuntimeInvestigationPackagingTests(unittest.TestCase):
    def test_packaged_fixture_survives_pck_generation(self):
        with tempfile.TemporaryDirectory() as folder:
            output = Path(folder) / "bootstrap.pck"
            subprocess.run([sys.executable, str(Path(__file__).with_name("make-bootstrap-pck.py")), str(output)], check=True)
            data = output.read_bytes()
            files = module.pck_files(data)
            self.assertIn(b'res://RuntimeProbeFixture.cs', files["res://runtime_probe.tscn"])
            self.assertIn(b'[Export]', files["res://RuntimeProbeFixture.cs"])
            damaged = bytearray(data)
            damaged[struct_file_base(data)] ^= 0xff
            with self.assertRaisesRegex(ValueError, "corrupt"):
                module.pck_files(damaged)


def struct_file_base(data):
    import struct
    return struct.unpack_from("<Q", data, 24)[0]


if __name__ == "__main__":
    unittest.main()
