import sys,os
from PIL import Image
lava,fire,zomb,out=sys.argv[1:5]
def find(root,suffix):
    for d in os.listdir(root):
        p=os.path.join(root,d,'pathname')
        if os.path.exists(p) and open(p,encoding='utf8').readline().strip().endswith(suffix): return os.path.join(root,d,'asset')
for m,mode in [('basecolor','RGB'),('emissive','RGB'),('normal','RGB'),('height','L'),('roughness','L')]:
    im=Image.open(find(lava,'_'+m+'.tga')).convert(mode).resize((2048,2048),Image.LANCZOS)
    im.save(os.path.join(out,f'T_Lava04_{m}.png')); print(m)
fl=Image.open(find(fire,'T_FB_Flame_16x5_04.png')).convert('L').resize((4096,2560),Image.LANCZOS)
fl.save(os.path.join(out,'T_FireFlipbook_16x5.png')); print('flame')
Image.open(find(fire,'T_SmokeLayer1.png')).save(os.path.join(out,'T_FireSmoke.png'))
Image.open(find(fire,'T_Spark.png')).save(os.path.join(out,'T_FireSpark.png'))
Image.open(os.path.join(zomb,'textures','z1.png')).convert('RGB').resize((1024,1024),Image.LANCZOS).save(os.path.join(out,'T_CorpseBurn.png')); print('corpse')
