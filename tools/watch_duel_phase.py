"""Stop at the next write to the observed phase field; never writes game memory."""
import base64
import json
from pathlib import Path
import struct
import time
from ppsspp_reference import Debugger

ROOT=Path(__file__).resolve().parents[1]
def main():
    out=ROOT/'analysis/duel-reference/phase-watch.json'
    if out.exists():raise ValueError('Preserve existing evidence')
    d=Debugger('ws://127.0.0.1:56244/debugger')
    address=0x09d743f4
    try:
        d.pause()
        d.call('memory.breakpoint.add',address=address,size=4,enabled=True,read=False,write=True,change=True)
        d.socket.send(json.dumps({'event':'cpu.resume'}))
        deadline=time.monotonic()+20
        while time.monotonic()<deadline:
            time.sleep(0.1)
            status=d.call('cpu.status')
            if status['stepping']:
                value=struct.unpack('<I',base64.b64decode(d.call('memory.read',address=address,size=4,replacements=False)['base64']))[0]
                print(json.dumps({'cpu':status,'phase_raw':value}))
                break
        else:raise TimeoutError('No phase write observed')
    finally:
        d.pause()
        d.call('memory.breakpoint.remove',address=address,size=4)
        out.write_text(json.dumps(d.transcript,indent=2)+'\n')
        d.socket.close()

if __name__=='__main__':main()
