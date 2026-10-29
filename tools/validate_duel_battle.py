"""Validate private PPSSPP captures and export an asset-free direct-attack fixture."""
import base64
import argparse
import hashlib
import json
from pathlib import Path
import struct

ROOT = Path(__file__).resolve().parents[1]
BASE = ROOT / 'analysis/duel-reference'
ISO = 'e99cff5774856e16329b94c9ff48f9eb0c6bc61ff1127882d0785ca4221bd707'


def digest(path):
    with path.open('rb') as stream:
        return hashlib.file_digest(stream, 'sha256').hexdigest()


def parse_args(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--iso', required=True, type=Path,
                        help='Path to the matching local source ISO; never copied into Git.')
    return parser.parse_args(argv)


def main(argv=None):
    args = parse_args(argv)
    extracted = ROOT / 'data/extracted' / ISO
    manifest = json.loads((extracted / 'manifest.json').read_text())
    if manifest['source_sha256'] != ISO:
        raise ValueError('Extraction manifest source hash mismatch')
    source = args.iso.resolve(strict=True)
    if digest(source) != ISO:
        raise ValueError('Source ISO changed')
    static = BASE / 'DuelEngineVerified/static-export.json'
    exported = json.loads(static.read_text())
    module = extracted / 'files/PSP_GAME/USRDIR/gmodule/rel_DuelEng.prx'
    if exported['analysis_timed_out'] or digest(module) != exported['source_sha256']:
        raise ValueError('Static evidence/module mismatch')
    code = base64.b64decode(exported['entry_code_base64'])
    snapshots = {}
    for name in ['m3-attack-before-1', 'm3-attack-after-1', 'm3-attack-before-2', 'm3-attack-after-2']:
        folder = BASE / name
        observation = json.loads((folder / 'observation.json').read_text())
        raw = (folder / 'ram.bin').read_bytes()
        if hashlib.sha256(raw).hexdigest() != observation['ram_sha256']:
            raise ValueError('Snapshot digest mismatch: ' + name)
        loaded = next(m for m in observation['modules']['modules'] if m['name'] == 'modduel_eng' and m['isActive'])
        if loaded['address'] != 0x09819300:
            raise ValueError('Unexpected module base')
        entry = int(exported['elf_entry_rebased'], 16) - 0x08800000
        if raw[entry:entry + len(code)] != code:
            raise ValueError('Runtime/static entry bytes differ')

        def u32(address):
            return struct.unpack_from('<I', raw, address - 0x08800000)[0]

        players = []
        for player in range(2):
            offset = player * 0xd88
            players.append({
                'lifePoints': u32(0x09d70e94 + offset),
                'handCount': u32(0x09d70ea0 + offset),
                'deckCount': u32(0x09d70ea4 + offset),
                'monsterCardIds': [u32(0x09d70ed4 + offset + zone * 0x18) & 0x3fff for zone in range(5)]
            })
        phase = u32(0x09d743f4)
        if phase != 3:
            raise ValueError('Expected Battle phase')
        projection = {'activePlayer': u32(0x09d743e0), 'turnNumber': u32(0x09d743ec) + 1,
                      'phase': 'Battle', 'players': players}
        snapshots[name] = {'projection': projection, 'ramSha256': observation['ram_sha256'],
                           'observationSha256': digest(folder / 'observation.json'),
                           'transcriptSha256': digest(folder / 'transcript.json'),
                           'frameSha256': digest(folder / 'frame-0.png')}
    before = snapshots['m3-attack-before-1']['projection']
    after = snapshots['m3-attack-after-1']['projection']
    if before != snapshots['m3-attack-before-2']['projection'] or after != snapshots['m3-attack-after-2']['projection']:
        raise ValueError('Attack projection did not repeat')
    expected = json.loads(json.dumps(before))
    expected['players'][1]['lifePoints'] = 6800
    if (before['activePlayer'], before['turnNumber'], before['players'][1]['lifePoints'],
        before['players'][0]['monsterCardIds'], before['players'][1]['monsterCardIds']) != (
            0, 3, 8000, [0, 0, 7716, 0, 0], [0, 0, 0, 0, 0]) or after != expected:
        raise ValueError('Unexpected battle transition')
    inputs = {name: digest(BASE / name / 'transcript.json') for name in
              ['m3-attack-confirm', 'm3-decline-effect', 'm3-attack-confirm-repeat', 'm3-decline-effect-repeat']}
    fixture = {
        'schemaVersion': 1, 'sourceIsoSha256': ISO, 'moduleSha256': exported['source_sha256'],
        'moduleBase': '0x09819300', 'emulator': 'PPSSPP v1.20.4, software renderer',
        'scope': 'Direct attack with optional effects declined. Selected observable fields; synthetic C# decks. '
                 'Does not verify effect behavior, hidden state, battle destruction, LP defeat, RNG or timing.',
        'repeatCount': 2, 'attackerCardId': 7716, 'attackerZone': 2,
        'attackerAttack': 1200, 'attackerDefense': 400, 'attackerLevel': 2,
        'statEvidence': 'Inspected attack-before/after frames display ATK 1200 / DEF 400, level 2.',
        'scenario': {'id': 'direct-attack', 'before': before, 'after': after},
        'snapshots': snapshots, 'inputTranscripts': inputs,
        'saveStateSha256': digest(BASE / 'm3-attack-baseline.ppst')
    }
    target = ROOT / 'config/reference/duel-battle.json'
    target.write_text(json.dumps(fixture, indent=2) + '\n')
    report = {'status': 'pass', 'sourceIsoSha256': ISO, 'moduleSha256': exported['source_sha256'],
              'validatedScenarioCount': 1, 'observableRepetitions': 2, 'validatedSnapshots': 4,
              'runtimeStaticEntryBytesMatch': True, 'entryBytesCompared': len(code),
              'fixtureSha256': digest(target), 'scope': fixture['scope'], 'snapshots': snapshots}
    (BASE / 'battle-validation.json').write_text(json.dumps(report, indent=2) + '\n')
    print(json.dumps({k: v for k, v in report.items() if k != 'snapshots'}, indent=2))


if __name__ == '__main__':
    main()
