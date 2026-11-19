# Dump upper-lid data for lid_fill.py: per-corner UVs of the skin triangles with each vertex's blink motion (mm) and
# position (world), and which eye it belongs to. usage: blender -b -P lid_data.py -- bake.blend out.json
import bpy, sys, json, numpy as np
argv = sys.argv[sys.argv.index('--') + 1:]
bpy.ops.wm.open_mainfile(filepath=argv[0])
FN = bpy.data.objects['HankFace']; me = FN.data; mw = np.array(FN.matrix_world); uvl = me.uv_layers.active.data
kb = me.shape_keys.key_blocks; get = lambda k: np.array([tuple(d.co) for d in k.data]) @ mw[:3, :3].T + mw[:3, 3]
B = get(kb[0]); mL = np.linalg.norm(get(kb['eyeBlinkLeft']) - B, axis=1); mR = np.linalg.norm(get(kb['eyeBlinkRight']) - B, axis=1)
T = []
for p in me.polygons:
    if p.material_index != 0: continue
    vs = list(p.vertices); T.append({'uv': [tuple(uvl[li].uv) for li in p.loop_indices], 'mL': mL[vs].tolist(), 'mR': mR[vs].tolist()})
json.dump({'tris': T, 'mLmax': float(mL.max()), 'mRmax': float(mR.max())}, open(argv[1], 'w')); print('LIDDATA tris', len(T), 'max mm', mL.max() * 1000, mR.max() * 1000)
