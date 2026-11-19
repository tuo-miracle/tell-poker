# Inspect a GLB: meshes, tris, verts, materials/textures, armature, shape keys, world bounds.
# usage: blender -b -P inspect.py -- file.glb
import bpy, sys
from mathutils import Vector
argv = sys.argv[sys.argv.index('--') + 1:]
src = argv[0]
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=src)
P = lambda *a: print('INSPECT', *a, flush=True)
sc = bpy.context.scene
bpy.context.view_layer.update()
dg = bpy.context.evaluated_depsgraph_get()
tt = tv = 0
allpts = []
for o in sc.objects:
    P('OBJ', o.name, o.type, 'parent', o.parent.name if o.parent else None,
      'loc', tuple(round(x, 4) for x in o.matrix_world.translation), 'scale', tuple(round(x, 4) for x in o.matrix_world.to_scale()))
    if o.type == 'MESH':
        me = o.data
        tris = sum(len(p.vertices) - 2 for p in me.polygons)
        tt += tris; tv += len(me.vertices)
        me2 = o.evaluated_get(dg).to_mesh()
        pts = [o.matrix_world @ v.co for v in me2.vertices]
        mn = Vector([min(p[i] for p in pts) for i in range(3)]); mx = Vector([max(p[i] for p in pts) for i in range(3)])
        allpts.append((o.name, mn, mx, tris))
        sk = me.shape_keys.key_blocks.keys() if me.shape_keys else []
        P('  MESH verts', len(me.vertices), 'polys', len(me.polygons), 'tris', tris, 'uv', me.uv_layers.keys(),
          'vgroups', len(o.vertex_groups), 'shapekeys', len(sk), sk[:60], 'mats', [m.name if m else None for m in me.materials],
          'mods', [m.type for m in o.modifiers])
        P('  BOUNDS(Blender Z-up) min', tuple(round(x, 4) for x in mn), 'max', tuple(round(x, 4) for x in mx),
          'size', tuple(round(x, 4) for x in (mx - mn)))
        # weld check
        import bmesh
        bm = bmesh.new(); bm.from_mesh(me); n0 = len(bm.verts)
        bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=1e-5); n1 = len(bm.verts)
        nm = sum(1 for e in bm.edges if not e.is_manifold); bnd = sum(1 for e in bm.edges if e.is_boundary)
        P('  WELD verts', n0, '->', n1, 'after weld: non-manifold edges', nm, 'boundary edges', bnd, 'faces', len(bm.faces))
        bm.free()
    if o.type == 'ARMATURE':
        bones = o.data.bones
        P('  ARMATURE bones', len(bones), [b.name for b in bones])
for m in bpy.data.materials:
    imgs = []
    if m.use_nodes:
        for n in m.node_tree.nodes:
            if n.type == 'TEX_IMAGE' and n.image:
                tgt = [l.to_socket.name + '@' + l.to_node.name for l in n.outputs[0].links]
                imgs.append((n.image.name, tuple(n.image.size), tgt))
    P('MAT', m.name, imgs)
for i in bpy.data.images: P('IMG', i.name, tuple(i.size), i.colorspace_settings.name)
P('TOTAL tris', tt, 'verts', tv)
