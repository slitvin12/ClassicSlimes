import UnityPy, os, json, sys, time
from UnityPy.helpers.TypeTreeGenerator import TypeTreeGenerator

GAME = r"C:\Program Files (x86)\Steam\steamapps\common\Slime Rancher"
DATA = os.path.join(GAME, "SlimeRancher_Data")
WS = r"D:\SR2ModDev\ClassicSlimes"


def load_env(unity_version="2019.4.29f1"):
    files = [f for f in os.listdir(DATA) if f.endswith('.assets') or (f.startswith('level') and '.' not in f) or f == 'globalgamemanagers']
    files = [os.path.join(DATA, f) for f in files] + [os.path.join(DATA, 'Resources', f) for f in os.listdir(os.path.join(DATA, 'Resources'))]
    env = UnityPy.load(*files)
    gen = TypeTreeGenerator(unity_version)
    gen.load_local_game(GAME)
    env.typetree_generator = gen
    return env


class Index:
    def __init__(self, env):
        self.env = env
        self.by_key = {}
        self.files = {}
        for obj in env.objects:
            fn = os.path.basename(obj.assets_file.name).lower()
            self.files[fn] = obj.assets_file
            self.by_key[(fn, obj.path_id)] = obj
        self.inv = []
        inv = os.path.join(WS, 'extract', 'sr1_inventory.tsv')
        with open(inv, encoding='utf-8') as f:
            for line in f:
                p = line.rstrip('\n').split('\t')
                if len(p) >= 5:
                    self.inv.append(p)

    def find(self, name, typ=None, cls=None):
        out = []
        for p in self.inv:
            if p[4] == name and (typ is None or p[2] == typ) and (cls is None or p[3] == cls):
                out.append(self.by_key.get((p[0].lower(), int(p[1]))))
        return [o for o in out if o is not None]

    def deref(self, src_obj, pptr):
        if not isinstance(pptr, dict):
            return None
        fid = pptr.get('m_FileID', 0)
        pid = pptr.get('m_PathID', 0)
        if pid == 0:
            return None
        af = src_obj.assets_file
        if fid == 0:
            fn = os.path.basename(af.name).lower()
        else:
            try:
                ext = af.externals[fid - 1]
                fn = os.path.basename(ext.path).lower()
            except Exception:
                return None
        return self.by_key.get((fn, pid))

    def label(self, o):
        if o is None:
            return None
        try:
            n = o.peek_name()
        except Exception:
            n = ''
        if o.type.name == 'MonoBehaviour':
            try:
                d = o.read_typetree()
                n = d.get('m_Name', '')
                cls = self.script_class(o)
                return f"MB<{cls}>:{n}"
            except Exception:
                return f"MB:{n}"
        return f"{o.type.name}:{n}"

    def resolve(self, src_obj, value, depth=0, maxlist=40):
        if isinstance(value, dict):
            if set(value.keys()) == {'m_FileID', 'm_PathID'}:
                t = self.deref(src_obj, value)
                if t is None:
                    return None if value.get('m_PathID', 0) == 0 else f"<ext {value}>"
                return f"@{self.label(t)}#{os.path.basename(t.assets_file.name)}:{t.path_id}"
            return {k: self.resolve(src_obj, v, depth + 1, maxlist) for k, v in value.items()}
        if isinstance(value, list):
            if len(value) > maxlist:
                return [self.resolve(src_obj, v, depth + 1, maxlist) for v in value[:maxlist]] + [f"... {len(value)} items"]
            return [self.resolve(src_obj, v, depth + 1, maxlist) for v in value]
        if isinstance(value, (bytes, bytearray)):
            return f"<bytes {len(value)}>"
        return value

    def script_class(self, o):
        raw = o.get_raw_data()
        fid = int.from_bytes(raw[16:20], 'little', signed=True)
        pid = int.from_bytes(raw[20:28], 'little', signed=True)
        s = self.deref(o, {'m_FileID': fid, 'm_PathID': pid})
        if s is None:
            return '?'
        try:
            return s.read_typetree().get('m_ClassName', '?')
        except Exception:
            return '?'

    def mb_class(self, o):
        d = o.read_typetree()
        return self.script_class(o), d

    def dump_gameobject(self, go, indent=0, lines=None, full=True):
        if lines is None:
            lines = []
        d = go.read_typetree()
        pad = '  ' * indent
        lines.append(f"{pad}[GO] {d['m_Name']} active={d.get('m_IsActive')} layer={d.get('m_Layer')} tag={d.get('m_Tag')}")
        children = []
        for c in d['m_Component']:
            comp = self.deref(go, c['component'])
            if comp is None:
                continue
            tn = comp.type.name
            try:
                cd = comp.read_typetree()
            except Exception as e:
                lines.append(f"{pad}  - {tn} <read error {e}>")
                continue
            if tn in ('Transform', 'RectTransform'):
                lines.append(f"{pad}  - {tn} pos={cd['m_LocalPosition']} rot={cd['m_LocalRotation']} scale={cd['m_LocalScale']}")
                for ch in cd['m_Children']:
                    t = self.deref(comp, ch)
                    if t is not None:
                        td = t.read_typetree()
                        g = self.deref(t, td['m_GameObject'])
                        if g is not None:
                            children.append(g)
            elif tn == 'MonoBehaviour':
                cls = self.script_class(comp)
                body = {k: v for k, v in cd.items() if k not in ('m_GameObject', 'm_Script', 'm_Name', 'm_EditorHideFlags', 'm_EditorClassIdentifier', 'm_ObjectHideFlags', 'm_CorrespondingSourceObject', 'm_PrefabInstance', 'm_PrefabAsset')}
                if full:
                    txt = json.dumps(self.resolve(comp, body), ensure_ascii=False, default=str)
                    if len(txt) > 3000:
                        txt = txt[:3000] + '...'
                else:
                    txt = ''
                lines.append(f"{pad}  - MB<{cls}> {txt}")
            elif tn in ('MeshRenderer', 'SkinnedMeshRenderer', 'ParticleSystemRenderer', 'TrailRenderer', 'LineRenderer'):
                mats = [self.resolve(comp, m) for m in cd.get('m_Materials', [])]
                extra = ''
                if tn == 'SkinnedMeshRenderer':
                    extra = f" mesh={self.resolve(comp, cd.get('m_Mesh'))} root={self.resolve(comp, cd.get('m_RootBone'))} bones={len(cd.get('m_Bones', []))}"
                if tn == 'ParticleSystemRenderer':
                    extra = f" mesh={self.resolve(comp, cd.get('m_Mesh'))} mode={cd.get('m_RenderMode')}"
                lines.append(f"{pad}  - {tn} enabled={cd.get('m_Enabled')} mats={mats}{extra}")
            elif tn == 'MeshFilter':
                lines.append(f"{pad}  - MeshFilter mesh={self.resolve(comp, cd.get('m_Mesh'))}")
            elif tn in ('SphereCollider', 'CapsuleCollider', 'BoxCollider', 'MeshCollider'):
                keep = {k: v for k, v in cd.items() if k in ('m_IsTrigger', 'm_Radius', 'm_Height', 'm_Center', 'm_Size', 'm_Direction', 'm_Convex', 'm_Mesh', 'm_Material')}
                lines.append(f"{pad}  - {tn} {self.resolve(comp, keep)}")
            elif tn == 'Rigidbody':
                lines.append(f"{pad}  - Rigidbody mass={cd.get('m_Mass')} drag={cd.get('m_Drag')} angDrag={cd.get('m_AngularDrag')} grav={cd.get('m_UseGravity')} kin={cd.get('m_IsKinematic')}")
            elif tn == 'Light':
                keep = {k: cd.get(k) for k in ('m_Type', 'm_Color', 'm_Intensity', 'm_Range', 'm_Shadows')}
                lines.append(f"{pad}  - Light {keep}")
            elif tn == 'ParticleSystem':
                keep = {}
                im = cd.get('InitialModule', {})
                keep['duration'] = cd.get('lengthInSec'); keep['loop'] = cd.get('looping')
                for k in ('startLifetime', 'startSpeed', 'startColor', 'startSize', 'maxNumParticles'):
                    v = im.get(k)
                    if isinstance(v, dict):
                        v = {kk: vv for kk, vv in v.items() if kk in ('minMaxState', 'scalar', 'minScalar', 'maxColor', 'minColor')}
                    keep[k] = v
                em = cd.get('EmissionModule', {})
                keep['rate'] = (em.get('rateOverTime') or {}).get('scalar')
                sm = cd.get('ShapeModule', {})
                keep['shape'] = {'type': sm.get('type'), 'radius': (sm.get('radius') or {}).get('value') if isinstance(sm.get('radius'), dict) else sm.get('radius')}
                lines.append(f"{pad}  - ParticleSystem {keep}")
            elif tn == 'Animator':
                lines.append(f"{pad}  - Animator ctrl={self.resolve(comp, cd.get('m_Controller'))} avatar={self.resolve(comp, cd.get('m_Avatar'))}")
            elif tn == 'AudioSource':
                lines.append(f"{pad}  - AudioSource clip={self.resolve(comp, cd.get('m_audioClip'))}")
            else:
                lines.append(f"{pad}  - {tn}")
        for g in children:
            self.dump_gameobject(g, indent + 1, lines, full)
        return lines
