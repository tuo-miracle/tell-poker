# Bake the character's colour (from the uncut character) onto the new face UVs with Cycles selected-to-active.
import bpy, sys, os, json
argv = sys.argv[sys.argv.index('--') + 1:]
name, fit_blend, glb, out_png, out_blend = argv[:5]
P = json.loads(argv[5]) if len(argv) > 5 else {}
sys.path.insert(0, os.path.dirname(__file__)); import wraplib as wl
NM = wl.char_names(name)
bpy.ops.wm.open_mainfile(filepath=fit_blend)
FN = bpy.data.objects[NM['obj']]; U = FN['U']
before = set(bpy.data.objects)
bpy.ops.import_scene.gltf(filepath=glb)
new = [o for o in bpy.data.objects if o not in before]
SRC = [o for o in new if o.type == 'MESH' and len(o.data.polygons) > 1000][0]
for o in new:
    if o.type == 'MESH' and o is not SRC: bpy.data.objects.remove(o)
# keep only source faces near the new face (so rays cannot pick up the hat brim)
import bmesh
from mathutils.bvhtree import BVHTree
dg = bpy.context.evaluated_depsgraph_get()
fbvh = BVHTree.FromObject(FN, dg)
mwS = SRC.matrix_world; mwF_inv = FN.matrix_world.inverted()
bm = bmesh.new(); bm.from_mesh(SRC.data)
far = []
for f in bm.faces:
    c = mwF_inv @ (mwS @ f.calc_center_median())
    loc, n, i, d = fbvh.find_nearest(c)
    if loc is None or d > P.get('src_near', 0.3) * U: far.append(f)
bmesh.ops.delete(bm, geom=far, context='FACES'); bm.to_mesh(SRC.data); bm.free()
print('SRC faces kept', len(SRC.data.polygons))
frame_faces = None
if P.get('exclude_json'):  # glasses frames (glasses_extract.py): which source faces they are, for a second (mask) bake
    from mathutils.kdtree import KDTree
    _gj = json.load(open(P['exclude_json'])); XP = _gj.get('paint_pts', _gj['frame_pts']); kx = KDTree(len(XP))
    for i_, p_ in enumerate(XP): kx.insert(p_, i_)
    kx.balance()
    frame_faces = [p_.index for p_ in SRC.data.polygons if any(kx.find(mwS @ SRC.data.vertices[v].co)[2] < 1e-4 for v in p_.vertices)]
    print('SRC frame faces', len(frame_faces))
    if 'outline' in _gj:  # zone round the old rims on the new face: frame paint the bake took from fused skin (not frame faces)
        import numpy as np
        rp = np.vstack([np.array(o) for o in _gj['outline'].values()]); mwF = FN.matrix_world; uvl_ = FN.data.uv_layers.active.data
        Vf = np.array([tuple(mwF @ v.co) for v in FN.data.vertices]); near = np.min(np.linalg.norm(Vf[:, None] - rp[None], axis=2), 1) < P.get('rim_zone', 0.005)
        zone = [[tuple(uvl_[li].uv) for li in p_.loop_indices] for p_ in FN.data.polygons if p_.material_index == 0 and near[list(p_.vertices)].any()]
        json.dump(zone, open(out_png.replace('.png', '_rimzone.json'), 'w')); print('SRC rim zone faces', len(zone))
if P.get('src_tex'):
    img = bpy.data.images.load(P['src_tex'])
    for n in SRC.data.materials[0].node_tree.nodes:
        if n.type == 'TEX_IMAGE': n.image = img
for k in FN.data.shape_keys.key_blocks[1:]: k.value = 0
sc = bpy.context.scene; sc.render.engine = 'CYCLES'; sc.cycles.samples = 1; sc.cycles.device = 'CPU'
R = P.get('res', 2048)
bimg = bpy.data.images.new(NM['img'], R, R, alpha=False)
m0 = FN.data.materials[0]; nt = m0.node_tree
for n in list(nt.nodes):
    if n.type == 'TEX_IMAGE': nt.nodes.remove(n)
tx = nt.nodes.new('ShaderNodeTexImage'); tx.image = bimg; nt.nodes.active = tx
dummy = bpy.data.images.new('dummy', 8, 8)
m1 = FN.data.materials[1]; d = m1.node_tree.nodes.new('ShaderNodeTexImage'); d.image = dummy; m1.node_tree.nodes.active = d
bpy.ops.object.select_all(action='DESELECT'); SRC.select_set(True); FN.select_set(True); bpy.context.view_layer.objects.active = FN
bpy.ops.object.bake(type='DIFFUSE', pass_filter={'COLOR'}, use_selected_to_active=True,
                    cage_extrusion=P.get('extrusion', 0.08) * U, max_ray_distance=P.get('maxray', 0.3) * U, margin=16, use_clear=True)
