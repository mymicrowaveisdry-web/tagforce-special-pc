"""Decode selected CPM and CPJ artwork locally. Reject truncated CIP/GIM profiles."""
import base64
import hashlib
import io
import json
from pathlib import Path
import re
import struct
from PIL import Image

ROOT=Path(__file__).resolve().parents[1]
HASH='e99cff5774856e16329b94c9ff48f9eb0c6bc61ff1127882d0785ca4221bd707'


def decode_psp_dxt1(data,width,height):
    if width<=0 or height<=0 or width%4 or height%4 or width*height>4096*4096:
        raise ValueError('Unsupported DXT1 dimensions')
    if len(data)!=width*height//2:raise ValueError('DXT1 payload length mismatch')
    rgba=bytearray(width*height*4)
    def color(v):return ((v>>8)&248,(v>>3)&252,(v<<3)&248,255)
    for by in range(height//4):
        for bx in range(width//4):
            off=(by*(width//4)+bx)*8
            # PSP stores four selector bytes before the two RGB565 colors.
            bits,c0,c1=struct.unpack_from('<IHH',data,off);a,b=color(c0),color(c1)
            colors=[a,b]
            if c0>c1:
                colors += [tuple((2*a[i]+b[i])//3 for i in range(3))+(255,),
                           tuple((a[i]+2*b[i])//3 for i in range(3))+(255,)]
            else:colors += [tuple((a[i]+b[i])//2 for i in range(3))+(255,),(0,0,0,0)]
            for y in range(4):
                for x in range(4):
                    target=((by*4+y)*width+bx*4+x)*4
                    rgba[target:target+4]=bytes(colors[(bits>>(2*(y*4+x)))&3])
    return bytes(rgba)


def decode_cpj_slot(slot):
    if len(slot)<0xa1:raise ValueError('Truncated CPJ slot')
    result=bytearray(slot);state=int.from_bytes(slot[0x9e:0xa0],'big')
    for i in range(0xa0,len(result)):
        state=((50849*state+3343)>>1)&65535
        result[i]^=(state>>2)&255
    return bytes(result)


def jpeg_header():
    # Read the MIT-licensed format reference locally; do not embed its header arrays in our source.
    source=(ROOT/'research/CIPTool/CIPTool.h').read_text(encoding='utf-8-sig')
    array=re.search(r'#define CPJ_JFIFHEADER_TFSP\s*\{([^}]+)\}',source)[1]
    return bytes(int(v.strip(),16) for v in array.split(',') if v.strip())


def main():
    base=ROOT/'analysis'/HASH/'stage4bVerified'
    sources=ROOT/'data/extracted'/HASH/'files/PSP_GAME/USRDIR/card'
    output=ROOT/'data/decoded/stage4c-images'
    output.mkdir(parents=True,exist_ok=False)
    report={'images':[],'unsupported':[],'source_hashes':{}}
    template=jpeg_header();report['jpeg_prefix_sha256']=hashlib.sha256(template).hexdigest()
    for name in ['cardm_j','cardj256_j','cardh_j']:
        path=sources/(name+'.cip')
        digest=hashlib.sha256(path.read_bytes()).hexdigest()
        listing=json.loads((base/(name+'-card-images-list.json')).read_text())
        assert digest==listing['sourceSha256']
        report['source_hashes'][path.name]=digest
        for card_id in [4007,4041,4842,11213]:
            entry=next(e for e in listing['index']['entries'] if e['cardId']==card_id)
            with path.open('rb') as source:source.seek(entry['offset']);slot=source.read(entry['storedSlotBytes'])
            if name=='cardm_j':
                rgba=decode_psp_dxt1(slot,64,64)
                image=Image.frombytes('RGBA',(64,64),rgba)
                target=output/f'{card_id}-cpm.png';image.save(target)
            elif name=='cardj256_j':
                decoded=decode_cpj_slot(slot);end=decoded.find(b'\xff\xd9',0xa0)
                if end<0:raise ValueError('JPEG terminator not found')
                jpeg=template+decoded[0xa0:end+2]
                image=Image.open(io.BytesIO(jpeg));image.load()
                if image.size!=(256,256):raise ValueError('Unexpected CPJ sample dimensions')
                target=output/f'{card_id}-cpj.jpg';target.write_bytes(jpeg)
            else:
                if not slot.startswith(b'MIG.00.1PSP'):raise ValueError('Missing GIM signature')
                width,height=struct.unpack_from('<HH',slot,0x48)
                required=128+width*height//2
                report['unsupported'].append(dict(card_id=card_id,container=path.name,
                    reason='Nominal GIM footprint exceeds its fixed slot; no cross-slot read or repair performed.',
                    slot_bytes=len(slot),nominal_bytes=required,width=width,height=height))
                continue
            report['images'].append(dict(card_id=card_id,source=path.name,path=target.relative_to(ROOT).as_posix(),
                width=image.width,height=image.height,sha256=hashlib.sha256(target.read_bytes()).hexdigest()))
        assert hashlib.sha256(path.read_bytes()).hexdigest()==digest
    (ROOT/'analysis'/HASH/'stage4c/image-samples.json').write_text(json.dumps(report,indent=2)+'\n')
    print(json.dumps({'decoded_images':len(report['images']),'unresolved_cip_profiles':len(report['unsupported']),
                      'output':str(output)},indent=2))


if __name__=='__main__':main()
