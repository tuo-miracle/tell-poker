# Decimate a Tripo GLB to a triangle target, keeping UVs/texture/skin. Based on hank-3d/decimate.py + decimate_face.py.
# - welds first (Tripo meshes are unwelded -> holes otherwise)
# - drops tiny helper meshes (e.g. the 2 m Icosphere Tripo ships with rigged characters)
# - face mode: protects the face with a vertex group (verts mostly weighted to mixamorig:Head, front half of head)
#   and an inverted Decimate group, bisecting the ratio to hit the total target
# usage: blender -b -P decimate.py -- src.glb out.glb target [face]
import bpy, bmesh, sys
argv = sys.argv[sys.argv.index('--') + 1:]
src, out, target = argv[0], argv[1], int(argv[2]); face = len(argv) > 3 and argv[3] == 'face'
P = lambda *a: print('DEC', *a, flush=True)
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=src)
for o in [o for o in bpy.context.scene.objects if o.type == 'MESH' and len(o.data.polygons) <= 1000]:
    P('drop helper mesh', o.name, len(o.data.polygons)); bpy.data.objects.remove(o)
o = [o for o in bpy.context.scene.objects if o.type == 'MESH'][0]
bpy.context.view_layer.objects.active = o
bm = bmesh.new(); bm.from_mesh(o.data); n0 = len(bm.verts)
bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=1e-5); bm.to_mesh(o.data); P('weld', n0, '->', len(bm.verts)); bm.free()
if o.data.has_custom_normals: bpy.ops.mesh.customdata_custom_splitnormals_clear()
for p in o.data.polygons: p.use_smooth = True
tri = lambda sel=None: sum(len(p.vertices) - 2 for p in o.data.polygons if sel is None or all(v in sel for v in p.vertices))
m = o.modifiers.new('dec', 'DECIMATE'); m.use_collapse_triangulate = True
fs = None
if face:
    arm = o.parent if o.parent and o.parent.type == 'ARMATURE' else next(x for x in bpy.data.objects if x.type == 'ARMATURE')
    gi = o.vertex_groups['mixamorig:Head'].index
    head = arm.matrix_world @ arm.pose.bones['mixamorig:Head'].head
    inv = o.matrix_world.inverted(); hy = (inv @ head).y
    fs = set(v.index for v in o.data.vertices if v.co.y < hy + 0.005 and any(g.group == gi and g.weight > 0.5 for g in v.groups))
    g = o.vertex_groups.new(name='face'); g.add(list(fs), 1.0, 'REPLACE')
    m.vertex_group = 'face'; m.invert_vertex_group = True; m.vertex_group_factor = 1.0
    P('face verts', len(fs), 'face tris before', tri(fs))
P('tris before', tri())
lo, hi = 0.005, 1.0
for _ in range(14):
    m.ratio = (lo + hi) / 2
    n = sum(len(p.vertices) - 2 for p in o.evaluated_get(bpy.context.evaluated_depsgraph_get()).to_mesh().polygons)
    lo, hi = (m.ratio, hi) if n < target else (lo, m.ratio)
bpy.ops.object.modifier_move_to_index(modifier='dec', index=0); bpy.ops.object.modifier_apply(modifier='dec')
P('ratio', round(m.ratio, 4) if False else round((lo + hi) / 2, 4), 'tris after', tri())
if face:
    arm = next(x for x in bpy.data.objects if x.type == 'ARMATURE'); gi = o.vertex_groups['mixamorig:Head'].index
    hy = (o.matrix_world.inverted() @ (arm.matrix_world @ arm.pose.bones['mixamorig:Head'].head)).y
    fs2 = set(v.index for v in o.data.vertices if v.co.y < hy + 0.005 and any(g.group == gi and g.weight > 0.5 for g in v.groups))
    P('face tris after', tri(fs2)); o.vertex_groups.remove(o.vertex_groups['face'])
bm = bmesh.new(); bm.from_mesh(o.data)
P('after: boundary edges', sum(1 for e in bm.edges if e.is_boundary), 'non-manifold', sum(1 for e in bm.edges if not e.is_manifold))
bm.free()
bpy.ops.export_scene.gltf(filepath=out, export_format='GLB', export_image_format='JPEG', export_jpeg_quality=90,
                          export_skins=True, export_animations=False)
