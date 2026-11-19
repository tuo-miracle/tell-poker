# Sample colours on both sides of the face-mask seam (inside: baked face texture; outside: the character's own
# texture on the cut body mesh). Writes {inner: [[r,g,b]...], outer: [...], ...} (sRGB 0..1) for skin_match.py.
# usage: blender -b bake.blend -P seam_bands.py -- name face_tex.png out.json ['{"in_rings":[1,4],"out":[0.05,0.45],"zmax_eye":0.0,"lm":char_lm.json,"cam":char_cam.json}']
import bpy, sys, os, json, numpy as np
from mathutils import Vector
from mathutils.bvhtree import BVHTree
sys.path.insert(0, os.path.dirname(__file__)); import wraplib as wl
argv = sys.argv[sys.argv.index('--') + 1:]
name, face_tex, out = argv[:3]
P = json.loads(argv[3]) if len(argv) > 3 else {}
FN = bpy.data.objects[wl.char_names(name)['obj']]; U = FN['U']
H = [o for o in bpy.data.objects if o.type == 'MESH' and o is not FN and len(o.data.polygons) > 1000][0]

def img_np(img):
    a = np.empty(img.size[0] * img.size[1] * 4, np.float32); img.pixels.foreach_get(a)
    return a.reshape(img.size[1], img.size[0], 4)[..., :3]  # row 0 = v=0 (bottom)
def sample(I, uv):
    h, w = I.shape[:2]; x = np.clip((uv[:, 0] * w).astype(int), 0, w - 1); y = np.clip((uv[:, 1] * h).astype(int), 0, h - 1)
    return I[y, x]
def bary_pts(V, UVs, F, n=4):
    """Points + UVs on a regular barycentric grid inside each triangle."""
    ws = np.array([(i, j, n - i - j) for i in range(n + 1) for j in range(n + 1 - i)], float) / n
    ws = ws[(ws > 0).all(1)] if n >= 3 else ws
    P_ = np.einsum('kw,fwd->fkd', ws, V[F]).reshape(-1, 3); Q = np.einsum('kw,fwd->fkd', ws, UVs).reshape(-1, 2)
    return P_, Q

def to_srgb(lin): return np.where(lin <= 0.0031308, lin * 12.92, 1.055 * np.clip(lin, 0, None) ** (1 / 2.4) - 0.055)
# --- face side ---
mw = np.array(FN.matrix_world); me = FN.data
Vf = np.array([v.co for v in me.vertices]) @ mw[:3, :3].T + mw[:3, 3]
ring = np.empty(len(Vf)); me.attributes['ring'].data.foreach_get('value', ring)
uvl = me.uv_layers.active.data
r0, r1 = P.get('in_rings', [1, 4])
Fi, UVi = [], []
for p in me.polygons:
    if p.material_index != 0: continue
    vs = list(p.vertices)
    if ring[vs].min() >= r0 and ring[vs].max() <= r1: Fi.append(vs); UVi.append([tuple(uvl[li].uv) for li in p.loop_indices])
