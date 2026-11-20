import sys,os,glob
from PIL import Image
src,dst=sys.argv[1],sys.argv[2]
for folder,short in [('icelandic_cracked_rock_tecmcizb_2k','Crack'),('coal_ash_rmqltsp0_2k','CoalAsh'),('coal_debris_pjbdz0_2k','Coal')]:
    def L(m,mode):
        f=[x for x in glob.glob(os.path.join(src,folder,'*.jpg')) if x.lower().endswith('_'+m.lower()+'.jpg')][0]
        return Image.open(f).convert(mode)
    a=L('BaseColor','RGB'); h=L('Displacement','L').resize(a.size)
    r,g,b=a.split(); Image.merge('RGBA',(r,g,b,h)).save(os.path.join(dst,f'T_UWG_{short}_AH.png'))
    nr,ng,_=L('Normal','RGB').split()
    Image.merge('RGBA',(nr,ng,L('Roughness','L').resize(a.size),L('AO','L').resize(a.size))).save(os.path.join(dst,f'T_UWG_{short}_NRA.png'))
    sm=a.resize((128,128)); hm=h.resize((128,128)); px=list(zip(sm.getdata(),hm.getdata()))
    ls=sorted(.2126*(r/255)**2.2+.7152*(g/255)**2.2+.0722*(b/255)**2.2 for (r,g,b),_ in px); hs=sorted(v/255 for _,v in px); n=len(px)
    print(short,'luma',round(sum(ls)/n,3),'h5',round(hs[n//20],2),'h95',round(hs[n*19//20],2))
