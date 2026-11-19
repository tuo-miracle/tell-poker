import bpy, bmesh, sys, json
from mathutils import Vector
argv = sys.argv[sys.argv.index('--') + 1:]
src, lm_path, cam_path, out, target, invert = argv[0], argv[1], argv[2], argv[3], int(argv[4]), argv[5] == '1'
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=src)
o = [o for o in bpy.context.scene.objects if o.type == 'MESH' and len(o.data.polygons) > 1000][0]
bpy.context.view_layer.objects.active = o
bm = bmesh.new(); bm.from_mesh(o.data); bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=1e-5); bm.to_mesh(o.data); bm.free()
if o.data.has_custom_normals: bpy.ops.mesh.customdata_custom_splitnormals_clear()
for p in o.data.polygons: p.use_smooth = True
cam = json.load(open(cam_path)); L = json.load(open(lm_path)); s, W = cam['s'], cam['W']
dg = bpy.context.evaluated_depsgraph_get()
def lm3d(i):
    px, py = L[i]
    hit, loc, *_ = bpy.context.scene.ray_cast(dg, Vector((cam['cx'] + (px / W - .5) * s, cam['cy'] - 3, cam['cz'] - (py / W - .5) * s)), Vector((0, 1, 0)))
    return o.matrix_world.inverted() @ loc
inv = o.matrix_world.inverted()
nose, chin, top = lm3d(1), lm3d(152), lm3d(10)
r = max((chin - nose).length, (top - nose).length) * 1.15
g = o.vertex_groups.new(name='face')
face = [v.index for v in o.data.vertices if (v.co - nose).length < r and v.co.y < nose.y + r * 0.45]
g.add(face, 1.0, 'REPLACE')
def tri_count(sel=None):
    return sum(len(p.vertices) - 2 for p in o.data.polygons if sel is None or all(v in sel for v in p.vertices))
fs = set(face)
print('BEFORE total', tri_count(), 'face', tri_count(fs))
m = o.modifiers.new('dec', 'DECIMATE'); m.vertex_group = 'face'; m.invert_vertex_group = invert; m.vertex_group_factor = 1.0
lo, hi = 0.05, 1.0
for _ in range(12):  # bisect ratio to hit the target
    m.ratio = (lo + hi) / 2
    n = sum(len(p.vertices) - 2 for p in o.evaluated_get(bpy.context.evaluated_depsgraph_get()).to_mesh().polygons)
    lo, hi = (m.ratio, hi) if n < target else (lo, m.ratio)
bpy.ops.object.modifier_move_to_index(modifier='dec', index=0); bpy.ops.object.modifier_apply(modifier='dec')
fs = set(v.index for v in o.data.vertices if (v.co - nose).length < r and v.co.y < nose.y + r * 0.45)
print('AFTER total', tri_count(), 'face', tri_count(fs), 'invert', invert)
o.vertex_groups.remove(o.vertex_groups['face'])
for img in bpy.data.images:
    if img.size[0] > 2048: img.scale(2048, 2048)
bpy.ops.export_scene.gltf(filepath=out, export_format='GLB', export_image_format='JPEG', export_jpeg_quality=90, export_skins=True, export_animations=False)
