"""Decode only the cardh atlas rectangles used by the verified card-panel renderer.

The nominal bottom-right 64x4 area has no stored DXT blocks. Vertex tables are
read from the user's local main ELF, never embedded as distributable game assets.
"""
import hashlib
import json
from pathlib import Path
import struct
from PIL import Image

if __package__:
    from .decode_card_images import decode_psp_dxt1
else:
    from decode_card_images import decode_psp_dxt1

ROOT = Path(__file__).resolve().parents[1]
HASH = 'e99cff5774856e16329b94c9ff48f9eb0c6bc61ff1127882d0785ca4221bd707'
MAIN_HASH = '8087681f81f87a87471591752b3c0c9bc3cec48ffff15a115a1e2525f670c975'


def decode_region(data, width, height, x, y, w, h):
    """Read the complete DXT blocks for one region using the nominal row stride."""
    if (width <= 0 or height <= 0 or width % 4 or height % 4 or width * height > 4096 * 4096 or
            min(x, y) < 0 or min(w, h) <= 0 or x + w > width or y + h > height):
        raise ValueError('Invalid atlas region')
    left, top = x // 4, y // 4
    right, bottom = (x + w + 3) // 4, (y + h + 3) // 4
    packed = bytearray()
    for by in range(top, bottom):
        for bx in range(left, right):
            offset = (by * (width // 4) + bx) * 8
            if offset + 8 > len(data):
                raise ValueError('Region samples an unstored DXT block')
            packed.extend(data[offset:offset + 8])
    block_width = (right - left) * 4
    decoded = decode_psp_dxt1(packed, block_width, (bottom - top) * 4)
    rows = []
    for row in range(h):
        offset = ((y - top * 4 + row) * block_width + x - left * 4) * 4
        rows.append(decoded[offset:offset + w * 4])
    return b''.join(rows)


def assemble_sprite(data, vertices):
    if not vertices or len(vertices) % 2:
        raise ValueError('Sprite vertex pairs required')
    width = max(v[2] for v in vertices)
    height = max(v[3] for v in vertices)
    if width <= 0 or height <= 0 or width * height > 4096 * 4096:
        raise ValueError('Invalid sprite dimensions')
    output, covered = bytearray(width * height * 4), bytearray(width * height)
    for first, second in zip(vertices[::2], vertices[1::2]):
        u, v, x, y, z = first
        u2, v2, x2, y2, z2 = second
        w, h = u2 - u, v2 - v
        if (min(x, y) < 0 or x2 - x != w or y2 - y != h or z or z2):
            raise ValueError('Only unscaled 2D sprite rectangles are supported')
        pixels = decode_region(data, 128, 64, u, v, w, h)
        for row in range(h):
            target = (y + row) * width + x
            if any(covered[target:target + w]):
                raise ValueError('Overlapping sprite rectangles')
            covered[target:target + w] = bytes([1]) * w
            output[target * 4:(target + w) * 4] = pixels[row * w * 4:(row + 1) * w * 4]
    if not all(covered):
        raise ValueError('Uncovered output pixels; padding is not fabricated')
    return width, height, bytes(output)


def read_vertices(elf, address, count):
    offset = struct.unpack_from('<I', elf, 28)[0]
    number = struct.unpack_from('<H', elf, 44)[0]
    relative = address - 0x08804000
    for i in range(number):
        segment = struct.unpack_from('<8I', elf, offset + i * 32)
        if segment[0] == 1 and segment[2] <= relative and relative + count * 10 <= segment[2] + segment[4]:
            start = segment[1] + relative - segment[2]
            return [struct.unpack_from('<HHhhh', elf, start + j * 10) for j in range(count)]
    raise ValueError('Vertex table is outside file-backed ELF segments')


def main():
    base = ROOT / 'analysis' / HASH
    files = ROOT / 'data/extracted' / HASH / 'files/PSP_GAME'
    elf = (files / 'SYSDIR/EBOOT.BIN').read_bytes()
    assert hashlib.sha256(elf).hexdigest() == MAIN_HASH
    tables = {}
    for name, address, count in [('regular-art', 0x0897a940, 6), ('regular-title', 0x0897a97c, 8),
                                  ('alternate-art', 0x0897a9cc, 8), ('alternate-title', 0x0897aa1c, 6)]:
        tables[name] = read_vertices(elf, address, count)
    source = files / 'USRDIR/card/cardh_j.cip'
    blob = source.read_bytes()
    digest = hashlib.sha256(blob).hexdigest()
    listing = json.loads((base / 'stage4bVerified/cardh_j-card-images-list.json').read_text())
    assert digest == listing['sourceSha256']
    output = ROOT / 'data/decoded/stage4d-cardh'
    output.mkdir(parents=True, exist_ok=False)
    report = dict(source_sha256=digest, main_sha256=MAIN_HASH, vertex_tables=tables, images=[],
                  scope='Reassembled atlas sprites only; not a complete rendered card or full nominal atlas.')
    for card_id in [4007, 4041, 4842, 11213]:
        entry = next(e for e in listing['index']['entries'] if e['cardId'] == card_id)
        slot = blob[entry['offset']:entry['offset'] + entry['storedSlotBytes']]
        assert len(slot) == 4096 and slot.startswith(b'MIG.00.1PSP\0')
        assert struct.unpack_from('<4H', slot, 0x44) == (8, 0, 128, 64)
        assert struct.unpack_from('<I', slot, 0x70)[0] == 0x40
        for name, vertices in tables.items():
            width, height, rgba = assemble_sprite(slot[0x80:], vertices)
            path = output / f'{card_id}-{name}.png'
            Image.frombytes('RGBA', (width, height), rgba).save(path)
            report['images'].append(dict(card_id=card_id, layout=name, width=width, height=height,
                                         path=path.relative_to(ROOT).as_posix(),
                                         sha256=hashlib.sha256(path.read_bytes()).hexdigest()))
    assert hashlib.sha256(source.read_bytes()).hexdigest() == digest
    (base / 'stage4d/cardh-atlas.json').write_text(json.dumps(report, indent=2) + '\n')
    print(json.dumps(dict(images=len(report['images']), output=str(output), source_unchanged=True), indent=2))


if __name__ == '__main__':
    main()
