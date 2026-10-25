import struct
import unittest
from tools.decode_card_images import decode_psp_dxt1,decode_cpj_slot


class ImageDecodeTests(unittest.TestCase):
    def test_psp_component_order_and_selectors(self):
        # red, blue, 2/3-red interpolation, 2/3-blue interpolation, repeated per row.
        block=struct.pack('<IHH',0xe4e4e4e4,0xf800,0x001f)
        rgba=decode_psp_dxt1(block,4,4)
        self.assertEqual(bytes([248,0,0,255]),rgba[:4])
        self.assertEqual(bytes([0,0,248,255]),rgba[4:8])
        self.assertEqual(bytes([165,0,82,255]),rgba[8:12])
    def test_transparent_mode(self):
        b=struct.pack('<IHH',0xffffffff,0,0xffff)
        self.assertEqual(bytes(64),decode_psp_dxt1(b,4,4))
    def test_bounds_and_dimensions(self):
        for data,w,h in [(b'',4,4),(bytes(8),3,4),(bytes(8),0,4)]:
            with self.assertRaises(ValueError):decode_psp_dxt1(data,w,h)
    def test_cpj_transform_roundtrip_and_header_preservation(self):
        original=bytes(range(256))*2
        encoded=decode_cpj_slot(original)
        self.assertEqual(original[:0xa0],encoded[:0xa0])
        self.assertEqual(original,decode_cpj_slot(encoded))
        with self.assertRaises(ValueError):decode_cpj_slot(bytes(160))
