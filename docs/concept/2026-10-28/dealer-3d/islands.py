# Count connected mesh islands after welding (separate eye/mouth geometry would show up as extra islands near the head).
import bpy, bmesh, sys
argv = sys.argv[sys.argv.index('--') + 1:]
bpy.ops.wm.read_factory_settings(use_empty=True); bpy.ops.import_scene.gltf(filepath=argv[0])
o = [o for o in bpy.context.scene.objects if o.type == 'MESH' and len(o.data.polygons) > 1000][0]
bm = bmesh.new(); bm.from_mesh(o.data); bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=1e-5); bm.verts.ensure_lookup_table()
seen = set(); isl = []
for v in bm.verts:
    if v.index in seen: continue
    st = [v]; seen.add(v.index); comp = []
    while st:
        x = st.pop(); comp.append(x)
        for e in x.link_edges:
            y = e.other_vert(x)
            if y.index not in seen: seen.add(y.index); st.append(y)
    zs = [c.co.z for c in comp]; isl.append((len(comp), round(min(zs), 3), round(max(zs), 3)))
isl.sort(reverse=True)
print('ISLANDS', len(isl), isl[:15])
