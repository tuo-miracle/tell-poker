# Render a wrapped GLB in poses captured in Unity (unity-r1's <name>_pose.json: per bone the glTF-local rotation
# `gltf_local`, quaternion [x,y,z,w]) at 1 m, camera in front of the seat. Each joint's world rotation is rebuilt from the
# GLB's node tree with the posed local rotations, then set on the Blender pose bone through its rest-axis correction.
# usage: blender -b -P pose_unity.py -- in.glb out_dir unity_h pose.json [seat_index] [extra_deg]
#   extra_deg: added to the Neck and Head turn of the left/right looks (margin over the game's angle)
import bpy, sys, os, json, struct, math
sys.path.insert(0, os.path.dirname(__file__)); import common
from mathutils import Quaternion, Matrix, Vector
a = sys.argv[sys.argv.index('--') + 1:]; glb, out, uh, pj = a[0], a[1], float(a[2]), a[3]
seat = int(a[4]) if len(a) > 4 else 0; extra = float(a[5]) if len(a) > 5 else 0.0
os.makedirs(out, exist_ok=True)
with open(glb, 'rb') as f:
    f.read(12); n, _ = struct.unpack('<II', f.read(8)); G = json.loads(f.read(n))
nodes = G['nodes']; parent = {c: i for i, nd in enumerate(nodes) for c in nd.get('children', [])}
q = lambda r: Quaternion((r[3], r[0], r[1], r[2]))  # glTF [x,y,z,w] -> Blender (w,x,y,z)
def world(i, local):
    r = Quaternion()
    while i is not None: r = local(i) @ r; i = parent.get(i)
    return r
byname = {nd.get('name'): i for i, nd in enumerate(nodes)}
conv = Matrix.Rotation(math.radians(90), 4, 'X').to_quaternion()  # glTF Y-up -> Blender Z-up
bpy.ops.wm.read_factory_settings(use_empty=True); bpy.ops.import_scene.gltf(filepath=glb)
for o in list(bpy.data.objects):
    if o.name.startswith('Icosphere'): bpy.data.objects.remove(o)
arm = [o for o in bpy.data.objects if o.type == 'ARMATURE'][0]; pb = arm.pose.bones
P = json.load(open(pj)); sc = common.setup_scene(1024, 0.45)
rest_local = lambda i: q(nodes[i].get('rotation', [0, 0, 0, 1]))
order = [b.name for b in arm.data.bones]  # parents before children
for look in P['seats'][seat]['looks']:
    bones = {k: v['gltf_local'] for k, v in look['bones'].items() if k in byname and k in pb}
    if extra and look['look'] in ('left', 'right'):
        s = 1 if look['look'] == 'left' else -1
        for k in ('mixamorig:Neck', 'mixamorig:Head'):
            if k in bones: bones[k] = list((q(bones[k]) @ Quaternion((0, 1, 0), math.radians(s * extra / 2)))[1:]) + [(q(bones[k]) @ Quaternion((0, 1, 0), math.radians(s * extra / 2)))[0]]
    pose_local = lambda i: q(bones[nodes[i]['name']]) if nodes[i].get('name') in bones else rest_local(i)
    for b in pb: b.rotation_mode = 'QUATERNION'; b.rotation_quaternion = Quaternion()
    bpy.context.view_layer.update()
    for name in order:
        if name not in bones: continue
        i = byname[name]; bo = arm.data.bones[name]
        Mrest = (arm.matrix_world @ bo.matrix_local).to_quaternion()
        C = (conv @ world(i, rest_local)).inverted() @ Mrest
        Mw = conv @ world(i, pose_local) @ C
        M = (arm.matrix_world.to_quaternion().inverted() @ Mw).to_matrix().to_4x4(); M.translation = pb[name].matrix.translation
        pb[name].matrix = M; bpy.context.view_layer.update()
    h = arm.matrix_world @ pb['mixamorig:Neck'].head
    for cam in [o for o in sc.objects if o.type == 'CAMERA']: bpy.data.objects.remove(cam)
    common.cam_persp(h + Vector((0, 0, 0.02)), (0, 1, 0), 1.0 / uh, 30)
    common.render(os.path.join(out, f"pose_{look['look']}.png"))
    print('POSE', look['look'], 'face yaw', look.get('face_yaw_deg_vs_body'), 'extra', extra)
