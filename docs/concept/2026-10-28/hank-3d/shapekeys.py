import bpy, bmesh, sys, json, math
from mathutils import Vector
argv = sys.argv[sys.argv.index('--') + 1:]
src, lm_path, cam_path, out_glb, shot = argv
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=src)
mesh = [o for o in bpy.context.scene.objects if o.type == 'MESH' and len(o.data.polygons) > 1000][0]
bmw = bmesh.new(); bmw.from_mesh(mesh.data); bmesh.ops.remove_doubles(bmw, verts=bmw.verts, dist=1e-5); bmw.to_mesh(mesh.data); bmw.free()
if mesh.data.has_custom_normals:
    bpy.context.view_layer.objects.active = mesh; bpy.ops.mesh.customdata_custom_splitnormals_clear()
for p in mesh.data.polygons: p.use_smooth = True
cam = json.load(open(cam_path)); L = json.load(open(lm_path))
s, W = cam['s'], cam['W']
dg = bpy.context.evaluated_depsgraph_get()
def lm3d(i):
    px, py = L[i]
    x = cam['cx'] + (px / W - 0.5) * s; z = cam['cz'] - (py / W - 0.5) * s
    hit, loc, *_ = bpy.context.scene.ray_cast(dg, Vector((x, cam['cy'] - 3, z)), Vector((0, 1, 0)))
    assert hit, i
    return mesh.matrix_world.inverted() @ loc
# face island = verts connected to the vertex nearest the nose tip
bm = bmesh.new(); bm.from_mesh(mesh.data); bm.verts.ensure_lookup_table()
nose = lm3d(1)
def islands():
    seen, out = set(), []
    for v0 in bm.verts:
        if v0.index in seen: continue
        isl, st = {v0.index}, [v0]; seen.add(v0.index)
        while st:
            v = st.pop()
            for e in v.link_edges:
                o2 = e.other_vert(v)
                if o2.index not in seen: seen.add(o2.index); isl.add(o2.index); st.append(o2)
        out.append(isl)
    return out
sc0 = mesh.matrix_world.to_scale()[0]
eye_pts = [lm3d(i) for i in (468, 473, 159, 145, 386, 374, 33, 133, 263, 362)]
island = set()
for isl in islands():
    xs = [bm.verts[i].co.x for i in isl]
    near_nose = any((bm.verts[i].co - nose).length * sc0 < 0.002 for i in isl)
    near_eye = (max(xs) - min(xs)) * sc0 < 0.10 and any(min((bm.verts[i].co - p).length for p in eye_pts) * sc0 < 0.015 for i in isl)
    if near_nose or near_eye: island |= isl
print('ISLAND', len(island), 'of', len(bm.verts))
co = [v.co.copy() for v in bm.verts]
mesh.shape_key_add(name='Basis', from_mix=False)
# object space: glTF import keeps +Z up, model faces -Y (scale may apply; displacements given in world metres)
sc_ = mesh.matrix_world.to_scale()[0]
def key(name, items, avg=False):
    """items: list of (landmark index, displacement world Vector, radius m)."""
    k = mesh.shape_key_add(name=name, from_mix=False)
    tgt = [(lm3d(i), d / sc_, r / sc_) for i, d, r in items] if avg else [(lm3d(i), d * 2.0 / sc_, r * 1.3 / sc_) for i, d, r in items]
    for vi in island:
        p = co[vi]; off = Vector(); wsum = 0.0; wmax = 0.0
        for c, d, r in tgt:
            dist = (p - c).length
            if dist < r:
                w = (1 - (dist / r) ** 2) ** 2; off += d * w; wsum += w; wmax = max(wmax, w)
        if avg and wsum > 0: off = off / wsum * wmax
        k.data[vi].co = p + off
