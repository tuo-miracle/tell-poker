# Replace the eyelid region of the baked face with the template's own lid texture (lashes, crease, caruncle),
# recoloured to the character's skin: the character's painted-on eye cannot be re-used on the moving 3-D lids.
# usage: python lid_tex.py face.png template_head.png seam.json out.png ['{"d0":1.7,"d1":2.4,"ring":[2.4,3.4]}']
import json, sys, os, numpy as np, cv2
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__))); import texfix as tf
face, tmpl, sj, out = sys.argv[1:5]
P = json.loads(sys.argv[5]) if len(sys.argv) > 5 else {}
S = json.load(open(sj))
A = cv2.imread(face)[..., ::-1].copy(); h = A.shape[0]
T = cv2.resize(cv2.imread(tmpl)[..., ::-1], (h, h), interpolation=cv2.INTER_AREA)
d, cov = tf.raster_vertex_field(np.array(S['tri_uv']), np.array(S['tri_eyed']), h)
d0, d1 = P.get('d0', 1.7), P.get('d1', 2.4)
w = (1 - tf.gain_weight(d, d0, d1)) * cov
r0, r1 = P.get('ring', [d1, d1 + 1.0])
band = cov & (d > r0) & (d < r1)  # skin ring just outside the lid region, same pixels in both textures
g = tf.band_gain(T[band] / 255.0, A[band] / 255.0, lo=0.4, hi=2.5)
Tg = tf.apply_gain(T, np.ones(T.shape[:2]), g)
if P.get('mode', 'detail') == 'detail':
    # the template's lid skin is darker and carries its own under-eye shading: take only its detail (lashes, crease)
    # on the character's own tone; keep the full template colour on the lid rim around the opening (d < d_full)
    if 'rim' in P:
        # the template's socket red shows as a saturated rim and a dark ring: keep only skin-toned detail, upper lashes
        # stronger than lower, and paint the inner-lid rim a natural pink derived from the character's skin
        z, _ = tf.raster_vertex_field(np.array(S['tri_uv']), np.array(S['tri_eyez']), h)
        up = np.clip((z + 0.1) / 0.2, 0, 1)
        k = P.get('lash', [0.85, 0.3]); kz = k[1] + (k[0] - k[1]) * up
        rm = (tf.rim_mask(T) * w > 0.01).astype(np.uint8)
        rm = cv2.dilate(rm, np.ones((5, 5), np.uint8))
        Tf = cv2.inpaint(Tg, rm * 255, 5, cv2.INPAINT_TELEA)
        Dt = tf.detail_transfer(A, Tf, P.get('sigma', 8.0), cov, kz)
        skin = np.median(A[band], 0); pink = skin * np.array(P.get('rim_tint', [1.0, 0.86, 0.84]))
        f = (P['rim'][1] + (P['rim'][0] - P['rim'][1]) * up)[..., None]
        ar = cv2.GaussianBlur(rm.astype(np.float32), (0, 0), 1.5)[..., None]
        Tg = np.clip(np.round(pink * f * ar + Dt * (1 - ar)), 0, 255).astype(np.uint8)
        print('LIDTEX rim px', int(rm.sum()), 'pink', np.round(pink).tolist())
    else:
        wf = (1 - tf.gain_weight(d, P.get('d_full', 1.15), P.get('d_full1', 1.4))) * cov
        Dt = tf.detail_transfer(A, Tg, P.get('sigma', 8.0), cov)
        Tg = np.clip(np.round(Tg * wf[..., None] + Dt * (1 - wf[..., None])), 0, 255).astype(np.uint8)
# nose base: the character's texture has its nostril shadow painted in (baked AO) where its own (different) nose was;
# the template nose now casts a real one. Flatten the mid-scale tone there, keep fine detail.
if P.get('nose_r1', 0) > 0 and 'tri_nosed' in S:
    nd, _ = tf.raster_vertex_field(np.array(S['tri_uv']), np.array(S['tri_nosed']), h)
    wn = (1 - tf.gain_weight(nd, P.get('nose_r0', 0.25), P['nose_r1'])) * cov
    A = tf.flatten_lowfreq(A, wn, P.get('nose_sd', 3.0), P.get('nose_sb', 40.0), cov)
    print('LIDTEX nose flatten px', int((wn > 0.5).sum()))
res = np.clip(np.round(A * (1 - w[..., None]) + Tg * w[..., None]), 0, 255).astype(np.uint8)
cv2.imwrite(out, res[..., ::-1])
print('LIDTEX gain', np.round(g, 3).tolist(), 'lid px', int((w > 0.5).sum()), 'ring px', int(band.sum()))
