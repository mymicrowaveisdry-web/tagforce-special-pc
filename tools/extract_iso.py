"""Stage 1: read-only ISO9660 listing and byte-preserving extraction."""
import argparse
import csv
from datetime import datetime, timezone
import hashlib
import importlib.metadata
import json
from pathlib import Path
import re
import shutil
import sys

import pycdlib

ROOT = Path(__file__).resolve().parents[1]
RESERVED = re.compile(r'^(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])(?:\.|$)', re.I)


def sha256(path):
    with path.open('rb') as stream:
        return hashlib.file_digest(stream, 'sha256').hexdigest()


def local_path(iso_path):
    parts = iso_path.lstrip('/').split('/')
    parts[-1] = re.sub(r';[0-9]+$', '', parts[-1])
    for part in parts:
        if (not part or part in ('.', '..') or part.endswith((' ', '.'))
                or any(ord(c) < 32 or c in '<>:"/\\|?*' for c in part)
                or RESERVED.match(part)):
            raise ValueError(f'Unsafe or unsupported Windows path: {iso_path!r}')
    return '/'.join(parts)


def inventory(iso):
    files, directories, seen = [], [], set()

    def register(path):
        relative = local_path(path)
        key = relative.casefold()
        if key in seen:
            raise ValueError(f'Colliding output path: {path}')
        seen.add(key)
        return relative

    for parent, dirs, names in iso.walk(iso_path='/'):
        for name in sorted(dirs):
            path = parent.rstrip('/') + '/' + name
            directories.append(register(path))
        for name in sorted(names):
            path = parent.rstrip('/') + '/' + name
            record = iso.get_record(iso_path=path)
            if record.is_symlink():
                raise ValueError(f'Symlink extraction is unsupported: {path}')
            files.append({'iso_path': path, 'path': register(path),
                          'size': record.data_length})
    files.sort(key=lambda entry: entry['path'])
    directories.sort()
    if not files:
        raise ValueError('ISO contains no files.')
    return files, directories


def process(source, output=None, list_only=False):
    source = source.resolve(strict=True)
    if source.suffix.lower() != '.iso':
        raise ValueError('Stage 1 accepts uncompressed .iso files only.')
    source_stat = source.stat()
    print('Hashing source ISO...', file=sys.stderr, flush=True)
    before = sha256(source)
    iso = pycdlib.PyCdlib()
    with source.open('rb') as stream:
        iso.open_fp(stream)
        try:
            files, directories = inventory(iso)
            summary = {'source_sha256': before, 'source_size': source_stat.st_size,
                       'file_count': len(files), 'total_file_bytes': sum(f['size'] for f in files),
                       'has_psp_game': any(d.upper() == 'PSP_GAME' for d in directories),
                       'directories': directories, 'files': files}
            if list_only:
                print(json.dumps(summary, indent=2))
                return summary
            destination = (output or ROOT / 'data' / 'extracted' / before).resolve()
            if not destination.is_relative_to(ROOT) or destination == ROOT:
                raise ValueError('Output must be a new directory within this workspace.')
            if destination.exists():
                raise ValueError(f'Output already exists; refusing to overwrite: {destination}')
            if source.is_relative_to(destination):
                raise ValueError('Output cannot contain the source ISO.')
            destination.parent.mkdir(parents=True, exist_ok=True)
            if shutil.disk_usage(destination.parent).free < summary['total_file_bytes'] + 1024 * 1024:
                raise ValueError('Not enough free disk space for extraction and manifests.')
            destination.mkdir()
            marker = destination / 'INCOMPLETE.txt'
            marker.write_text('Extraction has not completed. Do not use as verified input.\n', encoding='utf-8')
            tree = destination / 'files'
            tree.mkdir()
            for directory in directories:
                (tree / directory).mkdir(parents=True, exist_ok=True)
            for index, entry in enumerate(files, 1):
                target = tree / entry['path']
                target.parent.mkdir(parents=True, exist_ok=True)
                with target.open('xb') as out:
                    iso.get_file_from_iso_fp(out, iso_path=entry['iso_path'])
                if target.stat().st_size != entry['size']:
                    raise ValueError(f'Extracted size mismatch: {entry["iso_path"]}')
                entry['sha256'] = sha256(target)
                if index == 1 or index % 100 == 0 or index == len(files):
                    print(f'Extracted {index}/{len(files)} files', file=sys.stderr, flush=True)
            print('Verifying source ISO is unchanged...', file=sys.stderr, flush=True)
            after = sha256(source)
            if before != after or source.stat().st_size != source_stat.st_size:
                raise ValueError('Source ISO changed during extraction.')
            summary.update({'schema_version': 1, 'status': 'complete',
                            'created_utc': datetime.now(timezone.utc).isoformat(),
                            'source_name': source.name, 'source_sha256_after': after,
                            'python': sys.version.split()[0],
                            'pycdlib': importlib.metadata.version('pycdlib')})
            with (destination / 'sha256-manifest.csv').open('w', newline='', encoding='utf-8') as out:
                writer = csv.DictWriter(out, fieldnames=['iso_path', 'path', 'size', 'sha256'])
                writer.writeheader()
                writer.writerows(files)
            (destination / 'manifest.json').write_text(json.dumps(summary, indent=2) + '\n', encoding='utf-8')
            marker.unlink()
            print(f'Extraction complete: {destination}')
            return summary
        finally:
            iso.close()


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('iso', type=Path)
    parser.add_argument('--list', action='store_true', help='List and hash only; create no output')
    parser.add_argument('--output', type=Path, help='New output directory inside this workspace')
    args = parser.parse_args()
    if args.list and args.output:
        parser.error('--output cannot be combined with --list')
    try:
        process(args.iso, args.output, args.list)
    except Exception as error:
        print(f'ERROR: {error}', file=sys.stderr)
        return 1
    return 0


if __name__ == '__main__':
    sys.exit(main())
