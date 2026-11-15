import sys,os,glob
from PIL import Image
src,dst,short=sys.argv[1],sys.argv[2],sys.argv[3]
def L(m,mode):
    f=[x for x in glob.glob(os.path.join(src,'*.jpg')) if x.lower().endswith('_'+m.lower()+'.jpg')][0]
    return Image.open(f).convert(mode)
a=L('BaseColor','RGB'); h=L('Displacement','L').resize(a.size)
r,g,b=a.split(); Image.merge('RGBA',(r,g,b,h)).save(os.path.join(dst,f'T_UWG_{short}_AH.png'))
nr,ng,_=L('Normal','RGB').split()
Image.merge('RGBA',(nr,ng,L('Roughness','L').resize(a.size),L('AO','L').resize(a.size))).save(os.path.join(dst,f'T_UWG_{short}_NRA.png'))
import statistics
print(short,a.size, 'luma', sum(a.convert('L').resize((64,64)).getdata())/4096/255)
