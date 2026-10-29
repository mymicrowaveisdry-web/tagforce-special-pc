"""Read-only exports of full image paths, loader callbacks and GIM texture setup."""
import hashlib
import json
import os
from pathlib import Path
import subprocess

ROOT = Path(__file__).resolve().parents[1]
HASH = 'e99cff5774856e16329b94c9ff48f9eb0c6bc61ff1127882d0785ca4221bd707'


def main():
    base = ROOT / 'analysis' / HASH
    source = ROOT / 'data/extracted' / HASH / 'files/PSP_GAME/SYSDIR/EBOOT.BIN'
    assert hashlib.sha256(source.read_bytes()).hexdigest() == '8087681f81f87a87471591752b3c0c9bc3cec48ffff15a115a1e2525f670c975'
    paths = json.loads((ROOT / 'analysis/stage3-environment/paths.json').read_text())
    output = base / 'stage4d'
    output.mkdir(parents=True, exist_ok=True)
    anchors = [('FullCardHPath', 0x088d6d10), ('CardHPathTable', 0x0897b7b0),
               ('FullCardMPath', 0x088d6d88), ('CardMPathTable', 0x0897b7c8),
               ('FullCardJPath', 0x088d6e50), ('CardJPathTable', 0x0897b810)]
    for address in [0x08813a2c, 0x08813f98, 0x08814374, 0x08814400, 0x0881449c,
                    0x088145e8, 0x08814690, 0x08814a60, 0x0883de40, 0x0883e4dc,
                    0x08844500, 0x08844618, 0x08844690, 0x0880e29c,
                    0x08811840, 0x0880e694, 0x08811da8, 0x08812770,
                    0x0880ede8, 0x0880eedc]:
        anchors.append(('Function:' + hex(address), address))
    command = [str(Path(paths['ghidra']) / 'support/analyzeHeadless.bat'),
               str(base / 'stage3/MainVerified/projects'), 'MainVerified', '-process', 'EBOOT.BIN',
               '-readOnly', '-noanalysis', '-scriptPath', str(ROOT / 'tools/ghidra'),
               '-postScript', 'TraceCardAccess.java', str(output / 'image-loading.json')]
    for name, address in anchors:
        command.extend([name, hex(address)])
    with (output / 'image-loading.log').open('w') as log:
        subprocess.run(command, env=dict(os.environ, JAVA_HOME=paths['jdk']),
                       stdout=log, stderr=subprocess.STDOUT, check=True)
    print('Image-loader evidence: ' + str(output / 'image-loading.json'))


if __name__ == '__main__':
    main()
