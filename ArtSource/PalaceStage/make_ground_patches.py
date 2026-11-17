# 11-17 대전 앞 D4 staging: soft ground patches (mud / leaves / ash / dust) laid over the stone scan floor.
# Albedo + normal are the downloaded scans (Fab Megascans moist mud / fallen leaves / dirt road from 폐촌, Poly Haven
# burned_ground from the palace floor itself), tiled inside the patch; the alpha is a soft, warped blotch whose inside
# also follows the scan's own dark (low) parts, so the patch settles into hollows instead of reading as a stamped disc.
# Output: Assets/Game/RoomRun/ThirdParty/PalaceStage/Ground/PS_G_<kind>_<v>_A.png (RGBA) and _N.png (normal, GL).
import os, sys
import numpy as np
from PIL import Image

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), '..', '..'))
MS = os.path.join(ROOT, 'Assets/Game/AbandonedVillage/ThirdParty/Megascans')
LS = os.path.join(ROOT, 'Assets/Art/LookStudy/Stages/Textures')
OUT = os.path.join(ROOT, 'Assets/Game/RoomRun/ThirdParty/PalaceStage/Ground')
N = 1024

KINDS = {
    #        albedo, normal, tiles across the patch, grade (rgb mul), desat, mask threshold, hollow follow
    'Mud':   (MS + '/mud/AV_MS_Mud_A.jpg', MS + '/mud/AV_MS_Mud_N.jpg', 3, (0.62, 0.56, 0.5), 0.35, 0.42, 0.55),
    'Leaves': (MS + '/Ground/AV_FG_Leaves_A.jpg', MS + '/Ground/AV_FG_Leaves_N.jpg', 3, (0.8, 0.72, 0.62), 0.35, 0.45, 0.35),
    'Ash':   (LS + '/burned_ground_01_diff.jpg', LS + '/burned_ground_01_nor_gl.jpg', 2, (0.5, 0.48, 0.46), 0.7, 0.4, 0.45),
    'Dust':  (MS + '/Ground/AV_FG_DirtRoad_A.jpg', MS + '/Ground/AV_FG_DirtRoad_N.jpg', 3, (0.95, 0.88, 0.78), 0.45, 0.5, 0.25),
}


def grad_noise(n, cells, rng):
    """tileless gradient (Perlin-style) noise, n x n, `cells` lattice cells across"""
    g = rng.normal(size=(cells + 2, cells + 2, 2))
    g /= np.linalg.norm(g, axis=2, keepdims=True)
    t = np.linspace(0, cells, n, endpoint=False)
    x, y = np.meshgrid(t, t)
    xi, yi = x.astype(int), y.astype(int)
    xf, yf = x - xi, y - yi

    def dot(ix, iy, dx, dy):
        v = g[iy, ix]
        return v[..., 0] * dx + v[..., 1] * dy
    fade = lambda u: u * u * u * (u * (u * 6 - 15) + 10)
    u, v = fade(xf), fade(yf)
    n00 = dot(xi, yi, xf, yf); n10 = dot(xi + 1, yi, xf - 1, yf)
    n01 = dot(xi, yi + 1, xf, yf - 1); n11 = dot(xi + 1, yi + 1, xf - 1, yf - 1)
    return (n00 * (1 - u) + n10 * u) * (1 - v) + (n01 * (1 - u) + n11 * u) * v


def fbm(n, rng, base=3, oct=5):
    s, a, tot = np.zeros((n, n)), 1.0, 0.0
    for o in range(oct):
        s += a * grad_noise(n, base * 2 ** o, rng); tot += a; a *= 0.5
    return s / tot


def smooth(e0, e1, x):
    t = np.clip((x - e0) / (e1 - e0), 0, 1)
    return t * t * (3 - 2 * t)


def tile(img, reps):
    a = np.asarray(img.convert('RGB'), dtype=np.float32) / 255.0
    side = int(np.ceil(N / reps))
    a = np.asarray(Image.fromarray((a * 255).astype(np.uint8)).resize((side, side), Image.LANCZOS), dtype=np.float32) / 255.0
    return np.tile(a, (reps + 1, reps + 1, 1))[:N, :N]


def main():
    os.makedirs(OUT, exist_ok=True)
    yy, xx = np.mgrid[0:N, 0:N] / (N - 1) * 2 - 1
    for kind, (alb, nrm, reps, grade, desat, thr, follow) in KINDS.items():
        peak = {'Dust': 0.55, 'Ash': 0.75, 'Mud': 0.8}.get(kind, 0.85)
        A = tile(Image.open(alb), reps)
        Nm = tile(Image.open(nrm), reps)
        lum = A.mean(axis=2)
        A = A * (1 - desat) + lum[..., None] * desat
        A = np.clip(A * np.array(grade), 0, 1)
        lo = (lum - lum.mean()) / (lum.std() + 1e-5)          # scan's own dark = low parts
        for v in range(3):
            rng = np.random.default_rng(1117 + v * 31 + len(kind) * 7)
            wx, wy = fbm(N, rng, 2, 3), fbm(N, rng, 2, 3)
            r = np.sqrt((xx + wx * 0.55) ** 2 + ((yy + wy * 0.55) * (1.0 + 0.35 * v)) ** 2)   # warped, a bit elongated per variant
            body = 1 - smooth(0.45, 0.98, r)                                                  # soft falloff to the quad edge
            n = fbm(N, rng, 3, 6)
            fine = fbm(N, rng, 24, 3)                                                          # holes: stones peek through
            m = body * 0.85 + n * 1.5 + fine * 0.45 - lo * 0.18 * follow
            a = smooth(thr - 0.45, thr + 0.4, m) ** 1.3 * peak
            a *= smooth(0.0, 0.08, 1 - np.maximum(abs(xx), abs(yy)))                         # never reaches the quad border
            rgba = np.dstack([A, a]).clip(0, 1)
            Image.fromarray((rgba * 255).astype(np.uint8), 'RGBA').save(f'{OUT}/PS_G_{kind}_{v}_A.png', optimize=True)
            print(kind, v, 'cover %.2f' % (a > 0.5).mean())
        Image.fromarray((Nm * 255).astype(np.uint8)).save(f'{OUT}/PS_G_{kind}_N.png', optimize=True)


if __name__ == '__main__':
    main()
