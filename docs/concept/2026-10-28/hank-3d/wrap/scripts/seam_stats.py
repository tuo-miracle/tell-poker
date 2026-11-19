# Seam colour statistics from seam_renders.py output: mean sRGB of a band on each side of the face/Hank seam,
# split into forehead (above the eyes) and jaw (lower 45% of the face), and the CIEDE2000 / CIE76 difference.
# usage: python3 seam_stats.py render_dir out.json [overlay_dir]
import sys, os, json, numpy as np, cv2
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__))); import texlib as tl
d, out = sys.argv[1], sys.argv[2]; ovd = sys.argv[3] if len(sys.argv) > 3 else None
NEAR, FAR = 4, 28  # px at 1024 over 0.11-0.13 m: ~0.5-3.5 mm each side of the seam

def skin(bgr):
    h = cv2.cvtColor(bgr, cv2.COLOR_BGR2HSV)
    return ((h[..., 0] <= 25) | (h[..., 0] >= 165)) & (h[..., 1] > 45) & (h[..., 2] > 35)

res = {}
for view in ('front', 'q', 'side'):
    idm = cv2.imread(os.path.join(d, f'{view}_id.png')).astype(int)
    face = (idm[..., 2] > 128) & (idm[..., 1] < 64); hank = (idm[..., 1] > 128) & (idm[..., 2] < 64); eye = (idm[..., 0] > 128) & (idm[..., 2] < 64)
    ys = np.nonzero(face)[0]; ey = np.nonzero(eye)[0].mean() if eye.any() else ys.min() + 0.35 * (ys.max() - ys.min())
    yy = np.mgrid[:face.shape[0], :face.shape[1]][0]
    regions = {'forehead': yy < ey, 'jaw': yy > ey + 0.55 * (ys.max() - ey)}
    fb, hb = tl.seam_bands(face, hank, NEAR, FAR)
    for mode in ('albedo', 'lit', 'dim'):
        im = cv2.imread(os.path.join(d, f'{view}_{mode}.png'))
        sk = skin(cv2.imread(os.path.join(d, f'{view}_albedo.png')))
        for rn, rm in regions.items():
            mf, mh = fb & rm & sk, hb & rm & sk
            if mf.sum() < 50 or mh.sum() < 50: continue
            cf = im[mf][:, ::-1].mean(0); ch = im[mh][:, ::-1].mean(0)  # RGB
            lf, lh = tl.srgb8_to_lab(cf), tl.srgb8_to_lab(ch)
            res.setdefault(view, {}).setdefault(mode, {})[rn] = {
                'face_rgb': cf.round(1).tolist(), 'hank_rgb': ch.round(1).tolist(), 'n_face': int(mf.sum()), 'n_hank': int(mh.sum()),
                'dE2000': round(float(tl.delta_e2000(lf, lh)), 2), 'dE76': round(float(np.linalg.norm(lf - lh)), 2),
                'dL': round(float(lf[0] - lh[0]), 2)}
            if ovd and mode == 'albedo':
                os.makedirs(ovd, exist_ok=True); ov = im.copy(); ov[mf] = ov[mf] // 2 + (0, 0, 127); ov[mh] = ov[mh] // 2 + (0, 127, 0)
                cv2.imwrite(os.path.join(ovd, f'{view}_{rn}.jpg'), ov)
json.dump(res, open(out, 'w'), indent=1)
for v, m in res.items():
    for mode, r in m.items():
        print('SEAM', v, mode, {k: (x['dE2000'], x['dL']) for k, x in r.items()})
