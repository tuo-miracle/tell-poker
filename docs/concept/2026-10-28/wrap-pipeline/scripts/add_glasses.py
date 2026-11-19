# Rebuild clean glasses on a rigged wrap (rig.blend from rig.py) from glasses_extract.py's rim/temple lines, keep them clear
# of the new face in every tested expression, skin them to the Head bone, and re-export the GLB (same options as rig.py).
# usage: blender -b -P add_glasses.py -- NAME rig.blend glasses.json out.glb '{params}'
#   params: tube_r (rim radius, GLB units), temple_r, nseg, margin (min gap to the face), symmetric, shape 'oval' (+ nbins, exp), color [r,g,b], metallic,
#           roughness, drop_body_frame (true / 'any': delete body faces left on the Tripo frame points)
import bpy, bmesh, sys, os, json, numpy as np
from mathutils import Vector
from mathutils.bvhtree import BVHTree
sys.path.insert(0, os.path.dirname(__file__)); import acclib as al, wraplib as wl
argv = sys.argv[sys.argv.index('--') + 1:]
name, rig_blend, gj, out_glb = argv[:4]; P = json.loads(argv[4]) if len(argv) > 4 else {}
log = lambda *a: print('GLASSES', *a, flush=True)
NM = wl.char_names(name)
bpy.ops.wm.open_mainfile(filepath=rig_blend)
FN = bpy.data.objects[NM['obj']]; arm = FN.parent
H = [o for o in bpy.data.objects if o.type == 'MESH' and o is not FN and len(o.data.polygons) > 1000][0]
G = json.load(open(gj))
R, L = np.array(G['outline']['R']), np.array(G['outline']['L'])
if P.get('symmetric', True): R, L = al.mirror_average(R, L)
if P.get('shape') == 'oval':  # clean superellipse rims over the extracted extent (Tripo rims are lumpy where fused)
    R, L = (al.oval_outline(X, P.get('nbins', 32), P.get('exp', 2.5)) for X in (R, L))
    if P.get('symmetric', True): R, L = al.mirror_average(R, L)
TR, TL = np.array(G['temple']['R']), np.array(G['temple']['L'])
if P.get('symmetric', True):
    t = (TR * [-1, 1, 1] + TL) / 2; TL, TR = t, t * [-1, 1, 1]
    TR[0], TL[0] = R[np.argmin(R[:, 0])], L[np.argmax(L[:, 0])]  # temples start at the rims' outer points
# bridge: from the right rim's inner-top point to the left's, bowed up a little
n = len(R); top_in = lambda O, sg: O[np.argmax(sg * O[:, 0] + 0.6 * O[:, 2])]
b0, b1 = top_in(R, 1), top_in(L, -1); mid = (b0 + b1) / 2 + [0, -P.get('bridge_fwd', 0.001), P.get('bridge_up', 0.0015)]
t_ = np.linspace(0, 1, 9)[:, None]  # quadratic arc b0 -> mid -> b1
bridge = (1 - t_) ** 2 * b0 + 2 * t_ * (1 - t_) * (2 * mid - (b0 + b1) / 2) + t_ ** 2 * b1

# face surface in each tested expression (shape keys only; the eyes are bones and sit behind the lids)
mw = np.array(FN.matrix_world)
def face_world(keys):
    kb = FN.data.shape_keys.key_blocks; base = np.array([tuple(v.co) for v in kb[0].data])
    co = base.copy()
    for k, w in keys.items(): co += w * (np.array([tuple(v.co) for v in kb[k].data]) - base)
    return co @ mw[:3, :3].T + mw[:3, 3]
POSES = {'neutral': {}, 'wide': {'eyeWideLeft': 1, 'eyeWideRight': 1}, 'brows_up': {'browInnerUp': 1, 'browOuterUpLeft': 1, 'browOuterUpRight': 1},
         'smile_squint': {'mouthSmileLeft': 1, 'mouthSmileRight': 1, 'cheekSquintLeft': 1, 'cheekSquintRight': 1}}
FF = [tuple(p.vertices) for p in FN.data.polygons if p.material_index == 0]
r = P.get('tube_r', 0.0007); margin = P.get('margin', 0.0008)
BV = {nm: BVHTree.FromPolygons(face_world(keys).tolist(), FF) for nm, keys in POSES.items()}
def gaps(X, with_normals=False):
    """Per point: smallest gap (tube surface to face, + = in front) over the tested expressions; inf if no face nearby."""
    g = np.full(len(X), np.inf); N = np.zeros((len(X), 3))
    for bvh in BV.values():
        for i, p in enumerate(X):
            loc, nn, fi, d = bvh.find_nearest(Vector(p))
            if loc is not None and d < 0.02:
                gi = float(np.dot(p - np.array(loc), np.array(nn))) - r
                if gi < g[i]: g[i] = gi; N[i] = nn
    return (g, N) if with_normals else g
def relax_rim(O, floor, iters=4):
    """Where a rim still touches the face (a nose flank the forward shift cannot clear), bend it out along the face
    normal and spread the bend over its neighbours."""
    O = O.copy()
    for _ in range(iters):
        g, N = gaps(O, True); d = np.maximum(0, floor - g)
        if not d.any(): break
        D = d[:, None] * N
        for _ in range(2): D = (np.roll(D, 1, 0) + 2 * D + np.roll(D, -1, 0)) / 4
        O = O + D * 1.5
    return O
