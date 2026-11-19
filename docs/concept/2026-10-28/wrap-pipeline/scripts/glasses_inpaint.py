# Inpaint the texels of the baked face that were taken from the old Tripo glasses frames (bake.py exclude_json writes
# face_bake_framemask.png: frame texels green), grown a little, filled from the skin round them.
# usage: python glasses_inpaint.py face.png framemask.png out.png dbg.jpg ['{"grow":3,"radius":6,"zone":rimzone.json,"sat_max":0.2}']
import json, os, sys, numpy as np, cv2
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__))); import acclib as al
src, mp, out, dbg = sys.argv[1:5]; P = json.loads(sys.argv[5]) if len(sys.argv) > 5 else {}
im = cv2.imread(src); mk = cv2.imread(mp)
m = ((mk[..., 1] > 100) & (mk[..., 2] < 80)).astype(np.uint8) * 255
if P.get('zone'):  # plus grey/silver frame paint inside the zone round the old rims (acclib.frame_paint_mask)
    zone = np.zeros(m.shape, np.uint8)
    for t in json.load(open(P['zone'])): cv2.fillPoly(zone, [np.array([[u * zone.shape[1], (1 - v) * zone.shape[0]] for u, v in t]).round().astype(np.int32)], 255)
    zp = al.frame_paint_mask(im, zone > 0, P.get('sat_max', 0.2)); m = m | (zp.astype(np.uint8) * 255); print('GLASSINPAINT rim zone paint px', int(zp.sum()))
m = cv2.dilate(m, np.ones((3, 3), np.uint8), iterations=P.get('grow', 3))
res = cv2.inpaint(im, m, P.get('radius', 6), cv2.INPAINT_TELEA)
cv2.imwrite(out, res)
ys, xs = np.nonzero(m)
if len(xs):
    b = (max(xs.min() - 40, 0), max(ys.min() - 40, 0), xs.max() + 40, ys.max() + 40); ov = im.copy(); ov[m > 0] = (0, 0, 255)
    cv2.imwrite(dbg, np.hstack([ov[b[1]:b[3], b[0]:b[2]], res[b[1]:b[3], b[0]:b[2]]]))
print('GLASSINPAINT frame px', int((m > 0).sum()))
