"""Use PPSSPP's own quicksave/load commands on the isolated research process only."""
import argparse
import ctypes as c
from ctypes import wintypes as w
import json
from pathlib import Path
import time
from ppsspp_reference import Debugger

ROOT=Path(__file__).resolve().parents[1]

def main():
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('pid',type=int)
    parser.add_argument('action',choices=['save','load'])
    args=parser.parse_args()
    kernel=c.windll.kernel32
    kernel.OpenProcess.argtypes=[w.DWORD,w.BOOL,w.DWORD]
    kernel.OpenProcess.restype=w.HANDLE
    kernel.QueryFullProcessImageNameW.argtypes=[w.HANDLE,w.DWORD,w.LPWSTR,c.POINTER(w.DWORD)]
    kernel.CloseHandle.argtypes=[w.HANDLE]
    handle=kernel.OpenProcess(0x1000,False,args.pid)
    try:
        name=c.create_unicode_buffer(32768)
        length=w.DWORD(len(name))
        if not kernel.QueryFullProcessImageNameW(handle,0,name,c.byref(length)):
            raise RuntimeError('Cannot verify process executable')
        expected=ROOT/'third_party/stage3/ppsspp/PPSSPPWindows64.exe'
        if Path(name.value).resolve()!=expected.resolve():
            raise ValueError('Not the isolated research emulator')
    finally:
        kernel.CloseHandle(handle)
    user=c.windll.user32
    user.GetWindowThreadProcessId.argtypes=[w.HWND,c.POINTER(w.DWORD)]
    user.GetWindowTextW.argtypes=[w.HWND,w.LPWSTR,c.c_int]
    user.PostMessageW.argtypes=[w.HWND,w.UINT,w.WPARAM,w.LPARAM]
    windows=[]
    callback=c.WINFUNCTYPE(w.BOOL,w.HWND,w.LPARAM)
    @callback
    def visit(hwnd,_):
        owner=w.DWORD()
        user.GetWindowThreadProcessId(hwnd,c.byref(owner))
        title=c.create_unicode_buffer(512)
        if owner.value==args.pid:
            user.GetWindowTextW(hwnd,title,512)
            if title.value.startswith('PPSSPP v'):
                windows.append(hwnd)
        return True
    user.EnumWindows(visit,0)
    if len(windows)!=1:
        raise RuntimeError('Expected one research game window')
    d=Debugger('ws://127.0.0.1:56244/debugger')
    try:
        d.pause()
        # Official PPSSPP v1.20.4 Windows/resource.h and MainWindowMenu.cpp.
        command=40034 if args.action=='save' else 40035
        if not user.PostMessageW(windows[0],0x111,command,0):
            raise RuntimeError('Research window command failed')
        d.socket.send(json.dumps({'event':'cpu.resume'}))
        time.sleep(3)
        d.pause()
        print(json.dumps({'action':args.action,'process_id':args.pid,'cpu':d.call('cpu.status')}))
    finally:
        d.pause()
        d.socket.close()

if __name__=='__main__':
    main()
