"""Compare C# EHP listings to independent pointer decoding; never export payloads."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import struct
import subprocess

ROOT = Path(__file__).resolve().parents[1]
REFERENCE_COMMIT = 'bc43a8b39d0eb3107e1ef6a97a875d6920f2f1c7'


def reference_index(path):
    # Separate straight-line decoder of the layout documented by the research source.
    # This comparison is not a substitute for the C# malformed-input checks.
    data = path.read_bytes()
    magic, length, marker, count = struct.unpack_from('<4I', data)
    if magic != 0x03504845 or length != len(data):
        raise ValueError('Header/length mismatch')
    entries = []
    for i in range(count):
        info, offset = struct.unpack_from('<2I', data, 16 + i * 8)
        end = data.index(0, info)
        size = struct.unpack_from('<I', data, end + 1)[0]
        entries.append(dict(index=i, name=data[info:end].decode('ascii'),
                            infoOffset=info, offset=offset, length=size))
    return entries


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('extraction', type=Path)
    parser.add_argument('--name', default='stage4')
    args = parser.parse_args()
    if not args.name.isidentifier():
        parser.error('Name must be an identifier')
    extraction = args.extraction.resolve(strict=True)
    manifest = json.loads((extraction / 'manifest.json').read_text())
    if manifest['status'] != 'complete':
        raise ValueError('Incomplete extraction')
    output = ROOT / 'analysis' / manifest['source_sha256'] / args.name
    output.mkdir(parents=True, exist_ok=False)
    library = ROOT / 'TagForceSpecial.Tools/bin/Debug/net10.0/TagForceSpecial.Tools.dll'
    environment = dict(os.environ, DOTNET_CLI_HOME=str(ROOT / 'analysis/dotnet-home'),
                       DOTNET_CLI_TELEMETRY_OPTOUT='1')
    report = {'schema_version': 1, 'source_iso_sha256': manifest['source_sha256'],
              'reference_commit': REFERENCE_COMMIT,
              'reference_url': f'https://github.com/xan1242/ehppack/blob/{REFERENCE_COMMIT}/ehppack/ehppack.cpp',
              'reference_license': 'MIT; source inspected only, no third-party executable run.',
              'reader_sha256': hashlib.sha256((ROOT / 'TagForceSpecial.Assets/EhpReader.cs').read_bytes()).hexdigest(),
              'sdk_pin': json.loads((ROOT / 'global.json').read_text()),
              'fixtures': []}
    names = ['PSP_GAME/USRDIR/main/now_loading03.ehp',
             'PSP_GAME/USRDIR/database/cardalbum.ehp',
             'PSP_GAME/USRDIR/database/gallery/basic_lang_jpn.ehp']
    for name in names:
        path = extraction / 'files' / name
        expected = next(f for f in manifest['files'] if f['path'] == name)
        before = hashlib.sha256(path.read_bytes()).hexdigest()
        if before != expected['sha256']:
            raise ValueError('Fixture differs from extraction: ' + name)
        result = subprocess.run(['dotnet', str(library), 'ehp-list', str(path)],
                                capture_output=True, text=True, env=environment, cwd=ROOT)
        fixture = {'path': name, 'sha256': before, 'exit_code': result.returncode}
        if name.endswith('gallery/basic_lang_jpn.ehp'):
            assert result.returncode == 1 and not result.stdout.strip()
            assert 'Declared length 940 differs from actual length 938' in result.stderr
            fixture.update(status='rejected_size_mismatch', diagnostic=result.stderr.strip())
        else:
            if result.returncode:
                raise RuntimeError(result.stderr)
            listing = json.loads(result.stdout)
            reference = reference_index(path)
            assert listing['index']['entries'] == reference
            assert listing['sourceSha256'] == before == listing['sourceSha256After']
            again = subprocess.run(['dotnet', str(library), 'ehp-list', str(path)],
                                   capture_output=True, text=True, env=environment, cwd=ROOT, check=True)
            assert again.stdout == result.stdout
            (output / (path.stem + '-listing.json')).write_text(result.stdout, encoding='utf-8')
            fixture.update(status='pass', entry_count=len(reference),
                           reference_matches=True, repeat_output_identical=True,
                           diagnostics=listing['index']['diagnostics'])
        assert hashlib.sha256(path.read_bytes()).hexdigest() == before
        report['fixtures'].append(fixture)
    report['status'] = 'pass'
    (output / 'validation.json').write_text(json.dumps(report, indent=2) + '\n')
    print(json.dumps(report, indent=2))


if __name__ == '__main__':
    main()
