# Builds glow/engrave masks for the BossSeal floor from downloaded images (no drawing: every stroke comes from the photos/SVG renders).
import sys, os
from PIL import Image, ImageFilter, ImageOps, ImageChops
src, out = sys.argv[1], sys.argv[2]
os.makedirs(out, exist_ok=True)
def L(p): return Image.open(os.path.join(src, p))

# 1) centre seal: CC0 bagua octagon (trigrams + frame) with its yin-yang replaced by the Korean taegeuk (PD flag)
bg = L('bagua_octagon_1920.png').convert('LA')
W = bg.size[0]
alpha = bg.getchannel('A'); lum = bg.getchannel('L')
lines = ImageChops.multiply(alpha, ImageOps.invert(lum))          # black strokes -> 1
# wipe the yin-yang (circle centred, r ~ 0.365 W incl. its ring)
mask_c = Image.new('L', (W, W), 0)
from PIL import ImageDraw
d = ImageDraw.Draw(mask_c); r = int(W * 0.19); d.ellipse((W//2-r, W//2-r, W//2+r, W//2+r), fill=255)
lines = ImageChops.subtract(lines, mask_c)
flag = L('flag_kr_1920.png').convert('RGB')
fr = 320; cx, cy = 960, 640
tg = flag.crop((cx-fr, cy-fr, cx+fr, cy+fr))
rr, gg, bb = tg.split()
red = ImageChops.subtract(rr, bb); red = red.point(lambda v: 255 if v > 80 else 0)
blue = ImageChops.subtract(bb, rr); blue = blue.point(lambda v: 255 if v > 80 else 0)
tr = int(W * 0.175)   # taegeuk radius in the seal
red = red.resize((2*tr, 2*tr), Image.LANCZOS); blue = blue.resize((2*tr, 2*tr), Image.LANCZOS)
R = lines; G = Image.new('L', (W, W), 0); B = Image.new('L', (W, W), 0)
G.paste(red, (W//2-tr, W//2-tr)); B.paste(blue, (W//2-tr, W//2-tr))
A = Image.new('L', (W, W), 0); ImageDraw.Draw(A).ellipse((W//2-tr, W//2-tr, W//2+tr, W//2+tr), fill=255)
Image.merge('RGB', (R, G, B)).resize((2048, 2048), Image.LANCZOS).save(os.path.join(out, 'BS_Seal_TaegeukPalgwae.png'))

# 2) bujeok: the red cinnabar strokes of the Joseon 길상호신부 (photo, CC BY 4.0), paper cropped
ph = L('gilsang_woodblock_1920.jpg').convert('RGB')
paper = ph.crop((985, 520, 1530, 1270))
r_, g_, b_ = paper.split()
ink = ImageChops.subtract(r_, ImageChops.lighter(g_, b_))
ink = ink.point(lambda v: max(0, min(255, (v - 22) * 5)))
ink = ink.filter(ImageFilter.GaussianBlur(1.2))
ink = ImageOps.expand(ink, border=30, fill=0)
ink.resize((512, 704), Image.LANCZOS).save(os.path.join(out, 'BS_Bujeok_Gilsang.png'))

# 3) carved text: the 부적목판 woodblock in the same photo (南無阿彌陀佛 ...), carved grooves = dark vs local mean
wb = ph.crop((135, 430, 770, 1390)).convert('L')
blur = wb.filter(ImageFilter.GaussianBlur(14))
groove = ImageChops.subtract(blur, wb).point(lambda v: max(0, min(255, (v - 6) * 9)))
groove.resize((512, 736), Image.LANCZOS).save(os.path.join(out, 'BS_Woodblock_Text.png'))

# 4) 삼두매 부적 (Sin Jae-hyeon, Brooklyn Museum, PD): ink lines
hk = L('brooklyn_amulet.jpg').convert('L')
hb = hk.filter(ImageFilter.GaussianBlur(10))
hl = ImageChops.subtract(hb, hk).point(lambda v: max(0, min(255, (v - 8) * 6)))
hl.crop((12, 12, 556, 756)).resize((512, 704), Image.LANCZOS).save(os.path.join(out, 'BS_Samdumae.png'))
print('done')