eyes = json.loads(FN['eyes']); ez = (eyes['L']['center'][2] + eyes['R']['center'][2]) / 2
zmax = ez + P.get('zmax_eye', 0.0) * U  # skip the hairline: compare only below eye level by default
Pi, Qi = bary_pts(Vf, np.array(UVi), np.array(Fi))
keep = Pi[:, 2] < zmax; Pi, Qi = Pi[keep], Qi[keep]
ftex = bpy.data.images.load(face_tex); Ci = sample(img_np(ftex), Qi)
if not ftex.colorspace_settings.is_data: Ci = to_srgb(Ci)  # pixels are linear in Blender
# --- outer side: body mesh just outside the border ---
border_pts = Vf[ring < 0.5]
fbvh = BVHTree.FromPolygons(Vf.tolist(), [list(p.vertices) for p in me.polygons if p.material_index == 0])
mh = np.array(H.matrix_world); hme = H.data
Vh = np.array([v.co for v in hme.vertices]) @ mh[:3, :3].T + mh[:3, 3]
huv = hme.uv_layers.active.data
Fh = np.array([list(p.vertices) for p in hme.polygons]); UVh = np.array([[tuple(huv[li].uv) for li in p.loop_indices] for p in hme.polygons])
c = Vh[Fh].mean(1)
from mathutils.kdtree import KDTree
kd = KDTree(len(border_pts))
for i, b in enumerate(border_pts): kd.insert(b, i)
kd.balance()
d0, d1 = P.get('out', [0.05, 0.45])
near = np.array([kd.find(Vector(x))[2] for x in c])
selF = (near > d0 * U) & (near < d1 * U) & (c[:, 2] < zmax)
Po, Qo = bary_pts(Vh, UVh[selF], Fh[selF])
cov = np.array([fbvh.find_nearest(Vector(x))[3] for x in Po])
ok = cov > P.get('uncovered', 0.04) * U  # not hidden under the new face
Po, Qo = Po[ok], Qo[ok]
himg = [n.image for n in H.data.materials[0].node_tree.nodes if n.type == 'TEX_IMAGE' and n.image][0]
Co = to_srgb(sample(img_np(himg), Qo))
# per-corner fields over the face skin for skin_match.py / lid_tex.py: ring from the border, and an elliptic
# distance to the nearest eye centre in eye radii (vertical counts 1.4x so the lid region is wider than tall)
ec = {s: np.array(e['center']) for s, e in eyes.items()}; er = np.mean([e['radius'] for e in eyes.values()])
def eye_d(x):
    return min(np.linalg.norm((x - c) * np.array([1, 1, P.get('eye_zs', 1.4)])) for c in ec.values()) / er
ed = np.array([eye_d(x) for x in Vf])
# height above the nearest eye's centre, in eye radii (upper vs lower lid)
ez = np.array([(x[2] - min(ec.values(), key=lambda c: np.linalg.norm(x - c))[2]) / er for x in Vf])
# nose base (for flattening painted nostril shadows): character landmarks 2 (subnasale), 98/327 (alar) cast onto the face
nose_pts = []
if P.get('lm') and P.get('cam'):
    L_ = json.load(open(P['lm'])); c_ = json.load(open(P['cam']))
    fb_all = BVHTree.FromPolygons(Vf.tolist(), [list(p.vertices) for p in me.polygons])
    for i in (2, 98, 327, 64, 294):
        px, py = L_[i]; o = Vector((c_['cx'] + (px / c_['W'] - 0.5) * c_['s'], c_['cy'], c_['cz'] - (py / c_['W'] - 0.5) * c_['s']))
        loc, n_, fi_, dd_ = fb_all.ray_cast(o, Vector((0, 1, 0)))
        if loc is not None: nose_pts.append(np.array(loc))
nd = (np.array([min(np.linalg.norm(x - q) for q in nose_pts) for x in Vf]) / U) if nose_pts else np.full(len(Vf), 99.0)
tri_uv, tri_ring, tri_eyed, tri_nosed, tri_eyez = [], [], [], [], []
for p in me.polygons:
    if p.material_index == 0:
        tri_uv.append([tuple(uvl[li].uv) for li in p.loop_indices]); tri_ring.append([float(ring[v]) for v in p.vertices])
        tri_eyed.append([float(ed[v]) for v in p.vertices]); tri_nosed.append([float(nd[v]) for v in p.vertices]); tri_eyez.append([float(ez[v]) for v in p.vertices])
json.dump({'tri_uv': tri_uv, 'tri_ring': tri_ring, 'tri_eyed': tri_eyed, 'tri_nosed': tri_nosed, 'tri_eyez': tri_eyez, 'nose_pts': [q.tolist() for q in nose_pts], 'inner': Ci.tolist(), 'outer': Co.tolist(), 'inner_xyz': Pi.tolist(), 'outer_xyz': Po.tolist(), 'U': U, 'zmax': zmax}, open(out, 'w'))
print('SEAM inner', len(Ci), 'mean', np.round(Ci.mean(0), 4).tolist(), 'outer', len(Co), 'mean', np.round(Co.mean(0), 4).tolist())
