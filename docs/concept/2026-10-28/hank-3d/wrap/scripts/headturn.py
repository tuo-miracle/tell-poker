import bpy, sys, os, math
sys.path.insert(0, os.path.dirname(__file__)); import common
from mathutils import Vector
argv = sys.argv[sys.argv.index('--') + 1:]
bpy.ops.wm.read_factory_settings(use_empty=True); bpy.ops.import_scene.gltf(filepath=argv[0])
for o in list(bpy.data.objects):
    if o.name.startswith('Icosphere'): bpy.data.objects.remove(o)
arm = [o for o in bpy.data.objects if o.type == 'ARMATURE'][0]; pb = arm.pose.bones
FN = [o for o in bpy.data.objects if o.type == 'MESH' and o.data.shape_keys][0]
sc = common.setup_scene(1024, 0.45)
for nm, rot, keys in (('headturn_yaw', ('mixamorig:Head', (0, math.radians(30), 0)), {'jawOpen': 1}),
                      ('headturn_tilt', ('mixamorig:Neck', (math.radians(-20), 0, math.radians(15))), {'eyeBlinkLeft': 1, 'eyeBlinkRight': 1})):
    for b in pb: b.rotation_mode = 'XYZ'; b.rotation_euler = (0, 0, 0)
    pb[rot[0]].rotation_euler = rot[1]
    for k in FN.data.shape_keys.key_blocks[1:]: k.value = keys.get(k.name, 0)
    bpy.context.view_layer.update()
    h = arm.matrix_world @ pb['mixamorig:Head'].head
    for o in [o for o in sc.objects if o.type == 'CAMERA']: bpy.data.objects.remove(o)
    common.cam_ortho(h + Vector((0, 0, 0.04)), 0.16, (-0.3, 1, 0), 3.0); common.render(os.path.join(argv[1], nm + '.png'))
