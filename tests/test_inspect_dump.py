import contextlib
import hashlib
import io
import json
from pathlib import Path
import struct
import tempfile
import unittest

from tools.inspect_dump import ROOT, analyze, classify, parse_elf, parse_sfo


def sfo_fixture():
    return (struct.pack('<4s4I', b'\0PSF', 0x101, 36, 40, 1)
            + struct.pack('<HHIII', 0, 0x404, 4, 4, 0) + b'KEY\0' + struct.pack('<I', 42))


def elf_fixture():
    return (b'\x7fELF\x01\x01\x01' + bytes(9)
            + struct.pack('<HHIIIIIHHHHHH', 0xffa0, 8, 1, 0, 52, 0, 0, 52, 32, 1, 40, 0, 0)
            + struct.pack('<8I', 1, 84, 0, 0, 4, 4, 5, 4) + b'TEST')


class InspectionTests(unittest.TestCase):
    def test_sfo_integer(self):
        self.assertEqual({'KEY': 42}, parse_sfo(sfo_fixture()))

    def test_sfo_string_and_bounds(self):
        data = bytearray(sfo_fixture())
        struct.pack_into('<H', data, 22, 0x204)
        data[-4:] = b'ABC\0'
        self.assertEqual({'KEY': 'ABC'}, parse_sfo(data))
        data[-1] = 65
        with self.assertRaisesRegex(ValueError, 'Unterminated'):
            parse_sfo(data)
        data = bytearray(sfo_fixture())
        struct.pack_into('<I', data, 32, 5000)
        with self.assertRaises(ValueError):
            parse_sfo(data)

    def test_sfo_rejects_bad_tables_and_counts(self):
        for offset, value in [(4, 0), (8, 0), (12, 5000), (16, 0xffffffff)]:
            data = bytearray(sfo_fixture())
            struct.pack_into('<I', data, offset, value)
            with self.subTest(offset=offset), self.assertRaises(ValueError):
                parse_sfo(data)

    def test_elf_header_and_load_bounds(self):
        parsed = parse_elf(elf_fixture())
        self.assertEqual(8, parsed['machine'])
        self.assertEqual(4, parsed['segments'][0]['file_size'])
        with self.assertRaises(ValueError):
            parse_elf(elf_fixture()[:-1])
        bad = bytearray(elf_fixture())
        struct.pack_into('<I', bad, 52 + 20, 3)
        with self.assertRaisesRegex(ValueError, 'filesz exceeds memsz'):
            parse_elf(bad)

    def test_signatures_do_not_use_extension(self):
        self.assertEqual('unknown', classify(b'CPJ\x1a', 4, False))
        self.assertEqual('ehp_v3_candidate', classify(b'EHP\x03', 4, False))
        self.assertEqual('all_zero', classify(bytes(64), 1024, True))
        self.assertEqual('unknown', classify(bytes(64), 1024, False))
        self.assertEqual('empty', classify(b'', 0, True))

    def test_reports_reproducible_and_tampering_rejected(self):
        base = ROOT / 'analysis'
        base.mkdir(exist_ok=True)
        with tempfile.TemporaryDirectory(dir=base) as tmp:
            root = Path(tmp)
            extraction = root / 'extraction'
            tree = extraction / 'files'
            tree.mkdir(parents=True)
            payload = sfo_fixture()
            (tree / 'PARAM.SFO').write_bytes(payload)
            manifest = {'status': 'complete', 'source_sha256': 'synthetic',
                        'source_sha256_after': 'synthetic', 'files': [
                            {'path': 'PARAM.SFO', 'size': len(payload),
                             'sha256': hashlib.sha256(payload).hexdigest()}]}
            (extraction / 'manifest.json').write_text(json.dumps(manifest))
            with contextlib.redirect_stdout(io.StringIO()):
                a = analyze(extraction, root / 'first')
                b = analyze(extraction, root / 'second')
            self.assertEqual(a, b)
            self.assertEqual(payload, (tree / 'PARAM.SFO').read_bytes())
            (tree / 'PARAM.SFO').write_bytes(payload + b'x')
            with self.assertRaisesRegex(ValueError, 'Integrity mismatch'):
                analyze(extraction, root / 'bad')
            self.assertFalse((root / 'bad').exists())
