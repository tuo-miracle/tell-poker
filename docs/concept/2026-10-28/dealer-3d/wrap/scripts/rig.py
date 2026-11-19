# Skin the new face to the character's armature, add eye bones, export GLB.
import bpy, sys, os, json, numpy as np
from mathutils import Vector, Matrix
from mathutils.kdtree import KDTree
sys.path.insert(0, os.path.dirname(__file__)); import wraplib as wl
argv = sys.argv[sys.argv.index('--') + 1:]
name, bake_blend, tex_png, out_glb, out_blend = argv[:5]
NM = wl.char_names(name)
argv = argv[1:]  # keep the optional-argument indices below unchanged
P = json.loads(argv[5]) if len(argv) > 5 else {}
bpy.ops.wm.open_mainfile(filepath=bake_blend)
FN = bpy.data.objects[NM['obj']]; arm = FN.parent
H = [o for o in bpy.data.objects if o.type == 'MESH' and o is not FN and len(o.data.polygons) > 1000][0]
for o in list(bpy.data.objects):
    if o.type == 'MESH' and o not in (FN, H): print('RIG drop', o.name, len(o.data.polygons)); bpy.data.objects.remove(o)
# texture
img = bpy.data.images.load(tex_png); img.name = NM['img']
for n in FN.data.materials[0].node_tree.nodes:
    if n.type == 'TEX_IMAGE': n.image = img
img.pack()
for im in bpy.data.images:
    if im.users and not im.packed_file and im.source == 'FILE': im.pack()
if len(argv) > 4 and argv[4]:
    rimg = bpy.data.images.load(argv[4]); rimg.name = 'rb_inner_color'; rimg.pack()
    m1 = FN.data.materials[1]
    for n in m1.node_tree.nodes:
        if n.type == 'TEX_IMAGE': n.image = rimg
# face shades like the body it replaces: copy the body's Principled settings (Hank's face kept roughness 0.5 vs 0.9)
_hb = [n for n in H.data.materials[0].node_tree.nodes if n.type == 'BSDF_PRINCIPLED'][0]
_fb = FN.data.materials[0].node_tree.nodes['Principled BSDF']
for _k in ('Roughness', 'Metallic', 'Specular IOR Level', 'IOR'): _fb.inputs[_k].default_value = _hb.inputs[_k].default_value
if 'face_rough' in P: _fb.inputs['Roughness'].default_value = P['face_rough']
m1 = FN.data.materials[1]; m1.node_tree.nodes['Principled BSDF'].inputs['Roughness'].default_value = 0.95
meye = m1.copy(); meye.name = 'rb_eye'; meye.node_tree.nodes['Principled BSDF'].inputs['Roughness'].default_value = P.get('eye_rough', 0.35)
FN.data.materials.append(meye)
_part = np.empty(len(FN.data.vertices), int); FN.data.attributes['part'].data.foreach_get('value', _part)
for p in FN.data.polygons:
    if _part[p.vertices[0]] > 0: p.material_index = 2
info = json.loads(FN['eyes']); aim = {}
# the Rocketbox eyeball is a front cap; its vertex mean (fit.py's 'center') sits ~5 mm in front of the sphere centre, so
# turning the eye about it swings the cap out through the lids. Use the fitted sphere centre for every eye rotation.
if P.get('eye_sphere_centre', False):
    _mw = np.array(FN.matrix_world); _pp = np.empty(len(FN.data.vertices), int); FN.data.attributes['part'].data.foreach_get('value', _pp)
    _co = np.array([tuple(v.co) for v in FN.data.vertices]) @ _mw[:3, :3].T + _mw[:3, 3]
    for s_, pid in (('L', 1), ('R', 2)):
        c_, r_ = wl.fit_sphere(_co[_pp == pid]); print('RIG eye', s_, 'sphere centre shift mm', np.round((c_ - info[s_]['center']) * 1000, 2).tolist(), 'r mm', round(r_ * 1000, 2))
        info[s_]['center'] = c_.tolist(); info[s_]['radius'] = r_
