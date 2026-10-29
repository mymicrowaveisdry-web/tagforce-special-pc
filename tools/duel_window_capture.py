"""Capture only windows owned by the isolated research emulator, including hidden windows."""
import argparse
import ctypes as c
from ctypes import wintypes as w
from pathlib import Path
from PIL import Image

def capture(pid, output):
    root = Path(__file__).resolve().parents[1]
    output = Path(output).resolve()
    if not output.is_relative_to(root / 'analysis') or output.exists():
        raise ValueError('Choose a fresh analysis/ filename')
    user = c.windll.user32
    user.GetWindowThreadProcessId.argtypes = [w.HWND,c.POINTER(w.DWORD)]
    user.GetWindowRect.argtypes = [w.HWND,c.POINTER(w.RECT)]
    user.GetWindowTextW.argtypes = [w.HWND,w.LPWSTR,c.c_int]
    user.GetWindowDC.argtypes = [w.HWND]
    user.PrintWindow.argtypes = [w.HWND,w.HDC,w.UINT]
    user.ReleaseDC.argtypes = [w.HWND,w.HDC]
    user.SetProcessDPIAware()
    windows = []
    callback = c.WINFUNCTYPE(w.BOOL, w.HWND, w.LPARAM)
    @callback
    def visit(hwnd, _):
        owner = w.DWORD()
        user.GetWindowThreadProcessId(hwnd, c.byref(owner))
        if owner.value == pid:
            rect = w.RECT()
            user.GetWindowRect(hwnd, c.byref(rect))
            title = c.create_unicode_buffer(512)
            user.GetWindowTextW(hwnd, title, 512)
            if 'PPSSPP' in title.value and rect.right-rect.left > 400 and rect.bottom-rect.top > 200:
                windows.append((hwnd, rect, title.value))
        return True
    user.EnumWindows(visit, 0)
    if not windows:
        raise RuntimeError('No research window found')
    hwnd, rect, title = windows[0]
    width, height = rect.right-rect.left, rect.bottom-rect.top
    gdi = c.windll.gdi32
    gdi.CreateCompatibleDC.argtypes = [w.HDC]
    gdi.CreateCompatibleBitmap.argtypes = [w.HDC,c.c_int,c.c_int]
    gdi.SelectObject.argtypes = [w.HDC,w.HANDLE]
    gdi.GetDIBits.argtypes = [w.HDC,w.HBITMAP,w.UINT,w.UINT,c.c_void_p,c.c_void_p,w.UINT]
    gdi.DeleteObject.argtypes = [w.HANDLE]
    gdi.DeleteDC.argtypes = [w.HDC]
    user.GetWindowDC.restype = w.HDC
    gdi.CreateCompatibleDC.restype = w.HDC
    gdi.CreateCompatibleBitmap.restype = w.HBITMAP
    gdi.SelectObject.restype = w.HANDLE
    dc = user.GetWindowDC(hwnd)
    mem = gdi.CreateCompatibleDC(dc)
    bitmap = gdi.CreateCompatibleBitmap(dc, width, height)
    old = gdi.SelectObject(mem, bitmap)
    class Header(c.Structure):
        _fields_ = [('size',w.DWORD),('width',w.LONG),('height',w.LONG),
                    ('planes',w.WORD),('bits',w.WORD),('compression',w.DWORD),
                    ('imageSize',w.DWORD),('x',w.LONG),('y',w.LONG),
                    ('used',w.DWORD),('important',w.DWORD)]
    try:
        if not user.PrintWindow(hwnd, mem, 2):
            raise RuntimeError('PrintWindow failed')
        header = Header(c.sizeof(Header),width,-height,1,32,0,0,0,0,0,0)
        buffer = c.create_string_buffer(width*height*4)
        gdi.SelectObject(mem, old)
        if not gdi.GetDIBits(mem,bitmap,0,height,buffer,c.byref(header),0):
            raise RuntimeError('GetDIBits failed')
        output.parent.mkdir(parents=True,exist_ok=True)
        Image.frombuffer('RGB',(width,height),buffer,'raw','BGRX',0,1).save(output)
        print(f'Captured research window {title!r}: {output}')
    finally:
        gdi.DeleteObject(bitmap)
        gdi.DeleteDC(mem)
        user.ReleaseDC(hwnd,dc)

if __name__ == '__main__':
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('pid',type=int)
    parser.add_argument('output')
    args=parser.parse_args()
    capture(args.pid,args.output)
