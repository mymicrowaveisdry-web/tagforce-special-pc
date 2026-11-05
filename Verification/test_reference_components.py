"""Synthetic integrity checks; no original media or LocalReference fixtures."""

import json
from pathlib import Path
import struct
import tempfile
import unittest
import zlib

from Verification import reference_components as maps


def png(path, width=480, height=272):
    def chunk(kind, data):
        return struct.pack(">I", len(data)) + kind + data + struct.pack(">I", zlib.crc32(kind + data))
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_bytes(b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", struct.pack(">IIBBBBB", width, height, 8, 2, 0, 0, 0))
                     + chunk(b"IDAT", zlib.compress((b"\x00" + b"\x00" * width * 3) * height)) + chunk(b"IEND", b""))


class ReferenceComponentTests(unittest.TestCase):
    def test_unknowns_are_not_inferred_from_scenario_filename(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            image = root / "title/options-focused/render.png"
            png(image)
            mapping = maps.new_map(image, root)
            self.assertEqual("unverified", mapping["screen"])
            self.assertEqual("unverified", mapping["state"])
            self.assertFalse(mapping["componentInventoryComplete"])
            self.assertEqual(set(maps.COMPONENT_FIELDS), set(mapping["components"][0]))
            self.assertEqual({"unverified"}, set(mapping["components"][0].values()))

    def test_reviewed_map_is_preserved_on_rerun(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            png(root / "menu/entry/render.png")
            self.assertEqual(1, maps.maintain_maps(root)["created"])
            target = root / "component-maps/menu/entry/render.components.json"
            reviewed = json.loads(target.read_text())
            reviewed["screen"] = "manually-reviewed-menu"
            target.write_text(json.dumps(reviewed), encoding="utf-8")
            original = target.read_bytes()
            self.assertEqual(1, maps.maintain_maps(root)["preserved"])
            self.assertEqual(original, target.read_bytes())

    def test_changed_screenshot_fails_without_overwriting_map(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            image = root / "menu/entry/render.png"
            png(image)
            maps.maintain_maps(root)
            target = root / "component-maps/menu/entry/render.components.json"
            original = target.read_bytes()
            image.write_bytes(image.read_bytes() + b"changed")
            with self.assertRaisesRegex(ValueError, "SHA256 changed"):
                maps.maintain_maps(root)
            self.assertEqual(original, target.read_bytes())

    def test_scaled_reference_is_rejected(self):
        with tempfile.TemporaryDirectory() as directory:
            image = Path(directory) / "render.png"
            png(image, 960, 544)
            with self.assertRaisesRegex(ValueError, "native 480x272"):
                maps.new_map(image, Path(directory))

    def test_repository_output_is_rejected_before_creation(self):
        with tempfile.TemporaryDirectory() as directory:
            png(Path(directory) / "render.png")
            with self.assertRaisesRegex(ValueError, "outside the repository"):
                maps.maintain_maps(directory, maps.REPO / "artifacts/private-map-test")

    def test_check_mode_reports_missing_map_without_writing(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            png(root / "menu/entry/render.png")
            with self.assertRaisesRegex(ValueError, "Missing component map"):
                maps.maintain_maps(root, check=True)
            self.assertFalse((root / "component-maps").exists())


if __name__ == "__main__":
    unittest.main()
