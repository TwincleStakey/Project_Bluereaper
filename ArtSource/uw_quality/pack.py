import sys,os
from PIL import Image
src,dst=sys.argv[1],sys.argv[2]
os.makedirs(dst,exist_ok=True)
for id,short in [('monastery_stone_floor','Flag'),('burned_ground_01','Ash'),('rocky_trail','Rubble'),('brown_mud_rocks_01','Mud'),('dark_rock_02','Cliff')]:
    L=lambda m,mode: Image.open(os.path.join(src,f'{id}_{m}_2k.jpg')).convert(mode)
    a=L('diff','RGB'); h=L('disp','L')
    r,g,b=a.split(); Image.merge('RGBA',(r,g,b,h)).save(os.path.join(dst,f'T_UWG_{short}_AH.png'))
    n=L('nor_gl','RGB'); nr,ng,_=n.split(); ro=L('rough','L'); ao=L('ao','L')
    Image.merge('RGBA',(nr,ng,ro,ao)).save(os.path.join(dst,f'T_UWG_{short}_NRA.png'))
    print(id,'ok')
