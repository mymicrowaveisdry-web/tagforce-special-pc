"""Synthetic integrity/selection tests; no original-game observations."""
import hashlib
import json
from pathlib import Path
import tempfile
import unittest

from verify_getter_fixture import VerificationError, verify


class GetterFixtureTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.addCleanup(self.temporary.cleanup)
        self.root = Path(self.temporary.name)
        self.capture = {
            "binary_sha256": "a" * 64,
            "samples": [{"card_id": 1, "runtime": {"attack": 30},
                         "expected": {"attack": 999}, "fields_match": False}],
        }
        self.inputs = {"sourceName": "cardinfo_jpn.ehp", "sourceSha256": "b" * 64,
                       "index": {"rows": [{"cardId": 1, "rawPropertyWord0": 49153}]}}
        self.fixture = {
            "schemaVersion": 1, "module": "EBOOT.BIN", "functionAddress": "0x0881F334",
            "sourceSha256": "a" * 64,
            "capture": {"path": "private/capture.json"},
            "packedInputs": {"path": "private/inputs.json", "sourceSha256": "b" * 64,
                             "sourceName": "cardinfo_jpn.ehp"},
            "reproduction": {"localCaptureScript": "private/probe.py"},
            "samples": [{"cardId": 1, "runtimeV0": 30, "rawPropertyWord0": 49153}],
        }
        self.write_sources()

    def write(self, path, data):
        target = self.root / path
        target.parent.mkdir(parents=True, exist_ok=True)
        content = data if isinstance(data, bytes) else json.dumps(data).encode()
        target.write_bytes(content)
        return hashlib.sha256(content).hexdigest()

    def write_sources(self):
        self.fixture["capture"]["sha256"] = self.write("private/capture.json", self.capture)
        self.fixture["packedInputs"]["sha256"] = self.write("private/inputs.json", self.inputs)
        self.fixture["reproduction"]["scriptSha256"] = self.write("private/probe.py", b"# synthetic\n")
        self.write("config/reference/card-attack.json", self.fixture)

    def test_uses_recorded_runtime_and_separate_packed_input_not_expected_flags(self):
        self.assertEqual(1, verify(self.root)["samples"])

    def test_tampered_capture_hash_rejected(self):
        self.capture["samples"][0]["runtime"]["attack"] = 40
        self.write("private/capture.json", self.capture)
        with self.assertRaisesRegex(VerificationError, "SHA-256 mismatch"):
            verify(self.root)

    def test_changed_projected_output_or_word_rejected(self):
        for key, value in (("runtimeV0", 40), ("rawPropertyWord0", 1)):
            with self.subTest(field=key):
                original = self.fixture["samples"][0][key]
                self.fixture["samples"][0][key] = value
                self.write("config/reference/card-attack.json", self.fixture)
                with self.assertRaisesRegex(VerificationError, "projection mismatch"):
                    verify(self.root)
                self.fixture["samples"][0][key] = original

    def test_rehashed_wrong_module_or_archive_rejected(self):
        self.capture["binary_sha256"] = "c" * 64
        self.write_sources()
        with self.assertRaisesRegex(VerificationError, "module identity mismatch"):
            verify(self.root)
        self.capture["binary_sha256"] = "a" * 64
        self.inputs["sourceSha256"] = "c" * 64
        self.write_sources()
        with self.assertRaisesRegex(VerificationError, "archive identity mismatch"):
            verify(self.root)

    def test_duplicate_projection_rejected(self):
        self.fixture["samples"].append(dict(self.fixture["samples"][0]))
        self.write("config/reference/card-attack.json", self.fixture)
        with self.assertRaisesRegex(VerificationError, "Duplicate"):
            verify(self.root)

    def test_path_traversal_rejected(self):
        self.fixture["capture"]["path"] = "../outside.json"
        self.write("config/reference/card-attack.json", self.fixture)
        with self.assertRaisesRegex(VerificationError, "within the repository"):
            verify(self.root)


if __name__ == "__main__":
    unittest.main()
