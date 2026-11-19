# Match the face-mask skin colour to the body around the seam, feathered over the first rings of the mask.
# gain = outer-band mean / inner-band mean (linear). Source of the means: the render check (what the player sees,
# includes shading) when given, else the texture samples from seam_bands.py.
# usage: python skin_match.py face.png seam.json out.png [render_check.json] ['{"r0":0,"r1":2}']
import json, sys, os, numpy as np, cv2
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__))); import texfix as tf
src, sj, out = sys.argv[1:4]
rc = sys.argv[4] if len(sys.argv) > 4 and sys.argv[4] else None
P = json.loads(sys.argv[5]) if len(sys.argv) > 5 else {}
S = json.load(open(sj))
g_tex = tf.band_gain(np.array(S['inner']), np.array(S['outer']))
if P.get('no_gain'): g = np.ones(3)  # only clean the border (colour matching done by seam_match.py)
elif rc:
    R = json.load(open(rc)); g = np.clip(np.mean([v['ratio_lin'] for v in R.values()], 0), 0.67, 1.5)
else: g = g_tex
im = cv2.imread(src)[..., ::-1].copy(); h, w = im.shape[:2]
ring, cov = tf.raster_vertex_field(np.array(S['tri_uv']), np.array(S['tri_ring']), h)
wgt = tf.gain_weight(ring, P.get('r0', 0.0), P.get('r1', 2.0)) * cov
# carry the weight into the bake margin so island edges do not show a step
for _ in range(20):
    grow = cv2.dilate(wgt.astype(np.float32), np.ones((3, 3), np.uint8)); cov2 = cv2.dilate(cov.astype(np.uint8), np.ones((3, 3), np.uint8)) > 0
    new = cov2 & ~cov; wgt[new] = grow[new]; cov = cov2
bad = tf.border_outliers(im / 255.0, ring, cov, P.get('clean_ring', 1.5), P.get('clean_th', 0.1))
if bad.any():  # collar colour baked onto the mask edge -> fill from the surrounding skin
    im = cv2.inpaint(im, cv2.dilate(bad.astype(np.uint8) * 255, np.ones((5, 5), np.uint8)), 6, cv2.INPAINT_TELEA)
res = tf.apply_gain(im, wgt, g)
cv2.imwrite(out, res[..., ::-1])
print('SKINMATCH gain', np.round(g, 4).tolist(), 'texture-band gain', np.round(g_tex, 4).tolist(), 'weighted px', int((wgt > 0).sum()), 'border outliers', int(bad.sum()))
