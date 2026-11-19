# Eye-appearance renders from a GLB: at 1 m (front, 3/4) and close-up; neutral, smile, gaze left/right (Damped Track on
# the eye bones, like a Unity look-at); under the acceptance light and a dim ambient-only light.
# usage: blender -b -P eye_sheet.py -- in.glb out_prefix
import bpy, sys, os, math
sys.path.insert(0, os.path.dirname(__file__)); import common
from mathutils import Vector
glb, pre = sys.argv[sys.argv.index('--') + 1:][:2]
bpy.ops.wm.read_factory_settings(use_empty=True); bpy.ops.import_scene.gltf(filepath=glb)
for o in list(bpy.data.objects):
    if o.name.startswith('Icosphere'): bpy.data.objects.remove(o)
arm = [o for o in bpy.data.objects if o.type == 'ARMATURE'][0]; pb = arm.pose.bones
FN = [o for o in bpy.data.objects if o.type == 'MESH' and o.data.shape_keys][0]
BN = ('mixamorig:LeftEye', 'mixamorig:RightEye')
ec = sum((arm.matrix_world @ arm.data.bones[n].head_local for n in BN), Vector()) / 2
tgt = bpy.data.objects.new('target', None); bpy.context.scene.collection.objects.link(tgt)
for n in BN:
    c = pb[n].constraints.new('DAMPED_TRACK'); c.target = tgt; c.track_axis = 'TRACK_Y'
sc = common.setup_scene(1024, 0.45)
sun = [o for o in sc.objects if o.type == 'LIGHT'][0]; bg = sc.world.node_tree.nodes['Background'].inputs
fc = ec + Vector((0, 0, -0.012))
POSES = {'neutral': ({}, 0), 'smile': ({'mouthSmileLeft': 1, 'mouthSmileRight': 1}, 0), 'gazeL': ({}, 20), 'gazeR': ({}, -20)}
def pose(keys, yaw):
    for k in FN.data.shape_keys.key_blocks[1:]: k.value = keys.get(k.name, 0.0)
    y = math.radians(yaw); tgt.location = ec + Vector((math.sin(y), -math.cos(y), 0)); bpy.context.view_layer.update()
for light, en, col in (('lit', False, 0.45), ('dim', True, 0.25)):
    sun.hide_render = en; bg[0].default_value = (col, col, col, 1)
    for nm, (keys, yaw) in POSES.items():
        pose(keys, yaw)
        for view, d, cam in (('front', (0, 1, 0), (0.555, 22)), ('q', (-0.6, 1, 0), (0.555, 22)), ('close', (0, 1, 0), None)):
            for o in [o for o in sc.objects if o.type == 'CAMERA']: bpy.data.objects.remove(o)
            if cam: common.cam_persp(fc, d, *cam)
            else: common.cam_ortho(ec + Vector((0, 0, 0.002)), 0.075, d, 3.0)
            common.render(f'{pre}_{light}_{nm}_{view}.png')
print('EYESHEET done')
