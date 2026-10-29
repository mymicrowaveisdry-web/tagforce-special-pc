"""Validate local duel captures and export a small asset-free observable-state fixture."""
import base64
import argparse
import hashlib
import json
from pathlib import Path
import struct

ROOT=Path(__file__).resolve().parents[1]
ISO='e99cff5774856e16329b94c9ff48f9eb0c6bc61ff1127882d0785ca4221bd707'
BASE=ROOT/'analysis/duel-reference'
RAM=0x08800000
ENGINE=0x09819300

def digest(path):
    h=hashlib.sha256()
    with path.open('rb') as stream:
        for chunk in iter(lambda:stream.read(1024*1024),b''):h.update(chunk)
    return h.hexdigest()

def parse_args(argv=None):
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--iso',required=True,type=Path,help='Path to the matching local source ISO; never copied into Git.')
    return parser.parse_args(argv)


def main(argv=None):
    args=parse_args(argv)
    manifest=json.loads((ROOT/'data/extracted'/ISO/'manifest.json').read_text())
    if manifest['source_sha256']!=ISO:raise ValueError('Extraction manifest source hash mismatch')
    iso=args.iso.resolve(strict=True)
    if digest(iso)!=ISO:raise ValueError('Running source ISO no longer matches its manifest')
    static=BASE/'DuelEngineVerified/static-export.json'
    exported=json.loads(static.read_text())
    if exported['analysis_timed_out']:raise ValueError('Static analysis timed out')
    module=ROOT/'data/extracted'/ISO/'files/PSP_GAME/USRDIR/gmodule/rel_DuelEng.prx'
    if digest(module)!=exported['source_sha256']:raise ValueError('Overlay hash mismatch')
    snapshots={}
    def snapshot(name):
        if name in snapshots:return snapshots[name]['projection']
        folder=BASE/name
        observation=json.loads((folder/'observation.json').read_text())
        raw=(folder/'ram.bin').read_bytes()
        if hashlib.sha256(raw).hexdigest()!=observation['ram_sha256']:
            raise ValueError('Snapshot hash mismatch: '+name)
        loaded=next(m for m in observation['modules']['modules'] if m['name']=='modduel_eng' and m['isActive'])
        if loaded['address']!=ENGINE:raise ValueError('Unexpected overlay base')
        entry=int(exported['elf_entry_rebased'],16)-RAM
        code=base64.b64decode(exported['entry_code_base64'])
        if raw[entry:entry+len(code)]!=code:raise ValueError('Runtime/static entry bytes differ')
        def u32(address):return struct.unpack_from('<I',raw,address-RAM)[0]
        phase=u32(0x09d743f4)
        players=[]
        for player in range(2):
            offset=player*0xd88
            players.append({'lifePoints':u32(0x09d70e94+offset),
                'handCount':u32(0x09d70ea0+offset),'deckCount':u32(0x09d70ea4+offset),
                'monsterCardIds':[u32(0x09d70ed4+offset+zone*0x18)&0x3fff for zone in range(5)]})
        projection={'activePlayer':u32(0x09d743e0),
            'turnNumber':u32(0x09d743ec)+1,
            'phase':{0:'Draw',1:'Standby',2:'Main1',3:'Battle',4:'Main2',5:'End'}.get(phase,'Transition'),
            'players':players}
        snapshots[name]={'projection':projection,'phaseRaw':phase,
            'ramSha256':observation['ram_sha256'],'observationSha256':digest(folder/'observation.json'),
            'frameSha256':digest(folder/'frame-0.png')}
        return projection

    summon_before=snapshot('summon-baseline-30')
    summon_after=snapshot('summon-resolved-32')
    if summon_before!=snapshot('summon-repeat-before-33') or summon_after!=snapshot('summon-repeat-after-34'):
        raise ValueError('Normal summon projections did not repeat')
    end_before=snapshot('end-turn-input-41')
    end_after=snapshot('opponent-turn-44')
    if end_before!=snapshot('end-repeat-end-47') or end_after!=snapshot('draw-repeat-before-50'):
        raise ValueError('End turn projections did not repeat')
    draw_animated=snapshot('opponent-draw-after-45')
    if draw_animated!=snapshot('draw-repeat-after-51'):
        raise ValueError('Draw count projections did not repeat')
    draw_after=snapshot('draw-logical-standby-59')
    if draw_after['phase']!='Standby' or draw_after['players']!=draw_animated['players']:
        raise ValueError('Settled draw does not match the repeated count evidence')
    if (summon_before['players'][0]['handCount'],summon_after['players'][0]['handCount'],
        summon_after['players'][0]['monsterCardIds'])!=(5,4,[0,0,7716,0,0]):
        raise ValueError('Unexpected summon observation')
    if (end_after['turnNumber'],end_after['activePlayer'],draw_after['players'][1]['handCount'],
        draw_after['players'][1]['deckCount'])!=(2,1,6,34):
        raise ValueError('Unexpected turn/draw observation')
    fixture={'schemaVersion':1,'sourceIsoSha256':ISO,'moduleSha256':exported['source_sha256'],
        'moduleBase':'0x09819300','emulator':'PPSSPP v1.20.4, software renderer',
        'scope':'Selected observable fields only; C# uses synthetic ordered decks. No full hidden-state, effect, RNG or animation equivalence claim.',
        'repeatCount':2,'summonedCardId':7716,'summonedLevel':2,'summonedZone':2,
        'scenarios':[
            {'id':'normal-summon','before':summon_before,'after':summon_after,
             'evidence':['summon-baseline-30','summon-resolved-32','summon-repeat-before-33','summon-repeat-after-34']},
            {'id':'end-turn','before':end_before,'after':end_after,
             'evidence':['end-turn-input-41','opponent-turn-44','end-repeat-end-47','draw-repeat-before-50']},
            {'id':'draw','before':end_after,'after':draw_after,
             'evidence':['opponent-turn-44','opponent-draw-after-45','draw-repeat-before-50','draw-repeat-after-51','draw-logical-standby-59']}
        ],'snapshots':snapshots}
    target=ROOT/'config/reference/duel-primitives.json'
    target.parent.mkdir(parents=True,exist_ok=True)
    target.write_text(json.dumps(fixture,indent=2)+'\n')
    report={'status':'pass','sourceIsoSha256':ISO,'moduleSha256':exported['source_sha256'],
        'validatedScenarioCount':3,'observableRepetitions':2,'validatedSnapshots':len(snapshots),
        'runtimeStaticEntryBytesMatch':True,'entryBytesCompared':len(base64.b64decode(exported['entry_code_base64'])),
        'fixtureSha256':digest(target),'snapshots':snapshots,
        'staticEvidence':{str(p.relative_to(ROOT)):digest(p) for p in [static,BASE/'DuelEngineVerified/state-accessors.json',BASE/'DuelEngineVerified/phase-bounded.json']},
        'saveStates':{p.name:digest(p) for p in [BASE/'summon-baseline.ppst',BASE/'end-turn-baseline.ppst']},
        'scope':fixture['scope'],'limitations':['Only one standard-rules duel setup and a level-2 summon were sampled.',
            'Turn number mapping is corroborated for turns 1 and 2 only; tag ordering is unverified.',
            'Draw animation retains Draw phase after count changes; logical phase compared at the real Standby setter.',
            'C# does not reproduce original deck identities/order, hidden flags, effects, RNG, instruction timing or full replay.']}
    (BASE/'validation.json').write_text(json.dumps(report,indent=2)+'\n')
    # Candidate registry sizes remain analysis metadata, never executable match evidence.
    registry=ROOT/'config/npjh00142/overlays/rel_DuelEng_functions.csv'
    registry.write_text('address,body_bytes\n'+''.join(f"0x{f['address']},{f['body_bytes']}\n" for f in exported['functions']))
    print(json.dumps({k:v for k,v in report.items() if k not in ('snapshots','staticEvidence','saveStates','limitations')},indent=2))

if __name__=='__main__':main()
