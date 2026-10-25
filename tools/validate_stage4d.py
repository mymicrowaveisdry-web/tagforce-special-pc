"""Validate the local normalized catalog against previously verified decoded and raw fields."""
import hashlib
import json
import os
from pathlib import Path
import subprocess

ROOT = Path(__file__).resolve().parents[1]
HASH = 'e99cff5774856e16329b94c9ff48f9eb0c6bc61ff1127882d0785ca4221bd707'


def main():
    base = ROOT / 'analysis' / HASH
    output = base / 'stage4d'
    output.mkdir(parents=True, exist_ok=True)
    source = ROOT / 'data/extracted' / HASH / 'files/PSP_GAME/USRDIR/duelsys/cardinfo_jpn.ehp'
    before = hashlib.sha256(source.read_bytes()).hexdigest()
    command = ['dotnet', str(ROOT / 'TagForceSpecial.Tools/bin/Debug/net10.0/TagForceSpecial.Tools.dll'),
               'card-catalog', str(source), '3459', '11657']
    env = dict(os.environ, DOTNET_CLI_HOME=str(ROOT / 'analysis/dotnet-home'))
    text = subprocess.run(command, capture_output=True, text=True, check=True, env=env).stdout
    report = json.loads(text)
    catalog = report['index']
    assert catalog['source']['containerSha256'] == before == report['sourceSha256'] == report['sourceSha256After']
    decoded = {r['cardId']: r for r in json.loads((base / 'stage4c/card-details.json').read_text())['index']}
    rows = {r['cardId']: r for r in json.loads((base / 'stage4bVerified/cardinfo_jpn-cardinfo-list.json').read_text())['index']['rows']}
    assert catalog['count'] == len(catalog['cards']) == len(decoded)
    assert [c['gameCardId'] for c in catalog['cards']] == sorted(decoded)
    mapping = {'attribute': 'attributeCode', 'type': 'typeCode', 'rawKind': 'rawKindCode',
               'getterKind': 'getterKindCode', 'icon': 'iconCode',
               'field23To26': 'field23To26', 'field27To30': 'field27To30'}
    for card in catalog['cards']:
        expected = decoded[card['gameCardId']]
        raw = rows[card['gameCardId']]
        assert card['name'] == expected['name'] and card['description'] == expected['description']
        assert all(card['statistics'][k] == expected['properties'][k] for k in ['attack', 'defense', 'level'])
        assert all(card['codes'][k] == expected['properties'][v] for k, v in mapping.items())
        assert card['sourceRow'] == dict(internalIndex=raw['internalIndex'], propertyWord0=raw['rawPropertyWord0'],
                                        propertyWord1=raw['rawPropertyWord1'], descriptionBitOffset=raw['rawDescriptionIndex'])
    assert hashlib.sha256(source.read_bytes()).hexdigest() == before
    (output / 'card-catalog.json').write_text(text, encoding='utf-8')
    validation = dict(status='pass', normalized_cards=catalog['count'], all_decoded_fields_preserved=True,
                      all_raw_rows_preserved=True, source_sha256=before, source_unchanged=True,
                      scope='Structural import validation; visual evidence is recorded separately.')
    (output / 'catalog-validation.json').write_text(json.dumps(validation, indent=2) + '\n')
    print(json.dumps(validation, indent=2))


if __name__ == '__main__':
    main()
