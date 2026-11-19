# Fit the Rocketbox head onto the character: landmarks -> similarity -> TPS -> crop -> masked shrinkwrap -> eyes -> cut the character.
import bpy, bmesh, sys, os, json, numpy as np
sys.path.insert(0, os.path.dirname(__file__)); import wraplib as wl
from mathutils import Vector, Matrix
from mathutils.bvhtree import BVHTree
argv = sys.argv[sys.argv.index('--') + 1:]
name, hank_glb, rb_blend, rb_lm, rb_cam, h_lm, h_cam, out_blend = argv[:8]  # hank_* = the character being wrapped
P = json.loads(argv[8]) if len(argv) > 8 else {}
NM = wl.char_names(name)
LAM = P.get('lam', 0.003); ALPHA = P.get('alpha', 0.1)
log = lambda *a: print('FIT', *a, flush=True)

bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=hank_glb)
arm = [o for o in bpy.context.scene.objects if o.type == 'ARMATURE'][0]
H = [o for o in bpy.context.scene.objects if o.type == 'MESH' and len(o.data.polygons) > 1000][0]
for o in bpy.context.scene.objects:
    if o.type == 'MESH' and o is not H:
        log('small mesh', o.name, len(o.data.polygons), 'loc', tuple(round(x, 3) for x in o.matrix_world.translation),
            'dims', tuple(round(x, 3) for x in o.dimensions), 'parent', o.parent.name if o.parent else None)
bm = bmesh.new(); bm.from_mesh(H.data); bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=1e-5); bm.to_mesh(H.data); bm.free()
if H.data.has_custom_normals:
    bpy.context.view_layer.objects.active = H; bpy.ops.mesh.customdata_custom_splitnormals_clear()
for p in H.data.polygons: p.use_smooth = True
with bpy.data.libraries.load(rb_blend) as (src, dst): dst.objects = ['RBHead']
RB = dst.objects[0]; bpy.context.scene.collection.objects.link(RB)

def np_co(me):
    a = np.empty(len(me.vertices) * 3); me.vertices.foreach_get('co', a); return a.reshape(-1, 3)
def tris(me):
    me.calc_loop_triangles(); a = np.empty(len(me.loop_triangles) * 3, int); me.loop_triangles.foreach_get('vertices', a); return a.reshape(-1, 3)
def vnormals(V, F):
    fn = np.cross(V[F[:, 1]] - V[F[:, 0]], V[F[:, 2]] - V[F[:, 0]]); n = np.zeros_like(V)
    for k in range(3): np.add.at(n, F[:, k], fn)
    return n / (np.linalg.norm(n, axis=1, keepdims=True) + 1e-20)

HM = np.array(H.matrix_world)
dg = bpy.context.evaluated_depsgraph_get(); He = H.evaluated_get(dg); hme = He.to_mesh()
HW = np_co(hme) @ HM[:3, :3].T + HM[:3, 3]; He.to_mesh_clear()
HW0 = np_co(H.data) @ HM[:3, :3].T + HM[:3, 3]
log('hank evaluated-vs-rest max diff', np.abs(HW - HW0).max(), 'verts', len(HW))
HF = tris(H.data)
hbvh = BVHTree.FromPolygons(HW.tolist(), HF.tolist())
RB0 = np_co(RB.data); RF = tris(RB.data)
rbvh = BVHTree.FromPolygons(RB0.tolist(), RF.tolist())
part = np.empty(len(RB0), int); RB.data.attributes['part'].data.foreach_get('value', part)

def lm3d(bvh, lm_path, cam_path, need_front=True):
    L = json.load(open(lm_path)); c = json.load(open(cam_path)); s, W = c['s'], c['W']
    out = np.full((len(L), 3), np.nan)
    for i, (px, py) in enumerate(L):
        o = Vector((c['cx'] + (px / W - 0.5) * s, c['cy'], c['cz'] - (py / W - 0.5) * s))
        loc, n, fi, d = bvh.ray_cast(o, Vector((0, 1, 0)))
        if loc is not None and (not need_front or n.y < -0.3): out[i] = loc
    return out, np.array(L), c
