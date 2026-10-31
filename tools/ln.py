import sys
# usage: ln.py file start end   (1-based inclusive)
f, a, b = sys.argv[1], int(sys.argv[2]), int(sys.argv[3])
for i, line in enumerate(open(f, encoding='utf-8', errors='replace'), 1):
    if i < a: continue
    if i > b: break
    print(f"{i}: {line.rstrip()[:240]}")
