import json, sys, os, numpy as np, cv2
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__))); import texlib as tl
tex, ij, out = sys.argv[1:4]
P = json.loads(sys.argv[4]) if len(sys.argv) > 4 else {}
im = cv2.imread(tex).astype(np.float32); h, w = im.shape[:2]; I = json.load(open(ij))
gain = np.ones((h, w), np.float32)
for o in I:
    g = P.get('sclera_gain', 0.75) if o['part'] > 0 else (0.55 if o['n'] in (47, 36, 8) else 1.0)
    if g == 1.0: continue
    m = np.zeros((h, w), np.uint8)
    for t in o['uv']: cv2.fillPoly(m, [np.array([[u * w, (1 - v) * h] for u, v in t]).round().astype(np.int32)], 255)
    m = cv2.dilate(m, np.ones((9, 9), np.uint8)); gain[m > 0] = np.minimum(gain[m > 0], g)
tint = np.ones((h, w, 3), np.float32)
if 'sclera_tint' in P:  # BGR multipliers on the eyeball islands (Rocketbox sclera is a cool blue-white)
    tint[gain == P.get('sclera_gain', 0.75)] = P['sclera_tint']
res = np.clip(im * gain[..., None] * tint, 0, 255).astype(np.uint8)
if 'iris' in P:  # Rocketbox iris is pale olive with two baked catchlights: repaint brown, dark limbus, black pupil
    cx, cy, ri, rp = P['iris']
    if 'sclera_paint' in P:  # warm off-white instead of the template's grey
        x0, y0 = int(cx) - 118, int(cy) - 118; sub = res[y0:y0 + 237, x0:x0 + 237]
        res[y0:y0 + 237, x0:x0 + 237] = tl.paint_sclera(sub, (cx - x0, cy - y0), ri, P.get('sclera_rmax', 112), **{k: tuple(v) for k, v in P['sclera_paint'].items()})
    kw = {'stops': [(t, tuple(c)) for t, c in P['iris_stops']]} if 'iris_stops' in P else {}
    res = tl.repaint_iris(res, (cx, cy), ri, rp, pupil_scale=P.get('pupil_scale', 1.0), **kw)
    if 'catchlight' in P: res = tl.add_catchlight(res, (cx, cy), ri, **P['catchlight'])
cv2.imwrite(out, res); print('INNERTEX ok')
