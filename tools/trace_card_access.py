"""Read-only Ghidra export of card-table literal references and containing functions."""
import hashlib
import json
import os
from pathlib import Path
import struct
import subprocess

ROOT = Path(__file__).resolve().parents[1]
ISO_HASH = 'e99cff5774856e16329b94c9ff48f9eb0c6bc61ff1127882d0785ca4221bd707'


def main():
    output = ROOT / 'analysis' / ISO_HASH / 'stage4c'
    output.mkdir(parents=True, exist_ok=True)
    paths = json.loads((ROOT / 'analysis/stage3-environment/paths.json').read_text())
    project = ROOT / 'analysis' / ISO_HASH / 'stage3/MainVerified/projects'
    binary = ROOT / 'data/extracted' / ISO_HASH / 'files/PSP_GAME/SYSDIR/EBOOT.BIN'
    b = binary.read_bytes()
    assert hashlib.sha256(b).hexdigest() == '8087681f81f87a87471591752b3c0c9bc3cec48ffff15a115a1e2525f670c975'
    phoff = struct.unpack_from('<I', b, 28)[0]
    phnum = struct.unpack_from('<H', b, 44)[0]
    segments = [struct.unpack_from('<8I', b, phoff + 32*i) for i in range(phnum)]
    args = []
    for name in ['cardinfo_jpn.ehp','CARD_Prop.bin','CARD_IntID.bin','CARD_Named.bin','cardh_j.cip','cardm_j.cip','cardj256_j.cip']:
        off = b.index(name.encode() + b'\0')
        seg = next(s for s in segments if s[0] == 1 and s[1] <= off < s[1] + s[4])
        address = 0x08804000 + seg[2] + off - seg[1]
        args += [name, hex(address)]
    for name, address in [('NameTablePointer',0x08b571e8),('IndexTablePointer',0x08b571f0),
                          ('DescriptionTablePointer',0x08b571f8),('PropertyTablePointer',0x08b5721c),
                          ('IdMapPointer',0x08b57228),('HuffmanTablePointer',0x08b57248),
                          ('Function:PropertyRecordAccessor',0x0881ed64),
                          ('Function:PostHuffman1',0x0881fd2c),('Function:PostHuffman2',0x0881fe6c)]:
        args += [name, hex(address)]
    command = [str(Path(paths['ghidra']) / 'support/analyzeHeadless.bat'), str(project), 'MainVerified',
               '-process', 'EBOOT.BIN', '-readOnly', '-noanalysis', '-scriptPath', str(ROOT / 'tools/ghidra'),
               '-postScript', 'TraceCardAccess.java', str(output / 'card-access.json'), *args]
    with (output / 'ghidra-trace.log').open('w') as log:
        result = subprocess.run(command, cwd=ROOT, env=dict(os.environ,JAVA_HOME=paths['jdk']), stdout=log, stderr=subprocess.STDOUT)
    if result.returncode or not (output / 'card-access.json').exists():
        raise RuntimeError('Ghidra trace failed; see log')
    report = json.loads((output / 'card-access.json').read_text())
    print(json.dumps({'anchors':report['anchors'],'functions':[(f['entry'],f['name']) for f in report['functions']]},indent=2))


if __name__ == '__main__':
    main()
