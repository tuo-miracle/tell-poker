import bpy, sys
argv = sys.argv[sys.argv.index('--') + 1:]
src, out, target = argv[0], argv[1], int(argv[2])
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=src)
for o in [o for o in bpy.context.scene.objects if o.type == 'MESH' and len(o.data.polygons) > 1000]:
    import bmesh
    bm = bmesh.new(); bm.from_mesh(o.data); bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=1e-5); bm.to_mesh(o.data); bm.free()
    if o.data.has_custom_normals: bpy.context.view_layer.objects.active = o; bpy.ops.mesh.customdata_custom_splitnormals_clear()
    for p in o.data.polygons: p.use_smooth = True
    tris = sum(len(p.vertices) - 2 for p in o.data.polygons)
    bpy.context.view_layer.objects.active = o
    m = o.modifiers.new('dec', 'DECIMATE'); m.ratio = target / tris; m.use_collapse_triangulate = True
    bpy.ops.object.modifier_move_to_index(modifier='dec', index=0)
    bpy.ops.object.modifier_apply(modifier='dec')
    print('TRIS', o.name, tris, '->', sum(len(p.vertices) - 2 for p in o.data.polygons), 'vgroups', len(o.vertex_groups))
for img in bpy.data.images:
    if img.size[0] > 2048:
        print('IMG', img.name, tuple(img.size), '-> 2048'); img.scale(2048, 2048)
bpy.ops.export_scene.gltf(filepath=out, export_format='GLB', export_image_format='JPEG', export_jpeg_quality=90, export_skins=True, export_animations=False)
