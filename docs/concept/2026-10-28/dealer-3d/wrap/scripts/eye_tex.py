# Repaint the template's iris (eyeball UV islands) dark brown with a limbal ring and a clear pupil.
# usage: python eye_tex.py in.png inner_islands.json out.png debug.jpg ['{"iris":[r,g,b],"limbus":[...],"pupil":[...]}']
import json, sys, os, numpy as np, cv2
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__))); import texfix as tf
src, ij, out, dbg = sys.argv[1:5]
P = json.loads(sys.argv[5]) if len(sys.argv) > 5 else {}
im = cv2.imread(src)[..., ::-1].copy(); h, w = im.shape[:2]
res = im.copy(); dbgs = []
done = []
for o in json.load(open(ij)):
    if o['part'] == 0: continue
    m = np.zeros((h, w), np.uint8)
    for t in o['uv']: cv2.fillPoly(m, [np.array([[u * w, (1 - v) * h] for u, v in t]).round().astype(np.int32)], 255)
    ys, xs = np.nonzero(m); box = (xs.min(), ys.min(), xs.max() + 1, ys.max() + 1)
    if any(abs(box[0] - b[0]) < 4 and abs(box[1] - b[1]) < 4 for b in done): continue  # both eyes share one UV disc
    done.append(box)
    crop = im[box[1]:box[3], box[0]:box[2]].astype(float); lum = crop.mean(2); inside = m[box[1]:box[3], box[0]:box[2]] > 0
    yy0, xx0 = np.mgrid[:lum.shape[0], :lum.shape[1]]
    core = inside & (np.hypot(xx0 - lum.shape[1] / 2, yy0 - lum.shape[0] / 2) < 0.25 * min(lum.shape))  # pupil sits near the disc centre
    dark = core & (lum < np.percentile(lum[core], 3) + 15)
    yy, xx = np.nonzero(dark); c = (xx.mean(), yy.mean())
    prof = tf.radial_profile(lum, c, int(min(crop.shape[:2]) / 2))
    rp, ri = tf.iris_radii(prof)
    ri = int(round(ri * P.get('iris_scale', 1.0)))  # eyeball scaled in rig.py: keep the iris its real size
    rp = max(rp, int(round(P.get('pupil_frac', 0.44) * ri)))  # clearly visible pupil (Hank v2: 16/36 of the iris)
    kw = {k: tuple(P[k]) for k in ('iris', 'limbus', 'pupil') if k in P}
    cu8 = crop.astype(np.uint8)
    if P.get('sclera'): cu8 = tf.paint_sclera(cu8, c, ri, 0.48 * min(crop.shape[:2]))  # the template's is grey and red-edged
    cu8 = tf.paint_iris(cu8, c, rp, ri, **kw)
    if P.get('catchlight'): cu8 = tf.add_catchlight(cu8, c, ri)
    res[box[1]:box[3], box[0]:box[2]] = cu8
    info = {'pupil_px': [float(box[0] + c[0]), float(box[1] + c[1])], 'r_iris': float(ri), 'r_pupil': float(rp), 'tex': w,
            'pupil_uv': [float((box[0] + c[0]) / w), float(1 - (box[1] + c[1]) / h)]}
    print('EYETEX box', box, 'centre', np.round(c, 1).tolist(), 'pupil r', rp, 'iris r', ri, 'disc', crop.shape[:2])
    dbgs.append(np.hstack([im[box[1]:box[3], box[0]:box[2]], res[box[1]:box[3], box[0]:box[2]]]))
cv2.imwrite(out, res[..., ::-1])
json.dump(info, open(out.rsplit('.', 1)[0] + '.json', 'w')); print('EYETEX info', info)
if dbgs: cv2.imwrite(dbg, cv2.resize(dbgs[0], (1000, 500))[..., ::-1])
