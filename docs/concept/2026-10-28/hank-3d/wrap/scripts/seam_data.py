# Dump what seam_match.py needs from the bake blend: face-skin UV triangles, face vertex positions/rings/coverage,
# Hank vertices near the face border with their UVs, and Hank's colour texture.
import bpy, sys, os, json, numpy as np
from mathutils import Vector
from mathutils.bvhtree import BVHTree
argv = sys.argv[sys.argv.index('--') + 1:]
bake_blend, out_json, out_hank_png = argv[:3]
bpy.ops.wm.open_mainfile(filepath=bake_blend)
FN = bpy.data.objects['HankFace']
H = [o for o in bpy.data.objects if o.type == 'MESH' and o is not FN and len(o.data.polygons) > 1000][0]
me = FN.data; mw = FN.matrix_world; uvl = me.uv_layers.active.data
part = np.empty(len(me.vertices), int); me.attributes['part'].data.foreach_get('value', part)
ring = np.empty(len(me.vertices)); me.attributes['ring'].data.foreach_get('value', ring)
V = np.array([tuple(mw @ v.co) for v in me.vertices]); N = np.array([tuple((mw.to_3x3() @ v.normal).normalized()) for v in me.vertices])
tris = [[p.index, list(p.vertices), [tuple(uvl[li].uv) for li in p.loop_indices]] for p in me.polygons if p.material_index == 0]
fv = sorted(set(v for t in tris for v in t[1]))
# a face vertex is covered when Hank's remaining skin lies just in front of it (the border is tucked under Hank)
dg = bpy.context.evaluated_depsgraph_get(); hb = BVHTree.FromObject(H, dg); hmi = H.matrix_world.inverted()
cov = np.zeros(len(V), bool)
for i in fv:
    o = hmi @ Vector(V[i] + N[i] * 1e-4); d = (hmi.to_3x3() @ Vector(N[i])).normalized()
    loc, n, fi, dd = hb.ray_cast(o, d, 0.01)
    cov[i] = loc is not None
hw = H.matrix_world; huv = H.data.uv_layers.active.data
HV = np.array([tuple(hw @ v.co) for v in H.data.vertices]); HUV = np.zeros((len(HV), 2))
for l, u in zip(H.data.loops, huv): HUV[l.vertex_index] = tuple(u.uv)
border = V[[i for i in fv if ring[i] <= 0.5]]
near = np.nonzero(np.min(np.linalg.norm(HV[:, None] - border[None], axis=2), axis=1) < 0.015)[0]
img = [n.image for n in H.data.materials[0].node_tree.nodes if n.type == 'TEX_IMAGE'][0]
img.filepath_raw = out_hank_png; img.file_format = 'PNG'; img.save()
json.dump({'tris': [[t[1], t[2]] for t in tris], 'V': V.tolist(), 'ring': ring.tolist(), 'part': part.tolist(), 'covered': cov.tolist(),
           'face_verts': fv, 'hank_V': HV[near].tolist(), 'hank_uv': HUV[near].tolist()}, open(out_json, 'w'))
print('SEAMDATA face verts', len(fv), 'covered', int(cov[fv].sum()), 'hank near', len(near),
      'covered ring pct50/90/max', np.percentile(ring[fv][cov[fv]], [50, 90, 100]).round(2).tolist() if cov[fv].any() else None)