bimg.filepath_raw = out_png; bimg.file_format = 'PNG'; bimg.save()
if frame_faces is not None:  # same bake with the frame faces green and the rest black: exactly the texels taken from the frames
    def flatm(nm, rgb):
        m_ = bpy.data.materials.new(nm); m_.use_nodes = True; m_.node_tree.nodes['Principled BSDF'].inputs['Base Color'].default_value = (*rgb, 1); return m_
    SRC.data.materials.clear(); SRC.data.materials.append(flatm('id_skin', (0, 0, 0))); SRC.data.materials.append(flatm('id_frame', (0, 1, 0)))
    ff = set(frame_faces)
    for p_ in SRC.data.polygons: p_.material_index = 1 if p_.index in ff else 0
    mimg = bpy.data.images.new('framemask', R, R, alpha=False); tx.image = mimg
    bpy.ops.object.bake(type='DIFFUSE', pass_filter={'COLOR'}, use_selected_to_active=True,
                        cage_extrusion=P.get('extrusion', 0.08) * U, max_ray_distance=P.get('maxray', 0.3) * U, margin=16, use_clear=True)
    mimg.filepath_raw = out_png.replace('.png', '_framemask.png'); mimg.file_format = 'PNG'; mimg.save()
    tx.image = bimg; bpy.data.images.remove(mimg)
# lid faces sitting inside the character's painted eye opening (neutral): export their UV triangles for inpainting
if P.get('lm') and P.get('cam'):
    import numpy as np
    L = json.load(open(P['lm'])); c = json.load(open(P['cam'])); s_, W_ = c['s'], c['W']
    EYES = [[263, 249, 390, 373, 374, 380, 381, 382, 362, 398, 384, 385, 386, 387, 388, 466],
            [33, 7, 163, 144, 145, 153, 154, 155, 133, 173, 157, 158, 159, 160, 161, 246]]
    polys = []
    for ids in EYES:
        q = np.array([[c['cx'] + (L[i][0] / W_ - 0.5) * s_, c['cz'] - (L[i][1] / W_ - 0.5) * s_] for i in ids])
        ctr = q.mean(0); polys.append(ctr + (q - ctr) * P.get('eye_poly_scale', 1.15))
    sys.path.insert(0, os.path.dirname(__file__)); import wraplib as wl
    mw = FN.matrix_world; uvl = FN.data.uv_layers.active.data; tris_uv = []
    Vw = np.array([tuple(mw @ v.co) for v in FN.data.vertices])
    part = np.empty(len(Vw), int); FN.data.attributes['part'].data.foreach_get('value', part)
    from mathutils import Vector as _V
    F_ = [list(p_.vertices) for p_ in FN.data.polygons]
    def pose(vals):
        kb = FN.data.shape_keys.key_blocks; X = np.array([tuple(mw @ v.co) for v in kb[0].data])
        for n_, w_ in vals.items():
            K = np.array([tuple(mw @ v.co) for v in kb[n_].data]); X = X + w_ * (K - X if False else K - np.array([tuple(mw @ v.co) for v in kb[0].data]))
        return X
    def visible(X):
        b = BVHTree.FromPolygons(X.tolist(), F_); vis = np.zeros(len(F_), bool)
        for fi, f in enumerate(F_):
            c_ = X[f].mean(0)
            for dirv in ((0, 1, 0), (0, 0.8, -0.6)):  # front and slightly from above
                d_ = np.array(dirv) / np.linalg.norm(dirv)
                loc, n, hi, dd = b.ray_cast(_V(c_ - d_ * 0.5), _V(d_), 1.0)
                if hi == fi or (loc is not None and np.linalg.norm(np.array(loc) - c_) < 1e-5): vis[fi] = True
        return vis
    XB = pose({'eyeBlinkLeft': 1, 'eyeBlinkRight': 1}); vN = visible(Vw); vB = visible(XB)
    r_eye = np.mean([e['radius'] for e in json.loads(FN['eyes']).values()])
    for fi, p_ in enumerate(FN.data.polygons):
        if p_.material_index != 0: continue
        xz = Vw[F_[fi]][:, [0, 2]]
        inpoly = any(wl.inside_polygon(xz, q).all() for q in polys)
        mv = np.linalg.norm(XB[F_[fi]].mean(0) - Vw[F_[fi]].mean(0))
        if (vB[fi] and not vN[fi]) or inpoly or mv > P.get('lid_move', 0.4) * r_eye:
            tris_uv.append([tuple(uvl[li].uv) for li in p_.loop_indices])
    json.dump(tris_uv, open(out_png.replace('.png', '_lidmask.json'), 'w')); print('LIDMASK faces', len(tris_uv))
m1.node_tree.nodes.remove(d); bpy.data.images.remove(dummy)
nt.links.new(tx.outputs[0], nt.nodes['Principled BSDF'].inputs['Base Color'])
bimg.pack()
arm2 = SRC.parent; bpy.data.objects.remove(SRC)
if arm2 and arm2.type == 'ARMATURE': bpy.data.objects.remove(arm2)
for o in list(bpy.data.objects):
    if o.type == 'EMPTY' and not o.children: bpy.data.objects.remove(o)
bpy.ops.wm.save_as_mainfile(filepath=out_blend)
print('BAKED', out_png)
