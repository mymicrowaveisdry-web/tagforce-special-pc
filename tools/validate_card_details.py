"""Validate full C# decoding and compare controlled PPSSPP samples."""
import json
import os
from pathlib import Path
import re
import struct
import subprocess
from probe_card_accessors import huffman

ROOT=Path(__file__).resolve().parents[1]
HASH='e99cff5774856e16329b94c9ff48f9eb0c6bc61ff1127882d0785ca4221bd707'


def main():
    out=ROOT/'analysis'/HASH/'stage4c'
    source=ROOT/'data/extracted'/HASH/'files/PSP_GAME/USRDIR/duelsys/cardinfo_jpn.ehp'
    command=['dotnet',str(ROOT/'TagForceSpecial.Tools/bin/Debug/net10.0/TagForceSpecial.Tools.dll'),
             'card-details',str(source),'3459','11657']
    env=dict(os.environ,DOTNET_CLI_HOME=str(ROOT/'analysis/dotnet-home'),DOTNET_CLI_TELEMETRY_OPTOUT='1')
    a=subprocess.run(command,env=env,capture_output=True,text=True,check=True).stdout
    b=subprocess.run(command,env=env,capture_output=True,text=True,check=True).stdout
    assert a==b
    (out/'card-details.json').write_text(a,encoding='utf-8')
    decoded=json.loads(a)['index'];by_id={r['cardId']:r for r in decoded}
    blob=source.read_bytes();tables={}
    for i in range(struct.unpack_from('<I',blob,12)[0]):
        info,off=struct.unpack_from('<II',blob,16+i*8);end=blob.index(0,info);size=struct.unpack_from('<I',blob,end+1)[0]
        tables[blob[info:end].decode()]=blob[off:off+size]
    dictionary=tables['DICT_R.bin'];count,table,data=struct.unpack_from('<III',dictionary);words=[]
    for i in range(count):
        pos=data+struct.unpack_from('<I',dictionary,table+i*4)[0];end=pos
        while dictionary[end:end+2]!=b'\0\0':end+=2
        words.append(dictionary[pos:end].decode('utf-16le'))
    rows=json.loads((ROOT/'analysis'/HASH/'stage4bVerified/cardinfo_jpn-cardinfo-list.json').read_text())['index']['rows']
    for row in rows:
        text=huffman(tables['CARD_Huff_R.bin'],tables['CARD_Desc_R.bin'],row['rawDescriptionIndex'])
        text=re.sub(r'\$d([0-9]{3})',lambda m:words[int(m[1])],text)
        text=re.sub(r'\$m([0-9]+)',lambda m:by_id[int(m[1])]['name'],text)
        assert text==by_id[row['cardId']]['description'],row['cardId']
    runtime=json.loads((out/'accessor-probes.json').read_text())
    for sample in runtime['samples']:
        actual=by_id[sample['card_id']];fields=sample['runtime'];props=actual['properties']
        assert actual['name']==sample['name'] and actual['description']==sample['runtime_description']
        assert props['attack']==fields['attack'] and props['defense']==fields['defense']
        for key,field in [('attributeCode','field7_10'),('level','field11_14'),('typeCode','field18_22'),
                          ('getterKindCode','field1_6'),('iconCode','field15_17'),('field23To26','field23_26'),('field27To30','field27_30')]:
            assert props[key]==fields[field]
    report=dict(status='pass',decoded_cards=len(decoded),independent_description_matches=len(rows),
        runtime_sample_matches=len(runtime['samples']),repeat_output_identical=True,
        effects_scope='Effect text decoded; effect execution behavior is not reconstructed.',
        description_max_characters=max(len(r['description']) for r in decoded))
    (out/'detail-validation.json').write_text(json.dumps(report,indent=2)+'\n')
    print(json.dumps(report,indent=2))


if __name__=='__main__':main()
