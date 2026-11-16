# contact sheet: python sheet.py <dir> <prefix> <out.png> [cols]
import sys, glob
from PIL import Image
d, pre, out = sys.argv[1:4]; cols = int(sys.argv[4]) if len(sys.argv) > 4 else 4
fs = sorted(glob.glob(f'{d}/{pre}_*.png'))
ims = [Image.open(f) for f in fs]; w, h = ims[0].size
rows = (len(ims) + cols - 1) // cols
S = Image.new('RGB', (w * cols, h * rows))
for i, im in enumerate(ims): S.paste(im, ((i % cols) * w, (i // cols) * h))
S.thumbnail((2400, 2400)); S.save(out); print(out, len(ims))
