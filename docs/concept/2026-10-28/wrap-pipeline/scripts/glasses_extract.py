# Find Tripo glasses frames fused into the face and describe them for the wrap:
#  - frame_pts: frame vertices (plus `dilate` rings) that fit.py leaves out of its target surface (target_exclude_json),
#    so the template face is not pulled onto the frames; paint_pts: those plus the bridge found by colour on the nose
#    (bridge_box), whose baked texels bake.py marks for inpainting;
#  - outline/temple: rim centrelines round each pupil and temple lines, for add_glasses.py to rebuild clean frames.
# Frames: a ray from a frame vertex toward the head's vertical axis meets the mesh again within occ_max (the other side of
# the tube or the skin behind it); from skin it runs inside the head and meets the back of the skull much farther.
# Seeds inside `box`, grown through such vertices inside grow_box (keep the ears out: they are thin too).
# usage: blender -b -P glasses_extract.py -- char.glb char_front_lm.json char_front.json out.json '{params}'
import bpy, bmesh, sys, os, json, numpy as np
from mathutils.bvhtree import BVHTree
sys.path.insert(0, os.path.dirname(__file__)); import acclib as al
argv = sys.argv[sys.argv.index('--') + 1:]
src, lm_path, cam_path, out = argv[:4]; P = json.loads(argv[4]) if len(argv) > 4 else {}
log = lambda *a: print('GLASSES', *a, flush=True)
box = P.get('box', [[-0.065, 0.065], [-1, 0.0], [0.90, 0.94]]); gbox = P.get('grow_box', box)
bpy.ops.wm.read_factory_settings(use_empty=True); bpy.ops.import_scene.gltf(filepath=src)
H = max((o for o in bpy.context.scene.objects if o.type == 'MESH'), key=lambda o: len(o.data.polygons))
bm = bmesh.new(); bm.from_mesh(H.data); bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=1e-5); bm.to_mesh(H.data); bm.free()
me = H.data; M = H.matrix_world
V = np.array([tuple(M @ v.co) for v in me.vertices]); F = np.array([tuple(p.vertices) for p in me.polygons])
bvh = BVHTree.FromPolygons(V.tolist(), F.tolist())
inb = al.thin_seeds(np.zeros(len(V)), V, 1.0, gbox)
occ = np.zeros(len(V), bool)
for i in np.nonzero(inb)[0]:
    d = np.array([-V[i, 0], P.get('axis_y', 0.0) - V[i, 1], 0.0]); d /= np.linalg.norm(d) + 1e-12
    if bvh.ray_cast(tuple(V[i] + d * 1e-5), tuple(d), P.get('occ_max', 0.03))[0] is not None: occ[i] = True
seed = occ & al.thin_seeds(np.zeros(len(V)), V, 1.0, box)
reg = al.grow_region(seed, F, inb & occ)
if P.get('bridge_box'):  # the bridge lies on the nose (no gap behind it): take its painted silver by colour instead
    img = [n.image for n in me.materials[0].node_tree.nodes if n.type == 'TEX_IMAGE'][0]
    Wi, Hi = img.size; px = np.array(img.pixels[:]).reshape(Hi, Wi, -1)[..., :3]; uv = me.uv_layers.active.data
    col = np.zeros((len(V), 3)); cnt = np.zeros(len(V))
    for l in me.loops:
        u, v = uv[l.index].uv; col[l.vertex_index] += px[min(int(v * Hi), Hi - 1), min(int(u * Wi), Wi - 1)]; cnt[l.vertex_index] += 1
    col /= np.maximum(cnt, 1)[:, None]; mx, mn = col.max(1), col.min(1); sat = (mx - mn) / np.maximum(mx, 1e-6)
    br = al.thin_seeds(np.zeros(len(V)), V, 1.0, P['bridge_box']) & (sat < P.get('bridge_sat', 0.2)) & (mx > P.get('bridge_val', 0.5))
    log('bridge verts by colour', int(br.sum()))
else: br = np.zeros(len(V), bool)
ex = reg.copy()
E = np.concatenate([F[:, [0, 1]], F[:, [1, 2]], F[:, [2, 0]]])
for _ in range(P.get('dilate', 1)):
    n = ex.copy(); np.logical_or.at(n, E[:, 0], ex[E[:, 1]]); np.logical_or.at(n, E[:, 1], ex[E[:, 0]]); ex = n & inb
log('verts in box', int(inb.sum()), 'occluders', int(occ.sum()), 'frame', int(reg.sum()), 'excluded (dilated)', int(ex.sum()))
L = json.load(open(lm_path)); c = json.load(open(cam_path))
pupil = {s: (c['cx'] + (L[i][0] / c['W'] - 0.5) * c['s'], c['cz'] - (L[i][1] / c['W'] - 0.5) * c['s']) for s, i in (('R', 468), ('L', 473))}
res = {'frame_pts': V[ex].tolist(), 'paint_pts': V[ex | br].tolist(), 'outline': {}, 'temple': {}, 'pupil': pupil}  # paint: + bridge (bake only)
front_y = np.median(V[reg][np.abs(V[reg, 0] - 0) < 0.04, 1])
for s, sg in (('R', -1), ('L', 1)):
    side = reg & (np.sign(V[:, 0]) == sg)
    rim = side & (V[:, 1] < front_y + P.get('rim_depth', 0.012))
    O = al.lens_outline(V[rim], P.get('nbins', 24), center=pupil[s], order=P.get('order', 3), plane=P.get('plane', True))
    res['outline'][s] = O.tolist()
    hinge = O[np.argmax(sg * O[:, 0])]
    tp = V[side & (V[:, 1] >= front_y + P.get('rim_depth', 0.012))]
    end_y = P.get('temple_end_y', 0.012)
    if len(tp) >= 3:  # temple direction from its own points
        dvec = np.polyfit(tp[:, 1], tp[:, [0, 2]], 1)[0]; dx, dz = dvec
    else: dx, dz = 0.0, 0.0
    t = end_y - hinge[1]
    res['temple'][s] = [hinge.tolist(), [hinge[0] + dx * t, end_y, hinge[2] + dz * t]]
    log('side', s, 'rim pts', int(rim.sum()), 'temple pts', len(tp), 'outline x', O[:, 0].min().round(4), O[:, 0].max().round(4),
        'z', O[:, 2].min().round(4), O[:, 2].max().round(4), 'y', O[:, 1].min().round(4), O[:, 1].max().round(4))
json.dump(res, open(out, 'w'))
log('saved', out)
