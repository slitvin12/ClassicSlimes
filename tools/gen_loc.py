# Generates src/Assets/localized.csv (Cotton format: KEYS,en,ru) from SR1 texts + SR2 additions.
import json, re, os, sys
BASE = r'D:\SR2ModDev\ClassicSlimes'
D = json.load(open(os.path.join(BASE, 'extract', 'text', 'sr1_texts.json'), encoding='utf-8'))
EN, RU = D['en'], D['ru']

def norm(s):
    s = s.replace('\r', ' ').replace('\t', ' ')
    s = re.sub(r'[ \u00a0]*\\n[ \u00a0]*', r'\\n', s)   # literal \n with surrounding spaces
    s = re.sub(r'\s+', ' ', s).strip()
    return s

rows = {}
def put(key, en, ru):
    rows[key] = (norm(en), norm(ru if ru else en))

slimes = ['rad', 'quantum', 'mosaic', 'quicksilver', 'glitch']
missing = []
for k in slimes:
    keys = ['l.%s_slime' % k, 'm.intro.%s_slime' % k, 'm.slimeology.%s_slime' % k, 'm.risks.%s_slime' % k, 'm.plortonomics.%s_slime' % k]
    if k != 'glitch':
        keys.append('l.%s_plort' % k)
    for key in keys:
        if key in EN:
            put(key, EN[key], RU.get(key))
        else:
            missing.append(key)

# SR2 adaptation notes appended to pedia texts
NOTES = {
    'm.plortonomics.quicksilver_slime': (
        ' \\n \\nThere is no electricity to feed them in the Rainbow Islands, but quicksilver slimes happily drain the energy stored in rad plorts.',
        ' \\n \\nНа Радужных островах нет электричества, чтобы их кормить, но ртутные слаймы с удовольствием вытягивают энергию из радиоактивных плортов.'),
    'm.risks.glitch_slime': (
        ' \\n \\nIn the Rainbow Islands a splash of water works just as well as debug spray. Vac a revealed glitch slime to debug it for a small reward.',
        ' \\n \\nНа Радужных островах вместо отладочного спрея подойдёт обычная вода. Засосите разоблачённого глючного слайма, чтобы отладить его и получить небольшую награду.'),
}
for key, (en, ru) in NOTES.items():
    if key in rows:
        e, r = rows[key]
        rows[key] = (e + norm(en), r + norm(ru))

# SR1 largo names
for key in EN:
    if key.startswith('l.') and key.endswith('_largo') and re.search(r'(rad|quantum|mosaic)', key):
        put(key, EN[key], RU.get(key))

# Generated largo names for SR2 slimes that did not exist in SR1 (filled by sr2 slime list)
ADJ = json.load(open(os.path.join(BASE, 'tools', 'adjectives.json'), encoding='utf-8')) if os.path.exists(os.path.join(BASE, 'tools', 'adjectives.json')) else {}
OURS = {'rad': ('Rad', 'радиоактивный'), 'quantum': ('Quantum', 'квантовый'), 'mosaic': ('Mosaic', 'мозаичный')}
for o, (oen, oru) in OURS.items():
    for other, (en, ru) in list(ADJ.items()) + [(x, (OURS[x][0], OURS[x][1])) for x in OURS if x != o]:
        for a, b in ((o, other), (other, o)):
            key = 'l.%s_%s_largo' % (a, b)
            if key in rows:
                continue
            # EN: "Rad Pink Largo" in key order; RU: adjectives, hyphenated nouns go last
            ea = oen if a == o else en
            eb = en if a == o else oen
            ra = oru if a == o else ru
            rb = ru if a == o else oru
            parts = [ra, rb]
            nouns = [p for p in parts if p.endswith('-')]
            adjs = [p for p in parts if not p.endswith('-')]
            ru_name = ' '.join(adjs) + ' ' + ''.join(nouns) + 'ларго' if nouns else ' '.join(adjs) + ' ларго'
            ru_name = ru_name[0].upper() + ru_name[1:]
            put(key, '%s %s Largo' % (ea, eb), ru_name)

out = os.path.join(BASE, 'src', 'Assets', 'localized.csv')
with open(out, 'w', encoding='utf-8', newline='') as f:
    f.write('KEYS,en,ru\r\n')
    for k in sorted(rows):
        e, r = rows[k]
        f.write('%s,"%s","%s"\r\n' % (k, e.replace('"', '""'), r.replace('"', '""')))
print('rows', len(rows), 'missing', missing)