A_all, _, _ = lm3d(rbvh, rb_lm, rb_cam); B_all, HL2, hcam = lm3d(hbvh, h_lm, h_cam)
B_any, _, _ = lm3d(hbvh, h_lm, h_cam, need_front=False)
ok = np.array([np.isfinite(A_all[i]).all() and np.isfinite(B_all[i]).all() for i in range(468)])
idx = np.nonzero(ok)[0]; log('valid landmark pairs', len(idx))
inl = idx.copy()
for it in range(3):
    sim = wl.umeyama(A_all[inl], B_all[inl]); r = np.linalg.norm(wl.apply_sim(sim, A_all[idx]) - B_all[idx], axis=1)
    inl = idx[r < 2.5 * np.median(r)]
U = np.linalg.norm(B_all[33] - B_all[263])  # character outer-eye-corner distance = length unit
log('sim scale', round(sim[0], 4), 'median resid/U', np.median(r) / U, 'inliers', len(inl), 'U', U)
# nostril/alar landmarks land on crumpled Tripo geometry: leave them out of the warp (they still count for the similarity)
NOSTRIL = [2, 97, 98, 99, 326, 327, 328, 64, 294, 240, 460, 75, 305, 79, 309, 238, 458, 20, 250, 60, 290, 166, 392, 219, 439]
if P.get('tps_skip_nose', True): inl = np.array([i for i in inl if i not in NOSTRIL])
A_s = wl.apply_sim(sim, A_all[inl]); tps = wl.TPS(A_s, B_all[inl], lam=LAM)
res = np.linalg.norm(tps(A_s) - B_all[inl], axis=1); log('tps resid/U median/max', np.median(res) / U, res.max() / U)
for _i in (249, 390, 263, 392, 309, 305, 466, 359):
    log('lm', _i, 'A_sim', np.round(wl.apply_sim(sim, A_all[_i]) / U, 3).tolist() if np.isfinite(A_all[_i]).all() else None, 'B', np.round(B_all[_i] / U, 3).tolist(), 'Bany', np.round(B_any[_i] / U, 3).tolist(), 'inlier', bool(_i in set(inl.tolist())))
o_ = np.argsort(-res)[:6]; log('tps worst lm', [(int(inl[i]), round(float(res[i] / U), 3)) for i in o_])
# per-vertex stretch of warp: edge length ratio vs similarity
_E = wl.edges_of(RF); _l0 = np.linalg.norm(wl.apply_sim(sim, RB0[_E[:, 0]]) - wl.apply_sim(sim, RB0[_E[:, 1]]), axis=1)
_V1 = tps(wl.apply_sim(sim, RB0)); _l1 = np.linalg.norm(_V1[_E[:, 0]] - _V1[_E[:, 1]], axis=1); _r = _l1 / (_l0 + 1e-12)
log('warp edge ratio pct 1/50/99/max', np.percentile(_r, [1, 50, 99]).round(2).tolist(), _r.max().round(2), 'worst edges verts', _E[np.argsort(-_r)[:4]].tolist())
keyn = [k.name for k in RB.data.shape_keys.key_blocks[1:]]
K0 = {}
for k in RB.data.shape_keys.key_blocks[1:]:
    a = np.empty(len(RB0) * 3); k.data.foreach_get('co', a); K0[k.name] = a.reshape(-1, 3)
warp = lambda X: tps(wl.apply_sim(sim, X))
V1 = warp(RB0); K1 = {n: warp(a) for n, a in K0.items()}