rims = np.vstack([R, L]); g0 = gaps(rims)
w = np.argsort(g0)[:4]; log('rim worst gaps mm', (g0[w] * 1000).round(2).tolist(), 'at', rims[w].round(4).tolist())
shift = al.clearance_shift(g0, margin)  # the rims move as one rigid front
R, L, bridge = (X + [0, -shift, 0] for X in (R, L, bridge)); TR[0] += [0, -shift, 0]; TL[0] += [0, -shift, 0]
push = np.zeros(len(bridge))
for _ in range(4):  # bridge arches forward over the nose, ends stay on the rims (repeat: the nearest face point moves)
    p_ = np.maximum(0, margin - gaps(bridge)); p_[[0, -1]] = 0; push += p_; bridge = bridge + p_[:, None] * [0, -1, 0]
R, L = relax_rim(R, P.get('touch_floor', 0.0003)), relax_rim(L, P.get('touch_floor', 0.0003))
g1 = np.r_[gaps(np.vstack([R, L])), gaps(bridge)]
_a = np.vstack([R, L, bridge]); _w = np.argsort(g1)[:3]; log('after worst gaps mm', (g1[_w] * 1000).round(2).tolist(), 'at', _a[_w].round(4).tolist(), 'n rim', len(R) + len(L))
log('rim shift fwd mm', round(shift * 1000, 2), 'bridge push mm', (push * 1000).round(2).tolist(), 'min gap after mm', round(float(g1.min()) * 1000, 2))
parts = [al.tube(R, r, P.get('nseg', 8)), al.tube(L, r, P.get('nseg', 8)), al.tube(bridge, r * 0.8, P.get('nseg', 8), closed=False)]
tr = P.get('temple_r', r * 0.9)
for T in (TR, TL):
    parts.append(al.tube(np.linspace(T[0], T[1], 6), tr, P.get('nseg', 8), closed=False))
V, F, o = [], [], 0
for v, f in parts: V.append(v); F.append(f + o); o += len(v)
V, F = np.vstack(V), np.vstack(F)
inv = np.linalg.inv(np.array(H.matrix_world)); Vl = V @ inv[:3, :3].T + inv[:3, 3]
me = bpy.data.meshes.new('Glasses'); me.from_pydata(Vl.tolist(), [], F.tolist()); me.update()
bm = bmesh.new(); bm.from_mesh(me); bmesh.ops.recalc_face_normals(bm, faces=bm.faces); bm.to_mesh(me); bm.free()
for p in me.polygons: p.use_smooth = True
GL = bpy.data.objects.new('Glasses', me); bpy.context.scene.collection.objects.link(GL)
GL.parent = arm; GL.matrix_parent_inverse = H.matrix_parent_inverse.copy(); GL.matrix_basis = H.matrix_basis.copy()
GL.vertex_groups.new(name='mixamorig:Head').add(list(range(len(V))), 1.0, 'REPLACE')
GL.modifiers.new('Armature', 'ARMATURE').object = arm
m = bpy.data.materials.new(name + '_glasses'); m.use_nodes = True; b = m.node_tree.nodes['Principled BSDF']
b.inputs['Base Color'].default_value = (*P.get('color', [0.72, 0.66, 0.55]), 1)
b.inputs['Metallic'].default_value = P.get('metallic', 1.0); b.inputs['Roughness'].default_value = P.get('roughness', 0.35)
me.materials.append(m)
log('glasses tris', len(F), 'tube r mm(GLB)', round(r * 1000, 2))
if P.get('drop_body_frame'):
    from mathutils.kdtree import KDTree
    HWl = [H.matrix_world @ v.co for v in H.data.vertices]; kd = KDTree(len(HWl))
    for i, p in enumerate(HWl): kd.insert(p, i)
    kd.balance(); ex = np.zeros(len(HWl), bool)
    db = P.get('drop_box', [[-1, 1], [-1, 1], [-1, 2]])  # e.g. only the front, so no holes open at the temples
    for p in G['frame_pts']:
        c, i, d = kd.find(p)
        if d < 1e-4 and all(db[k][0] <= p[k] <= db[k][1] for k in range(3)): ex[i] = True
    bm = bmesh.new(); bm.from_mesh(H.data); bm.faces.ensure_lookup_table()
    test = any if P['drop_body_frame'] == 'any' else all  # 'any': also the faces that only touch the frame
    dl = [f for f in bm.faces if test(ex[v.index] for v in f.verts)]
    bmesh.ops.delete(bm, geom=dl, context='FACES'); bm.to_mesh(H.data); bm.free(); log('body frame faces dropped', len(dl))
json.dump({'rim_gap_before_min_mm': float(g0.min() * 1000), 'rim_shift_mm': shift * 1000, 'bridge_push_mm': (push * 1000).tolist(),
           'gap_after_min_mm': float(g1.min() * 1000), 'poses': list(POSES), 'tris': len(F)}, open(out_glb.replace('.glb', '_glasses.json'), 'w'), indent=1)
bpy.ops.object.select_all(action='DESELECT')
for ob in (arm, H, FN, GL): ob.select_set(True)
bpy.context.view_layer.objects.active = arm
bpy.ops.export_scene.gltf(filepath=out_glb, export_format='GLB', use_selection=True, export_image_format='JPEG', export_jpeg_quality=90,
                          export_skins=True, export_morph=True, export_morph_normal=True, export_animations=False, export_apply=False)
log('exported', out_glb, os.path.getsize(out_glb))
