import numpy as np, shutil
from PIL import Image
T = r'D:\SR2ModDev\ClassicSlimes\extract\tex\\'
A = r'D:\SR2ModDev\ClassicSlimes\src\Assets\\'
shutil.copy(T + 'msk_mosaic.png', A + 'msk_mosaic.png')
shutil.copy(T + 'silver_sphere.png', A + 'silver_sphere.png')
n = np.asarray(Image.open(T + 'nrm_mosaic.png').convert('RGBA')).astype(np.float32) / 255.0
x = n[..., 3] * 2 - 1; y = n[..., 1] * 2 - 1
z = np.sqrt(np.clip(1 - x * x - y * y, 0, 1))
out = np.stack([(x + 1) / 2, (y + 1) / 2, (z + 1) / 2, np.ones_like(x)], -1)
Image.fromarray((out * 255 + 0.5).astype(np.uint8), 'RGBA').save(A + 'nrm_mosaic.png')
print('ok', out.shape, 'x mean', x.mean().round(3), 'y mean', y.mean().round(3), 'R channel mean before', n[..., 0].mean().round(3))