# ---------- crop in character space ----------
OVAL = [10, 338, 297, 332, 284, 251, 389, 356, 454, 323, 361, 288, 397, 365, 379, 378, 400, 377, 152, 148, 176, 149, 150, 136, 172, 58, 132, 93, 234, 127, 162, 21, 54, 103, 67, 109]
s_, W_ = hcam['s'], hcam['W']
poly = np.array([[hcam['cx'] + (HL2[i][0] / W_ - 0.5) * s_, hcam['cz'] - (HL2[i][1] / W_ - 0.5) * s_] for i in OVAL])
ctr = poly.mean(0); poly = ctr + (poly - ctr) * P.get('oval_scale', 1.0)
ztop = poly[:, 1].max() - P.get('top_cut', 0.0) * U
yfront = np.nanmean([B_any[234][1], B_any[454][1]]) + P.get('front_off', 0.0) * U
log('crop ztop', ztop, 'yfront', yfront)
inside = wl.inside_polygon(V1[:, [0, 2]], poly) & (V1[:, 2] < ztop) & (V1[:, 1] < yfront)
moved = np.zeros(len(RB0), bool)
for n_, a in K0.items(): moved |= np.linalg.norm(a - RB0, axis=1) > P.get('move_th', 0.001)
moved &= part == 0
grow = moved.copy(); _E0 = wl.edges_of(RF)
for _ in range(P.get('grow', 2)):
    g2 = grow.copy(); g2[_E0[:, 0]] |= grow[_E0[:, 1]]; g2[_E0[:, 1]] |= grow[_E0[:, 0]]; grow = g2
grow &= (V1[:, 1] < yfront) & (V1[:, 2] < ztop)
log('crop oval', int(inside.sum()), 'moved+grow', int(grow.sum()), 'added', int((grow & ~inside).sum()))
inside = inside | grow
# close notches in the mask outline (morphological closing on the vertex graph): a notch leaves body skin showing
# through the mask as a separately lit patch (dealer's right temple)
for _ in range(P.get('crop_close', 0)):
    d_ = inside.copy(); d_[_E0[:, 0]] |= inside[_E0[:, 1]]; d_[_E0[:, 1]] |= inside[_E0[:, 0]]; inside = d_ & (V1[:, 1] < yfront) & (V1[:, 2] < ztop)
for _ in range(P.get('crop_close', 0)):
    e_ = inside.copy(); e_[_E0[:, 0]] &= inside[_E0[:, 1]]; e_[_E0[:, 1]] &= inside[_E0[:, 0]]; inside = e_ | (inside & grow)
log('crop after closing', int(inside.sum()))
vkeep = inside | (part > 0)
fk = vkeep[RF].all(1)
# largest skin component + eyes
Fk = RF[fk]
lab = np.arange(len(RB0))
def find(a):
    while lab[a] != a: lab[a] = lab[lab[a]]; a = lab[a]
    return a
for f in Fk:
    for a, b in ((f[0], f[1]), (f[1], f[2])):
        ra, rb_ = find(a), find(b)
        if ra != rb_: lab[ra] = rb_
roots = np.array([find(f[0]) for f in Fk])
skin_roots = roots[part[Fk[:, 0]] == 0]
big = np.bincount(skin_roots).argmax()
fsel = (roots == big) | (part[Fk[:, 0]] > 0)
Fk = Fk[fsel]
# keep the original polygon index for UVs
orig_face = np.nonzero(fk)[0][fsel]
used = np.unique(Fk); remap = -np.ones(len(RB0), int); remap[used] = np.arange(len(used))
Fc = remap[Fk]; Vc = V1[used].copy(); Kc = {n: a[used].copy() for n, a in K1.items()}
R0c = RB0[used]; partc = part[used]; nV = len(Vc)
log('cropped verts', nV, 'tris', len(Fc))

# ---------- materials via UV islands (main face skin island -> baked texture) ----------
uvl = RB.data.uv_layers.active.data
poly_uv = {}
for pi in orig_face:
    p = RB.data.polygons[int(pi)]; poly_uv[int(pi)] = [tuple(uvl[li].uv) for li in p.loop_indices]
# note: RB tris are polygons (all triangles) so loop order matches vertices
flab = np.arange(len(Fc))
def ff(a):
    while flab[a] != a: flab[a] = flab[flab[a]]; a = flab[a]
    return a
