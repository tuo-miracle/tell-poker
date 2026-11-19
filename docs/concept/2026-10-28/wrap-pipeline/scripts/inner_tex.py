import json, sys, os, numpy as np, cv2
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__))); import texlib as tl
tex, ij, out = sys.argv[1:4]
P = json.loads(sys.argv[4]) if len(sys.argv) > 4 else {}
im = cv2.imread(tex).astype(np.float32); h, w = im.shape[:2]; I = json.load(open(ij))
gain = np.ones((h, w), np.float32)
for o in I:
    # mouth islands: 47/36/8 faces were Hank's (Male_Adult_03) mouth walls; another template's islands of those sizes are
    # other parts (Male_Adult_09: the tongue), so mouth_dark lists them per template; mouth_gain lifts the whole mouth (cavity read black)
    g = P.get('sclera_gain', 0.75) if o['part'] > 0 else (0.55 if o['n'] in P.get('mouth_dark', (47, 36, 8)) else P.get('mouth_gain', 1.0))
    if g == 1.0: continue
    m = np.zeros((h, w), np.uint8)
    for t in o['uv']: cv2.fillPoly(m, [np.array([[u * w, (1 - v) * h] for u, v in t]).round().astype(np.int32)], 255)
    m = cv2.dilate(m, np.ones((9, 9), np.uint8)) > 0
    if g < 1: gain[m] = np.minimum(gain[m], g)
    else: gain[m & (gain == 1)] = g  # lift: only where no darkening island claimed the pixel
tint = np.ones((h, w, 3), np.float32)
if 'sclera_tint' in P:  # BGR multipliers on the eyeball islands (Rocketbox sclera is a cool blue-white)
    tint[gain == P.get('sclera_gain', 0.75)] = P['sclera_tint']
res = np.clip(im * gain[..., None] * tint, 0, 255).astype(np.uint8)
if 'iris' in P:  # Rocketbox iris is pale olive with two baked catchlights: repaint brown-hazel, dark limbus, black pupil
    cx, cy, ri, rp = P['iris']; res = tl.repaint_iris(res, (cx, cy), ri, rp, pupil_scale=P.get('pupil_scale', 1.0))
cv2.imwrite(out, res); print('INNERTEX ok')
