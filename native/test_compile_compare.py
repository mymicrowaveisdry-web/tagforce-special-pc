"""Synthetic ELF fixtures only: no game bytes or assets."""
import json
from pathlib import Path
import struct
import tempfile
import unittest

from compile_compare import (Elf, PSP_REL, comparison, difference, local_output,
                            original, require_artifact, reset_trial, sha)


def fixture(relocation=False, symbols=False):
    data = bytearray(1024)
    count = 3 + (2 if symbols else 0)
    struct.pack_into('<16sHHIIIIIHHHHHH', data, 0, b'\x7fELF\x01\x01\x01' + bytes(9),
                     2, 8, 1, 0x1000, 52, 512, 0, 52, 32, 1, 40, count, 0)
    struct.pack_into('<8I', data, 52, 1, 256, 0x1000, 0, 16, 16, 5, 4)
    data[256:272] = bytes(range(16))
    struct.pack_into('<10I', data, 552, 0, 1, 6, 0x1000, 256, 16, 0, 0, 4, 0)
    if relocation:
        struct.pack_into('<10I', data, 592, 0, PSP_REL, 0, 0, 280, 8, 0, 1, 4, 8)
        struct.pack_into('<II', data, 280, 4, 4)
    if symbols:
        struct.pack_into('<10I', data, 632, 0, 2, 0, 0, 300, 16, 4, 0, 4, 16)
        struct.pack_into('<10I', data, 672, 0, 3, 0, 0, 330, 13, 0, 0, 1, 0)
        struct.pack_into('<IIIBBH', data, 300, 1, 0x1000, 12, 0x12, 0, 1)
        data[330:343] = b'\0card_attack\0'
    return data


