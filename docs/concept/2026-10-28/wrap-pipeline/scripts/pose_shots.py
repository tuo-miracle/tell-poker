# Render a wrapped GLB in given bone poses (degrees, XYZ euler in each bone's local space, as Blender's pose bones) at 1 m.
# usage: blender -b -P pose_shots.py -- in.glb out_dir unity_h poses.json
#   poses.json: {"name": {"mixamorig:Spine2": [x, y, z], "mixamorig:Neck": [...], ...}, ...}
import bpy, sys, os, math, json
sys.path.insert(0, os.path.dirname(__file__)); import common
from mathutils import Vector
a = sys.argv[sys.argv.index('--') + 1:]; glb, out, uh, pj = a[0], a[1], float(a[2]), a[3]
os.makedirs(out, exist_ok=True); poses = json.load(open(pj))
bpy.ops.wm.read_factory_settings(use_empty=True); bpy.ops.import_scene.gltf(filepath=glb)
for o in list(bpy.data.objects):
    if o.name.startswith('Icosphere'): bpy.data.objects.remove(o)
arm = [o for o in bpy.data.objects if o.type == 'ARMATURE'][0]; pb = arm.pose.bones
sc = common.setup_scene(1024, 0.45)
c = arm.matrix_world @ pb['mixamorig:Neck'].head + Vector((0, 0, 0.02))
for nm, rot in poses.items():
    for b in pb: b.rotation_mode = 'XYZ'; b.rotation_euler = (0, 0, 0)
    for b, e in rot.items(): pb[b].rotation_euler = [math.radians(x) for x in e]
    bpy.context.view_layer.update()
    for cam in [o for o in sc.objects if o.type == 'CAMERA']: bpy.data.objects.remove(cam)
    common.cam_persp(c, (0, 1, 0), 1.0 / uh, 30); common.render(os.path.join(out, nm + '.png'))
print('POSES done', list(poses))
