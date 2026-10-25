"""Read-only real-dump cross-checks for image indexes and linked card metadata."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import struct
import subprocess

ROOT = Path(__file__).resolve().parents[1]


def decode_ehp(path):
    b = path.read_bytes()
    magic, length, marker, count = struct.unpack_from('<4I', b)
    assert magic == 0x03504845 and length == len(b)
    entries = {}
    for i in range(count):
        info, offset = struct.unpack_from('<2I', b, 16 + i * 8)
        end = b.index(0, info)
        size = struct.unpack_from('<I', b, end + 1)[0]
        assert offset + size <= len(b)
        entries[b[info:end].decode('ascii')] = b[offset:offset + size]
    return entries


def independent_images(path):
    with path.open('rb') as stream:
        h = struct.unpack('<12I', stream.read(48))
        stream.seek(h[7]); entries = []
        for card_id in range(h[5], h[6] + 1):
            sector, second = struct.unpack('<II', stream.read(8))
            assert second == 0 and not sector & 0x80000000
            if sector:
                entries.append({'cardId': card_id, 'offset': sector * 2048, 'storedSlotBytes': h[8] * 2048})
        assert stream.read(8) == b'\xff' * 8
    return entries


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('extraction', type=Path)
    parser.add_argument('--name', default='stage4b')
    args = parser.parse_args()
    if not args.name.isidentifier():
        parser.error('Run name must be an identifier')
    extraction = args.extraction.resolve(strict=True)
    manifest = json.loads((extraction / 'manifest.json').read_text())
    assert manifest['status'] == 'complete'
    output = ROOT / 'analysis' / manifest['source_sha256'] / args.name
    output.mkdir(parents=True, exist_ok=False)
    cli = ROOT / 'TagForceSpecial.Tools/bin/Debug/net10.0/TagForceSpecial.Tools.dll'
    environment = dict(os.environ, DOTNET_CLI_HOME=str(ROOT / 'analysis/dotnet-home'), DOTNET_CLI_TELEMETRY_OPTOUT='1')

    def invoke(command, relative, *parameters):
        source = extraction / 'files' / relative
        entry = next(e for e in manifest['files'] if e['path'] == relative)
        before = hashlib.sha256(source.read_bytes()).hexdigest()
        assert before == entry['sha256']
        cmd = ['dotnet', str(cli), command, str(source), *map(str, parameters)]
        first = subprocess.run(cmd, env=environment, capture_output=True, text=True, check=True).stdout
        second = subprocess.run(cmd, env=environment, capture_output=True, text=True, check=True).stdout
        assert first == second
        result = json.loads(first)
        assert result['sourceSha256'] == result['sourceSha256After'] == before
        assert hashlib.sha256(source.read_bytes()).hexdigest() == before
        (output / (source.stem + '-' + command + '.json')).write_text(first, encoding='utf-8')
        return result

    images = []
    for name in ['cardh_j.cip', 'cardj256_j.cip', 'cardm_j.cip']:
        relative = 'PSP_GAME/USRDIR/card/' + name
        result = invoke('card-images-list', relative)
        assert result['index']['entries'] == independent_images(extraction / 'files' / relative)
        images.append(result)
    minimum = images[0]['index']['minimumCardId']
    maximum = images[0]['index']['maximumCardId']
    for image in images:
        assert (image['index']['minimumCardId'], image['index']['maximumCardId']) == (minimum, maximum)
    relative = 'PSP_GAME/USRDIR/duelsys/cardinfo_jpn.ehp'
    invoke('ehp-list', relative)
    metadata = invoke('cardinfo-list', relative, minimum, maximum)
    tables = decode_ehp(extraction / 'files' / relative)
    props, indexes, names, id_map = [tables[n] for n in ['CARD_Prop.bin', 'CARD_Indx_R.bin', 'CARD_Name_R.bin', 'CARD_IntID.bin']]
    expected = []
    for i in range(1, len(props) // 8):
        first, second = struct.unpack_from('<II', props, i * 8)
        offset, raw_desc = struct.unpack_from('<II', indexes, i * 8)
        end = offset
        while names[end:end + 2] != b'\0\0':
            end += 2
        expected.append(dict(internalIndex=i, cardId=first & 0x3fff,
                             name=names[offset:end].decode('utf-16le'), rawPropertyWord0=first,
                             rawPropertyWord1=second, rawDescriptionIndex=raw_desc))
    assert metadata['index']['rows'] == expected
    card_ids = {r['cardId'] for r in expected}
    assert len(card_ids) == len(expected)
    for image in images:
        assert {e['cardId'] for e in image['index']['entries']} == card_ids
    for i in range(len(id_map) // 2):
        row = struct.unpack_from('<H', id_map, i * 2)[0]
        if row:
            assert expected[row - 1]['cardId'] == minimum + i
    report = dict(status='pass', source_iso_sha256=manifest['source_sha256'],
                  populated_card_ids=len(card_ids), missing_ids=metadata['index']['missingIds'],
                  minimum_card_id=minimum, maximum_card_id=maximum,
                  image_containers=[dict(name=r['sourceName'], signature=r['index']['signature'],
                                         entries=len(r['index']['entries']), slot_sectors=r['index']['slotSectors'],
                                         sha256=r['sourceSha256']) for r in images],
                  cardinfo_sha256=metadata['sourceSha256'], table_count=len(tables),
                  independent_decoders_match=True, all_image_ids_match_property_ids=True,
                  repeat_output_identical=True, selected_sources_unchanged=True,
                  limitations=metadata['index']['limitations'],
                  provenance={name: hashlib.sha256((ROOT / name).read_bytes()).hexdigest() for name in
                              ['TagForceSpecial.Assets/CardImageIndexReader.cs', 'TagForceSpecial.Assets/CardInfoReader.cs', 'tools/validate_card_data.py']})
    executable_name = 'PSP_GAME/SYSDIR/EBOOT.BIN'
    executable = (extraction / 'files' / executable_name).read_bytes()
    executable_hash = hashlib.sha256(executable).hexdigest()
    assert executable_hash == next(e['sha256'] for e in manifest['files'] if e['path'] == executable_name)
    anchors = []
    for needle in [b'cardinfo_jpn.ehp', b'CARD_Prop.bin', b'CARD_IntID.bin',
                   b'cardh_j.cip', b'cardm_j.cip', b'cardj256_j.cip']:
        offset = executable.find(needle + b'\0')
        assert offset >= 0
        anchors.append(dict(filename=needle.decode('ascii'), file_offset=hex(offset)))
    report['executable_string_anchors'] = dict(path=executable_name, sha256=executable_hash,
        anchors=anchors, confidence='Literal filename bytes only; caller/xref/loading behavior is unverified.')
    reference = ROOT / 'research/CIPTool'
    report['image_format_reference'] = dict(
        commit=(reference / 'source-commit.txt').read_text(encoding='utf-8-sig').strip(),
        license='MIT', source_sha256=hashlib.sha256((reference / 'CIPTool.cpp').read_bytes()).hexdigest(),
        usage='Format research only; no third-party binary executed and no image codec copied.')
    (output / 'validation.json').write_text(json.dumps(report, indent=2) + '\n')
    print(json.dumps(report, indent=2))


if __name__ == '__main__':
    main()