class ElfTests(unittest.TestCase):
    def test_mapping_uses_section_and_segment(self):
        raw, mapping = Elf(fixture()).extract(0x1004, 4)
        self.assertEqual(bytes([4, 5, 6, 7]), raw)
        self.assertEqual(260, mapping['fileOffset'])

    def test_section_segment_disagreement_rejected(self):
        data = fixture()
        struct.pack_into('<I', data, 56, 260)
        with self.assertRaisesRegex(ValueError, 'disagree'):
            Elf(data).extract(0x1000, 4)

    def test_non_file_backed_boundary_rejected(self):
        with self.assertRaises(ValueError):
            Elf(fixture()).extract(0x100c, 8)

    def test_overlapping_sections_rejected(self):
        data = fixture()
        data[592:632] = data[552:592]
        with self.assertRaisesRegex(ValueError, 'uniquely'):
            Elf(data).extract(0x1000, 4)

    def test_psp_relocation_mapped_via_segment(self):
        entries = Elf(fixture(relocation=True)).relocations(0x1000, 16)
        self.assertEqual([{'sectionIndex': 2, 'address': 0x1004,
                           'functionOffset': 4, 'type': 4, 'info': 4}], entries)
        self.assertEqual([], Elf(fixture(relocation=True)).relocations(0x1008, 4))

    def test_bad_psp_relocation_segment_rejected(self):
        data = fixture(relocation=True)
        struct.pack_into('<I', data, 284, 0x104)
        with self.assertRaisesRegex(ValueError, 'segment'):
            Elf(data).relocations(0x1000, 16)

    def test_symbol_boundaries_come_from_symbol_table(self):
        self.assertEqual((0x1000, 12), Elf(fixture(symbols=True)).symbol('card_attack'))
        with self.assertRaisesRegex(ValueError, 'defined function'):
            Elf(fixture(symbols=True)).symbol('missing')

    def test_invalid_input(self):
        for data in [b'', bytes(52), b'\x7fELF\x02' + bytes(47)]:
            with self.assertRaises(ValueError):
                Elf(data)

    def test_truncated_section(self):
        data = fixture()
        struct.pack_into('<I', data, 568, 1023)
        with self.assertRaises(ValueError):
            Elf(data).extract(0x1000, 16)

    def test_diff_includes_size_mismatches(self):
        self.assertEqual([], difference(b'ab', b'ab'))
        self.assertEqual([{'offset': 1, 'original': 98, 'rebuilt': None}], difference(b'ab', b'a'))
        self.assertEqual([{'offset': 0, 'original': 97, 'rebuilt': 98}], difference(b'a', b'b'))

    def test_wrong_module_hash_rejected_before_analysis(self):
        with tempfile.TemporaryDirectory() as tmp:
            path = Path(tmp) / 'input'
            path.write_bytes(fixture())
            with self.assertRaisesRegex(ValueError, 'module SHA'):
                original(path, {'moduleSha256': '0' * 64})

    def test_comparison_recomputes_original_and_rebuilt_bytes(self):
        data = fixture(symbols=True)
        target = {'moduleSha256': sha(data), 'originalFunctionSha256': sha(data[256:272]),
                  'runtimeAddress': 0x9000, 'loadBase': 0x8000,
                  'elfAddress': 0x1000, 'length': 16, 'symbol': 'card_attack'}
        with tempfile.TemporaryDirectory() as tmp:
            module, rebuilt = Path(tmp) / 'original', Path(tmp) / 'rebuilt'
            module.write_bytes(data)
            rebuilt.write_bytes(data)
            # Original length16 versus symbol length12 must be a mismatch.
            report = comparison(module, rebuilt, target)
            self.assertFalse(report['equal'])
            self.assertEqual(4, len(report['diff']))
            # Merely supplying an identical claimed hash cannot conceal a changed ELF.
            data[256] = 255
            rebuilt.write_bytes(data)
            changed = comparison(module, rebuilt, target)
            self.assertNotEqual(report['rebuiltSha256'], changed['rebuiltSha256'])
            self.assertEqual(255, changed['diff'][0]['rebuilt'])

    def test_comparison_rejects_wrong_link_address(self):
        data = fixture(symbols=True)
        target = {'moduleSha256': sha(data), 'originalFunctionSha256': sha(data[256:272]),
                  'runtimeAddress': 0x9000, 'loadBase': 0x8000,
                  'elfAddress': 0x1000, 'length': 16, 'symbol': 'card_attack'}
        with tempfile.TemporaryDirectory() as tmp:
            module, rebuilt = Path(tmp) / 'original', Path(tmp) / 'rebuilt'
            module.write_bytes(data)
            struct.pack_into('<I', data, 304, 0x1004)
            rebuilt.write_bytes(data)
            with self.assertRaisesRegex(ValueError, 'wrong ELF address'):
                comparison(module, rebuilt, target)

    def test_relocatable_candidate_rejected(self):
        data = fixture(symbols=True)
        target = {'moduleSha256': sha(data), 'originalFunctionSha256': sha(data[256:272]),
                  'runtimeAddress': 0x9000, 'loadBase': 0x8000,
                  'elfAddress': 0x1000, 'length': 16, 'symbol': 'card_attack'}
        with tempfile.TemporaryDirectory() as tmp:
            module, rebuilt = Path(tmp) / 'original', Path(tmp) / 'rebuilt'
            module.write_bytes(data)
            struct.pack_into('<H', data, 16, 1)
            rebuilt.write_bytes(data)
            with self.assertRaisesRegex(ValueError, 'linked ET_EXEC'):
                comparison(module, rebuilt, target)

    def test_stale_artifacts_and_reports_are_removed(self):
        with tempfile.TemporaryDirectory() as tmp:
            output = Path(tmp)
            for name in ('card_attack.o', 'card_attack.elf', 'comparison.json', 'blocked.json'):
                (output / name).write_text('stale')
            (output / 'unrelated').write_text('preserve')
            reset_trial(output)
            self.assertEqual(['unrelated'], [p.name for p in output.iterdir()])
            # Successful no-op compilation after reset cannot reuse the old object.
            with self.assertRaisesRegex(ValueError, 'without producing'):
                require_artifact(output / 'card_attack.o')

    def test_empty_compiler_artifact_rejected(self):
        with tempfile.TemporaryDirectory() as tmp:
            artifact = Path(tmp) / 'card_attack.o'
            artifact.touch()
            with self.assertRaisesRegex(ValueError, 'without producing'):
                require_artifact(artifact)

    def test_public_output_refused(self):
        with self.assertRaisesRegex(ValueError, 'ignored analysis'):
            local_output('native/output')

    def test_target_linker_addresses_agree(self):
        root = Path(__file__).parent
        target = json.loads((root / 'target.json').read_text())
        linker = (root / 'card_attack.ld').read_text()
        self.assertIn(f"0x{target['elfAddress']:08x}", linker)
        self.assertIn(f"0x{target['dependency']['elfAddress']:08x}", linker)


if __name__ == '__main__':
    unittest.main()
