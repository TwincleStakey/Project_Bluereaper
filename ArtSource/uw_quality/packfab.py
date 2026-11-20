import sys,os,glob
from PIL import Image
src,dst=sys.argv[1],sys.argv[2]
for folder,short in [('flagstone_floor_vhogcgc_2k','Flag'),('burnt_forest_floor_uhwpehcdy_2k','Ash'),('gravel_on_soil_rmrk3sp0_2k','Rubble')]:
    def L(m,mode):
        f=glob.glob(os.path.join(src,folder,f'*_2K_{m}.jpg'))[0]; return Image.open(f).convert(mode)
    a=L('BaseColor','RGB'); h=L('Displacement','L')
    r,g,b=a.split(); Image.merge('RGBA',(r,g,b,h)).save(os.path.join(dst,f'T_UWG_{short}_AH.png'))
    nr,ng,_=L('Normal','RGB').split()
    Image.merge('RGBA',(nr,ng,L('Roughness','L'),L('AO','L'))).save(os.path.join(dst,f'T_UWG_{short}_NRA.png'))
    print(short,'ok')
