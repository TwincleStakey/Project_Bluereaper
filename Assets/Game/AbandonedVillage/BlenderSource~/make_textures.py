# Generates small helper textures: soft fog puff, wisp glow and the dry-grass vertical gradient.
# blender -b --factory-startup -P make_textures.py -- <out dir>
import bpy, sys, os, math
import numpy as np
from mathutils import noise, Vector

OUT = sys.argv[sys.argv.index("--") + 1:][0]
os.makedirs(OUT, exist_ok=True)


def save(name, arr):
    h, w, _ = arr.shape
    img = bpy.data.images.new(name, w, h, alpha=True)
    img.pixels.foreach_set(arr.astype(np.float32).ravel())
    img.filepath_raw = os.path.join(OUT, name + ".png"); img.file_format = "PNG"; img.save()
    print("saved", name)


# fog puff: fbm noise inside a soft round falloff
N = 256
fog = np.zeros((N, N, 4))
for j in range(N):
    for i in range(N):
        x, y = i / N * 2 - 1, j / N * 2 - 1
        r = math.sqrt(x * x + y * y)
        fall = max(0.0, 1 - r) ** 1.6
        n = 0.0; a = 1.0; f = 2.0
        for o in range(5):
            n += a * noise.noise(Vector((x * f + 3.1, y * f + 7.7, 0.5))); a *= 0.5; f *= 2.1
        v = max(0.0, min(1.0, (0.55 + n * 0.75))) * fall
        fog[j, i] = (1, 1, 1, v)
save("AV_FogPuff", fog)

# wisp / glow dot
N = 64
g = np.zeros((N, N, 4))
for j in range(N):
    for i in range(N):
        x, y = i / N * 2 - 1, j / N * 2 - 1
        r = math.sqrt(x * x + y * y)
        v = max(0.0, 1 - r) ** 2.2
        g[j, i] = (1, 1, 1, v)
save("AV_Glow", g)

# dry grass gradient (v = 0 root .. 1 tip), a little horizontal streaking
W, H = 32, 128
gr = np.zeros((H, W, 4))
root = np.array((0.10, 0.08, 0.05)); mid = np.array((0.36, 0.30, 0.19)); tip = np.array((0.62, 0.55, 0.40))
for j in range(H):
    t = j / (H - 1)
    c = root + (mid - root) * min(1, t / 0.45) if t < 0.45 else mid + (tip - mid) * ((t - 0.45) / 0.55)
    for i in range(W):
        s = 1 + 0.12 * noise.noise(Vector((i * 0.35, j * 0.05, 1.3)))
        gr[j, i] = (*(c * s), 1)
save("AV_DryGrassGrad", gr)
