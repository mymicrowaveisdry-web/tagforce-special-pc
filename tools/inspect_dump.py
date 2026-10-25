"""Stage 2: verify extracted bytes and inspect metadata/signatures, without unpacking."""
import argparse
from collections import Counter
import csv
import hashlib
import json
from pathlib import Path
import struct

ROOT = Path(__file__).resolve().parents[1]
REFERENCES = [
    'https://github.com/hrydgard/ppsspp/blob/master/Core/ELF/ParamSFO.cpp',
    'https://refspecs.linuxfoundation.org/elf/elf.pdf',
    'https://github.com/xan1242/ehppack',
]


def bounded(data, offset, size):
    if offset < 0 or size < 0 or offset > len(data) or size > len(data) - offset:
        raise ValueError('Field extends beyond file bounds')
    return data[offset:offset + size]


def parse_sfo(data):
    magic, version, keys, values, count = struct.unpack('<4s4I', bounded(data, 0, 20))
    if magic != b'\0PSF' or version != 0x101:
        raise ValueError('Unsupported SFO magic/version')
    if not 20 + count * 16 <= keys <= values <= len(data):
        raise ValueError('Invalid SFO table bounds')
    result = {}
    for i in range(count):
        key, fmt, length, capacity, offset = struct.unpack('<HHIII', bounded(data, 20 + 16*i, 16))
        start = keys + key
        if not keys <= start < values:
            raise ValueError('Invalid SFO key offset')
        end = data.find(b'\0', start, values)
        if end < 0:
            raise ValueError('Unterminated SFO key')
        name = data[start:end].decode('utf-8')
        if name in result or length > capacity:
            raise ValueError('Duplicate key or invalid SFO value length')
        bounded(data, values + offset, capacity)
        raw = bounded(data, values + offset, length)
        if fmt == 0x404:
            if length != 4:
                raise ValueError('SFO integer must be four bytes')
            value = struct.unpack('<I', raw)[0]
        elif fmt == 0x204:
            if not raw.endswith(b'\0'):
                raise ValueError('Unterminated SFO string')
            value = raw[:-1].decode('utf-8')
        else:
            value = {'unsupported_format': hex(fmt), 'length': length}
        result[name] = value
    return result


def parse_elf(data):
    ident = bounded(data, 0, 16)
    if ident[:7] != b'\x7fELF\x01\x01\x01':
        raise ValueError('Only ELF32 little-endian version 1 is supported')
    fields = struct.unpack('<HHIIIIIHHHHHH', bounded(data, 16, 36))
    typ, machine, version, entry, phoff, shoff, flags, ehsize, phsize, phnum, shsize, shnum, shstr = fields
    if version != 1 or ehsize != 52 or (phnum and phsize != 32) or (shnum and shsize != 40):
        raise ValueError('Unsupported ELF header/table sizes')
    bounded(data, phoff, phsize * phnum)
    bounded(data, shoff, shsize * shnum)
    segments = []
    for i in range(phnum):
        pt, off, va, pa, fs, ms, fl, align = struct.unpack('<8I', bounded(data, phoff + i * phsize, 32))
        bounded(data, off, fs)
        if pt == 1 and fs > ms:
            raise ValueError('ELF load segment filesz exceeds memsz')
        segments.append(dict(type=hex(pt), offset=off, virtual_address=hex(va),
                             physical_address=hex(pa), file_size=fs, memory_size=ms,
                             flags=hex(fl), alignment=align))
    for i in range(shnum):
        section = struct.unpack('<10I', bounded(data, shoff + i * shsize, 40))
        if section[1] != 8:  # SHT_NOBITS has no file payload.
            bounded(data, section[4], section[5])
    return {'class': 32, 'endianness': 'little', 'type': hex(typ),
            'machine': machine, 'machine_label': 'MIPS' if machine == 8 else 'unknown',
            'entry_header_value': hex(entry), 'flags': hex(flags),
            'section_count': shnum, 'section_name_table_index': shstr,
            'segments': segments,
            'caveat': 'Header values are not runtime addresses; PSP relocation/loading is unverified.'}


def classify(head, size, all_zero):
    if size == 0:
        return 'empty'
    if all_zero:
        return 'all_zero'
    signatures = [
        (b'\x7fELF', 'elf'), (b'~SCE', 'sony_sce_container'),
        (b'~PSP', 'sony_psp_container'), (b'\0PSF', 'sfo'),
        (b'EHP\x03', 'ehp_v3_candidate'), (b'CIP\x1a', 'cip_candidate'),
        (b'\x1f\x8b\x08', 'gzip_candidate'), (b'\x89PNG\r\n\x1a\n', 'png'),
        (b'MIG.00.1PSP', 'gim_candidate'), (b'PSMF', 'psmf_candidate'),
        (b'@UTF', 'cri_utf_table_candidate'), (b'AFS2', 'afs2_candidate'),
        (b'[DUEL QUESTION]', 'duel_question_text_candidate'),
    ]
    return next((name for magic, name in signatures if head.startswith(magic)), 'unknown')


