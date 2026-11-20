"""Focus the existing editor and refresh. Never touches Unity processes or licensing data."""
import ctypes,time
from ctypes import wintypes
u=ctypes.windll.user32
u.GetForegroundWindow.restype=wintypes.HWND
u.SetForegroundWindow.argtypes=[wintypes.HWND]
cb=ctypes.WINFUNCTYPE(wintypes.BOOL,wintypes.HWND,wintypes.LPARAM)
found=[]
def visit(h,l):
 b=ctypes.create_unicode_buffer(512);u.GetWindowTextW(h,b,512)
 if b.value.startswith('S - ') and 'Unity' in b.value:found.append(h)
 return True
u.EnumWindows(cb(visit),0)
if found:
 u.SetForegroundWindow(found[0]);time.sleep(.2)
 if u.GetForegroundWindow()==found[0]:
  u.keybd_event(17,0,0,0);time.sleep(.1);u.keybd_event(82,0,0,0);time.sleep(.1);u.keybd_event(82,0,2,0);u.keybd_event(17,0,2,0)
  print('Unity refresh requested')