# move the eyeball sideways, away from the nose (mm): the inner-corner rim stands in front of the eyeball, so a level iris
# butts against it with no white on the nasal side and the eyes read as crossed
if P.get('eye_dx_mm'):
    _mw = np.array(FN.matrix_world); _mi = np.linalg.inv(_mw); _pr = np.empty(len(FN.data.vertices), int); FN.data.attributes['part'].data.foreach_get('value', _pr)
    for s_, pid in (('L', 1), ('R', 2)):
        dx = np.sign(info[s_]['center'][0]) * P['eye_dx_mm'] / 1000
        sh = Vector(_mi[:3, :3] @ np.array([dx, 0, 0]))
        for kb in FN.data.shape_keys.key_blocks:
            for i in np.nonzero(_pr == pid)[0]: kb.data[i].co += sh
        for i in np.nonzero(_pr == pid)[0]: FN.data.vertices[i].co += sh
        info[s_]['center'][0] += dx
# eyeball size: at r ~ 10 mm on a 1 m character the cap is oversized (a real eye here is ~7.6 mm) and its rim pokes through
# the skin. Scale it about the pupil, so the pupil stays where it is and the rim recedes behind the skin.
if P.get('eye_scale'):
    _mw = np.array(FN.matrix_world); _mi = np.linalg.inv(_mw); _pr = np.empty(len(FN.data.vertices), int); FN.data.attributes['part'].data.foreach_get('value', _pr)
    _uvl = FN.data.uv_layers.active.data; _puv = np.array(json.load(open(P['eye_tex_json']))['pupil_uv'] if 'eye_tex_json' in P else P.get('pupil_uv', [0.26355, 0.06983]))
    _kbs = FN.data.shape_keys.key_blocks
    for s_, pid in (('L', 1), ('R', 2)):
        ei = np.nonzero(_pr == pid)[0]
        best = min(((np.sum((np.array(_uvl[li].uv) - _puv) ** 2), FN.data.loops[li].vertex_index) for p_ in FN.data.polygons if _pr[p_.vertices[0]] == pid for li in p_.loop_indices))
        pw = _mw[:3, :3] @ np.array(_kbs[0].data[best[1]].co) + _mw[:3, 3]
        for kb in _kbs:
            X = np.array([tuple(kb.data[i].co) for i in ei]) @ _mw[:3, :3].T + _mw[:3, 3]
            X = wl.scale_about(X, pw, P['eye_scale']) @ _mi[:3, :3].T + _mi[:3, 3]
            for i, x in zip(ei, X): kb.data[i].co = x
        for i in ei: FN.data.vertices[i].co = _kbs[0].data[i].co
        info[s_]['center'] = wl.scale_about(np.array(info[s_]['center']), pw, P['eye_scale']).tolist(); info[s_]['radius'] = info[s_]['radius'] * P['eye_scale']
        print('RIG eye', s_, 'scaled about pupil, r mm', round(info[s_]['radius'] * 1000, 2))
