import struct
import unittest
from tools.decode_cardh_atlas import decode_region, assemble_sprite


class CardhAtlasTests(unittest.TestCase):
    def test_strided_region_works_without_unreferenced_trailing_block(self):
        red = struct.pack('<IHH', 0, 0xf800, 0)
        blue = struct.pack('<IHH', 0, 0x001f, 0)
        green = struct.pack('<IHH', 0, 0x07e0, 0)
        data = red + blue + green  # Last 4x4 block in an 8x8 texture is deliberately absent.
        self.assertEqual(bytes([0, 252, 0, 255]) * 16, decode_region(data, 8, 8, 0, 4, 4, 4))
        self.assertEqual(bytes([0, 0, 248, 255]) * 4, decode_region(data, 8, 8, 5, 1, 2, 2))
        with self.assertRaisesRegex(ValueError, 'unstored'):
            decode_region(data, 8, 8, 4, 4, 4, 4)

    def test_real_profile_extent_keeps_only_stored_last_row_half(self):
        data = struct.pack('<IHH', 0, 0xffff, 0) * 496
        self.assertEqual(64 * 4 * 4, len(decode_region(data, 128, 64, 0, 60, 64, 4)))
        with self.assertRaisesRegex(ValueError, 'unstored'):
            decode_region(data, 128, 64, 64, 60, 4, 4)
        with self.assertRaises(ValueError):
            decode_region(data, 128, 64, -1, 0, 1, 1)

    def test_assembly_rejects_holes_and_uses_destination_coordinates(self):
        data = struct.pack('<IHH', 0, 0xf800, 0) * 496
        vertices = [(0, 0, 0, 0, 0), (4, 4, 4, 4, 0)]
        width, height, rgba = assemble_sprite(data, vertices)
        self.assertEqual((4, 4, bytes([248, 0, 0, 255]) * 16), (width, height, rgba))
        with self.assertRaisesRegex(ValueError, 'Uncovered'):
            assemble_sprite(data, [(0, 0, 1, 0, 0), (4, 4, 5, 4, 0)])
        with self.assertRaisesRegex(ValueError, 'unscaled'):
            assemble_sprite(data, [(0, 0, 0, 0, 0), (4, 4, 5, 4, 0)])
