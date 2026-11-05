"""Scoped PPSSPP v6 GE draw measurements; no runtime approval is inferred.

Format authority: PPSSPP v1.20.4 GPU/Debugger/RecordFormat.h, GPUState.cpp
and ge_constants.h. Supports the measured title's short-position vertices only.
"""
import json, struct, sys
from pathlib import Path
from compression import zstd

def read_dump(path):
    b=Path(path).read_bytes()
    if b[:8]!=b'PPSSPPGE' or struct.unpack_from('<I',b,8)[0]!=6: raise ValueError('Expected PPSSPP v6 frame dump')
    n,size=struct.unpack_from('<II',b,24);p=32;blocks=[]
    for _ in range(2):
        length=struct.unpack_from('<I',b,p)[0];p+=4;blocks.append(zstd.decompress(b[p:p+length]));p+=length
    commands,data=blocks
    if len(commands)!=n*9 or len(data)!=size:raise ValueError('Dump length mismatch')
    return [struct.unpack_from('<BII',commands,i*9)for i in range(n)],data

def measure(path):
    commands,data=read_dump(path);state={};mat={0x3a:[0.]*12,0x3c:[0.]*12,0x3e:[0.]*16};index={};vertices=b'';texture=b'';clut=b'';rows=[]
    def f(v):return struct.unpack('<f',struct.pack('<I',v<<8))[0]
    def m43(m,v):return [sum(m[j*3+i]*v[j] for j in range(3))+m[9+i]for i in range(3)]
    def transform(v):
        if state.get(0x12,0)&0x800000:return v[:2]
        v=m43(mat[0x3a],[x/32768 for x in v]);v=m43(mat[0x3c],v)+[1.]
        q=[sum(mat[0x3e][j*4+i]*v[j]for j in range(4))for i in range(4)]
        return [q[0]/q[3]*f(state[0x42])+f(state[0x45])-state[0x4c]/16,q[1]/q[3]*f(state[0x43])+f(state[0x46])-state[0x4d]/16]
    def op(word):
        c=word>>24;v=word&0xffffff;state[c]=v
        if c in mat:index[c]=v
        if c-1 in mat:
            k=c-1;i=index.get(k,0)
            if i<len(mat[k]):mat[k][i]=f(v)
            index[k]=i+1
        if c==4:
            vt=state[0x12];count=v&65535;points=[];uv=[]
            if vt in [0x102,0x100]:
                stride=10 if vt==0x102 else 6
                if len(vertices)!=count*stride:raise ValueError('Unsupported vertex extent')
                for i in range(count):
                    off=i*stride
                    if vt==0x102:
                        u,w=struct.unpack_from('<HH',vertices,off);uv.append([u/32768*f(state.get(0x48,0))+f(state.get(0x4a,0)),w/32768*f(state.get(0x49,0))+f(state.get(0x4b,0))]);off+=4
                    points.append(transform(struct.unpack_from('<hhh',vertices,off)))
            rows.append(dict(ordinal=len(rows),primitive=v>>16,vtype=vt,points=points,uv=uv,state=dict(state),texture=texture,clut=clut))
    for kind,size,p in commands:
        blob=data[p:p+size]
        if len(blob)!=size:raise ValueError('Command out of bounds')
        if kind==0:
            for word, in struct.iter_unpack('<I',blob[17*4:]):
                if word>>24==12:break
                op(word)
        if kind==1:
            for word, in struct.iter_unpack('<I',blob):op(word)
        if kind==2:vertices=blob
        if kind==16:texture=blob
        if kind==4:clut=blob
    return rows

def decode_indexed_texture(row):
    s=row['state'];w=1<<(s[0xb8]&15);h=1<<((s[0xb8]>>8)&15);fmt=s[0xc3]&15
    if fmt not in (4,5):raise ValueError('Unsupported texture format')
    stride=s[0xa8]&0x7ff;bpp=4 if fmt==4 else 8;pitch=stride*bpp//8
    raw=row['texture'];clut=row['clut'];rgba=bytearray();mode=s[0xc5]
    mask=(mode>>8)&255;shift=(mode>>2)&31;start=((mode>>16)&31)*16
    if mode&3!=3:raise ValueError('Unsupported CLUT format')
    for y in range(h):
        for x in range(w):
            byte=x*bpp//8
            offset=((y//8)*max(1,pitch//16)*128+(byte//16)*128+(y%8)*16+byte%16) if s[0xc2]&1 else y*pitch+byte
            index=raw[offset]
            if bpp==4:index=(index>>(4*(x%2)))&15
            at=(((index>>shift)&mask)|start)*4
            rgba+=clut[at:at+4]
    if len(rgba)!=w*h*4:raise ValueError('Incomplete native texture/CLUT')
    return w,h,bytes(rgba)

if __name__=='__main__':
    for r in measure(sys.argv[1]):
        if r['points']:
            print(r['ordinal'],r['primitive'],hex(r['vtype']), 'xy',[[round(a,3)for a in p]for p in r['points']], 'uv',[[round(a,3)for a in p]for p in r['uv']], 'texture',len(r['texture']))
