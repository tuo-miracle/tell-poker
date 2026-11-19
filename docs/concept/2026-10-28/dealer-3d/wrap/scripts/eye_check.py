# Eye/lid checks on an exported GLB, eyes driven by the eye bones only (Damped Track on bone Y, like a Unity look-at).
#  - visible iris / pupil fraction when looking at a point 1 m ahead (level, 15 deg up, 15 deg down), seen from that point
#  - blink closure: rays from the front onto the closed eye must not reach the eyeball
#  - eyeball poke-through: eye vertices lying outside the lid skin, over gaze yaw +-25 x pitch +-20 and lid poses
# usage: blender -b -P eye_check.py -- in.glb out.json [render_prefix] [eye_tex_info.json]
import bpy, sys, os, json, math, numpy as np
from mathutils import Vector
from mathutils.bvhtree import BVHTree
sys.path.insert(0, os.path.dirname(__file__)); import common
argv = sys.argv[sys.argv.index('--') + 1:]; glb, out = argv[:2]; pre = argv[2] if len(argv) > 2 and argv[2] else None
PUPIL_PX, R_IRIS, R_PUPIL, TEX = (539.75, 1904.98), 36.0, 16.0, 2048  # Hank (Male_Adult_03) defaults
if len(argv) > 3:  # eye_tex.py info json for another template
    _e = json.load(open(argv[3])); PUPIL_PX, R_IRIS, R_PUPIL, TEX = tuple(_e['pupil_px']), _e['r_iris'], _e['r_pupil'], _e['tex']
bpy.ops.wm.read_factory_settings(use_empty=True); bpy.ops.import_scene.gltf(filepath=glb)
for o in list(bpy.data.objects):
    if o.name.startswith('Icosphere'): bpy.data.objects.remove(o)
arm = [o for o in bpy.data.objects if o.type == 'ARMATURE'][0]; pb = arm.pose.bones
FN = [o for o in bpy.data.objects if o.type == 'MESH' and o.data.shape_keys][0]
H = [o for o in bpy.data.objects if o.type == 'MESH' and o is not FN][0]
BN = {'L': 'mixamorig:LeftEye', 'R': 'mixamorig:RightEye'}
C = {s: arm.matrix_world @ arm.data.bones[n].head_local for s, n in BN.items()}
ec = (C['L'] + C['R']) / 2
eye_mat = [i for i, m in enumerate(FN.data.materials) if m.name.startswith('rb_eye')][0]
gi = {g.name: g.index for g in FN.vertex_groups}
side_of = {}
for v in FN.data.vertices:
    for g in v.groups:
        for s, n in BN.items():
            if g.group == gi[n] and g.weight > 0.5: side_of[v.index] = s
tgt = bpy.data.objects.new('target', None); bpy.context.scene.collection.objects.link(tgt)
for n in BN.values():
    c = pb[n].constraints.new('DAMPED_TRACK'); c.target = tgt; c.track_axis = 'TRACK_Y'

def pose(keys=None, yaw=0.0, pitch=0.0, dist=1.0):
    for k in FN.data.shape_keys.key_blocks[1:]: k.value = (keys or {}).get(k.name, 0.0)
    d = Vector((math.sin(math.radians(yaw)) * math.cos(math.radians(pitch)), -math.cos(math.radians(yaw)) * math.cos(math.radians(pitch)), math.sin(math.radians(pitch))))
    tgt.location = ec + d * dist; bpy.context.view_layer.update()

def scene_bvh():
    dg = bpy.context.evaluated_depsgraph_get(); tris, mats, uvs, V = [], [], [], []
    e = FN.evaluated_get(dg); m = e.to_mesh(); m.calc_loop_triangles(); mw = FN.matrix_world
    X = [mw @ v.co for v in m.vertices]; uvl = m.uv_layers.active.data
    for t in m.loop_triangles:
        tris.append(list(t.vertices)); mats.append(t.material_index); uvs.append([tuple(uvl[li].uv) for li in t.loops])
    eyev = {i: side_of[i] for i in side_of}; Xn = np.array([tuple(x) for x in X]); e.to_mesh_clear()
    he = H.evaluated_get(dg); hm = he.to_mesh(); hm.calc_loop_triangles(); off = len(X)
    X += [H.matrix_world @ v.co for v in hm.vertices]
    for t in hm.loop_triangles: tris.append([i + off for i in t.vertices]); mats.append(-1); uvs.append(None)
    he.to_mesh_clear()
    return BVHTree.FromPolygons(X, tris), np.array(mats), uvs, [np.array([tuple(X[i]) for i in t]) for t in tris], Xn, eyev

