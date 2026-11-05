# Copies extracted SR1 assets into src/Assets (embedded into the DLL).
import os, shutil, wave, array
BASE = r'D:\SR2ModDev\ClassicSlimes'
EX = os.path.join(BASE, 'extract')
OUT = os.path.join(BASE, 'src', 'Assets')
os.makedirs(OUT, exist_ok=True)

for f in os.listdir(os.path.join(EX, 'icons')):
    if f.endswith('.png'):
        shutil.copy2(os.path.join(EX, 'icons', f), os.path.join(OUT, f))
# mosaicGordo_LOD0 (SR1 Mosaic gordo facet shell) comes from tools/sr1gordo.py
for f in ['mosaic_LOD0', 'slime_quicksilver', 'quicksilvercrest', 'sf_sphere_lowpoly', 'slime_default', 'plort', 'mosaicGordo_LOD0']:
    shutil.copy2(os.path.join(EX, 'mesh', f + '.csm'), os.path.join(OUT, f + '.csm'))

CLIPS = ['radslime_aura_03', 'player_radiation_sickness', 'quantumslime_warp_01', 'quantumslime_warp_05',
         'quantumslime_despawn_03b', 'quantumslime_idle_07', 'slime_mosaic_glint_spawn_01', 'slime_mosaic_glint_blast_03',
         'slime_mosaic_aura_03', 'quicksilver_energy_aura_01', 'quicksilver_hit_success_01', 'quicksilver_hit_success_02',
         'quicksilver_hit_fail_01', 'vo_glitch_fear_01', 'vo_glitch_fun_01', 'vo_glitch_damage_01', 'vo_glitch_jump_01',
         'slime_glitch_split_01', 'vo_quicksilver_happy_01']
MAXSEC = {'player_radiation_sickness': 3.0}
total = 0
for c in CLIPS:
    src = os.path.join(EX, 'audio', c + '.wav')
    if not os.path.exists(src):
        print('missing', c); continue
    w = wave.open(src, 'rb')
    ch, sw, rate, n = w.getnchannels(), w.getsampwidth(), w.getframerate(), w.getnframes()
    data = array.array('h', w.readframes(n)); w.close()
    if ch == 2:
        data = array.array('h', [(data[i] + data[i + 1]) // 2 for i in range(0, len(data) - 1, 2)])
    if rate == 44100:
        data = array.array('h', [(data[i] + data[i + 1]) // 2 for i in range(0, len(data) - 1, 2)]); rate = 22050
    if c in MAXSEC:
        data = data[:int(MAXSEC[c] * rate)]
    dst = os.path.join(OUT, c + '.wav')
    o = wave.open(dst, 'wb'); o.setnchannels(1); o.setsampwidth(2); o.setframerate(rate); o.writeframes(data.tobytes()); o.close()
    total += os.path.getsize(dst)
print('audio bytes', total, 'files', len(os.listdir(OUT)))
