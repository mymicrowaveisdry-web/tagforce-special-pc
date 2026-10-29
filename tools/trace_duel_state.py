"""Export bounded state-accessor evidence from the saved duel-overlay analysis."""
import json
import argparse
import os
from pathlib import Path
import subprocess

ROOT=Path(__file__).resolve().parents[1]
def main():
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--handlers',action='store_true')
    args=parser.parse_args()
    out=ROOT/'analysis/duel-reference/DuelEngineVerified'
    target=out/('phase-bounded.json' if args.handlers else 'state-accessors.json')
    if target.exists():raise ValueError('Preserve existing evidence')
    paths=json.loads((ROOT/'analysis/stage3-environment/paths.json').read_text())
    anchors={'PlayerLP':0x09d70e94,'HandCount':0x09d70ea0,'DeckCount':0x09d70ea4,
             'MonsterRecords':0x09d70ebc,'TurnCandidateA':0x09d743e0,
             'TurnCandidateB':0x09d743e4,'PhaseCandidate':0x09d743f4}
    if args.handlers:
        anchors={'Function:PhaseSync':0x09870980,'Function:PhaseChange':0x099cfb14,
                 'Function:TurnChange':0x099cf800,'Function:PlayerReset':0x09870abc,
                 'MonsterArray':0x09d70ed4}
    command=[str(Path(paths['ghidra'])/'support/analyzeHeadless.bat'),str(out/'projects'),
             'DuelEngineStatic','-process','rel_DuelEng.prx','-readOnly','-noanalysis',
             '-scriptPath',str(ROOT/'tools/ghidra'),'-postScript',
             'TraceDuelState.java',str(target)]
    for key,value in anchors.items():command.extend([key,hex(value)])
    with (out/('phase-bounded.log' if args.handlers else 'accessors.log')).open('w') as log:
        subprocess.run(command,cwd=ROOT,env=dict(os.environ,JAVA_HOME=paths['jdk']),stdout=log,stderr=subprocess.STDOUT,check=True)
    print(target)

if __name__=='__main__':main()