V = Vector
for side, sx, brow_in, brow_mid, brow_out, up_lid, lo_lid, lid_ring, corner, cheek, nose_w in (
        ('Left', 1, 336, 334, 300, 386, 374, (385, 387, 384, 398), 291, 280, 358),
        ('Right', -1, 107, 105, 70, 159, 145, (158, 160, 157, 173), 61, 50, 129)):
    key('browDown' + side, [(brow_in, V((-sx * 0.002, 0, -0.004)), 0.018), (brow_mid, V((0, 0, -0.002)), 0.015)])
    key('browOuterUp' + side, [(brow_out, V((0, 0, 0.005)), 0.02), (brow_mid, V((0, 0, 0.003)), 0.015)])
    key('eyeSquint' + side, [(lo_lid, V((0, 0, 0.002)), 0.008), (cheek, V((0, -0.001, 0.003)), 0.02)])
    key('mouthSmile' + side, [(corner, V((sx * 0.004, 0.002, 0.006)), 0.02), (cheek, V((0, -0.001, 0.002)), 0.02)])
    key('mouthFrown' + side, [(corner, V((sx * 0.001, 0, -0.005)), 0.018)])
    key('noseSneer' + side, [(nose_w, V((0, 0, 0.003)), 0.012)])
# Displacement-only blink: narrows the painted eye to about half. A full blink needs eyelid geometry (painted eyes).
for side, up, lo in (('Left', [466, 388, 387, 386, 385, 384, 398], [249, 390, 373, 374, 380, 381, 382]),
                     ('Right', [246, 161, 160, 159, 158, 157, 173], [7, 163, 144, 145, 153, 154, 155])):
    items = []
    for u, l in zip(up, lo):
        d = lm3d(l) - lm3d(u)
        items += [(u, d * 0.95, 0.0055), (u, d * 0.35, 0.011)]
    key('eyeBlink' + side, items, avg=True)
key('browInnerUp', [(107, V((0, 0, 0.006)), 0.018), (336, V((0, 0, 0.006)), 0.018)])
key('mouthPress', [(0, V((0, 0, -0.0015)), 0.012), (17, V((0, 0, 0.0015)), 0.012)])
key('cheekPuff', [(50, V((-0.004, -0.004, 0)), 0.025), (280, V((0.004, -0.004, 0)), 0.025)])
print('KEYS', [k.name for k in mesh.data.shape_keys.key_blocks])
bpy.ops.export_scene.gltf(filepath=out_glb, export_format='GLB', export_image_format='JPEG', export_jpeg_quality=90, export_skins=True, export_morph=True, export_animations=False)
# review renders: neutral + a few combined expressions
sc = bpy.context.scene; sc.render.engine = 'BLENDER_EEVEE'
sc.render.resolution_x = sc.render.resolution_y = 600
sc.world = bpy.data.worlds.new('w'); sc.world.use_nodes = True
sc.world.node_tree.nodes['Background'].inputs[0].default_value = (0.8, 0.8, 0.8, 1)
sc.view_settings.view_transform = 'Standard'
l = bpy.data.objects.new('sun', bpy.data.lights.new('sun', 'SUN')); l.data.energy = 3; l.rotation_euler = (math.radians(60), 0, math.radians(20)); sc.collection.objects.link(l)
for o in bpy.context.scene.objects:
    if o.type == 'MESH' and o is not mesh: o.hide_render = True
c = bpy.data.objects.new('cam', bpy.data.cameras.new('cam')); sc.collection.objects.link(c); sc.camera = c
c.data.type = 'ORTHO'; c.data.ortho_scale = s * 0.75
c.location = (cam['cx'], cam['cy'] - 3, cam['cz'] - s * 0.08); c.rotation_euler = (math.radians(90), 0, 0)
kb = mesh.data.shape_keys.key_blocks
looks = {'neutral': {}, 'smile': {'mouthSmileLeft': 1, 'mouthSmileRight': 1, 'eyeSquintLeft': .6, 'eyeSquintRight': .6},
         'frown': {'mouthFrownLeft': 1, 'mouthFrownRight': 1, 'browDownLeft': 1, 'browDownRight': 1},
         'surprise': {'browInnerUp': 1, 'browOuterUpLeft': 1, 'browOuterUpRight': 1},
         'blink': {'eyeBlinkLeft': 1, 'eyeBlinkRight': 1}, 'smirk': {'mouthSmileLeft': 1, 'browOuterUpRight': .7, 'noseSneerLeft': .5}}
for n, vals in looks.items():
    for k in kb[1:]: k.value = vals.get(k.name, 0)
    sc.render.filepath = f'{shot}_{n}.png'; bpy.ops.render.render(write_still=True)
