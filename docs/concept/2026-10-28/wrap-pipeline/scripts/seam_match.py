# Match the baked face texture to Hank's skin across the face-mask seam and feather it inward.
# usage: python3 seam_match.py seam_data.json hank_tex.png face_tex.png out.png [params_json]
# Gains are linear-light, per channel, from texture colours (blurred) at mesh vertices: Hank's skin within `radius` of
# each border vertex vs. the first visible face rings (the outer rings are tucked under Hank). Rasterised over the
# face UVs and multiplied into the texture.
import sys, os, json, numpy as np, cv2
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__))); import texlib as tl
dj, hank_png, face_png, out = sys.argv[1:5]
P = json.loads(sys.argv[5]) if len(sys.argv) > 5 else {}
D = json.load(open(dj))
V = np.array(D['V']); ring = np.array(D['ring']); fv = np.array(D['face_verts'])
face = cv2.imread(face_png); hank = cv2.imread(hank_png); res = face.shape[0]
blur = lambda im, s: cv2.GaussianBlur(im, (0, 0), s * im.shape[0] / 2048)
def sample(im, uv):
    h, w = im.shape[:2]; x = np.clip(uv[:, 0] * w - 0.5, 0, w - 1); y = np.clip((1 - uv[:, 1]) * h - 0.5, 0, h - 1)
    return np.stack([cv2.remap(im[..., k].astype(np.float32), x.astype(np.float32)[:, None], y.astype(np.float32)[:, None], cv2.INTER_LINEAR)[:, 0] for k in range(3)], -1)
vuv = np.zeros((len(V), 2))
for vs, uvs in D['tris']:
    for v, u in zip(vs, uvs): vuv[v] = u
fcol = sample(blur(face, P.get('blur', 6)), vuv[fv])            # BGR 0-255
hcol = sample(blur(hank, P.get('blur', 6)), np.array(D['hank_uv']))
hsv = cv2.cvtColor(hcol[None].astype(np.uint8), cv2.COLOR_BGR2HSV)[0]
skin = ((hsv[:, 0] <= 25) | (hsv[:, 0] >= 165)) & (hsv[:, 1] > 45) & (hsv[:, 2] > P.get('vmin', 35))  # drop hat, hair, stubble-grey (vmin: also dark red hair)
print('SEAMMATCH hank samples', len(hcol), 'skin', int(skin.sum()))
g = tl.seam_gains(V[fv], tl.srgb8_to_linear(fcol), ring[fv], np.array(D['hank_V'])[skin], tl.srgb8_to_linear(hcol[skin]),
                  radius=P.get('radius', 0.010), radius_face=P.get('radius_face', 0.020), radius_smooth=P.get('radius_smooth', 0.025),
                  src_rings=tuple(P.get('src_rings', (2, 3))), full=P.get('full', 2.0), fade=P.get('fade', 3.0), clip=tuple(P.get('clip', (0.8, 1.25))))
G = np.ones((len(V), 3)); G[fv] = g
print('SEAMMATCH gain (B,G,R) at ring<=2 mean', g[ring[fv] <= 2].mean(0).round(3).tolist(), 'min', g.min(0).round(3).tolist(), 'max', g.max(0).round(3).tolist())
tri_uv = np.array([uvs for vs, uvs in D['tris']]); tri_g = np.array([G[vs] for vs, uvs in D['tris']])
gm, cov = tl.raster_vertex_field(tri_uv, tri_g, res)
# extend gains into the bake margin (nearest covered texel) so UV-island edges do not show
dist, lab = cv2.distanceTransformWithLabels((~cov).astype(np.uint8), cv2.DIST_L2, 5, labelType=cv2.DIST_LABEL_PIXEL)
ys, xs = np.nonzero(cov); src = np.zeros((lab.max() + 1, 3)); src[lab[ys, xs]] = gm[ys, xs]
gfull = np.where(cov[..., None], gm, np.where((dist <= P.get('margin', 20))[..., None], src[lab], 1.0))
cv2.imwrite(out, tl.apply_gain_srgb(face, gfull))
dbg = out.replace('.png', '_gain.jpg'); cv2.imwrite(dbg, np.clip((gfull - 1) * 400 + 128, 0, 255).astype(np.uint8))
print('SEAMMATCH wrote', out)
