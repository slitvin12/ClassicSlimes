import UnityPy, os, sys, json, re, struct, io, wave
from collections import defaultdict
sys.path.insert(0, os.path.dirname(__file__))
GAME = r"C:\Program Files (x86)\Steam\steamapps\common\Slime Rancher"
DATA = os.path.join(GAME, "SlimeRancher_Data")
WS = r"D:\SR2ModDev\ClassicSlimes"
OUT = os.path.join(WS, 'extract')

inv = []
with open(os.path.join(OUT, 'sr1_inventory.tsv'), encoding='utf-8') as f:
    for line in f:
        p = line.rstrip('\n').split('\t')
        if len(p) >= 5:
            inv.append(p)

SPRITES = ['iconSlimeRad', 'iconSlimeQuantum', 'iconSlimeMosaic', 'iconSlimeGlitch', 'iconSlimeQuickSilver',
           'iconPlortRad', 'iconPlortQuantum', 'iconPlortMosaic', 'iconPlortQuickSilver',
           'iconGordoRad', 'iconGordoQuantum', 'iconGordoMosaic']
MESHES = ['mosaic_LOD0', 'slime_quicksilver', 'quicksilvercrest', 'slime_default', 'sf_sphere_lowpoly', 'plort']
AUDIO_RX = re.compile(r'(?i)^(radslime|quantumslime|slime_mosaic|quicksilver_energy|quicksilver_hit|vo_quicksilver_happy_0[1-4]$|vo_glitch_(fear|fun|jump|damage)|player_radiation|slime_glitch_split|plort_quicksilver)')
MATRX = re.compile(r'(Rad[A-Z_]|rad[A-Z_]|^rad|Rad$|[Qq]uantum|[Qq]ubit|[Mm]osaic|[Gg]litch|[Qq]uick[Ss]ilver)')
STAGES = sys.argv[1:] or ['sprites','mats','tex','mesh','audio','text']
SKIP = set(open(os.path.join(OUT,'tex_skip.txt')).read().split()) if os.path.exists(os.path.join(OUT,'tex_skip.txt')) else set()

need_files = set()
for p in inv:
    t, n = p[2], p[4]
    if (t == 'Sprite' and n in SPRITES) or (t == 'Mesh' and n in MESHES) or (t == 'AudioClip' and AUDIO_RX.search(n)) or (t == 'TextAsset' and n in ('actor', 'pedia')) or (t == 'Material' and MATRX.search(n)):
        need_files.add(p[0])
# textures can live anywhere; load all .assets + resources
files = [os.path.join(DATA, f) for f in os.listdir(DATA) if f.endswith('.assets') or f.endswith('.resource') or f.endswith('.resS') or f == 'globalgamemanagers' or (f.startswith('level') and '.' not in f)]
print('loading', len(files), 'files; needed', sorted(need_files), flush=True)
env = UnityPy.load(*files)
by_key = {}
for obj in env.objects:
    by_key[(os.path.basename(obj.assets_file.name).lower(), obj.path_id)] = obj

def deref(src, pptr):
    fid = pptr.m_FileID if hasattr(pptr, 'm_FileID') else pptr.get('m_FileID', 0)
    pid = pptr.m_PathID if hasattr(pptr, 'm_PathID') else pptr.get('m_PathID', 0)
    if pid == 0:
        return None
    af = src.assets_file
    if fid == 0:
        fn = os.path.basename(af.name).lower()
    else:
        try:
            fn = os.path.basename(af.externals[fid - 1].path).lower()
        except Exception:
            return None
    return by_key.get((fn, pid))

def objs(typ, pred):
    for p in inv:
        if p[2] == typ and pred(p[4]):
            o = by_key.get((p[0].lower(), int(p[1])))
            if o is not None:
                yield p, o

for d in ('icons', 'tex', 'mesh', 'audio', 'text'):
    os.makedirs(os.path.join(OUT, d), exist_ok=True)

# sprites
for p, o in ([] if 'sprites' not in STAGES else None) or objs('Sprite', lambda n: n in SPRITES):
    try:
        s = o.read()
        s.image.save(os.path.join(OUT, 'icons', p[4] + '.png'))
    except Exception as e:
        print('sprite err', p[4], e)
print('sprites done', flush=True)

# materials
mat_lines = []
tex_needed = {}
for p, o in objs('Material', lambda n: bool(MATRX.search(n))):
    try:
        d = o.read_typetree()
    except Exception as e:
        print('mat err', p[4], e); continue
    sh = deref(o, d['m_Shader'])
    shname = '?'
    if sh is not None:
        try:
            shname = sh.read_typetree().get('m_ParsedForm', {}).get('m_Name', '?')
        except Exception:
            try: shname = sh.peek_name()
            except Exception: pass
    props = d['m_SavedProperties']
    def kv(lst):
        out = {}
        for it in lst:
            if isinstance(it, (list, tuple)):
                k, v = it
            else:
                k, v = it.get('first'), it.get('second')
            out[k] = v
        return out
    cols = {k: [round(v['r'], 4), round(v['g'], 4), round(v['b'], 4), round(v['a'], 4)] for k, v in kv(props['m_Colors']).items()}
    floats = {k: round(v, 4) for k, v in kv(props['m_Floats']).items()}
    texs = {}
    for k, v in kv(props['m_TexEnvs']).items():
        t = deref(o, v['m_Texture'])
        if t is not None:
            tn = t.peek_name()
            texs[k] = f"{tn} s{round(v['m_Scale']['x'],3)},{round(v['m_Scale']['y'],3)} o{round(v['m_Offset']['x'],3)},{round(v['m_Offset']['y'],3)}"
            tex_needed[(os.path.basename(t.assets_file.name).lower(), t.path_id)] = tn
    kw = d.get('m_ShaderKeywords') or ' '.join(d.get('m_ValidKeywords', []))
    mat_lines.append(f"MAT {p[4]} [{p[0]}:{p[1]}] | {shname} | kw={kw} rq={d.get('m_CustomRenderQueue')}\n  cols={json.dumps(cols)}\n  floats={json.dumps(floats)}\n  tex={json.dumps(texs)}")
