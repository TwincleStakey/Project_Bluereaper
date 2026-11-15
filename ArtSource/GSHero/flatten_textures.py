# GS_Hero visibility pass (11-15): flatten the noisy Tripo basecolor detail into big colour blocks.
# Originals are untouched; writes <name>_flat.png next to them (Assets/Game/GSHero/Art).
# Per texture: k-means colour classes -> blur inside each class only (class borders stay crisp, islands of one
# material merge into one block) -> pull a part of the way to the class mean (posterize) -> keep a little of the
# original detail. Needs only numpy + ffmpeg (run with Blender's bundled python):
#   "E:/SteamLibrary/steamapps/common/Blender/5.3/python/bin/python.exe" -I flatten_textures.py
import subprocess, sys, os
import numpy as np

ART = r"F:/UnityProject/BulletHeavenArt/Assets/Game/GSHero/Art"
FF = "ffmpeg"
OUT = sys.argv[1] if len(sys.argv) > 1 else ART

def load(path):
    probe = subprocess.run(["ffprobe", "-v", "error", "-select_streams", "v:0", "-show_entries", "stream=width,height",
                            "-of", "csv=p=0", path], capture_output=True, text=True, check=True).stdout.strip().split(",")
    w, h = int(probe[0]), int(probe[1])
    raw = subprocess.run([FF, "-v", "error", "-i", path, "-f", "rawvideo", "-pix_fmt", "rgb24", "-"], capture_output=True, check=True).stdout
    return np.frombuffer(raw, np.uint8).reshape(h, w, 3).astype(np.float32) / 255.0

def save(img, path):
    h, w, _ = img.shape
    data = (np.clip(img, 0, 1) * 255 + 0.5).astype(np.uint8).tobytes()
    subprocess.run([FF, "-v", "error", "-y", "-f", "rawvideo", "-pix_fmt", "rgb24", "-s", f"{w}x{h}", "-i", "-", path], input=data, check=True)

def box(a, r):
    # separable box blur via cumulative sums, edge-clamped, on HxW or HxWxC
    def one(x, axis):
        n = x.shape[axis]
        pad = [(0, 0)] * x.ndim; pad[axis] = (r + 1, r)
        c = np.cumsum(np.pad(x, pad, mode="edge"), axis=axis, dtype=np.float64)
        hi = np.take(c, np.arange(2 * r + 1, 2 * r + 1 + n), axis=axis)
        lo = np.take(c, np.arange(0, n), axis=axis)
        return ((hi - lo) / (2 * r + 1)).astype(np.float32)
    return one(one(a, 0), 1)

def feat(img):
    # roughly perceptual: lightness + two opponent channels (hue matters more than texture value)
    L = img @ np.array([0.2126, 0.7152, 0.0722], np.float32)
    L = np.sqrt(np.maximum(L, 0))
    rg = (img[..., 0] - img[..., 1]) * 1.6
    yb = ((img[..., 0] + img[..., 1]) * 0.5 - img[..., 2]) * 1.6
    return np.stack([L, rg, yb], -1)

def kmeans(x, k, iters=25, seed=3):
    rng = np.random.default_rng(seed)
    c = x[rng.choice(len(x), k, replace=False)]
    for _ in range(iters):
        d = ((x[:, None, :] - c[None]) ** 2).sum(-1)
        lab = d.argmin(1)
        for j in range(k):
            s = x[lab == j]
            if len(s): c[j] = s.mean(0)
    return c

def flatten(name, k, radius, toward_mean, keep_detail, extra=None, merge_dark=False):
    src = os.path.join(ART, name + ".png")
    img = load(src)
    h, w, _ = img.shape
    f = feat(img)
    sample = f[::8, ::8].reshape(-1, 3)
    cent = kmeans(sample, k)
    lab = np.zeros((h, w), np.int16)
    best = np.full((h, w), 1e9, np.float32)
    for j in range(k):
        d = ((f - cent[j]) ** 2).sum(-1)
        sel = d < best; best[sel] = d[sel]; lab[sel] = j
    if merge_dark:
        # all near-neutral dark classes become one block (the armour pattern survived as class borders)
        dark = [j for j in range(k) if np.hypot(cent[j][1], cent[j][2]) < 0.06 and cent[j][0] < 0.45]
        for j in dark[1:]: lab[lab == j] = dark[0]
        print("  merged dark classes", dark)
    out = np.zeros_like(img)
    for j in range(k):
        m = (lab == j).astype(np.float32)
        if m.sum() == 0: continue
        num = box(img * m[..., None], radius)
        den = box(m, radius)[..., None]
        blk = num / np.maximum(den, 1e-4)
        mean = img[lab == j].mean(0)
        blk = blk + (mean - blk) * toward_mean
        out = np.where((lab == j)[..., None], blk, out)
        print(f"  {name} class {j}: {int(m.sum())} px, mean {np.round(mean * 255).astype(int)}")
    out = out + (img - out) * keep_detail
    if extra: out = extra(out, img, lab, cent)
    dst = os.path.join(OUT, name + "_flat.png")
    save(out, dst)
    print("wrote", dst)

if __name__ == "__main__":
    # body: black armour / leather, burgundy, skin, cyan spots (emission lives in its own map)
    flatten("GS_Hero_basecolor", k=7, radius=24, toward_mean=0.5, keep_detail=0.12, merge_dark=True)
    # shirt: burgundy cloth + skin; seams and folds flattened
    flatten("GS_Shirt_basecolor", k=4, radius=28, toward_mean=0.5, keep_detail=0.1)
    # sword: steel plates + red wraps; the dried blood specks on the blade are merged into the steel blocks
    flatten("GS_Greatsword_basecolor_iron", k=5, radius=16, toward_mean=0.5, keep_detail=0.1, merge_dark=True)


