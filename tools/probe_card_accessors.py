"""Invoke a fixed set of observed read-only getters in the disposable PPSSPP profile.
Emulator registers and the touched stack/scratch bytes are restored after each call.
This is a controlled function experiment, not a claim about on-screen presentation.
"""
import base64
import hashlib
import json
from pathlib import Path
import struct
import time
import re
from ppsspp_reference import Debugger

ROOT=Path(__file__).resolve().parents[1]
HASH='e99cff5774856e16329b94c9ff48f9eb0c6bc61ff1127882d0785ca4221bd707'


def huffman(tree,data,offset,max_chars=2048):
    words=struct.unpack('<'+'H'*(len(tree)//2),tree)
    out=[];bit=offset;steps=0
    while len(out)<max_chars:
        node=0
        while True:
            if bit>=len(data)*8 or node+2>=len(words):raise ValueError('Huffman bounds')
            edge=words[node+((data[bit//8]>>(7-bit%8))&1)]
            steps+=1
            if steps>max_chars*1024:raise ValueError('Huffman traversal limit')
            if edge==0:break
            if edge%2:raise ValueError('Unaligned Huffman node')
            node=edge//2;bit+=1
        char=words[node+2]
        if char==0:return bytes().join(struct.pack('<H',c) for c in out).decode('utf-16le')
        out.append(char)
    raise ValueError('Huffman output limit')


def main():
    output=ROOT/'analysis'/HASH/'stage4c'
    listing=json.loads((ROOT/'analysis'/HASH/'stage4bVerified/cardinfo_jpn-cardinfo-list.json').read_text())
    records={r['cardId']:r for r in listing['index']['rows']}
    archive=(ROOT/'data/extracted'/HASH/'files/PSP_GAME/USRDIR/duelsys/cardinfo_jpn.ehp').read_bytes()
    tables={}
    for i in range(struct.unpack_from('<I',archive,12)[0]):
        info,off=struct.unpack_from('<II',archive,16+i*8);end=archive.index(0,info);size=struct.unpack_from('<I',archive,end+1)[0]
        tables[archive[info:end].decode()]=archive[off:off+size]
    d=Debugger('ws://127.0.0.1:56244/debugger');d.pause()
    report={'scope':'Controlled calls to game getters in an isolated disposable emulator; not visual UI comparison.',
            'binary_sha256':'8087681f81f87a87471591752b3c0c9bc3cec48ffff15a115a1e2525f670c975','samples':[]}
    def read(address,size):return base64.b64decode(d.call('memory.read',address=address,size=size,replacements=False)['base64'])
    def write(address,b):d.call('memory.write',address=address,base64=base64.b64encode(b).decode())
    def invoke(address,a0,a1=0,a2=0):
        assert address in [0x0881ec58,0x0881f334,0x0881f388,0x0881f148,0x0881f170,0x0881f11c,0x0881f2b0,0x0881f2e0,0x0881f238,0x0881f274,0x0881fc5c,0x0881eca0]
        regs=d.call('cpu.getAllRegs')['categories'][0]
        saved=dict(zip(regs['registerNames'],regs['uintValues']))
        pc=saved['pc'];sp=saved['sp'];scratch=sp-0x8000
        stack=read(scratch,0x8100)
        try:
            for name,value in [('a0',a0),('a1',a1),('a2',a2),('ra',pc),('pc',address)]:d.call('cpu.setReg',name=name,value=value)
            d.socket.send(json.dumps({'event':'cpu.runUntil','address':pc}))
            deadline=time.monotonic()+10
            while time.monotonic()<deadline:
                state=d.call('cpu.status')
                if state['stepping'] and state['pc']==pc:break
                time.sleep(0.01)
            else:raise TimeoutError('Getter did not return')
            return d.call('cpu.getReg',name='v0')['uintValue']
        finally:
            d.pause();write(scratch,stack)
            for name,value in saved.items():
                if name not in ('zr','zero','r0'):d.call('cpu.setReg',name=name,value=value)
    try:
        dictionary=tables['DICT_R.bin'];dict_count,dict_table,dict_data=struct.unpack_from('<III',dictionary)
        words=[]
        for i in range(dict_count):
            offset=dict_data+struct.unpack_from('<I',dictionary,dict_table+i*4)[0];end=offset
            while dictionary[end:end+2]!=b'\0\0':end+=2
            words.append(dictionary[offset:end].decode('utf-16le'))
        for card_id in [4007,4041,4842,11213]:
            record=records[card_id];w0=record['rawPropertyWord0'];w1=record['rawPropertyWord1']
            name_pointer=invoke(0x0881ec58,card_id)
            text=read(name_pointer,256);end=next(i for i in range(0,254,2) if text[i:i+2]==b'\0\0')
            name=text[:end].decode('utf-16le')
            expected={'field18_22':(w1>>18)&31,'field7_10':(w1>>7)&15,'field11_14':(w1>>11)&15,
                      'field15_17':(w1>>15)&7,'field1_6':11 if card_id in (4998,4999,5000) else (w1>>1)&63,
                      'field23_26':(w1>>23)&15,'field27_30':(w1>>27)&15,
                      'attack':0 if ((w0>>14)&511)==511 else ((w0>>14)&511)*10,
                      'defense':0 if (w0>>23)==511 else (w0>>23)*10}
            funcs={'attack':0x0881f334,'defense':0x0881f388,'field7_10':0x0881f148,
                   'field11_14':0x0881f170,'field18_22':0x0881f11c,'field15_17':0x0881f2b0,'field1_6':0x0881f2e0,
                   'field23_26':0x0881f238,'field27_30':0x0881f274}
            observed={k:invoke(f,card_id) for k,f in funcs.items()}
            decoded=huffman(tables['CARD_Huff_R.bin'],tables['CARD_Desc_R.bin'],record['rawDescriptionIndex'])
            sample={'card_id':card_id,'name':name,'name_matches':name==record['name'],
                    'expected':expected,'runtime':observed,'fields_match':expected==observed,
                    'huffman_text':decoded,'huffman_text_length':len(decoded)}
            # Final description getter includes dictionary and card-name substitutions.
            ptr=invoke(0x0881eca0,card_id)
            text=read(ptr,1976);end=next(i for i in range(0,1974,2) if text[i:i+2]==b'\0\0')
            final=text[:end].decode('utf-16le')
            sample['runtime_description']=final
            sample['raw_huffman_matches_final']=decoded==final
            expanded=re.sub(r'\$d([0-9]{3})',lambda m:words[int(m[1])],decoded)
            expanded=re.sub(r'\$m([0-9]+)',lambda m:records[int(m[1])]['name'],expanded)
            sample['decoded_description']=expanded
            sample['expanded_description_matches']=expanded==final
            report['samples'].append(sample)
        report['status']='pass' if all(s['name_matches'] and s['fields_match'] and s['expanded_description_matches'] for s in report['samples']) else 'divergence'
        print(json.dumps({**report,'samples':[{k:v for k,v in s.items() if k not in ('huffman_text','runtime_description','decoded_description')} for s in report['samples']]},indent=2))
    finally:
        (output/'accessor-probes.json').write_text(json.dumps(report,indent=2)+'\n')
        d.socket.close()


if __name__=='__main__':main()
