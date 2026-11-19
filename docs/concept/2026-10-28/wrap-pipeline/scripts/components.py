# Connected components of the main mesh after welding (finds separate pieces such as glasses).
# usage: blender -b -P components.py -- in.glb [min_tris]
import bpy, bmesh, sys
argv = sys.argv[sys.argv.index('--') + 1:]; mn = int(argv[1]) if len(argv) > 1 else 1
bpy.ops.wm.read_factory_settings(use_empty=True); bpy.ops.import_scene.gltf(filepath=argv[0])
o = max((o for o in bpy.context.scene.objects if o.type == 'MESH'), key=lambda o: len(o.data.polygons))
bm = bmesh.new(); bm.from_mesh(o.data); bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=1e-5); bm.faces.ensure_lookup_table()
seen = set(); comps = []
for f in bm.faces:
    if f.index in seen: continue
    st = [f]; seen.add(f.index); fs = []
    while st:
        g = st.pop(); fs.append(g)
        for e in g.edges:
            for h in e.link_faces:
                if h.index not in seen: seen.add(h.index); st.append(h)
    vs = {v for g in fs for v in g.verts}; co = [o.matrix_world @ v.co for v in vs]
    comps.append((len(fs), [round(min(c[i] for c in co), 3) for i in range(3)], [round(max(c[i] for c in co), 3) for i in range(3)]))
comps.sort(key=lambda c: -c[0])
for c in comps:
    if c[0] >= mn: print('COMP tris', c[0], 'min', c[1], 'max', c[2])
print('COMPS total', len(comps))