def iris_vis(s, from_pt, bvh, mats, uvs, tv):
    """Fraction of the iris / pupil disc visible from from_pt (rays aimed over a grid around the eye centre)."""
    c = C[s]; r = 0.0075; fwd = (c - from_pt).normalized(); x = fwd.cross(Vector((0, 0, 1))).normalized(); y = x.cross(fwd)
    n_ir = n_pu = 0; vis_ir = vis_pu = 0
    eb = BVHTree.FromPolygons([Vector(p) for t in tv for p in t], [[3 * i, 3 * i + 1, 3 * i + 2] for i in range(len(tv)) if mats[i] == eye_mat])
    eidx = [i for i in range(len(tv)) if mats[i] == eye_mat]
    for a in np.linspace(-r, r, 61):
        for b in np.linspace(-r, r, 61):
            p = c + x * a + y * b; d = (p - from_pt).normalized()
            loc, nrm, fi, dd = eb.ray_cast(from_pt, d)
            if fi is None: continue
            fi = eidx[fi]; t = tv[fi]; bc = _bary(np.array(loc), t); u = np.array(uvs[fi]); uv = bc @ u
            rpx = math.hypot(uv[0] * TEX - PUPIL_PX[0], (1 - uv[1]) * TEX - PUPIL_PX[1])
            if rpx > R_IRIS: continue
            loc2, n2, fi2, d2 = bvh.ray_cast(from_pt, d)
            seen = fi2 is not None and mats[fi2] == eye_mat and abs(d2 - dd) < 1e-5
            n_ir += 1; vis_ir += seen
            if rpx <= R_PUPIL: n_pu += 1; vis_pu += seen
    return round(vis_ir / max(n_ir, 1), 3), round(vis_pu / max(n_pu, 1), 3)

def _bary(p, t):
    v0, v1, v2 = t[1] - t[0], t[2] - t[0], p - t[0]
    d00, d01, d11, d20, d21 = v0 @ v0, v0 @ v1, v1 @ v1, v2 @ v0, v2 @ v1; den = d00 * d11 - d01 * d01
    b1 = (d11 * d20 - d01 * d21) / den; b2 = (d00 * d21 - d01 * d20) / den; return np.array([1 - b1 - b2, b1, b2])

def eye_exposed(bvh, mats, dirs):
    """Rays from in front of each eye (several directions) that reach the eyeball first."""
    hits = 0
    for s in C:
        for dv in dirs:
            d = Vector(dv).normalized()
            for a in np.linspace(-0.009, 0.009, 31):
                for b in np.linspace(-0.009, 0.009, 31):
                    o = C[s] + Vector((a, 0, b)) - d * 0.1
                    loc, n, fi, dd = bvh.ray_cast(o, d, 0.2)
                    if fi is not None and mats[fi] == eye_mat: hits += 1
    return hits

def poke(bvh, mats, Xn, eyev):
    """Eye vertices outside the lids: the ray from the eye centre to the vertex crosses face skin (material 0) in the
    outer 30 % of that ray. (Hank's old head surface and the mouth bag sit inside the eye and are hidden; ignored.)
    Returns (count deeper than 0.1 mm, max depth in mm)."""
    n = 0; worst = 0.0
    for i, s in eyev.items():
        c = C[s]; v = Vector(Xn[i]); d = v - c; L = d.length
        if L < 1e-6: continue
        u = d.normalized(); o = c; left = L - 1e-5
        loc, nr, fi, dd = bvh.ray_cast(o, u, left)
        while fi is not None and (mats[fi] != 0 or (loc - c).length < 0.7 * L):
            o = loc + u * 1e-5; left = L - 1e-5 - (o - c).length
            if left <= 0: fi = None; break
            loc, nr, fi, dd = bvh.ray_cast(o, u, left)
        if fi is not None:
            depth = (L - (loc - c).length) * 1000; worst = max(worst, depth); n += depth > 0.1
    return n, round(worst, 3)

