import bpy, sys, json, numpy as np
argv = sys.argv[sys.argv.index('--') + 1:]
FN = bpy.data.objects['HankFace']; me = FN.data; uvl = me.uv_layers.active.data
part = np.empty(len(me.vertices), int); me.attributes['part'].data.foreach_get('value', part)
polys = [p for p in me.polygons if p.material_index == 1]
lab = {p.index: p.index for p in polys}
def f(a):
    while lab[a] != a: lab[a] = lab[lab[a]]; a = lab[a]
    return a
emap = {}
for p in polys:
    for li in p.loop_indices:
        key = (me.loops[li].vertex_index, tuple(round(x, 5) for x in uvl[li].uv))
        if key in emap:
            a, b = f(p.index), f(emap[key])
            if a != b: lab[a] = b
        else: emap[key] = p.index
isl = {}
for p in polys: isl.setdefault(f(p.index), []).append(p)
out = []
mw = FN.matrix_world
for k, ps in sorted(isl.items(), key=lambda kv: -len(kv[1])):
    vs = sorted(set(v for p in ps for v in p.vertices))
    c = np.mean([tuple(mw @ me.vertices[v].co) for v in vs], 0)
    out.append({'id': k, 'n': len(ps), 'part': int(np.bincount(part[vs]).argmax()), 'ctr': c.round(4).tolist(),
                'uv': [[tuple(uvl[li].uv) for li in p.loop_indices] for p in ps]})
json.dump(out, open(argv[0], 'w')); print('ISL', [(o['n'], o['part'], o['ctr']) for o in out])