edge_owner = {}
for fi, (pi, f) in enumerate(zip(orig_face, Fc)):
    uvs = poly_uv[int(pi)]
    for k in range(3):
        a, b = f[k], f[(k + 1) % 3]; ua, ub = uvs[k], uvs[(k + 1) % 3]
        key = (min(a, b), max(a, b)); val = (ua, ub) if a < b else (ub, ua)
        if key in edge_owner:
            fj, vj = edge_owner[key]
            if np.allclose(np.array(val), np.array(vj), atol=1e-6):
                ra, rb_ = ff(fi), ff(fj)
                if ra != rb_: flab[ra] = rb_
        else: edge_owner[key] = (fi, val)
isl = np.array([ff(i) for i in range(len(Fc))])
ids, cnt = np.unique(isl, return_counts=True)
order = ids[np.argsort(-cnt)]
for i_ in order[:8]:
    m = isl == i_; ctr_ = Vc[Fc[m]].mean((0, 1))
    log('uv island', int(i_), int(m.sum()), 'tris; part', np.bincount(partc[Fc[m][:, 0]]).tolist(), 'ctr', np.round(ctr_, 3).tolist())
face_island = order[0]
mat_idx = np.where(isl == face_island, 0, 1)
# inner-lip faces inside the main island stay in the face material (baked)
# only main-island (outer skin) vertices are pulled onto the character; mouth walls and teeth just follow the field
on_face = np.zeros(len(Vc), bool); on_face[np.unique(Fc[mat_idx == 0])] = True

# ---------- topology helpers ----------
skin = partc == 0
Fs = Fc[skin[Fc].all(1)]
loops = wl.boundary_loops(Fs)
eyeC_rb = {s: np.array(c) for s, c in json.loads(RB['eyes']).items()}
border = max(loops, key=len)
EYE_CONT = {'L': [263, 249, 390, 373, 374, 380, 381, 382, 362, 398, 384, 385, 386, 387, 388, 466],
            'R': [33, 7, 163, 144, 145, 153, 154, 155, 133, 173, 157, 158, 159, 160, 161, 246]}
eye_pts = {}
for sd, ids_ in EYE_CONT.items():
    q = np.array([A_all[i] for i in ids_ if np.isfinite(A_all[i]).all()])
    if np.linalg.norm(q.mean(0) - eyeC_rb[sd]) > 0.03:  # MediaPipe L/R is image-based: swap if needed
        sd = 'L' if sd == 'R' else 'R'
    eye_pts[sd] = np.concatenate([q, (q + np.roll(q, -1, 0)) / 2])
log('loops', [len(l) for l in loops], 'border', len(border), 'eye pts', {k: len(v) for k, v in eye_pts.items()})
E = wl.edges_of(Fc)
Es = E[skin[E].all(1)]
ring = wl.ring_distance(nV, Es, border)
# protection weights (RB metric space)
def dist_to(pts, X): return np.min(np.linalg.norm(X[:, None] - pts[None], axis=2), axis=1)
eye_rim_pts = np.concatenate(list(eye_pts.values()))
INNER_LIP = [78, 191, 80, 81, 82, 13, 312, 311, 310, 415, 308, 324, 318, 402, 317, 14, 87, 178, 88, 95]
lp = np.array([A_all[i] for i in INNER_LIP if np.isfinite(A_all[i]).all()])
lp = np.concatenate([lp, (lp + np.roll(lp, -1, 0)) / 2])
d_eye = dist_to(eye_rim_pts, R0c); d_lip = dist_to(lp, R0c)
prot = wl.ramp(d_eye, P.get('eye_r0', 0.004), P.get('eye_r1', 0.012)) * wl.ramp(d_lip, P.get('lip_r0', 0.003), P.get('lip_r1', 0.010))
# nostrils/alar base: Tripo noses are often crumpled there, so keep the template's own nostrils (TPS-placed)
if P.get('nose_r1', 0.012) > 0:
    nq = np.array([A_all[i] for i in NOSTRIL if np.isfinite(A_all[i]).all()])
    prot *= wl.ramp(dist_to(nq, R0c), P.get('nose_r0', 0.004), P.get('nose_r1', 0.012))
sk_idx = np.nonzero(skin)[0]; smap = -np.ones(nV, int); smap[sk_idx] = np.arange(len(sk_idx))
Es_l = smap[Es]

