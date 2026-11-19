# Head-turn check at 1 m: neck turned +-deg (left / right), plus neutral; perspective camera 1 m away in Unity terms.
# usage: blender -b -P turn_shots.py -- in.glb out_dir unity_h [deg]
import bpy, sys, os, math
sys.path.insert(0, os.path.dirname(__file__)); import common
from mathutils import Vector
a = sys.argv[sys.argv.index('--') + 1:]; glb, out, uh = a[0], a[1], float(a[2]); deg = float(a[3]) if len(a) > 3 else 35.0
os.makedirs(out, exist_ok=True)
bpy.ops.wm.read_factory_settings(use_empty=True); bpy.ops.import_scene.gltf(filepath=glb)
for o in list(bpy.data.objects):
    if o.name.startswith('Icosphere'): bpy.data.objects.remove(o)
arm = [o for o in bpy.data.objects if o.type == 'ARMATURE'][0]; pb = arm.pose.bones
sc = common.setup_scene(1024, 0.45)
c = arm.matrix_world @ pb['mixamorig:Head'].head + Vector((0, 0, 0.005))
for nm, d in (('turn_neutral', 0.0), ('turn_left', deg), ('turn_right', -deg)):
    for b in pb: b.rotation_mode = 'XYZ'; b.rotation_euler = (0, 0, 0)
    pb['mixamorig:Neck'].rotation_euler = (0, math.radians(d), 0); bpy.context.view_layer.update()
    for cam in [o for o in sc.objects if o.type == 'CAMERA']: bpy.data.objects.remove(cam)
    common.cam_persp(c, (0, 1, 0), 1.0 / uh, 30); common.render(os.path.join(out, nm + '.png'))
print('TURN done')
