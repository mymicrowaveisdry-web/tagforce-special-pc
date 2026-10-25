"""Import the duel-engine overlay at its observed research-run base, without editing it."""
import hashlib
import argparse
import json
import os
from pathlib import Path
import struct
import subprocess

ROOT=Path(__file__).resolve().parents[1]
ISO='e99cff5774856e16329b94c9ff48f9eb0c6bc61ff1127882d0785ca4221bd707'
def main():
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--name',default='DuelEngineStatic')
    args=parser.parse_args()
    if not args.name.isidentifier():
        parser.error('Use an identifier for the fresh run name')
    out=ROOT/'analysis/duel-reference'/args.name
    out.mkdir(exist_ok=False)
    projects=out/'projects'
    projects.mkdir()
    paths=json.loads((ROOT/'analysis/stage3-environment/paths.json').read_text())
    extracted=ROOT/'data/extracted'/ISO
    manifest=json.loads((extracted/'manifest.json').read_text())
    relative='PSP_GAME/USRDIR/gmodule/rel_DuelEng.prx'
    entry=next(f for f in manifest['files'] if f['path']==relative)
    source=extracted/'files'/relative
    digest=hashlib.sha256(source.read_bytes()).hexdigest()
    if digest!=entry['sha256']:
        raise ValueError('Overlay source hash mismatch')
    observation=json.loads((ROOT/'analysis/duel-reference/summon-baseline-30/observation.json').read_text())
    module=next(m for m in observation['modules']['modules'] if m['name']=='modduel_eng' and m['isActive'])
    base=module['address']
    command=[str(Path(paths['ghidra'])/'support/analyzeHeadless.bat'),str(projects),'DuelEngineStatic',
             '-import',str(source),'-loader','PspElfLoader','-loader-imagebase',hex(base),
             '-max-cpu','4','-analysisTimeoutPerFile','600','-scriptPath',str(ROOT/'tools/ghidra'),
             '-postScript','ExportStage3.java',str(out/'static-export.json'),digest,
             hex(struct.unpack_from('<I',source.read_bytes(),24)[0]),
             '-postScript','TraceCardAccess.java',str(out/'state-xrefs.json'),
             'CandidateHandArray','0x09d71024','CandidateRenderHandArray','0x09cf8f56']
    (out/'provenance.json').write_text(json.dumps({'source_sha256':digest,'iso_sha256':ISO,
        'runtime_module':module,'runtime_evidence':'summon-baseline-30/observation.json','command':command},indent=2)+'\n')
    with (out/'console.log').open('w') as log:
        result=subprocess.run(command,cwd=ROOT,env=dict(os.environ,JAVA_HOME=paths['jdk']),stdout=log,stderr=subprocess.STDOUT)
    if result.returncode:
        raise RuntimeError('Overlay import failed; inspect console.log')
    print(out)

if __name__=='__main__':main()