# ---------- masked shrinkwrap (smooth displacement field) ----------
D_total = np.zeros((nV, 3)); shrink_w = np.zeros(nV)
for it in range(P.get('iters', 2)):
    N = vnormals(Vc, Fc)
    c = np.zeros((len(sk_idx), 3)); w = np.zeros(len(sk_idx)); nh = np.zeros((len(sk_idx), 3))
    for j, vi in enumerate(sk_idx):
        loc, n, fi, dd = hbvh.find_nearest(Vector(Vc[vi]))
        if loc is None: continue
        nh[j] = n
        if dd < 0.25 * U and np.dot(N[vi], n) > 0.3 and on_face[vi]:
            c[j] = np.array(loc) - Vc[vi]; w[j] = prot[vi]
    if P.get('eye_pin_r'):  # hold the lid rims where the warp put them (on the character's eye landmarks)
        pin = d_eye[sk_idx] < P['eye_pin_r']; w[pin] = P.get('eye_pin', 1.0); c[pin] = 0
    d = wl.smooth_field(len(sk_idx), Es_l, w, c, alpha=ALPHA)
    log('shrink iter', it, 'accepted', int((w > 0.5).sum()), '/', len(sk_idx), 'max|d|/U', np.abs(d).max() / U)
    Vc[sk_idx] += d; D_total[sk_idx] += d
    shrink_w = np.zeros(nV); shrink_w[sk_idx] = w
    for n_ in Kc: Kc[n_][sk_idx] += d
# small outward offset in the overlap band so the new face sits on top of the character, border tucked under
off = P.get('offset', 0.03) * U
o_w = (wl.ramp(ring, 0, P.get('offset_rings', 3)) * 2 - 1) * off
N = vnormals(Vc, Fc); acc = prot * skin  # continuous: a hard prot>0.99 cut left a ~1.7 mm step around nose and mouth
dv = (o_w * acc)[:, None] * N
Vc += dv
for n_ in Kc: Kc[n_] += dv

_rim = np.nonzero(skin & (d_eye < 0.004))[0]
for _s in (1, -1):
    _m = _rim[np.sign(Vc[_rim, 0]) == _s]; log('lid rim shrink drift mm, side', _s, 'dx/dy/dz', np.round(D_total[_m].mean(0) * 1000, 2).tolist(), 'n', len(_m))
