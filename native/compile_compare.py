"""Local-only, exact unrelocated MIPS compile/compare. Python stdlib only.

Exit codes: 0 inspected/validated/equal; 1 error; 2 unequal; 3 compiler absent.
No command writes native-matches.json or certifies a match.
"""
import argparse
import hashlib
import json
from pathlib import Path
import shutil
import struct
import subprocess
import sys

HERE = Path(__file__).resolve().parent
ROOT = HERE.parent
PSP_REL = 0x700000a0


def sha(data):
    return hashlib.sha256(data).hexdigest()


class Elf:
    def __init__(self, data):
        self.data = data
        self.read(0, 52)
        if data[:7] != b'\x7fELF\x01\x01\x01':
            raise ValueError('Expected ELF32 little-endian version 1')
        h = struct.unpack_from('<16sHHIIIIIHHHHHH', data)
        self.kind, machine = h[1:3]
        if machine != 8:
            raise ValueError('Expected MIPS machine')
        po, so, pe, pn, se, sn = h[5], h[6], h[9], h[10], h[11], h[12]
        if (pn and pe != 32) or (sn and se != 40):
            raise ValueError('Unexpected ELF table entry size')
        self.segments = [struct.unpack('<8I', self.read(po + i * pe, pe)) for i in range(pn)]
        self.sections = [struct.unpack('<10I', self.read(so + i * se, se)) for i in range(sn)]

    def read(self, off, size):
        if off < 0 or size < 0 or off + size > len(self.data):
            raise ValueError('Truncated or out-of-range ELF data')
        return self.data[off:off + size]

    def extract(self, address, size):
        if size <= 0:
            raise ValueError('Function length must be positive')
        sections = [(i, s) for i, s in enumerate(self.sections)
                    if s[1] == 1 and s[2] & 4 and s[3] <= address and address + size <= s[3] + s[5]]
        if len(sections) != 1:
            raise ValueError('Function must map uniquely inside an executable PROGBITS section')
        i, s = sections[0]
        off = s[4] + address - s[3]
        if self.kind != 1:
            segments = [p for p in self.segments if p[0] == 1 and p[2] <= address
                        and address + size <= p[2] + p[4]
                        and p[1] + address - p[2] == off]
            if len(segments) != 1:
                raise ValueError('Section and file-backed PT_LOAD mapping disagree')
        return self.read(off, size), {'sectionIndex': i, 'sectionType': s[1],
                                     'sectionFlags': s[2], 'sectionAddress': s[3],
                                     'sectionFileOffset': s[4], 'fileOffset': off}

    def symbol(self, name):
        found = []
        for s in self.sections:
            if s[1] != 2:
                continue
            if s[9] != 16 or s[5] % 16 or s[6] >= len(self.sections):
                raise ValueError('Malformed symbol table')
            st = self.sections[s[6]]
            strings = self.read(st[4], st[5])
            for off in range(s[4], s[4] + s[5], 16):
                n, value, size, info, _, idx = struct.unpack('<IIIBBH', self.read(off, 16))
                if n >= len(strings):
                    raise ValueError('Invalid symbol name offset')
                if strings[n:].split(b'\0', 1)[0].decode('ascii') == name and idx != 0:
                    if info & 15 != 2 or size <= 0:
                        raise ValueError('Expected sized function symbol')
                    found.append((value, size))
        if len(found) != 1:
            raise ValueError('Expected one defined function symbol: ' + name)
        return found[0]

    def relocations(self, address, size):
        entries = []
        for i, s in enumerate(self.sections):
            if s[1] not in (9, 4, PSP_REL):
                continue
            stride = 12 if s[1] == 4 else 8
            if s[5] % stride:
                raise ValueError('Malformed relocation table')
            for off in range(s[4], s[4] + s[5], stride):
                r, info = struct.unpack('<II', self.read(off, 8))
                mapped = r
                if s[1] == PSP_REL:
                    seg = (info >> 8) & 255
                    if seg >= len(self.segments):
                        raise ValueError('Invalid PSP relocation segment')
                    mapped += self.segments[seg][2]
                if address <= mapped < address + size:
                    entries.append({'sectionIndex': i, 'address': mapped,
                                    'functionOffset': mapped - address,
                                    'type': info & 255, 'info': info})
        return entries


def difference(original, rebuilt):
    return [{'offset': i, 'original': original[i] if i < len(original) else None,
             'rebuilt': rebuilt[i] if i < len(rebuilt) else None}
            for i in range(max(len(original), len(rebuilt)))
            if i >= len(original) or i >= len(rebuilt) or original[i] != rebuilt[i]]


def original(module, target):
    data = module.read_bytes()
    if sha(data) != target['moduleSha256']:
        raise ValueError('Original module SHA-256 mismatch')
    if target['runtimeAddress'] - target['loadBase'] != target['elfAddress']:
        raise ValueError('Runtime/load-base mapping disagrees')
    elf = Elf(data)
    raw, mapping = elf.extract(target['elfAddress'], target['length'])
    if sha(raw) != target['originalFunctionSha256']:
        raise ValueError('Original function SHA-256 mismatch')
    return raw, mapping, elf.relocations(target['elfAddress'], target['length'])


