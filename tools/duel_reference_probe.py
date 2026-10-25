"""Record controlled input and paused memory/screens in the disposable PPSSPP profile."""
import argparse
import base64
from datetime import datetime, timezone
import hashlib
import json
from pathlib import Path
import time
from PIL import Image
from ppsspp_reference import Debugger

ROOT=Path(__file__).resolve().parents[1]
OUTPUT=ROOT/'analysis/duel-reference'

def main():
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('name')
    parser.add_argument('--button',choices=['cross','circle','triangle','square','start','select','up','down','left','right','ltrigger','rtrigger'])
    parser.add_argument('--seconds',type=float,default=0)
    parser.add_argument('--ram',action='store_true')
    parser.add_argument('--until-standby',action='store_true',help='Stop just after the observed phase setter writes Standby')
    args=parser.parse_args()
    if not args.name.replace('-','_').isidentifier() or not 0<=args.seconds<=30:
        parser.error('Use a simple run name and a duration of 0-30 seconds')
    out=OUTPUT/args.name
    out.mkdir(parents=True,exist_ok=False)
    d=Debugger('ws://127.0.0.1:56244/debugger')
    report={'created_utc':datetime.now(timezone.utc).isoformat(), 'input':vars(args),
            'screenshot_scope':'Diagnostic raw VRAM candidates, RGBA8888, stride 512, 480x272. Select visible frame by inspection; not a GPU capture.'}
    def read(address,size):
        return base64.b64decode(d.call('memory.read',address=address,size=size,replacements=False)['base64'])
    try:
        d.pause()
        report['before_cpu']=d.call('cpu.status')
        if args.until_standby:
            # state-accessors.json/phase-bounded.json identify the phase setter.
            d.call('cpu.breakpoint.add',address=0x09870980,enabled=True,condition='a0 == 1')
        if args.button or args.seconds or args.until_standby:
            request={'event':'cpu.resume'}
            d.transcript.append({'request':request})
            d.socket.send(json.dumps(request))
            if args.button:
                d.call('input.buttons.press',button=args.button,duration=3)
            time.sleep(args.seconds)
            if args.until_standby:
                deadline=time.monotonic()+20
                while time.monotonic()<deadline:
                    time.sleep(0.1)
                    if d.call('cpu.status')['stepping']:
                        break
                else:raise TimeoutError('Standby setter was not reached')
                report['phase_handler_entry']=d.call('cpu.status')
                # Step the real transition handler through its phase write.
                for _ in range(40):
                    if int.from_bytes(read(0x09d743f4,4),'little')==1:
                        break
                    previous=d.call('cpu.status')['pc']
                    request={'event':'cpu.stepOver'}
                    d.transcript.append({'request':request})
                    d.socket.send(json.dumps(request))
                    step_deadline=time.monotonic()+5
                    while time.monotonic()<step_deadline:
                        time.sleep(0.02)
                        status=d.call('cpu.status')
                        if status['stepping'] and status['pc']!=previous:
                            break
                    else:raise TimeoutError('Phase handler step did not complete')
                report['phase_raw_at_stop']=int.from_bytes(read(0x09d743f4,4),'little')
                if report['phase_raw_at_stop']!=1:
                    raise ValueError('Breakpoint did not establish Standby')
            else:
                d.pause()
        report['after_cpu']=d.call('cpu.status')
        report['game']=d.call('game.status')
        report['modules']=d.call('hle.module.list')
        vram=read(0x04000000,0x200000)
        (out/'vram.bin').write_bytes(vram)
        report['vram_sha256']=hashlib.sha256(vram).hexdigest()
        for offset in (0,0x88000):
            Image.frombytes('RGBA',(512,272),vram[offset:offset+512*272*4]).convert('RGB').crop((0,0,480,272)).save(out/f'frame-{offset:x}.png')
        if args.ram:
            with (out/'ram.bin').open('wb') as stream:
                for address in range(0x08800000,0x0a000000,0x40000):
                    stream.write(read(address,0x40000))
            report['ram_start']='0x08800000'
            report['ram_sha256']=hashlib.sha256((out/'ram.bin').read_bytes()).hexdigest()
        print(json.dumps({'output':str(out),'modules':report['modules']['modules']},indent=2))
    finally:
        try:
            d.pause()
            if args.until_standby:
                d.call('cpu.breakpoint.remove',address=0x09870980)
        finally:
            # Large base64 responses stay in binary snapshots, not duplicated in JSON.
            for item in d.transcript:
                if 'response' in item and 'base64' in item['response']:
                    response=item['response']
                    raw=base64.b64decode(response.pop('base64'))
                    response['captured_bytes']=len(raw)
                    response['sha256']=hashlib.sha256(raw).hexdigest()
            (out/'observation.json').write_text(json.dumps(report,indent=2)+'\n')
            (out/'transcript.json').write_text(json.dumps(d.transcript,indent=2)+'\n')
            d.socket.close()

if __name__=='__main__':
    main()
