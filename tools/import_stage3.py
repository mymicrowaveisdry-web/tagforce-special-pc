"""Import the verified main ELF using the installed PSP/Allegrex loader."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import subprocess
import struct

ROOT = Path(__file__).resolve().parents[1]


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('extraction', type=Path)
    parser.add_argument('--name', default='MainBaseline', help='Fresh project/run name')
    args = parser.parse_args()
    if not args.name.isidentifier():
        parser.error('Name must be an identifier')
    paths = json.loads((ROOT / 'analysis/stage3-environment/paths.json').read_text())
    extraction = args.extraction.resolve(strict=True)
    manifest = json.loads((extraction / 'manifest.json').read_text())
    entry = next(f for f in manifest['files'] if f['path'] == 'PSP_GAME/SYSDIR/EBOOT.BIN')
    source = extraction / 'files' / entry['path']
    digest = hashlib.sha256(source.read_bytes()).hexdigest()
    if digest != entry['sha256'] or manifest['status'] != 'complete':
        raise ValueError('Source does not match completed extraction')
    output = ROOT / 'analysis' / manifest['source_sha256'] / 'stage3' / args.name
    output.mkdir(parents=True, exist_ok=False)
    projects = output / 'projects'
    projects.mkdir()
    command = [str(Path(paths['ghidra']) / 'support/analyzeHeadless.bat'),
               str(projects), args.name, '-import', str(source),
               '-loader', 'PspElfLoader', '-loader-imagebase', '0x08804000',
               '-max-cpu', '4', '-analysisTimeoutPerFile', '600',
               '-scriptPath', str(ROOT / 'tools/ghidra'),
               '-postScript', 'ExportStage3.java', str(output / 'static-export.json'), digest,
               hex(struct.unpack_from('<I', source.read_bytes(), 24)[0]),
               '-log', str(output / 'application.log'), '-scriptlog', str(output / 'script.log')]
    provenance = {'command': command, 'source_sha256': digest,
                  'export_script_sha256': hashlib.sha256((ROOT / 'tools/ghidra/ExportStage3.java').read_bytes()).hexdigest(),
                  'base_address_status': 'Candidate 0x08804000; must be verified against PPSSPP runtime.',
                  'toolchain': json.loads((ROOT / 'third_party/stage3/toolchain-lock.json').read_text())}
    (output / 'provenance.json').write_text(json.dumps(provenance, indent=2) + '\n')
    print('Import log: ' + str(output / 'console.log'), flush=True)
    with (output / 'console.log').open('w') as log:
        environment = dict(os.environ, JAVA_HOME=paths['jdk'])
        result = subprocess.run(command, stdout=log, stderr=subprocess.STDOUT, cwd=ROOT, env=environment)
    if result.returncode or not (output / 'static-export.json').exists():
        raise RuntimeError(f'Import/export failed; inspect {output / "console.log"}')
    export = json.loads((output / 'static-export.json').read_text())
    if export['analysis_timed_out']:
        raise RuntimeError('Analysis timed out; not accepted as complete')
    if hashlib.sha256(source.read_bytes()).hexdigest() != digest:
        raise RuntimeError('Input bytes changed')
    print(json.dumps({k: v for k, v in export.items() if k not in ('functions', 'memory_blocks', 'entry_code_base64')}, indent=2))


if __name__ == '__main__':
    main()
