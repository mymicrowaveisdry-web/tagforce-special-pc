"""Capture exact loaded card-table bytes through the isolated PPSSPP debugger."""
import base64
import hashlib
import json
from pathlib import Path
import struct
from ppsspp_reference import Debugger

ROOT=Path(__file__).resolve().parents[1]
ISO_HASH='e99cff5774856e16329b94c9ff48f9eb0c6bc61ff1127882d0785ca4221bd707'


def main():
    output=ROOT/'analysis'/ISO_HASH/'stage4c'
    d=Debugger('ws://127.0.0.1:56244/debugger')
    report={'scope':'Loaded table byte verification; not an on-screen display comparison.',
            'version':d.call('version',name='TagForceCardVerification',version='1'),
            'game':d.call('game.status'),'tables':[]}
    try:
        d.pause()
        def read(address,size):
            result=d.call('memory.read',address=address,size=size,replacements=False)
            return base64.b64decode(result['base64'])
        source=ROOT/'data/extracted'/ISO_HASH/'files/PSP_GAME/USRDIR/duelsys/cardinfo_jpn.ehp'
        blob=source.read_bytes();assert hashlib.sha256(blob).hexdigest()=='6cd21aa6ca4fb82289f329400c9645da0532bf5a5092a604bef4c3e1d82d9060'
        entries={}
        for i in range(struct.unpack_from('<I',blob,12)[0]):
            info,off=struct.unpack_from('<II',blob,16+i*8);end=blob.index(0,info);size=struct.unpack_from('<I',blob,end+1)[0]
            entries[blob[info:end].decode()]=blob[off:off+size]
        for name,global_address in [('CARD_Name_R.bin',0x08b571e8),('CARD_Indx_R.bin',0x08b571f0),
                                    ('CARD_Desc_R.bin',0x08b571f8),('CARD_Prop.bin',0x08b5721c),
                                    ('CARD_IntID.bin',0x08b57228),('CARD_Huff_R.bin',0x08b57248)]:
            address=struct.unpack('<I',read(global_address,4))[0]
            expected=entries[name]
            assert 0x08000000<=address<0x0c000000,(name,hex(address))
            runtime=read(address,len(expected))
            match=runtime==expected
            report['tables'].append(dict(name=name,global_address=hex(global_address),runtime_address=hex(address),
                                        bytes=len(expected),sha256=hashlib.sha256(runtime).hexdigest(),matches_source=match))
        report['modules']=d.call('hle.module.list')
        report['status']='pass' if all(t['matches_source'] for t in report['tables']) else 'divergence'
        print(json.dumps(report,indent=2))
    finally:
        (output/'runtime-tables.json').write_text(json.dumps(report,indent=2)+'\n')
        # Transcript contains locally owned game data: keep it only under ignored analysis/.
        (output/'runtime-transcript.json').write_text(json.dumps(d.transcript,indent=2)+'\n')
        d.socket.close()


if __name__=='__main__':main()
