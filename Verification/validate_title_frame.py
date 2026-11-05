"""Independent private original-GE/source-pixel and measured-geometry audit. Not a unit-test runner."""
import argparse,hashlib,json,subprocess
from pathlib import Path
from title_gpu_frame import measure,decode_indexed_texture

REPO=Path(__file__).resolve().parents[1]
def sha(path):
    digest=hashlib.sha256()
    with Path(path).open('rb')as stream:
        for data in iter(lambda:stream.read(1024*1024),b''):digest.update(data)
    return digest.hexdigest()

def main():
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--iso',type=Path,required=True)
    parser.add_argument('--data-root',type=Path,required=True)
    parser.add_argument('--dump',type=Path,required=True)
    parser.add_argument('--profile',type=Path,default=REPO/'TagForceSpecial.PC/App/Screens/Title/title-frame.json')
    parser.add_argument('--output',type=Path,required=True)
    args=parser.parse_args()
    for path in [args.iso,args.data_root,args.dump,args.output]:
        if any((p/'.git').exists()for p in [path.resolve(),*path.resolve().parents]):raise ValueError('Original evidence stays outside Git')
    if sha(args.iso)!='e99cff5774856e16329b94c9ff48f9eb0c6bc61ff1127882d0785ca4221bd707':raise ValueError('Exact source image mismatch')
    host=REPO/'TagForceSpecial.Tools/bin/Debug/net10.0/TagForceSpecial.Tools.dll'
    for arguments in [['source-check',str(args.iso)],['presentation-check',str(args.data_root)]]:
        subprocess.run(['dotnet',str(host),*arguments],check=True,capture_output=True,text=True)
    profile=json.loads(args.profile.read_text());dump_sha=sha(args.dump)
    if profile['FrameDumpSha256']!=dump_sha:raise ValueError('Measured original frame identity changed')
    binding=json.loads((args.data_root/'presentation/active.json').read_text())
    package=args.data_root/'presentation/generations'/binding['generation']/'package-cache/packages'/binding['packageId']
    manifest=json.loads((package/'manifest.json').read_text())
    resources={r['AssetKey']:r for r in manifest['Resources']};rows=measure(args.dump);compared=set()
    for draw in profile['Draws']:
        row=rows[draw['Ordinal']]
        if draw['Primitive']!=row['primitive']or draw['Points']!=row['points']or draw['Uv']!=row['uv']:raise ValueError('Geometry differs from original draw')
        state=row['state']
        if draw['LinearSampling']!=(state.get(0xc6,0)==0x101):raise ValueError('Native sampling differs')
        if draw['Additive']!=(state.get(0xdf,0)==0xa2 and state.get(0xe1,0)==0xffffff):raise ValueError('Native blend differs')
        if draw['ClearStencil']!=bool(state.get(0xd3,0)&1)or draw['WriteMask']!=(state.get(0xe8,0)==0xffffff)or draw['Stencil']!=bool(state.get(0x24,0)):raise ValueError('Original mask/coverage state differs')
        color,ambient=state.get(0x55,0),state.get(0x5c,0xffffff)
        expected=[((color>>(8*i))&255)*((ambient>>(8*i))&255)//255 for i in range(3)]+[state.get(0x58,255)*state.get(0x5d,255)//255]
        if draw['Color']!=expected:raise ValueError('Measured original tint differs')
        key=draw['AssetKey']
        if key is not None:
            w,h,native=decode_indexed_texture(row);resource=resources[key]
            blob=(package/'blobs'/(resource['ContentSha256']+'.bin')).read_bytes()
            if (w,h)!=(resource['Width'],resource['Height'])or native!=blob:raise ValueError('Original/source texture pixels differ')
            compared.add(key)
    result=dict(sourceSha256=sha(args.iso),frameDumpSha256=dump_sha,profileSha256=sha(args.profile),
        validatedSourceTextures=len(compared),measuredDraws=len(profile['Draws']),sourcePixelsExact=True,
        geometryMatchesCapturedCommands=True,originalScreenAcceptance=False,
        exclusions=['Complete original save/transfer flow','Original start-menu font','Intro/BGM/animation timing','Full-frame pixel equivalence'])
    if result['sourceSha256']!='e99cff5774856e16329b94c9ff48f9eb0c6bc61ff1127882d0785ca4221bd707':raise ValueError('Source changed during audit')
    with args.output.open('x',encoding='utf-8')as output:json.dump(result,output,indent=2)
    print(json.dumps(result))

if __name__=='__main__':main()
