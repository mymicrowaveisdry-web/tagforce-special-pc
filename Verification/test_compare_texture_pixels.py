import unittest

from compare_texture_pixels import compare


class TextureComparisonTests(unittest.TestCase):
    def test_alpha_disagreement_rejects_identical_rgb(self):
        row = compare(bytes([1, 2, 3, 0]), bytes([1, 2, 3, 255]), [1, 1], [1, 1])
        self.assertEqual(row["result"], "rejected-disagreement")
        self.assertEqual(row["differingChannels"], [0, 0, 0, 1])

    def test_dimensions_are_part_of_comparison(self):
        row = compare(bytes(8), bytes(8), [2, 1], [1, 2])
        self.assertEqual(row["result"], "rejected-disagreement")

    def test_agreement_does_not_certify_runtime_or_palette(self):
        row = compare(bytes(4), bytes(4), [1, 1], [1, 1])
        self.assertEqual(row["result"], "decoder-agreement")
        self.assertEqual(row["referenceVerification"], "unverified")
        self.assertEqual(row["paletteInterpolation"], "unverified")

    def test_samples_include_swizzle_tile_edges(self):
        row = compare(bytes(17 * 17 * 4), bytes(17 * 17 * 4), [17, 17], [17, 17])
        points = {(s["x"], s["y"]) for s in row["samples"]}
        self.assertTrue({(7, 7), (8, 8), (15, 15), (16, 16)} <= points)
