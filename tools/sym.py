import pefile, os, sys, urllib.request, ctypes, ctypes.wintypes as w
dll = r"C:\Program Files\dotnet\shared\Microsoft.NETCore.App\6.0.16\coreclr.dll"
rvas = [int(x, 16) for x in sys.argv[1:]] or [0x1d2361, 0x15e589, 0x15e86c]
pe = pefile.PE(dll, fast_load=True)
pe.parse_data_directories(directories=[pefile.DIRECTORY_ENTRY['IMAGE_DIRECTORY_ENTRY_DEBUG']])
raw = open(dll, 'rb').read()
i = [d.struct.PointerToRawData for d in pe.DIRECTORY_ENTRY_DEBUG if d.struct.Type == 2][0]
import struct, uuid
gb = raw[i+4:i+20]; age = struct.unpack('<I', raw[i+20:i+24])[0]
d1, d2, d3 = struct.unpack('<IHH', gb[:8])
g = '%08X%04X%04X%s' % (d1, d2, d3, gb[8:].hex().upper())
name = raw[i+24:raw.index(b'\0', i+24)].decode().split('\\')[-1]
key = g + '%X' % age
symdir = os.path.join(os.environ['TEMP'], 'sym')
os.makedirs(symdir, exist_ok=True)
pdb = os.path.join(symdir, name)
print('pdb', name, key)
if not os.path.exists(pdb):
    url = 'https://msdl.microsoft.com/download/symbols/%s/%s/%s' % (name, key, name)
    req = urllib.request.Request(url, headers={'User-Agent': 'Microsoft-Symbol-Server/10.0'})
    data = urllib.request.urlopen(req, timeout=120).read()
    open(pdb, 'wb').write(data)
    print('downloaded', len(data))
size = pe.OPTIONAL_HEADER.SizeOfImage
dbg = ctypes.WinDLL('dbghelp.dll')
k32 = ctypes.windll.kernel32
k32.GetCurrentProcess.restype = w.HANDLE
h = k32.GetCurrentProcess()
dbg.SymSetOptions(0x2 | 0x10 | 0x40 | 0x80000000)  # UNDNAME | LOAD_LINES | LOAD_ANYTHING | DEBUG
CB=ctypes.WINFUNCTYPE(w.BOOL, w.HANDLE, w.ULONG, ctypes.c_uint64, ctypes.c_uint64)
def cb(hp, act, data, ctx):
    if act == 0x10:
        try: print('dbg:', ctypes.wstring_at(data).strip()[:200])
        except Exception: pass
    return False
_cb=CB(cb)
dbg.SymInitializeW.argtypes = [w.HANDLE, w.LPCWSTR, w.BOOL]
print('init', dbg.SymInitializeW(h, symdir, False))
dbg.SymRegisterCallbackW64.argtypes=[w.HANDLE, CB, ctypes.c_uint64]
dbg.SymRegisterCallbackW64(h, _cb, 0)
dbg.SymLoadModuleExW.restype = ctypes.c_uint64
dbg.SymLoadModuleExW.argtypes = [w.HANDLE, w.HANDLE, w.LPCWSTR, w.LPCWSTR, ctypes.c_uint64, w.DWORD, ctypes.c_void_p, w.DWORD]
base = 0x180000000
r = dbg.SymLoadModuleExW(h, None, dll, None, base, size, None, 0)
print('load', hex(r), k32.GetLastError())
buf=ctypes.create_string_buffer(4096)
struct.pack_into('<I', buf, 0, 3256)
dbg.SymGetModuleInfoW64.argtypes=[w.HANDLE, ctypes.c_uint64, ctypes.c_void_p]
print('modinfo', dbg.SymGetModuleInfoW64(h, base, buf), struct.unpack_from('<II', buf, 28))
class SI(ctypes.Structure):
    _fields_ = [('SizeOfStruct', w.ULONG), ('TypeIndex', w.ULONG), ('Reserved', ctypes.c_uint64 * 2), ('Index', w.ULONG),
                ('Size', w.ULONG), ('ModBase', ctypes.c_uint64), ('Flags', w.ULONG), ('Value', ctypes.c_uint64),
                ('Address', ctypes.c_uint64), ('Register', w.ULONG), ('Scope', w.ULONG), ('Tag', w.ULONG),
                ('NameLen', w.ULONG), ('MaxNameLen', w.ULONG), ('Name', w.WCHAR * 1024)]
dbg.SymFromAddrW.argtypes = [w.HANDLE, ctypes.c_uint64, ctypes.POINTER(ctypes.c_uint64), ctypes.POINTER(SI)]
for rva in rvas:
    si = SI(); si.SizeOfStruct = 88; si.MaxNameLen = 1000
    disp = ctypes.c_uint64(0)
    ok = dbg.SymFromAddrW(h, base + rva, ctypes.byref(disp), ctypes.byref(si))
    print(hex(rva), si.Name if ok else ('fail %d' % k32.GetLastError()), '+0x%x' % disp.value)
ENUM=ctypes.WINFUNCTYPE(w.BOOL, ctypes.POINTER(SI), w.ULONG, ctypes.c_void_p)
syms=[]
def en(p, size, ctx):
    s=p.contents
    syms.append((s.Address-base, s.Size, ctypes.wstring_at(ctypes.addressof(s)+SI.Name.offset, s.NameLen)))
    return True
_en=ENUM(en)
dbg.SymEnumSymbolsW.argtypes=[w.HANDLE, ctypes.c_uint64, w.LPCWSTR, ENUM, ctypes.c_void_p]
print('enum', dbg.SymEnumSymbolsW(h, base, '*', _en, None), len(syms))
syms.sort()
import bisect
addrs=[a for a,_,_ in syms]
for rva in rvas:
    k=bisect.bisect_right(addrs, rva)-1
    for j in range(max(0,k-1), min(len(syms),k+2)):
        print('  %x near %x size=%x %s' % (rva, syms[j][0], syms[j][1], syms[j][2][:150]))
