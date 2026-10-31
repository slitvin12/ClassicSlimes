import UnityPy, sys, os, time
from UnityPy.enums import ClassIDType

root = r"C:\Program Files (x86)\Steam\steamapps\common\Slime Rancher\SlimeRancher_Data"
out = r"D:\SR2ModDev\ClassicSlimes\extract\sr1_inventory.tsv"
os.makedirs(os.path.dirname(out), exist_ok=True)
t = time.time()
files = [f for f in os.listdir(root) if f.endswith('.assets') or f.startswith('level') and '.' not in f or f in ('globalgamemanagers',)]
files = [os.path.join(root, f) for f in files] + [os.path.join(root, 'Resources', f) for f in os.listdir(os.path.join(root, 'Resources'))]
env = UnityPy.load(*files)
print('loaded', len(env.objects), time.time() - t, flush=True)
script_cache = {}
with open(out, 'w', encoding='utf-8') as f:
    for obj in env.objects:
        tn = obj.type.name
        name = ''
        cls = ''
        try:
            if tn == 'MonoBehaviour':
                d = obj.read(check_read=False)
                name = getattr(d, 'm_Name', '')
                sp = d.m_Script
                key = (sp.m_FileID, sp.m_PathID)
                if key not in script_cache:
                    try:
                        s = sp.read()
                        script_cache[key] = (s.m_Namespace + '.' if s.m_Namespace else '') + s.m_ClassName
                    except Exception as e:
                        script_cache[key] = '?'
                cls = script_cache[key]
            elif tn in ('GameObject', 'Material', 'Texture2D', 'Mesh', 'Shader', 'AudioClip', 'Sprite', 'AnimationClip', 'MonoScript', 'TextAsset', 'Cubemap', 'Font', 'RuntimeAnimatorController', 'AnimatorController'):
                name = obj.peek_name() or ''
        except Exception as e:
            name = 'ERR:' + str(e)[:60]
        f.write(f"{os.path.basename(obj.assets_file.name)}\t{obj.path_id}\t{tn}\t{cls}\t{name}\n")
print('done', time.time() - t, flush=True)