def analyze(extraction, output=None):
    extraction = extraction.resolve(strict=True)
    manifest = json.loads((extraction / 'manifest.json').read_text(encoding='utf-8'))
    if manifest['status'] != 'complete' or (extraction / 'INCOMPLETE.txt').exists():
        raise ValueError('Extraction is incomplete')
    if manifest['source_sha256'] != manifest['source_sha256_after']:
        raise ValueError('Extraction source hash mismatch')
    tree = extraction / 'files'
    expected = {item['path'] for item in manifest['files']}
    actual = {p.relative_to(tree).as_posix() for p in tree.rglob('*') if p.is_file()}
    if len(expected) != len(manifest['files']) or expected != actual:
        raise ValueError('Extracted file set differs from manifest')
    rows, metadata, executables, archives, issues = [], [], [], [], []
    for item in manifest['files']:
        path = (tree / item['path']).resolve(strict=True)
        if not path.is_relative_to(tree.resolve()):
            raise ValueError('Manifest path escapes extraction')
        digest, size, all_zero, head = hashlib.sha256(), 0, True, b''
        with path.open('rb') as stream:
            while chunk := stream.read(1024 * 1024):
                if not head:
                    head = chunk[:64]
                digest.update(chunk)
                size += len(chunk)
                all_zero = all_zero and not any(chunk)
        if size != item['size'] or digest.hexdigest() != item['sha256']:
            raise ValueError(f'Integrity mismatch: {item["path"]}')
        kind = classify(head, size, all_zero)
        row = dict(path=item['path'], size=size, sha256=digest.hexdigest(),
                   extension=path.suffix.lower(), classification=kind, header_hex=head[:16].hex())
        rows.append(row)
        if kind == 'sfo':
            try:
                if size > 1024 * 1024:
                    raise ValueError('SFO exceeds inspection size limit')
                metadata.append({**row, 'fields': parse_sfo(path.read_bytes())})
            except ValueError as error:
                issues.append({'path': item['path'], 'issue': str(error)})
        if kind == 'elf' or path.suffix.lower() == '.prx' or '/SYSDIR/' in item['path'] and path.suffix.lower() == '.bin':
            candidate = dict(row)
            if kind == 'elf':
                try:
                    if size > 64 * 1024 * 1024:
                        raise ValueError('ELF exceeds inspection size limit')
                    candidate['elf'] = parse_elf(path.read_bytes())
                except ValueError as error:
                    candidate['header_error'] = str(error)
                    issues.append({'path': item['path'], 'issue': str(error)})
            executables.append(candidate)
        if kind in ('ehp_v3_candidate', 'cip_candidate', 'gzip_candidate', 'cri_utf_table_candidate', 'afs2_candidate') or path.suffix.lower() == '.cip':
            archives.append({**row, 'candidate_basis': 'extension_only' if kind == 'unknown' else 'signature'})
    report = {'schema_version': 1, 'source_sha256': manifest['source_sha256'],
              'inspector_sha256': hashlib.sha256(Path(__file__).read_bytes()).hexdigest(),
              'extraction_manifest_sha256': hashlib.sha256((extraction / 'manifest.json').read_bytes()).hexdigest(),
              'integrity': 'all extracted files match Stage 1 hashes and sizes',
              'file_count': len(rows), 'total_file_bytes': sum(r['size'] for r in rows),
              'classification_counts': dict(sorted(Counter(r['classification'] for r in rows).items())),
              'extension_counts': dict(sorted(Counter(r['extension'] for r in rows).items())),
              'metadata': metadata, 'issues': issues, 'references': REFERENCES,
              'unknowns': [
                  'Filename English v2.1 label and patch provenance are not independently verified.',
                  'SFO version fields do not establish translation patch version or original-release identity.',
                  'REGION is recorded as a raw bitmask; geographic interpretation is unverified.',
                  'Signatures identify candidates, not validated inner formats, compression integrity or contents.',
                  'ELF runtime bases, overlays, imports, exports, symbols and PSP relocation behavior are unverified.',
                  'All-zero files may be placeholders; why they are zero is unknown.',
                  'CPJ and CPM headers occur in two .cip files; their structures and relationship to CIP are unverified.',
              ]}
    output = (output or ROOT / 'analysis' / manifest['source_sha256'] / 'stage2').resolve()
    if not output.is_relative_to(ROOT / 'analysis'):
        raise ValueError('Reports must be under workspace analysis/')
    output.mkdir(parents=True, exist_ok=False)
    for name, content in [('summary', report), ('metadata', metadata),
                          ('executables', executables), ('archive-candidates', archives)]:
        (output / f'{name}.json').write_text(json.dumps(content, indent=2, ensure_ascii=False) + '\n', encoding='utf-8')
    with (output / 'file-signatures.csv').open('w', newline='', encoding='utf-8') as stream:
        writer = csv.DictWriter(stream, fieldnames=list(rows[0]))
        writer.writeheader()
        writer.writerows(rows)
    (output / 'unknowns.md').write_text('# Unknowns\n\n' + '\n'.join('- ' + u for u in report['unknowns']) + '\n', encoding='utf-8')
    print(json.dumps({'output': str(output), 'file_count': len(rows),
                      'classification_counts': report['classification_counts'], 'issues': issues}, indent=2))
    return report


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('extraction', type=Path, help='Stage 1 directory containing manifest.json and files/')
    parser.add_argument('--output', type=Path, help='New report directory under analysis/')
    args = parser.parse_args()
    try:
        analyze(args.extraction, args.output)
    except (ValueError, OSError, KeyError, struct.error) as error:
        parser.exit(1, f'ERROR: {error}\n')


if __name__ == '__main__':
    main()