# ---- aim the pupils: a narrow lid opening sits below the eyeball centre, so a straight-ahead pupil hides under the
# upper lid. Rotate each eyeball (all shape keys, rigidly about its centre) so the pupil points at a point eye_conv_m ahead
# and eye_pitch_deg below eye level. Each eye bone's rest axis is laid through the pupil, so a plain look-at aims the pupil.
if P.get('eye_pitch_deg') is not None:
    mw = np.array(FN.matrix_world); mwi = np.linalg.inv(mw)
    uvl = FN.data.uv_layers.active.data; puv = np.array(json.load(open(P['eye_tex_json']))['pupil_uv'] if 'eye_tex_json' in P else P.get('pupil_uv', [0.26355, 0.06983]))
    _pt = np.empty(len(FN.data.vertices), int); FN.data.attributes['part'].data.foreach_get('value', _pt)
    ec = (np.array(info['L']['center']) + np.array(info['R']['center'])) / 2; D = P.get('eye_conv_m', 1.0)
    T = ec + np.array([0, -D, -D * np.tan(np.radians(P['eye_pitch_deg']))])
    kbs = FN.data.shape_keys.key_blocks
    for s_, pid in (('L', 1), ('R', 2)):
        c = np.array(info[s_]['center']); ei = np.nonzero(_pt == pid)[0]
        best = min(((np.sum((np.array(uvl[li].uv) - puv) ** 2), FN.data.loops[li].vertex_index) for p_ in FN.data.polygons
                    if _pt[p_.vertices[0]] == pid for li in p_.loop_indices))
        pw = mw[:3, :3] @ np.array(kbs[0].data[best[1]].co) + mw[:3, 3]
        R = wl.rot_between(pw - c, T - c)
        for kb in kbs:
            X = np.array([tuple(kb.data[i].co) for i in ei]) @ mw[:3, :3].T + mw[:3, 3]
            X = (c + (X - c) @ R.T) @ mwi[:3, :3].T + mwi[:3, 3]
            for i, x in zip(ei, X): kb.data[i].co = x
        for i in ei: FN.data.vertices[i].co = kbs[0].data[i].co
        a0 = (pw - c) / np.linalg.norm(pw - c); a1 = (T - c) / np.linalg.norm(T - c); aim[s_] = a1
        print('RIG eye', s_, 'pupil dir', a0.round(3).tolist(), '->', a1.round(3).tolist(), 'deg', round(float(np.degrees(np.arccos(np.clip(a0 @ a1, -1, 1)))), 2))
# ---- open the upper lids: a narrow lid opening hid the pupil at level gaze. Rotate upper-lid skin about each
# eye centre (stays on its shell, so no new contact with the eyeball), then re-base the shape keys so lid-closing motion
# (eyeBlink, the closing part of eyeSquint/eyeLook*) keeps its absolute end pose and opening motion (eyeWide) keeps its delta.
# eyeLook* keys in Rocketbox also turn the eyeball; the eye bones already turn it, so a rig driving both double-rotates.
# Optionally keep only their lid motion.
if P.get('eyelook_lids_only'):
    _pe = np.empty(len(FN.data.vertices), int); FN.data.attributes['part'].data.foreach_get('value', _pe); _ei = np.nonzero(_pe > 0)[0]
    _b = FN.data.shape_keys.key_blocks[0]
    for kb in FN.data.shape_keys.key_blocks[1:]:
        if kb.name.startswith('eyeLook'):
            for i in _ei: kb.data[i].co = _b.data[i].co
_co_pre = [v.co.copy() for v in FN.data.vertices]  # skin weights are looked up from the un-lifted face
if P.get('lid_open_deg') or P.get('lower_lid_deg'):
    mw = np.array(FN.matrix_world); mwi = np.linalg.inv(mw); kbs = FN.data.shape_keys.key_blocks
    _pt = np.empty(len(FN.data.vertices), int); FN.data.attributes['part'].data.foreach_get('value', _pt)
    get = lambda kb: np.array([tuple(d.co) for d in kb.data]) @ mw[:3, :3].T + mw[:3, 3]
    B0 = get(kbs[0]); B1 = B0.copy(); sk = np.nonzero(_pt == 0)[0]
    for s_ in ('L', 'R'):
        c = np.array(info[s_]['center']); r = info[s_]['radius']
        Y, w = wl.lid_lift(B1[sk], c, r, P.get('lid_open_deg', 0.0), *P.get('lid_falloff', (1.25, 2.3, -0.45, -0.28)))
        if P.get('lid_smooth', 0):  # soften the lifted rim (displacement smoothing, kept on each point's shell)
            E_ = np.array([e.vertices[:] for e in FN.data.edges]); E_ = E_[(_pt[E_] == 0).all(1)]
            g = -np.ones(len(B1), int); g[sk] = np.arange(len(sk)); E_ = g[E_]
            Y = wl.smooth_shell_displacement(B1[sk], Y, E_, c, iters=P['lid_smooth'])
        if P.get('lower_lid_deg'):  # lower the lower lid too (its wet rim covered the iris from below)
            Y, w2 = wl.lid_lift(Y, c, r, -P['lower_lid_deg'], *P.get('lower_lid_falloff', (1.25, 2.3, -0.10, -0.28)))
            print('RIG lower lid', s_, 'moved verts', int((w2 > 0.01).sum()))
        B1[sk] = Y; print('RIG lid', s_, 'lifted verts', int((w > 0.01).sum()), 'full', int((w > 0.99).sum()))
    Kn = wl.compensate_keys(B0, B1, {kb.name: get(kb) for kb in kbs[1:]}, P.get('lid_full_at', 0.5))
    to_l = lambda X: X @ mwi[:3, :3].T + mwi[:3, 3]
    kbs[0].data.foreach_set('co', to_l(B1).ravel()); FN.data.vertices.foreach_set('co', to_l(B1).ravel())
    for kb in kbs[1:]: kb.data.foreach_set('co', to_l(Kn[kb.name]).ravel())
    FN.data.update()