def comparison(module, rebuilt_path, target):
    before, mapping, relocations = original(module, target)
    elf = Elf(rebuilt_path.read_bytes())
    if elf.kind != 2:
        raise ValueError('Rebuilt ELF must be a linked ET_EXEC executable')
    address, length = elf.symbol(target['symbol'])
    if address != target['elfAddress']:
        raise ValueError('Rebuilt symbol linked at wrong ELF address')
    after, rebuilt_mapping = elf.extract(address, length)
    if elf.relocations(address, length):
        raise ValueError('Rebuilt function still has unresolved relocations')
    return {'originalLength': len(before), 'rebuiltLength': len(after),
            'originalSha256': sha(before), 'rebuiltSha256': sha(after),
            'originalMapping': mapping, 'rebuiltMapping': rebuilt_mapping,
            'originalRelocations': relocations,
            'originalHex': before.hex(), 'rebuiltHex': after.hex(),
            'diff': difference(before, after), 'equal': before == after}


def compiler_command(compiler, output, flags):
    return [compiler, *flags, '-c', str(HERE / 'card_attack.c'), '-o', str(output / 'card_attack.o')]


def reset_trial(output):
    # Remove only exact known output files inside the validated private directory.
    # A successful no-op compiler must never reuse an earlier trial's artifacts.
    for name in ('card_attack.o', 'card_attack.elf', 'comparison.json', 'blocked.json',
                 'commands.json', 'build-0.log', 'build-1.log'):
        (output / name).unlink(missing_ok=True)


def require_artifact(path):
    if not path.is_file() or path.stat().st_size == 0:
        raise ValueError('Compiler returned success without producing ' + path.name)


def local_output(value):
    path = Path(value).resolve()
    private_root = (ROOT / 'analysis').resolve()
    if path == private_root or private_root not in path.parents:
        raise ValueError('Output must be a subdirectory of ignored analysis/')
    path.mkdir(parents=True, exist_ok=True)
    return path


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('command', choices=['inspect', 'run', 'validate'])
    parser.add_argument('--module', required=True, type=Path)
    parser.add_argument('--out', default='analysis/native/card-attack')
    parser.add_argument('--compiler', default='psp-gcc')
    args = parser.parse_args()
    target = json.loads((HERE / 'target.json').read_text())
    out = local_output(args.out)
    before, mapping, relocations = original(args.module, target)
    identity = {'runnerSha256': sha(Path(__file__).read_bytes()),
                'targetSha256': sha((HERE / 'target.json').read_bytes()),
                'sourceSha256': sha((HERE / 'card_attack.c').read_bytes()),
                'linkerScriptSha256': sha((HERE / 'card_attack.ld').read_bytes())}
    if args.command == 'validate':
        report = json.loads((out / 'comparison.json').read_text())
        if report['inputs'] != identity or report['comparison'] != comparison(args.module, out / 'card_attack.elf', target):
            raise ValueError('Evidence changed: independently recomputed bytes/inputs disagree')
        print('Validated actual original and rebuilt bytes; equality=' + str(report['comparison']['equal']))
        return 0
    report = {'schemaVersion': 1, 'inputs': identity, 'moduleSha256': target['moduleSha256'],
              'mapping': mapping, 'originalRelocations': relocations,
              'originalLength': len(before), 'originalSha256': sha(before),
              'status': 'inspected', 'nativeMatchRecorded': False}
    if args.command == 'inspect':
        (out / 'inspection.json').write_text(json.dumps(report, indent=2) + '\n')
        print(json.dumps(report, indent=2))
        return 0
    reset_trial(out)
    compiler = shutil.which(args.compiler)
    if not compiler:
        report.update(status='blocked', blocker='PSP MIPS compiler not found: ' + Path(args.compiler).name,
                      nextStep='Use the PSPDEV Docker wrapper or supply a trusted PSP GCC executable with --compiler.')
        (out / 'blocked.json').write_text(json.dumps(report, indent=2) + '\n')
        print(report['blocker'])
        return 3
    version = subprocess.run([compiler, '--version'], check=True, capture_output=True, text=True).stdout
    commands = [compiler_command(compiler, out, target['compileFlags']),
                [compiler, '-nostdlib', '-Wl,--build-id=none', '-Wl,-T,' + str(HERE / 'card_attack.ld'),
                 str(out / 'card_attack.o'), '-o', str(out / 'card_attack.elf')]]
    # Commands and diagnostics may contain local paths: keep them in ignored output.
    (out / 'commands.json').write_text(json.dumps(commands, indent=2) + '\n')
    for i, command in enumerate(commands):
        result = subprocess.run(command, capture_output=True, text=True)
        (out / ('build-' + str(i) + '.log')).write_text(result.stdout + result.stderr)
        if result.returncode:
            report.update(status='blocked', compilerVersion=version, blocker='Compiler/linker command failed; inspect local build-' + str(i) + '.log')
            (out / 'blocked.json').write_text(json.dumps(report, indent=2) + '\n')
            print(report['blocker'])
            return 3
        artifact = out / ('card_attack.o' if i == 0 else 'card_attack.elf')
        try:
            require_artifact(artifact)
        except ValueError as error:
            report.update(status='blocked', compilerVersion=version, blocker=str(error))
            (out / 'blocked.json').write_text(json.dumps(report, indent=2) + '\n')
            print(report['blocker'])
            return 3
    report.update(status='compared', compilerVersion=version, compileFlags=target['compileFlags'],
                  comparison=comparison(args.module, out / 'card_attack.elf', target))
    (out / 'comparison.json').write_text(json.dumps(report, indent=2) + '\n')
    print('Compared original/rebuilt bytes; equality=' + str(report['comparison']['equal']))
    return 0 if report['comparison']['equal'] else 2


if __name__ == '__main__':
    try:
        sys.exit(main())
    except (ValueError, OSError, KeyError, subprocess.SubprocessError) as error:
        print(str(error), file=sys.stderr)
        sys.exit(1)
