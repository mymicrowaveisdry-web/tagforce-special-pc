"""Native-size scientific overlay/diff; comparisons do not grant screen acceptance."""
import argparse, hashlib, json
from pathlib import Path
from PIL import Image, ImageChops, ImageEnhance

def private(path):
    p=Path(path).resolve()
    if any((parent/'.git').exists() for parent in (p,*p.parents)):
        raise ValueError('Original media and derived comparisons must stay outside Git worktrees')
    return p

def main():
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('reference',type=Path)
    parser.add_argument('pc',type=Path)
    parser.add_argument('--output',type=Path,required=True)
    parser.add_argument('--reference-sha256',required=True,help='Reviewed original image identity, not inferred from the PC output')
    args=parser.parse_args()
    reference,pc,out=private(args.reference),private(args.pc),private(args.output)
    reference_bytes=reference.read_bytes()
    if hashlib.sha256(reference_bytes).hexdigest()!=args.reference_sha256:raise ValueError('Original reference hash mismatch')
    out.mkdir(parents=True,exist_ok=False)
    left=Image.open(reference).convert('RGBA');right=Image.open(pc).convert('RGBA')
    if left.size!=(480,272) or right.size!=(480,272):raise ValueError('Require exact native 480x272 frames; no rescaling')
    a,b=left.tobytes(),right.tobytes();rgb_different=sum(a[i:i+3]!=b[i:i+3] for i in range(0,len(a),4))
    report=dict(referenceSha256=args.reference_sha256,pcSha256=hashlib.sha256(pc.read_bytes()).hexdigest(),
        dimensions=[480,272],differentRgbPixels=rgb_different,totalPixels=480*272,
        differentAlphaPixels=sum(x!=y for x,y in zip(a[3::4],b[3::4])),
        rgbMeanAbsoluteError=sum(abs(a[i]-b[i]) for i in range(len(a)) if i%4!=3)/(480*272*3),
        rgbMaximumError=max(abs(a[i]-b[i]) for i in range(len(a)) if i%4!=3),
        result='exact' if a==b else 'mismatch-requires-review',acceptanceGranted=False)
    Image.blend(left,right,.5).save(out/'overlay.png')
    diff=ImageChops.difference(left.convert('RGB'),right.convert('RGB'))
    ImageEnhance.Brightness(diff).enhance(4).save(out/'diff-x4.png')
    (out/'report.json').write_text(json.dumps(report,indent=2)+'\n')
    print(json.dumps(report))

if __name__=='__main__':main()
