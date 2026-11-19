# Render-space seam check: render the exported GLB from the front (and two 3/4 views) with the acceptance lighting,
# project the seam band sample points (seam_bands.py output) and compare mean pixel colour inside vs outside.
# usage: blender -b -P seam_render_check.py -- model.glb seam.json out_prefix
import bpy, sys, os, json, math, numpy as np
sys.path.insert(0, os.path.dirname(__file__)); import common
from mathutils import Vector
argv = sys.argv[sys.argv.index('--') + 1:]
glb, seam_json, prefix = argv[:3]
bpy.ops.wm.read_factory_settings(use_empty=True); bpy.ops.import_scene.gltf(filepath=glb)
for o in list(bpy.data.objects):
    if o.type == 'MESH' and o.name.startswith('Icosphere'): bpy.data.objects.remove(o)
S = json.load(open(seam_json)); Pi, Po = np.array(S['inner_xyz']), np.array(S['outer_xyz'])
sc = common.setup_scene(1024, 0.45)
ctr = np.concatenate([Pi, Po]).mean(0); size = 0.16
res = {}
dg = bpy.context.evaluated_depsgraph_get()
for nm, d in (('front', (0, 1, 0)), ('q_left', (-0.6, 1, 0)), ('q_right', (0.6, 1, 0))):
    for o in [o for o in sc.objects if o.type == 'CAMERA']: bpy.data.objects.remove(o)
    cam = common.cam_ortho(Vector(ctr), size, d, 3.0)
    path = f'{prefix}_{nm}.png'; common.render(path)
    img = bpy.data.images.load(path); W = img.size[0]
    px = np.empty(W * W * 4, np.float32); img.pixels.foreach_get(px); px = px.reshape(W, W, 4)[..., :3]
    dn = Vector(d).normalized(); M = np.array(cam.matrix_world.inverted())
    def mean_vis(X):
        out = []
        for x in X:
            o = Vector(x) - dn * 0.5
            hit, loc, n, i, ob, mm = sc.ray_cast(dg, o, dn)
            if not hit or (loc - Vector(x)).length > 0.0015: continue
            c = M[:3, :3] @ x + M[:3, 3]
            u = int((c[0] / size + 0.5) * W); v = int((c[1] / size + 0.5) * W)  # pixels row 0 = bottom
            if 0 <= u < W and 0 <= v < W: out.append(px[v, u])
        return np.array(out)
    a, b = mean_vis(Pi), mean_vis(Po)
    res[nm] = {'n_in': len(a), 'n_out': len(b), 'in': a.mean(0).round(4).tolist(), 'out': b.mean(0).round(4).tolist(),
               'ratio_lin': (b.mean(0) / a.mean(0)).round(3).tolist()}
    print('SEAMCHECK', nm, res[nm])
json.dump(res, open(prefix + '.json', 'w'), indent=1)
