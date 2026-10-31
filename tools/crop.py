import sys
from PIL import Image
# crop.py name x0 y0 x1 y1 [outw]
n=sys.argv[1]; x0,y0,x1,y1=map(int,sys.argv[2:6]); w=int(sys.argv[6]) if len(sys.argv)>6 else 640
p=r'D:\SteamLibrary\steamapps\common\Slime Rancher 2\UserData\CreatorToolkitAgent\screenshots\%s.png'%n
im=Image.open(p).convert('RGB').crop((x0,y0,x1,y1))
h=int(im.height*w/im.width)
im=im.resize((w,h),Image.LANCZOS)
im.save(r'D:\SR2ModDev\ClassicSlimes\tools\crop.jpg',quality=70)
print('ok',w,h)