# ---------- eyes: rigid + uniform scale, follow socket displacement, keep lid clearance ----------
fwd = np.array([0, -1.0, 0])
eye_info = {}
for s, pid in (('L', 1), ('R', 2)):
    ei = np.nonzero(partc == pid)[0]
    se = wl.umeyama(R0c[ei], V1[used][ei])
    near = np.nonzero(skin & (dist_to(eye_pts[s], R0c) < 0.006))[0]
    shift = D_total[near].mean(0) + dv[near].mean(0)
    Vc[ei] = wl.apply_sim(se, R0c[ei]) + shift
    for n_ in Kc: Kc[n_][ei] = wl.apply_sim(se, K0[n_][used][ei]) + shift
    c0 = R0c[ei].mean(0); r0 = np.linalg.norm(R0c[ei] - c0, axis=1).mean()
    lid = np.nonzero(skin & (np.linalg.norm(R0c - c0, axis=1) < 1.6 * r0) & ((R0c - c0) @ fwd > 0))[0]
    def rel(Vlid, c, r): return np.linalg.norm(Vlid - c, axis=1) / r
    tests = ['eyeBlink' + ('Left' if s == 'L' else 'Right'), 'eyeWide' + ('Left' if s == 'L' else 'Right')]
    ref = np.stack([rel(R0c[lid], c0, r0)] + [rel(K0[t][used][lid], c0, r0) for t in tests])
    c1 = Vc[ei].mean(0); r1 = np.linalg.norm(Vc[ei] - c1, axis=1).mean()
    poses = [Vc[lid]] + [Kc[t][lid] for t in tests]
    sel = (ref > 0.85) & (ref < 1.6)
    pts = np.concatenate([X[m] for X, m in zip(poses, sel)]); rho = ref[sel]
    cf, rf = wl.fit_sphere_ratio(pts, rho, c1, r1)
    k = float(np.clip(rf / r1, P.get('eye_kmin', 0.75), 1.1))
    Vc[ei] = cf + k * (Vc[ei] - c1)
    for n_ in Kc: Kc[n_][ei] = cf + k * (Kc[n_][ei] - c1)
    fitres = float(np.abs(rel(pts, cf, rf) - rho).mean())
    # enforce: no lid-surface vertex deeper (relative) than in Rocketbox, in any tested pose
    back, shrink = P.get('eye_back0', 0.0) * r1, 1.0  # eye_back0: fixed push-back, in eye radii
    for _ in range(80):
        cc = cf - fwd * back; rr = r1 * k * shrink
        poses = [Vc[lid]] + [Kc[t][lid] for t in tests]
        viol = float((ref - np.stack([rel(X, cc, rr) for X in poses]))[sel].max())
        if viol <= P.get('eye_tol2', 0.02): break
        if back < P.get('eye_back_max', 0.35) * r1: back += 0.01 * r1
        elif shrink > 0.8: shrink -= 0.01
        else: break
    for X in [Vc] + list(Kc.values()): X[ei] = cc + shrink * (X[ei] - cf)
    k *= shrink; cf = cc
    eye_info[s] = {'center': cf.tolist(), 'radius': r1 * k, 'scale': se[0] * k, 'k': k, 'moved_r': float(np.linalg.norm(cf - c1) / r1),
                   'viol': viol, 'fit_res': fitres, 'n': int(sel.sum()), 'back_r': back / r1, 'shrink': shrink,
                   'open_c': warp(eye_pts[s]).mean(0).tolist()}
    log('eye', s, eye_info[s])

# ---------- shape keys: attenuate near the outer border ----------
att = np.where(skin, wl.ramp(ring, 0.5, P.get('att_rings', 3.5)), 1.0)
for n_ in Kc: Kc[n_] = Vc + (Kc[n_] - Vc) * att[:, None]

# ---------- diagnostic: signed distance of new face to the character's original surface ----------
sd = np.zeros(nV)
for vi in range(nV):
    loc, n, fi, dd = hbvh.find_nearest(Vector(Vc[vi]))
    if loc is not None: sd[vi] = np.dot(Vc[vi] - np.array(loc), np.array(n)) / U
osk = np.unique(Fc[mat_idx == 0])
fg = np.zeros(nV); Nn = vnormals(Vc, Fc)
for vi in osk:
    loc, n, fi, dd = hbvh.ray_cast(Vector(Vc[vi] + Nn[vi] * 1e-5), Vector(Nn[vi]), 0.6 * U)
    if loc is not None: fg[vi] = dd / U
log('front gap >0.03U', int((fg > 0.03).sum()), '>0.08U', int((fg > 0.08).sum()), '>0.15U', int((fg > 0.15).sum()), 'of', len(osk))
for th in (0.02, 0.05, 0.1):
    log('sdist |d|>%.2fU' % th, 'below', int((sd[osk] < -th).sum()), 'above', int((sd[osk] > th).sum()), 'of', len(osk))
# ---------- build FaceNew in character local space ----------
Minv = np.linalg.inv(HM)
to_local = lambda X: X @ Minv[:3, :3].T + Minv[:3, 3]
me = bpy.data.meshes.new(NM['obj']); me.from_pydata(to_local(Vc).tolist(), [], Fc.tolist()); me.update()
uv = me.uv_layers.new(name='UVMap')
for p, pi in zip(me.polygons, orig_face):
    for li, u in zip(p.loop_indices, poly_uv[int(pi)]): uv.data[li].uv = u