R = {'look': {}, 'look_key': {}, 'blink_exposed_rays': {}, 'poke': {}}
for w_, pitch in ((0.5, -13), (1.0, -26)):  # eyeLookDown key together with the matching gaze, as Unity drives them
    pose({'eyeLookDownLeft': w_, 'eyeLookDownRight': w_}, pitch=pitch); bvh, mats, uvs, tv, Xn, eyev = scene_bvh()
    R['look_key']['down%d_w%.1f' % (-pitch, w_)] = {s: dict(zip(('iris', 'pupil'), iris_vis(s, tgt.location.copy(), bvh, mats, uvs, tv))) for s in C}
for nm, pitch in (('level', 0), ('up15', 15), ('down15', -15)):
    pose(pitch=pitch); bvh, mats, uvs, tv, Xn, eyev = scene_bvh()
    R['look'][nm] = {s: dict(zip(('iris', 'pupil'), iris_vis(s, tgt.location.copy(), bvh, mats, uvs, tv))) for s in C}
FRONT = [(0, 1, 0), (0.3, 1, 0), (-0.3, 1, 0), (0, 1, 0.3), (0, 1, -0.3)]
for nm, k in (('blink', {'eyeBlinkLeft': 1, 'eyeBlinkRight': 1}), ('blink_squint', {'eyeBlinkLeft': 1, 'eyeBlinkRight': 1, 'eyeSquintLeft': 1, 'eyeSquintRight': 1}),
              ('blink_lookdown', {'eyeBlinkLeft': 1, 'eyeBlinkRight': 1, 'eyeLookDownLeft': 1, 'eyeLookDownRight': 1})):
    pose(k); bvh, mats, *_ = scene_bvh(); R['blink_exposed_rays'][nm] = eye_exposed(bvh, mats, FRONT)
LIDS = {'neutral': {}, 'blink': {'eyeBlinkLeft': 1, 'eyeBlinkRight': 1}, 'halfblink': {'eyeBlinkLeft': 0.5, 'eyeBlinkRight': 0.5},
        'squint': {'eyeSquintLeft': 1, 'eyeSquintRight': 1}, 'wide': {'eyeWideLeft': 1, 'eyeWideRight': 1},
        'lookdown': {'eyeLookDownLeft': 1, 'eyeLookDownRight': 1}}
for ln, k in LIDS.items():
    worst = (0, 0.0)
    for yaw in (-25, 0, 25):
        for pitch in (-20, 0, 20):
            pose(k, yaw, pitch); bvh, mats, uvs, tv, Xn, eyev = scene_bvh(); p_ = poke(bvh, mats, Xn, eyev)
            worst = (max(worst[0], p_[0]), max(worst[1], p_[1]))
    R['poke'][ln] = {'n_over_0.1mm': worst[0], 'max_mm': worst[1]}
json.dump(R, open(out, 'w'), indent=1); print('EYECHECK', json.dumps(R))
if pre:
    sc = common.setup_scene(1024, 0.45)
    def shot(name, keys=None, pitch=0.0, size=0.06, persp=None, yaw=0.0):
        pose(keys, yaw, pitch)
        for o in [o for o in sc.objects if o.type == 'CAMERA']: bpy.data.objects.remove(o)
        if persp: common.cam_persp(ec + Vector((0, 0, -0.022)), (0, 1, 0), 1.0, persp)
        else: common.cam_ortho(ec + Vector((0, 0, 0.004)), size, (0, 1, 0), 3.0)
        common.render(f'{pre}_{name}.png')
    shot('look_level'); shot('look_up15', pitch=15); shot('look_down15', pitch=-15); shot('look_level_1m', persp=22)
    shot('blink', LIDS['blink']); shot('blink_1m', LIDS['blink'], persp=22); shot('squint', LIDS['squint']); shot('wide', LIDS['wide'])
    shot('look_yaw25_pitch20', None, 20, yaw=25); shot('look_yaw-25_pitch-20', None, -20, yaw=-25)
