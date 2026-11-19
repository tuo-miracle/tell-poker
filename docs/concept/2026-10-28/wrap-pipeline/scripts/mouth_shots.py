# Mouth close-ups at a real-world distance: neutral and jawOpen, perspective camera `dist_m` metres away in Unity
# (the GLB is 1 m tall, Unity scales it to unity_h), same light as final_renders.
# usage: blender -b -P mouth_shots.py -- in.glb out_dir unity_h [dist_m]
import bpy, sys, os
sys.path.insert(0, os.path.dirname(__file__)); import common
from mathutils import Vector
a = sys.argv[sys.argv.index('--') + 1:]; glb, out, uh = a[0], a[1], float(a[2]); dist = float(a[3]) if len(a) > 3 else 0.5
os.makedirs(out, exist_ok=True)
bpy.ops.wm.read_factory_settings(use_empty=True); bpy.ops.import_scene.gltf(filepath=glb)
for o in list(bpy.data.objects):
    if o.name.startswith('Icosphere'): bpy.data.objects.remove(o)
arm = [o for o in bpy.data.objects if o.type == 'ARMATURE'][0]; pb = arm.pose.bones
FN = [o for o in bpy.data.objects if o.type == 'MESH' and o.data.shape_keys][0]
ec = (arm.matrix_world @ pb['mixamorig:LeftEye'].head + arm.matrix_world @ pb['mixamorig:RightEye'].head) / 2
sc = common.setup_scene(1024, 0.45)
for nm, keys in (('neutral', {}), ('jaw', {'jawOpen': 1.0})):
    for k in FN.data.shape_keys.key_blocks[1:]: k.value = keys.get(k.name, 0.0)
    bpy.context.view_layer.update()
    for c in [o for o in sc.objects if o.type == 'CAMERA']: bpy.data.objects.remove(c)
    common.cam_persp(ec + Vector((0, 0, -0.045)), (0, 1, 0), dist / uh, 20)
    common.render(os.path.join(out, f'mouth{int(dist * 100):02d}_{nm}.png'))
print('MOUTH done')
