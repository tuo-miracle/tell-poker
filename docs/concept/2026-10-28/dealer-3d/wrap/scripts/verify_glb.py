import bpy, sys, json
argv = sys.argv[sys.argv.index('--') + 1:]
bpy.ops.wm.read_factory_settings(use_empty=True); bpy.ops.import_scene.gltf(filepath=argv[0])
arms = [o for o in bpy.context.scene.objects if o.type == 'ARMATURE']
out = {'armatures': len(arms), 'bones': len(arms[0].data.bones), 'eye_bones': [b.name for b in arms[0].data.bones if 'Eye' in b.name],
       'eye_parent': {b.name: b.parent.name for b in arms[0].data.bones if 'Eye' in b.name}, 'meshes': {}}
tot = 0
for o in bpy.context.scene.objects:
    if o.type != 'MESH': continue
    t = sum(len(p.vertices) - 2 for p in o.data.polygons); tot += t
    sk = o.data.shape_keys
    out['meshes'][o.name] = {'tris': t, 'shape_keys': len(sk.key_blocks) - 1 if sk else 0, 'skinned': any(m.type == 'ARMATURE' for m in o.modifiers),
                             'vgroups': len(o.vertex_groups), 'mats': [m.name for m in o.data.materials],
                             'keys': [k.name for k in sk.key_blocks[1:]] if sk else []}
out['total_tris'] = tot
out['images'] = {i.name: list(i.size) for i in bpy.data.images}
json.dump(out, open(argv[1], 'w'), indent=1); print('VERIFY', json.dumps({k: v for k, v in out.items() if k != 'meshes'}))
for n, m in out['meshes'].items(): print('VERIFY mesh', n, {k: v for k, v in m.items() if k != 'keys'})
