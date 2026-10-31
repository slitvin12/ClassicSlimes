# Launches the game N times up to the end of largo generation and counts coreclr crashes.
# usage: python -u crashloop.py N
import subprocess, time, os, sys
G = r'D:\SteamLibrary\steamapps\common\Slime Rancher 2'
LOGS = os.path.join(G, 'MelonLoader', 'Logs')
N = int(sys.argv[1]) if len(sys.argv) > 1 else 10

def running():
    out = subprocess.run('tasklist /fi "imagename eq SlimeRancher2.exe"', shell=True, capture_output=True).stdout
    return b'SlimeRancher2.exe' in out

def kill():
    subprocess.run('taskkill /f /im SlimeRancher2.exe', shell=True, capture_output=True)

res = []
for i in range(N):
    kill()
    while running(): time.sleep(1)
    time.sleep(4)
    before = set(os.listdir(LOGS))
    t0 = time.time()
    subprocess.run('start "" steam://rungameid/1657630', shell=True)
    log, outcome, last = None, 'timeout', ''
    while time.time() - t0 < 300:
        time.sleep(2)
        if log is None:
            new = [f for f in os.listdir(LOGS) if f not in before]
            if new: log = os.path.join(LOGS, sorted(new)[-1])
            continue
        try: txt = open(log, encoding='utf-8', errors='replace').read()
        except Exception: continue
        if 'Largo generation finished' in txt: outcome = 'ok'; break
        if time.time() - t0 > 40 and not running():
            outcome = 'crash'
            last = [l for l in txt.splitlines() if l.strip()][-1][:160] if txt.strip() else ''
            break
    if outcome == 'ok':
        time.sleep(15)
        if not running(): outcome = 'crash-late'
    res.append(outcome)
    print(i + 1, outcome, int(time.time() - t0), 's', last, flush=True)
kill()
print('RESULT ok=%d crash=%d of %d %s' % (res.count('ok'), sum(r.startswith('crash') for r in res), N, res), flush=True)
