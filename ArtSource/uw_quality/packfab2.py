import sys,os,glob
from PIL import Image
src,dst=sys.argv[1],sys.argv[2]
for folder,short in [('historic_medieval_churchruin_floor_stone_03_xjvkbhqs_2k','Slab'),('dark_soil_scmlbap0_2k','Soil'),('rocky_ground_vl0hccalw_2k','Rocky')]:
    def L(m,mode):
        f=[x for x in glob.glob(os.path.join(src,folder,'*.jpg')) if x.lower().endswith('_'+m.lower()+'.jpg')][0]
        return Image.open(f).convert(mode)
    a=L('BaseColor','RGB'); h=L('Displacement','L').resize(a.size)
    r,g,b=a.split(); Image.merge('RGBA',(r,g,b,h)).save(os.path.join(dst,f'T_UWG_{short}_AH.png'))
    nr,ng,_=L('Normal','RGB').split()
    Image.merge('RGBA',(nr,ng,L('Roughness','L').resize(a.size),L('AO','L').resize(a.size))).save(os.path.join(dst,f'T_UWG_{short}_NRA.png'))
    print(short,a.size)
