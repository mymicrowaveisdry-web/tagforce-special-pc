"""Inspect GIM dimensions within each cardh slot without decoding across slot boundaries."""
from collections import Counter
import hashlib
import json
from pathlib import Path
import struct

ROOT = Path(__file__).resolve().parents[1]
HASH = 'e99cff5774856e16329b94c9ff48f9eb0c6bc61ff1127882d0785ca4221bd707'


def main():
    base = ROOT / 'analysis' / HASH
    listing = json.loads((base / 'stage4bVerified/cardh_j-card-images-list.json').read_text())
    source = ROOT / 'data/extracted' / HASH / 'files/PSP_GAME/USRDIR/card/cardh_j.cip'
    blob = source.read_bytes()
    digest = hashlib.sha256(blob).hexdigest()
    assert digest == listing['sourceSha256']
    profiles = Counter()
    for entry in listing['index']['entries']:
        slot = blob[entry['offset']:entry['offset'] + entry['storedSlotBytes']]
        assert slot.startswith(b'MIG.00.1PSP\0')
        fmt, order, width, height = struct.unpack_from('<4H', slot, 0x44)
        # This observed profile has an image info block at 0x40, a frame offset
        # table at info+0x30, and frame zero pointing to info+0x40 = slot+0x80.
        assert struct.unpack_from('<I', slot, 0x58)[0] == 0x30
        frame = 0x40 + struct.unpack_from('<I', slot, 0x70)[0]
        profiles[(fmt, order, width, height, frame, len(slot))] += 1
    report = dict(source_sha256=digest, entries_checked=len(listing['index']['entries']), profiles=[
        dict(format=fmt, pixel_order=order, width=w, height=h, frame_offset=frame, slot_bytes=size,
             nominal_dxt1_end=frame + w * h // 2, nominal_overrun_bytes=frame + w * h // 2 - size,
             cards=count)
        for (fmt, order, w, h, frame, size), count in sorted(profiles.items())],
        interpretation='Header footprint only. See cardh-atlas.json and the renderer trace for bounded sprite decoding; a full nominal atlas is not stored.')
    assert hashlib.sha256(source.read_bytes()).hexdigest() == digest
    (base / 'stage4d/cardh-profile.json').write_text(json.dumps(report, indent=2) + '\n')
    print(json.dumps(report, indent=2))


if __name__ == '__main__':
    main()
