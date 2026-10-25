import contextlib
import hashlib
import io
import json
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch

import pycdlib
from tools.extract_iso import ROOT, inventory, local_path, process, sha256


class ExtractionTests(unittest.TestCase):
    def setUp(self):
        temporary_root = ROOT / 'analysis'
        temporary_root.mkdir(exist_ok=True)
        self.temp = tempfile.TemporaryDirectory(dir=temporary_root)
        self.addCleanup(self.temp.cleanup)
        self.base = Path(self.temp.name)
        self.source = self.base / 'synthetic.iso'
        self.payload = b'Non-game synthetic fixture\x00\xff\x01'
        disc = pycdlib.PyCdlib()
        disc.new()
        disc.add_directory('/PSP_GAME')
        disc.add_directory('/PSP_GAME/EMPTY')
        disc.add_fp(io.BytesIO(self.payload), len(self.payload), '/PSP_GAME/TEST.BIN;1')
        disc.write(str(self.source))
        disc.close()

    def test_preserves_bytes_and_records_hashes(self):
        before = self.source.read_bytes()
        output = self.base / 'result'
        manifest = process(self.source, output)
        self.assertEqual(before, self.source.read_bytes())
        self.assertEqual(self.payload, (output / 'files/PSP_GAME/TEST.BIN').read_bytes())
        self.assertTrue((output / 'files/PSP_GAME/EMPTY').is_dir())
        self.assertFalse((output / 'INCOMPLETE.txt').exists())
        self.assertEqual('complete', json.loads((output / 'manifest.json').read_text())['status'])
        self.assertEqual(hashlib.sha256(self.payload).hexdigest(), manifest['files'][0]['sha256'])
        self.assertEqual(manifest['source_sha256'], manifest['source_sha256_after'])
        self.assertIn('/PSP_GAME/TEST.BIN;1', (output / 'sha256-manifest.csv').read_text())
        with self.assertRaisesRegex(ValueError, 'already exists'):
            process(self.source, output)

    def test_listing_creates_no_outputs(self):
        before = set(self.base.iterdir())
        with contextlib.redirect_stdout(io.StringIO()):
            result = process(self.source, list_only=True)
        self.assertTrue(result['has_psp_game'])
        self.assertEqual(1, result['file_count'])
        self.assertEqual(before, set(self.base.iterdir()))

    def test_rejects_unsafe_paths(self):
        for path in ['/../BAD', '/CON.TXT;1', '/BAD:NAME;1', '/DIR/BAD\\NAME;1', '/BAD.']:
            with self.subTest(path=path), self.assertRaises(ValueError):
                local_path(path)

    def test_refuses_output_outside_workspace(self):
        with self.assertRaisesRegex(ValueError, 'within this workspace'):
            process(self.source, ROOT.parent / 'not-an-output')

    def test_failed_extraction_stays_marked_incomplete(self):
        output = self.base / 'failed'
        with patch.object(pycdlib.PyCdlib, 'get_file_from_iso_fp', side_effect=OSError('synthetic failure')):
            with self.assertRaises(OSError):
                process(self.source, output)
        self.assertTrue((output / 'INCOMPLETE.txt').exists())
        self.assertFalse((output / 'manifest.json').exists())

    def test_source_hash_change_prevents_success(self):
        output = self.base / 'changed'
        real_hash = sha256(self.source)
        with patch('tools.extract_iso.sha256', side_effect=[real_hash, 'filehash', 'changedhash']):
            with self.assertRaisesRegex(ValueError, 'Source ISO changed'):
                process(self.source, output)
        self.assertTrue((output / 'INCOMPLETE.txt').exists())
        self.assertFalse((output / 'manifest.json').exists())

    def test_iso_versions_cannot_overwrite_same_local_file(self):
        disc = pycdlib.PyCdlib()
        disc.new()
        self.addCleanup(disc.close)
        disc.add_fp(io.BytesIO(b'a'), 1, '/TEST.BIN;1')
        disc.add_fp(io.BytesIO(b'b'), 1, '/TEST.BIN;2')
        with self.assertRaisesRegex(ValueError, 'Colliding output path'):
            inventory(disc)


if __name__ == '__main__':
    unittest.main()