# eyeLookDown: Rocketbox's upper lid follows only half of a downward gaze, leaving white above the iris. Rebuild the upper-lid
# part from the eyeBlink end pose: `lookdown_lid_mm` of travel at the lid margin, along an arc round the eye centre. The rest
# of the key (lower lid, cheek) and the eyeball (no delta) stay as they were.
if P.get('lookdown_lid_mm'):
    mw = np.array(FN.matrix_world); mwi = np.linalg.inv(mw); kbs = FN.data.shape_keys.key_blocks
    _pl = np.empty(len(FN.data.vertices), int); FN.data.attributes['part'].data.foreach_get('value', _pl)
    get = lambda kb: np.array([tuple(d.co) for d in kb.data]) @ mw[:3, :3].T + mw[:3, 3]
    B0 = get(kbs[0])
    for s_, sd in (('L', 'Left'), ('R', 'Right')):
        c = np.array(info[s_]['center']); Bl = get(kbs['eyeBlink' + sd]); K = get(kbs['eyeLookDown' + sd]); dB = Bl - B0; tr = np.linalg.norm(dB, axis=1)
        m = wl.ramp(tr, 0.0002, 0.0012) * (dB[:, 2] < 0) * (_pl == 0)                    # upper lid: goes down in a blink
        rim = (m > 0.99) & (tr >= np.percentile(tr[m > 0.99], 60))                       # lid margin: the biggest blink travel
        f = wl.fraction_for_travel(B0[rim], Bl[rim], c, P['lookdown_lid_mm'] / 1000)
        Y = wl.lid_arc_lerp(B0, Bl, c, f)
        Kn = K * (1 - m[:, None]) + Y * m[:, None]
        print('RIG lookdown', s_, 'f', round(f, 3), 'rim verts', int(rim.sum()), 'rim travel mm before', np.round(np.percentile(np.linalg.norm(K[rim] - B0[rim], axis=1) * 1000, [50, 100]), 2).tolist(),
              'after', np.round(np.percentile(np.linalg.norm(Kn[rim] - B0[rim], axis=1) * 1000, [50, 100]), 2).tolist(), 'blink rim mm', round(float(np.median(tr[rim]) * 1000), 2))
        Xl = Kn @ mwi[:3, :3].T + mwi[:3, 3]; kd_ = kbs['eyeLookDown' + sd]
        for i in np.nonzero(m > 0)[0]: kd_.data[i].co = Xl[i]  # only the upper lid: every other vertex keeps its exact value
# ---- eye bones ----
bpy.context.view_layer.objects.active = arm; bpy.ops.object.mode_set(mode='EDIT')
eb = arm.data.edit_bones; head_b = eb['mixamorig:Head']
ainv = arm.matrix_world.inverted()
names = {'L': 'mixamorig:LeftEye', 'R': 'mixamorig:RightEye'}
for s, nm in names.items():
    c = Vector(info[s]['center']); r = info[s]['radius']
    fwd = Vector(aim[s]) if s in aim else Vector((0, -1, 0))
    b = eb.new(nm); b.head = ainv @ c; b.tail = ainv @ (c + fwd * 2.5 * r); b.roll = 0; b.parent = head_b
    b.use_deform = True
