# Repaint the upper lids of the baked face: the bake put Hank's painted eye and lit patches there, which only show when the
# lid closes. Smooth membrane from the skin around each lid + skin detail borrowed from just below the eye + a lash line.
# usage: python lid_fill.py face.png lid_data.json out.png dbg.jpg ['{"m0":0.12,"lash":0.35}']
import sys, os, json, numpy as np, cv2
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__))); import texlib as tl
face, dj, out, dbg = sys.argv[1:5]; P = json.loads(sys.argv[5]) if len(sys.argv) > 5 else {}
A = cv2.imread(face); h = A.shape[0]; D = json.load(open(dj)); res = A.copy(); dbgs = [A.copy()]
for key, mx in (('mL', D['mLmax']), ('mR', D['mRmax'])):
    tris = [t for t in D['tris'] if max(t[key]) / mx > P.get('m0', 0.12)]
    uv = np.array([t['uv'] for t in tris]); val = np.array([t[key] for t in tris]) / mx
    field, cov = tl.raster_vertex_field(uv, val, h)
    mask = cv2.dilate(cov.astype(np.uint8), np.ones((3, 3), np.uint8)) > 0
    filled = tl.membrane_fill(res, mask, iters=P.get('iters', 4000)).astype(np.float32)
    # skin detail: high-pass of the same-shaped area straight below the lid
    ys, xs = np.nonzero(mask); dy = int(P.get('detail_dy', 1.6) * (ys.max() - ys.min() + 1))
    hp = res.astype(np.float32) - cv2.GaussianBlur(res.astype(np.float32), (0, 0), P.get('detail_sigma', 6.0))
    src = np.roll(hp, -dy, 0)  # image y grows downward: pull the texels from below (past the painted eye) up into the lid
    w = cv2.GaussianBlur(mask.astype(np.float32), (0, 0), 2.0)[..., None]
    new = filled + P.get('detail', 0.6) * src * w
    # lash line along the lid margin (largest blink motion)
    t = np.clip((field - P.get('lash0', 0.8)) / (1 - P.get('lash0', 0.8)), 0, 1)
    new = new * (1 - P.get('lash', 0.35) * (t * t * (3 - 2 * t)))[..., None]
    res[mask] = np.clip(np.round(new[mask]), 0, 255).astype(np.uint8)
    print('LIDFILL', key, 'tris', len(tris), 'px', int(mask.sum()), 'bbox', xs.min(), xs.max(), ys.min(), ys.max())
cv2.imwrite(out, res)
yy, xx = np.nonzero((res != A).any(2)); y0, y1, x0, x1 = yy.min() - 40, yy.max() + 40, xx.min() - 40, xx.max() + 40
s = 1000 / (2 * (x1 - x0)); cv2.imwrite(dbg, cv2.resize(np.hstack([A[y0:y1, x0:x1], res[y0:y1, x0:x1]]), None, fx=s, fy=s))
