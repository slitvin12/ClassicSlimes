# Mosaic gradient masks (image top = slime top on the SR2 body UVs).
# Smooth blue (R) / emerald (G) / amber (B) height weights: 85% continuous gradient, 15% tile-centre variation.
# Broad overlapping transitions and softly tinted grout avoid hard blue/green/orange bands.
# Writes src\Assets\msk_mosaicGrad.png; "python mkmosaic.py variants" also writes test variants to UserData\ClassicSlimes\mg_<v>.png
import numpy as np, random, sys
from PIL import Image
from collections import deque
A = r"D:\SR2ModDev\ClassicSlimes\src\Assets"
UD = r"D:\SteamLibrary\steamapps\common\Slime Rancher 2\UserData\ClassicSlimes"
src = np.asarray(Image.open(A + r"\msk_mosaic.png").convert("RGB")).astype(np.float32) / 255
h, w = src.shape[:2]
val = src.max(axis=2)
tile = val > 0.5
lab = -np.ones((h, w), np.int32); n = 0
for y in range(h):
    for x in range(w):
        if tile[y, x] and lab[y, x] < 0:
            q = deque([(y, x)]); lab[y, x] = n
            while q:
                cy, cx = q.popleft()
                for dy, dx in ((1, 0), (-1, 0), (0, 1), (0, -1)):
                    ny, nx = cy + dy, (cx + dx) % w
                    if 0 <= ny < h and tile[ny, nx] and lab[ny, nx] < 0:
                        lab[ny, nx] = n; q.append((ny, nx))
            n += 1
full = lab.copy()
for it in range(5):
    for dy, dx in ((1, 0), (-1, 0), (0, 1), (0, -1)):
        sh = np.roll(full, (dy, dx), (0, 1)); f = (full < 0) & (sh >= 0); full[f] = sh[f]
yy = np.repeat(np.arange(h)[:, None], w, 1).astype(np.float64)
ys = np.zeros(n); cnt = np.zeros(n)
np.add.at(ys, lab[tile], yy[tile]); np.add.at(cnt, lab[tile], 1)
tv = 1 - ys / np.maximum(cnt, 1) / h            # tile centre v (1 = top)
def ss(a, b, x): t = np.clip((x - a) / (b - a), 0, 1); return t * t * (3 - 2 * t)
def W(v, b0, b1, o0, o1):
    bl = ss(b0, b1, v); orr = 1 - ss(o0, o1, v); gr = np.clip(1 - bl - orr, 0, 1)
    return np.stack([bl, gr, orr], -1)
rnd = random.Random(5); jit = np.array([rnd.uniform(-1, 1) for _ in range(n)])
pv = 1 - yy / h
def make(name, b0=0.50, b1=0.64, o0=0.18, o1=0.36, j=0.05, grout=0.0, smooth=False, gain=1.0, flow=0.0):
    if smooth:
        out = W(pv, b0, b1, o0, o1) * (0.7 + 0.3 * val[..., None])
    else:
        vt = np.where(full >= 0, tv[np.maximum(full, 0)] + jit[np.maximum(full, 0)] * j, pv)
        wt = W(vt, b0, b1, o0, o1) * (1 - flow) + W(pv, b0, b1, o0, o1) * flow
        k = np.where(full >= 0, np.clip(val * gain, 0, 1), 0)[..., None]
        out = wt * k + W(pv, b0, b1, o0, o1) * grout * (1 - k)
    img = np.concatenate([out, np.ones((h, w, 1))], -1)
    Image.fromarray((np.clip(img, 0, 1) * 255).astype(np.uint8), "RGBA").save(name if name.endswith(".png") else UD + "\\mg_" + name + ".png")
    return out
vs = {"final": make(A + r"\msk_mosaicGrad.png", b0=0.42, b1=0.88, o0=0.08, o1=0.52, j=0.012, grout=0.45, flow=0.85)}
if "v2" in sys.argv:
    vs.update({"e": make("e", o0=0.16, o1=0.38, grout=0.55, gain=2.0), "f": make("f", o0=0.16, o1=0.38, grout=0.8, gain=2.0)})
if "variants" in sys.argv:
    vs.update({"a": make("a"), "b": make("b", grout=0.45), "c": make("c", smooth=True), "d": make("d", b0=0.56, b1=0.68, o0=0.12, o1=0.30, grout=0.3)})
pal = np.array([[0.08, 0.38, 0.98], [0.06, 0.78, 0.38], [1, 0.40, 0.035]]); base = np.array([0.08, 0.20, 0.48])
prev = Image.new("RGB", (256 * len(vs), 256))
for i, (k, g) in enumerate(vs.items()):
    vis = g @ pal + (1 - g.sum(2, keepdims=True)).clip(0, 1) * base
    prev.paste(Image.fromarray((vis.clip(0, 1) * 255).astype(np.uint8)).resize((256, 256)), (256 * i, 0))
prev.save(r"D:\SR2ModDev\ClassicSlimes\tools\sheet.jpg", quality=88)
print("ok", n)
import os
for f in ("msk_mosaicGradF.png", "msk_bands.png"):
    if os.path.exists(A + "\\" + f): os.remove(A + "\\" + f)
