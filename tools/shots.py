import sys, time, urllib.request, urllib.parse
from PIL import Image
# shots.py tag Rad Quantum ... [--dist=3] [--wait=1.5] [--box=x0,y0,x1,y1]
B='http://127.0.0.1:9898'
SD=r'D:\SteamLibrary\steamapps\common\Slime Rancher 2\UserData\CreatorToolkitAgent\screenshots\%s.png'
tag=sys.argv[1]; names=[a for a in sys.argv[2:] if not a.startswith('--')]
opt=dict(a[2:].split('=',1) for a in sys.argv[2:] if a.startswith('--'))
dist=opt.get('dist','3'); wait=float(opt.get('wait','1.5'))
box=tuple(map(int,opt.get('box','560,330,1360,780').split(',')))
def get(p,data=None):
    r=urllib.request.urlopen(B+p,data=data,timeout=20); return r.read().decode('utf8','replace')
ims=[]
for n in names:
    if n.startswith('cmd:'):
        open(r'D:\SteamLibrary\steamapps\common\Slime Rancher 2\UserData\ClassicSlimes\cmd.txt','w').write(n[4:].replace('_',' '))
        time.sleep(wait)
    elif n!='none':
        q=urllib.parse.urlencode({'item':'SlimeDefinition.'+n if '.' not in n else n,'count':1,'distance':dist})
        s=get('/spawn?'+q,data=b'')
        if '"ok":true' not in s: print('spawn fail',n,s[:200])
        time.sleep(wait)
    nm='%s_%d'%(tag,len(ims))
    get('/screenshot?name='+nm); time.sleep(0.3)
    im=Image.open(SD%nm).convert('RGB').crop(box)
    ims.append(im)
    if 'killafter' in opt and not n.startswith('cmd:') and n!='none':
        open(r'D:\SteamLibrary\steamapps\common\Slime Rancher 2\UserData\ClassicSlimes\cmd.txt','w').write('kill'); time.sleep(1.2)
w=box[2]-box[0]; h=box[3]-box[1]
cols=2 if len(ims)>1 else 1; rows=(len(ims)+cols-1)//cols
sheet=Image.new('RGB',(w*cols,h*rows))
for i,im in enumerate(ims): sheet.paste(im,((i%cols)*w,(i//cols)*h))
sc=min(1.0,1400/sheet.width)
sheet=sheet.resize((int(sheet.width*sc),int(sheet.height*sc)),Image.LANCZOS)
sheet.save(r'D:\SR2ModDev\ClassicSlimes\tools\sheet.jpg',quality=80)
print('ok',sheet.size)