with open(os.path.join(OUT, 'materials.txt'), 'w', encoding='utf-8') as f:
    f.write('\n'.join(mat_lines))
print('materials', len(mat_lines), 'textures', len(tex_needed), flush=True)

for key, tn in (tex_needed.items() if 'tex' in STAGES else []):
    o = by_key.get(key)
    if tn in SKIP or os.path.exists(os.path.join(OUT, 'tex', tn + '.png')): continue
    _fmt = int(o.read().m_TextureFormat)
    print('TEX', tn, _fmt, flush=True)
    if _fmt in (28, 29, 64, 65):
        open(os.path.join(OUT, 'tex_crunched.txt'), 'a').write(f'{tn} {key[0]} {key[1]} {_fmt}\n'); continue
    try:
        t = o.read()
        img = t.image
        img.save(os.path.join(OUT, 'tex', tn + '.png'))
    except Exception as e:
        print('tex err', tn, e)
print('textures done', flush=True)

# meshes
from UnityPy.helpers.MeshHelper import MeshHandler
mesh_info = []
done_mesh = set()
for p, o in (objs('Mesh', lambda n: n in MESHES) if 'mesh' in STAGES else []):
    if p[4] in done_mesh:
        continue
    done_mesh.add(p[4])
    try:
        m = o.read()
        h = MeshHandler(m)
        h.process()
        verts = h.m_Vertices; norms = h.m_Normals; uv = h.m_UV0
        tris = []
        for sm in h.get_triangles():
            for tri in sm:
                tris.extend(tri)
        bw = getattr(h, 'm_BoneWeights', None)
        nv = len(verts)
        # binary: magic, nv, ntri, flags; verts(3f) norms(3f) uv(2f) [bone idx 4i + w 4f] tris(int)
        flags = (1 if norms else 0) | (2 if uv else 0) | (4 if bw else 0)
        buf = io.BytesIO()
        buf.write(b'CSM1'); buf.write(struct.pack('<iii', nv, len(tris), flags))
        for v in verts: buf.write(struct.pack('<3f', *v[:3]))
        if norms:
            for v in norms: buf.write(struct.pack('<3f', *v[:3]))
        if uv:
            for v in uv: buf.write(struct.pack('<2f', *v[:2]))
        if bw:
            bidx = getattr(h, 'm_BoneIndices', None)
            print('BI sample', bidx[0] if bidx else None, flush=True)
            for i, ww in enumerate(bw):
                bi = bidx[i]
                buf.write(struct.pack('<4i', *[int(x) for x in bi]))
                buf.write(struct.pack('<4f', *[float(x) for x in ww]))
        buf.write(struct.pack(f'<{len(tris)}i', *tris))
        bps = list(getattr(m, 'm_BindPose', []) or [])
        buf.write(struct.pack('<i', len(bps)))
        for bpm in bps:
            if not hasattr(bpm, 'e00'):
                print('BP sample', type(bpm), dir(bpm)[:40], flush=True)
            for r in range(4):
                for c in range(4):
                    buf.write(struct.pack('<f', float(getattr(bpm, f'e{r}{c}'))))
        if bps:
            print('BP0', [round(float(getattr(bps[0], f'e{r}{c}')), 3) for r in range(4) for c in range(4)], flush=True)
        with open(os.path.join(OUT, 'mesh', p[4] + '.csm'), 'wb') as f:
            f.write(buf.getvalue())
        xs = [v[0] for v in verts]; ys = [v[1] for v in verts]; zs = [v[2] for v in verts]
        bp = len(getattr(m, 'm_BindPose', []) or [])
        mesh_info.append(f"{p[4]} nv={nv} ntri={len(tris)//3} flags={flags} bindposes={bp} bounds x[{min(xs):.3f},{max(xs):.3f}] y[{min(ys):.3f},{max(ys):.3f}] z[{min(zs):.3f},{max(zs):.3f}] submeshes={len(m.m_SubMeshes)}")
    except Exception as e:
        import traceback; traceback.print_exc()
        print('mesh err', p[4], e)
with open(os.path.join(OUT, 'mesh', 'info.txt'), 'w') as f:
    f.write('\n'.join(mesh_info))
print('\n'.join(mesh_info), flush=True)

# audio
na = 0
for p, o in ([] if 'audio' not in STAGES else None) or objs('AudioClip', lambda n: bool(AUDIO_RX.search(n))):
    try:
        a = o.read()
        for name, data in a.samples.items():
            with open(os.path.join(OUT, 'audio', p[4] + '.wav'), 'wb') as f:
                f.write(data)
            na += 1
            break
    except Exception as e:
        print('audio err', p[4], e)
print('audio', na, flush=True)

# text
for p, o in ([] if 'text' not in STAGES else None) or objs('TextAsset', lambda n: n in ('actor', 'pedia', 'ui', 'range')):
    try:
        t = o.read()
        s = t.m_Script
        if isinstance(s, str):
            b = s.encode('utf-8', 'surrogateescape')
        else:
            b = bytes(s)
        with open(os.path.join(OUT, 'text', f"{p[4]}_{p[1]}.txt"), 'wb') as f:
            f.write(b)
    except Exception as e:
        print('text err', p[4], e)
print('ALL DONE', flush=True)
