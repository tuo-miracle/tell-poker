# Compare two wrapped GLBs: per-mesh vertex/shape-key positions, eye bone heads (IPD), and texture pixels.
# usage: blender -b -P compare_glb.py -- a.glb b.glb out.json
import bpy, sys, json, numpy as np
argv = sys.argv[sys.argv.index('--') + 1:]

def load(path):
    bpy.ops.wm.read_factory_settings(use_empty=True); bpy.ops.import_scene.gltf(filepath=path)
    d = {'mesh': {}, 'bones': {}, 'img': {}}
    for o in bpy.context.scene.objects:
        if o.type == 'MESH':
            me = o.data; co = np.empty(len(me.vertices) * 3); me.vertices.foreach_get('co', co)
            keys = {}
            if me.shape_keys:
                for k in me.shape_keys.key_blocks:
                    a = np.empty(len(me.vertices) * 3); k.data.foreach_get('co', a); keys[k.name] = a.reshape(-1, 3)
            d['mesh'][o.name.split('.')[0] if o.name.startswith('tripo') else o.name] = (co.reshape(-1, 3), keys)
        if o.type == 'ARMATURE':
            for b in o.pose.bones: d['bones'][b.name] = np.array(o.matrix_world @ b.head)
    for i in bpy.data.images:
        if i.size[0]: d['img'][i.name] = np.array(i.pixels[:]).reshape(i.size[1], i.size[0], -1)
    return d

A, B = load(argv[0]), load(argv[1]); R = {'mesh': {}, 'img': {}}
for n in sorted(set(A['mesh']) | set(B['mesh'])):
    if n not in A['mesh'] or n not in B['mesh']: R['mesh'][n] = 'missing in ' + ('a' if n not in A['mesh'] else 'b'); continue
    (ca, ka), (cb, kb) = A['mesh'][n], B['mesh'][n]
    if ca.shape != cb.shape: R['mesh'][n] = {'verts': [len(ca), len(cb)]}; continue
    kd = max([float(np.abs(ka[k] - kb[k]).max()) for k in ka if k in kb] or [0.0])
    R['mesh'][n] = {'verts': len(ca), 'max_d': float(np.abs(ca - cb).max()), 'keys': [len(ka), len(kb)], 'key_max_d': kd}
eye = lambda D: float(np.linalg.norm(D['bones']['mixamorig:LeftEye'] - D['bones']['mixamorig:RightEye'])) if 'mixamorig:LeftEye' in D['bones'] else None
R['eye_bone_dist'] = [eye(A), eye(B)]
R['bone_max_d'] = max(float(np.linalg.norm(A['bones'][b] - B['bones'][b])) for b in A['bones'] if b in B['bones'])
for n in sorted(set(A['img']) & set(B['img'])):
    a, b = A['img'][n], B['img'][n]
    R['img'][n] = {'max_d8': float(np.abs(a - b).max() * 255), 'mean_d8': float(np.abs(a - b).mean() * 255)} if a.shape == b.shape else 'size differs'
print('COMPARE', json.dumps(R)); json.dump(R, open(argv[2], 'w'), indent=1)
