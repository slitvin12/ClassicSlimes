import re, os, json
T = r'D:\SR2ModDev\ClassicSlimes\extract\text'
files = {'en': ['actor_1791.txt', 'pedia_1834.txt'], 'ru': ['actor_1778.txt', 'pedia_1927.txt']}
rx = re.compile(r'(?i)(rad|quantum|mosaic|glitch|quicksilver|qubit)')
out = {}
for lang, fl in files.items():
    d = {}
    for fn in fl:
        txt = open(os.path.join(T, fn), encoding='utf-8').read()
        # handle line continuations: lines ending with backslash continue
        txt = txt.replace('\\\n', '')
        for line in txt.splitlines():
            if '=' not in line or line.lstrip().startswith('#'):
                continue
            k, v = line.split('=', 1)
            k = k.strip(); v = v.strip()
            if rx.search(k):
                d[k] = v
    out[lang] = d
json.dump(out, open(os.path.join(T, 'sr1_texts.json'), 'w', encoding='utf-8'), ensure_ascii=False, indent=1)
for k in out['en']:
    print(k, '|', out['en'][k][:70])
