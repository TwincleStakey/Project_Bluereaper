# Emission map for the 거점 허수아비 (11-20 Kido: "허수아비도 뭔가 빛을줘"): talisman glyphs glow pale cyan, paper and the
# white mask glow faintly. Masks come from the base colour, filtered to large connected blobs so straw specks stay dark.
# python scarecrow_emission.py <base.jpg> <out_emission.png> [debug.png]
import sys
import numpy as np
from PIL import Image, ImageFilter
from scipy import ndimage

src, out = sys.argv[1], sys.argv[2]
a = np.asarray(Image.open(src).convert('RGB')).astype(np.float32) / 255
r, g, b = a[..., 0], a[..., 1], a[..., 2]
mx, mn = a.max(-1), a.min(-1); sat = (mx - mn) / (mx + 1e-4)

def blobs(m, min_px, grow=2):
    m = ndimage.binary_closing(m, iterations=2)
    lab, n = ndimage.label(m)
    sizes = ndimage.sum(m, lab, range(1, n + 1))
    keep = np.isin(lab, 1 + np.nonzero(sizes >= min_px)[0])
    return ndimage.binary_dilation(keep, iterations=grow) if grow else keep

H = a.shape[0]
paper = blobs((r > 0.5) & (g > 0.3) & (b < 0.3) & (sat > 0.55), int(0.00015 * H * H), grow=0)
paper = ndimage.binary_fill_holes(ndimage.binary_closing(paper, iterations=4))
mask = blobs((mx > 0.6) & (sat < 0.15), int(0.0004 * H * H), grow=1)
ink = ((r > 0.4) & (g < 0.36) & (sat > 0.6)) & paper
ink = ndimage.binary_dilation(ink, iterations=1)

e = np.zeros_like(a)
e[paper] += np.array([0.10, 0.16, 0.15])
e[ink] = np.array([0.45, 1.0, 0.95])
e[mask] += np.array([0.20, 0.24, 0.26]) * mx[mask][:, None]
img = Image.fromarray((np.clip(e, 0, 1) * 255).astype(np.uint8)).filter(ImageFilter.GaussianBlur(1.2))
img.save(out)
print("paper %.4f mask %.4f ink %.4f" % (paper.mean(), mask.mean(), ink.mean()))
if len(sys.argv) > 3:
    Image.fromarray((np.concatenate([a, np.asarray(img) / 255 * 2], 1).clip(0, 1) * 255).astype(np.uint8)).resize((1024, 512)).save(sys.argv[3])
