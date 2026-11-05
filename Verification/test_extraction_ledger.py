"""Synthetic ledger policy cases. Run manually; no private fixtures required."""
import unittest

from extraction_ledger import classify, new_record, unknown_paths, validate_record


class ExtractionLedgerTests(unittest.TestCase):
    def record(self, prefix=b"MIG.00.1PSP\0", name="synthetic.gim"):
        family, stage, basis = classify(prefix, name)
        row = new_record(dict(kind="iso-file", source_path=name, offset=0, length=64),
                         family, stage, basis, "private:synthetic.bin", "a" * 64)
        row["unknowns"] = sorted(unknown_paths(row))
        return row

    def test_signature_overrides_misleading_card_filename(self):
        self.assertEqual(classify(b"CPJ\x1a", "cardh_j.cip")[0], "card-artwork-cpj")
        self.assertEqual(classify(b"CIP\x1a", "cardj256_j.cip")[0], "card-thumbnails")

    def test_gzip_wrapper_is_separate_from_nested_archive(self):
        self.assertEqual(classify(b"\x1f\x8b", "nested.ehp.gz")[0], "gzip")
        self.assertEqual(classify(b"EHP\x03", "nested.ehp")[0], "ehp")

    def test_model_and_font_filenames_are_candidates_not_conversions(self):
        for name in ("synthetic.tms", "synthetic.tmt", "synthetic.pgf"):
            self.assertEqual(classify(b"authored", name)[1], "candidate")

    def test_raw_and_classified_do_not_imply_decoded_or_mapped(self):
        row = self.record()
        self.assertEqual(row["evidence"]["raw"]["state"], "retained")
        self.assertEqual(row["evidence"]["classified"]["state"], "classified")
        for stage in ("decoded", "mapped", "runtime_approved", "reference_verified"):
            self.assertEqual(row["evidence"][stage]["state"], "unverified")
        validate_record(row)

    def test_imported_png_is_not_a_runtime_approval(self):
        row = self.record(b"\x89PNG\r\n\x1a\n", "synthetic.png")
        row["evidence"]["decoded"] = dict(state="decoded", evidence=["private:synthetic.png"])
        row["unknowns"] = sorted(unknown_paths(row))
        validate_record(row)
        self.assertEqual(row["evidence"]["runtime_approved"]["state"], "unverified")

    def test_every_unknown_must_be_enumerated(self):
        row = self.record()
        row["details"]["glyph_metrics"] = "unverified"
        with self.assertRaisesRegex(ValueError, "unresolved field"):
            validate_record(row)

    def test_package_receipt_does_not_allow_automatic_approval(self):
        for stage in ("mapped", "runtime_approved", "reference_verified"):
            row = self.record()
            row["evidence"][stage] = dict(state="runtime-approved", evidence=["private:integrity-only.json"])
            row["unknowns"] = sorted(unknown_paths(row))
            with self.assertRaisesRegex(ValueError, "cannot promote"):
                validate_record(row)


if __name__ == "__main__":
    unittest.main()