for p, mi in zip(me.polygons, mat_idx): p.use_smooth = True; p.material_index = int(mi)
FN = bpy.data.objects.new(NM['obj'], me); bpy.context.scene.collection.objects.link(FN)
FN.parent = arm; FN.matrix_parent_inverse = H.matrix_parent_inverse.copy(); FN.matrix_basis = H.matrix_basis.copy()
at = me.attributes.new('part', 'INT', 'POINT'); at.data.foreach_set('value', partc)
at = me.attributes.new('sdist', 'FLOAT', 'POINT'); at.data.foreach_set('value', sd)
at = me.attributes.new('fgap', 'FLOAT', 'POINT'); at.data.foreach_set('value', fg)
at = me.attributes.new('shrink_w', 'FLOAT', 'POINT'); at.data.foreach_set('value', shrink_w)
at = me.attributes.new('ring', 'FLOAT', 'POINT'); at.data.foreach_set('value', np.minimum(ring, 99).astype(float))
FN.shape_key_add(name='Basis', from_mix=False)
for n_ in keyn:
    k = FN.shape_key_add(name=n_, from_mix=False); k.data.foreach_set('co', to_local(Kc[n_]).ravel()); k.value = 0.0
mface = bpy.data.materials.new(NM['mat']); mface.use_nodes = True
minner = RB.data.materials[0]; minner.name = 'rb_inner'
me.materials.append(mface); me.materials.append(minner)
# copy the character texture into face material as placeholder (bake replaces it)
himg = [n.image for n in H.data.materials[0].node_tree.nodes if n.type == 'TEX_IMAGE' and n.image][0]
nt = mface.node_tree; tx = nt.nodes.new('ShaderNodeTexImage'); tx.image = himg
nt.links.new(tx.outputs[0], nt.nodes['Principled BSDF'].inputs['Base Color'])
FN['eyes'] = json.dumps(eye_info); FN['U'] = float(U)
log('face mats tris', np.bincount(mat_idx).tolist())

# ---------- cut the character under the face ----------
outer = mat_idx == 0
Fo = Fc[outer]
fbvh = BVHTree.FromPolygons(Vc.tolist(), Fo.tolist())
bd = dist_to(Vc[border], Vc)  # euclid distance to outer border
fbd = bd[Fo].min(1)
MARG = P.get('margin', 0.08) * U
rm_v = np.zeros(len(HW), bool)
hz = (HW[:, 2] > Vc[:, 2].min() - 0.5 * U) & (HW[:, 2] < Vc[:, 2].max() + 0.5 * U)
fN = vnormals(Vc, Fc)
for vi in np.nonzero(hz)[0]:
    loc, n, fi, dd = fbvh.find_nearest(Vector(HW[vi]))
    if loc is None: continue
    sgn = np.dot(HW[vi] - np.array(loc), np.array(n)) / U  # + = Hank vertex in front of new face
    inner = fbd[fi] > 3 * MARG
    far = P.get('cut_deep', 0.6) if inner else P.get('cut_far', 0.25)
    # well inside the mask the old surface may sit in front of the new face (unshrunk eye/nose areas): cut it too
    front = P.get('cut_front_inner', 0.06) if inner else P.get('cut_front', 0.06)
    if fbd[fi] > MARG and dd < far * U and sgn < front:
        rm_v[vi] = True
    # body skin poking out in front of the mask away from its border edge (not the tuck-under overlap): cut it too
    elif P.get('cut_poke_marg') is not None and fbd[fi] > P['cut_poke_marg'] * MARG and dd < far * U and sgn > 0:
        rm_v[vi] = True
hf_rm = [p.index for p in H.data.polygons if all(rm_v[v] for v in p.vertices)]
log('cut hank faces', len(hf_rm), 'of', len(H.data.polygons))
bm = bmesh.new(); bm.from_mesh(H.data); bm.faces.ensure_lookup_table()
bmesh.ops.delete(bm, geom=[bm.faces[i] for i in hf_rm], context='FACES'); bm.to_mesh(H.data); bm.free()
bpy.data.objects.remove(RB)
json.dump({'U': U, 'eyes': eye_info, 'sim_scale': sim[0], 'n_face_tris': len(Fc), 'cut': len(hf_rm)}, open(out_blend.replace('.blend', '_info.json'), 'w'), indent=1)
bpy.ops.wm.save_as_mainfile(filepath=out_blend)
log('saved', out_blend)