bpy.ops.object.mode_set(mode='OBJECT')
# ---- weights ----
part = np.empty(len(FN.data.vertices), int); FN.data.attributes['part'].data.foreach_get('value', part)
ring = np.empty(len(FN.data.vertices)); FN.data.attributes['ring'].data.foreach_get('value', ring)
mwH, mwF = H.matrix_world, FN.matrix_world
kd = KDTree(len(H.data.vertices))
for v in H.data.vertices: kd.insert(mwH @ v.co, v.index)
kd.balance()
hg = {g.index: g.name for g in H.vertex_groups}
for g in FN.vertex_groups: FN.vertex_groups.remove(g)
G = {n: FN.vertex_groups.new(name=n) for n in ('mixamorig:Head', 'mixamorig:Neck', names['L'], names['R'])}
blend_rings = 4.0
for v in FN.data.vertices:
    i = v.index
    if part[i] == 1: G[names['L']].add([i], 1.0, 'REPLACE'); continue
    if part[i] == 2: G[names['R']].add([i], 1.0, 'REPLACE'); continue
    t = float(np.clip(ring[i] / blend_rings, 0, 1))  # 0 at border -> copy the character, 1 inside -> pure Head
    co, hi, d = kd.find(mwF @ _co_pre[i])
    w = {hg[g.group]: g.weight for g in H.data.vertices[hi].groups if hg[g.group] in ('mixamorig:Head', 'mixamorig:Neck')}
    tot = sum(w.values()) or 1.0
    wh = w.get('mixamorig:Head', 0) / tot if w else 1.0; wn = 1 - wh
    wh = t * 1.0 + (1 - t) * wh; wn = (1 - t) * wn
    G['mixamorig:Head'].add([i], wh, 'REPLACE')
    if wn > 1e-4: G['mixamorig:Neck'].add([i], wn, 'REPLACE')
# border normals: blend the face's vertex normals toward the body's next to the seam, so the mask edge shades like the
# skin it meets (its own smooth normals left a visible outline in lit renders)
if P.get('normal_rings'):
    hme_n = np.array([tuple((mwH.to_3x3() @ v.normal).normalized()) for v in H.data.vertices])
    fn_n = np.array([tuple((mwF.to_3x3() @ v.normal).normalized()) for v in FN.data.vertices])
    wN = np.clip(ring / P['normal_rings'], 0, 1); wN = wN * wN * (3 - 2 * wN)
    newN = fn_n.copy()
    for v in FN.data.vertices:
        if part[v.index] > 0 or wN[v.index] >= 1: continue
        co, hi, d = kd.find(mwF @ _co_pre[v.index])
        newN[v.index] = (1 - wN[v.index]) * hme_n[hi] + wN[v.index] * fn_n[v.index]
    newN /= np.linalg.norm(newN, axis=1, keepdims=True)
    inv = np.array(mwF.to_3x3().inverted())
    FN.data.normals_split_custom_set_from_vertices([tuple(inv @ n) for n in newN])
    print('RIG border normals blended', int((wN < 1).sum()))
mod = FN.modifiers.new('Armature', 'ARMATURE'); mod.object = arm
for k in FN.data.shape_keys.key_blocks[1:]: k.value = 0
print('RIG face tris', sum(len(p.vertices) - 2 for p in FN.data.polygons), 'hank tris', sum(len(p.vertices) - 2 for p in H.data.polygons))
bpy.ops.wm.save_as_mainfile(filepath=out_blend)
bpy.ops.object.select_all(action='DESELECT')
for o in (arm, H, FN): o.select_set(True)
bpy.context.view_layer.objects.active = arm
bpy.ops.export_scene.gltf(filepath=out_glb, export_format='GLB', use_selection=True, export_image_format='JPEG', export_jpeg_quality=90,
                          export_skins=True, export_morph=True, export_morph_normal=True, export_animations=False, export_apply=False)
print('RIG exported', out_glb, os.path.getsize(out_glb))
