# Rank candidate template heads by 2-D landmark shape similarity to the character's front render.
# usage: python rank_templates.py char_front_lm.json out_grid.jpg tagA=lmA.json:frontA.png tagB=... (front pngs optional)
import json, sys, numpy as np
from PIL import Image, ImageDraw
OVAL = [10, 338, 297, 332, 284, 251, 389, 356, 454, 323, 361, 288, 397, 365, 379, 378, 400, 377, 152, 148, 176, 149,
        150, 136, 172, 58, 132, 93, 234, 127, 162, 21, 54, 103, 67, 109]
def load(p): return np.array(json.load(open(p)))[:468]
def procrustes_rms(A, B):
    """RMS residual after the best 2-D similarity A -> B."""
    a, b = A - A.mean(0), B - B.mean(0)
    U, S, Vt = np.linalg.svd(b.T @ a); R = U @ Vt; s = S.sum() / (a ** 2).sum()
    return float(np.sqrt((np.linalg.norm(s * a @ R.T - b, axis=1) ** 2).mean()))
def ratios(L):
    w = np.linalg.norm(L[234] - L[454])
    return {'w/h': round(w / np.linalg.norm(L[10] - L[152]), 3), 'jaw/w': round(np.linalg.norm(L[172] - L[397]) / w, 3),
            'eyes/w': round(np.linalg.norm(L[33] - L[263]) / w, 3)}
D = load(sys.argv[1]); io = np.linalg.norm(D[33] - D[263]); grid = sys.argv[2]
print('RANK character', ratios(D))
rows = []
for spec in sys.argv[3:]:
    tag, _, paths = spec.partition('='); lm, _, png = paths.partition(':'); L = load(lm)
    full, oval = procrustes_rms(L, D) / io, procrustes_rms(L[OVAL], D[OVAL]) / io
    rows.append({'tag': tag, 'score': round(full + oval, 4), 'all': round(full, 4), 'oval': round(oval, 4), **ratios(L), 'png': png})
rows.sort(key=lambda r: r['score'])
for r in rows: print('RANK', {k: v for k, v in r.items() if k != 'png'})
ims = [(r['tag'], Image.open(r['png']).convert('RGB')) for r in rows if r['png']]
if ims:
    C = 300; s = Image.new('RGB', (C * min(len(ims), 7), C * ((len(ims) + 6) // 7)), 'white')
    for k, (t, im) in enumerate(ims):
        w, h = im.size; im = im.crop((w // 4, h // 7, 3 * w // 4, 5 * h // 7)); im.thumbnail((C, C))
        s.paste(im, ((k % 7) * C, (k // 7) * C)); ImageDraw.Draw(s).text(((k % 7) * C + 5, (k // 7) * C + 5), f'{k + 1}. {t}', fill='red')
    s.save(grid)
