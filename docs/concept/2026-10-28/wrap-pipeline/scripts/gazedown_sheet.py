# Gaze-down renders from a GLB: eyeLookDown weight with the matching eye-bone pitch (Damped Track, as Unity drives them), at
# 1 m front and 3/4 and close-up; blink and blink + eyeLookDown for the additive overshoot.
# usage: blender -b -P gazedown_sheet.py -- in.glb out_prefix
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
fc = ec + Vector((0, 0, -0.012)); D = {'eyeLookDownLeft': 1, 'eyeLookDownRight': 1}; Bl = {'eyeBlinkLeft': 1, 'eyeBlinkRight': 1}
POSES = {'w0': ({}, 0), 'w05': ({k: 0.5 for k in D}, -13), 'w1': (D, -26), 'blink': (Bl, 0), 'blink_down': ({**Bl, **D}, -26)}
for nm, (keys, pitch) in POSES.items():
    for k in FN.data.shape_keys.key_blocks[1:]: k.value = keys.get(k.name, 0.0)
    p = math.radians(pitch); tgt.location = ec + Vector((0, -math.cos(p), math.sin(p))); bpy.context.view_layer.update()
    for view, d, cam in (('front', (0, 1, 0), (0.555, 22)), ('q', (-0.6, 1, 0), (0.555, 22)), ('close', (0, 1, 0), None)):
        for o in [o for o in sc.objects if o.type == 'CAMERA']: bpy.data.objects.remove(o)
        if cam: common.cam_persp(fc, d, *cam)
        else: common.cam_ortho(ec + Vector((0, 0, 0.002)), 0.075, d, 3.0)
        common.render(f'{pre}_{nm}_{view}.png')
print('GAZEDOWN done')
