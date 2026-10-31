import subprocess, time, json, urllib.request, os, sys
# relaunch.py [--nobuild]  : kill game, build+deploy, start, load test save, wait for gameplay
B='http://127.0.0.1:9898'
def ping():
    try: return json.loads(urllib.request.urlopen(B+'/ping',timeout=4).read().decode('utf8'))
    except Exception: return None
def click(x,y):
    try: urllib.request.urlopen(B+'/ui/click?x=%d&y=%d'%(x,y),timeout=6).read()
    except Exception as e: print('click err',e)
subprocess.run('taskkill /im SlimeRancher2.exe /f',shell=True,capture_output=True)
time.sleep(4)
if '--nobuild' not in sys.argv:
    r=subprocess.run(r'cd /d D:\SR2ModDev\ClassicSlimes\src & call ..\tools\env.cmd >nul & dotnet build -c Release -p:Deploy=true -v q -nologo',shell=True,capture_output=True,text=True)
    out=r.stdout+r.stderr
    errs=[l for l in out.splitlines() if ' error ' in l]
    print('build', 'OK' if r.returncode==0 else 'FAIL', len(errs)); [print(e[:300]) for e in errs[:10]]
    if r.returncode!=0: sys.exit(1)
subprocess.run('start "" steam://rungameid/1657630',shell=True)
t0=time.time()
while time.time()-t0<200:
    time.sleep(3); p=ping()
    if p and p.get('scene')=='MainMenuEnvironment' and not p.get('loading'): break
else:
    print('menu timeout', p and p.get('scene')); sys.exit(2)
print('menu at %.0fs'%(time.time()-t0)); time.sleep(6)
click(1620,754); time.sleep(3); click(636,928); time.sleep(2); click(960,1024)
t1=time.time()
while time.time()-t1<150:
    time.sleep(3); p=ping()
    if p and p.get('ready') and p.get('scene')!='MainMenuEnvironment' and not p.get('loading'): break
else:
    print('load timeout', p and p.get('scene')); sys.exit(3)
print('loaded at %.0fs'%(time.time()-t0))
