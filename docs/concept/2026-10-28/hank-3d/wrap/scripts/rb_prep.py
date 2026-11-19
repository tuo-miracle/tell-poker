# Build a standalone Rocketbox head (world metres): head-material faces only, 52 ARKit keys, part labels.
import bpy, sys, os, numpy as np, json
sys.path.insert(0, os.path.dirname(__file__)); import common
argv = sys.argv[sys.argv.index('--') + 1:]
fbx, tex_dir, out_blend = argv
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=fbx)
src = [o for o in bpy.context.scene.objects if o.type == 'MESH'][0]; me = src.data
mw = np.array(src.matrix_world)
def world(co):
    co = np.c_[co, np.ones(len(co))]; return (co @ mw.T)[:, :3]
nv = len(me.vertices)
basis = np.empty(nv * 3); me.vertices.foreach_get('co', basis); basis = world(basis.reshape(-1, 3))
faces = [p for p in me.polygons if p.material_index == 1]
keep = sorted(set(v for p in faces for v in p.vertices)); remap = {v: i for i, v in enumerate(keep)}
V = basis[keep]
F = [[remap[v] for v in p.vertices] for p in faces]
uvl = me.uv_layers.active.data
UV = [[tuple(uvl[li].uv) for li in p.loop_indices] for p in faces]
# part labels from dominant vertex group
vg = {g.index: g.name for g in src.vertex_groups}
def dom(i):
    gs = sorted(me.vertices[i].groups, key=lambda g: -g.weight); return vg[gs[0].group] if gs else ''
part = np.zeros(len(keep), int)  # 0 skin/mouth, 1 eyeL, 2 eyeR
grp = {}
for k, i in enumerate(keep):
    d = dom(i)
    if d == 'Bip01 LEye': part[k] = 1
    elif d == 'Bip01 REye': part[k] = 2
    for g in me.vertices[i].groups:
        n = vg[g.group]
        if n in ('Bip01 MTongue', 'Bip01 MJaw', 'Bip01 Head', 'Bip01 Neck', 'Bip01 Spine2'):
            grp.setdefault(n, np.zeros(len(keep)))[k] = g.weight
keys = {}
for kb in me.shape_keys.key_blocks:
    if kb.name.startswith('AK_'):
        a = np.empty(nv * 3); kb.data.foreach_get('co', a); a = world(a.reshape(-1, 3))[keep]
        n = kb.name.split('_', 2)[2]; n = n[0].lower() + n[1:]
        keys[n] = a
print('KEYS', len(keys), list(keys)[:5])
# build new object
nm = bpy.data.meshes.new('RBHead'); nm.from_pydata(V.tolist(), [], F); nm.update()
uv = nm.uv_layers.new(name='UVMap')
for p, fu in zip(nm.polygons, UV):
    for li, u in zip(p.loop_indices, fu): uv.data[li].uv = u
for p in nm.polygons: p.use_smooth = True
ob = bpy.data.objects.new('RBHead', nm); bpy.context.scene.collection.objects.link(ob)
at = nm.attributes.new('part', 'INT', 'POINT'); at.data.foreach_set('value', part)
for n, w in grp.items():
    g = ob.vertex_groups.new(name=n.replace('Bip01 ', 'rb_'))
    for k in np.nonzero(w > 0)[0]: g.add([int(k)], float(w[k]), 'REPLACE')
ob.shape_key_add(name='Basis', from_mix=False)
for n, a in keys.items():
    k = ob.shape_key_add(name=n, from_mix=False); k.data.foreach_set('co', a.ravel()); k.value = 0.0
m = common.tex_material('rb_head', os.path.join(tex_dir, 'm004_head_color.tga'), 0.5); nm.materials.append(m)
# eye centres (from eye islands)
eyes = {s: V[part == p].mean(0).tolist() for s, p in (('L', 1), ('R', 2))}
ob['eyes'] = json.dumps(eyes)
bpy.data.objects.remove(src); [bpy.data.objects.remove(o) for o in list(bpy.data.objects) if o.type in ('ARMATURE', 'EMPTY')]
print('RB', len(V), 'verts', len(F), 'tris', 'eyes', eyes, 'zrange', V[:, 2].min(), V[:, 2].max())
moved = {n: int((np.linalg.norm(a - V, axis=1) > 1e-5).sum()) for n, a in keys.items()}
print('MOVED', moved)
bpy.ops.wm.save_as_mainfile(filepath=out_blend)
