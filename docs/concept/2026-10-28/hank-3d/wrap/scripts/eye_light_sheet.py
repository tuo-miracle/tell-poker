# Eyes as seen at 1 m under three lights: the usual acceptance light, a flat frontal light, and 'urp' = the game's MrLighting
# rig (key + fill directions, colours and intensities, flat ambient 0.5, no reflections; Blender sun strength = pi * URP intensity).
# Gaze uses the eye bones only (Damped Track, like a Unity look-at); renders neutral, gaze left/right/table, blink.
# usage: blender -b -P eye_light_sheet.py -- in.glb out_prefix
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
sc = common.setup_scene(1024, 0.45); sun = [o for o in sc.objects if o.type == 'LIGHT'][0]
bg = sc.world.node_tree.nodes['Background'].inputs; rot0 = sun.rotation_euler.copy(); en0 = sun.data.energy
def track(d): return Vector(d).to_track_quat('-Z', 'Y').to_euler()
LIGHTS = {'usual': (0.45, en0, rot0), 'flat': (0.55, 1.2, (math.radians(90), 0, 0)),  # flat: sun along the view axis
          'urp': (0.5, 3.3, track((0.23, 0.71, -0.67)))}
fill = bpy.data.objects.new('fill', bpy.data.lights.new('fill', 'SUN')); sc.collection.objects.link(fill)
fill.data.energy = 1.26; fill.data.color = (0.57, 0.67, 1.0); fill.rotation_euler = track((-0.44, -0.76, -0.47))
B = {'eyeBlinkLeft': 1, 'eyeBlinkRight': 1}
D_ = lambda w: {'eyeLookDownLeft': w, 'eyeLookDownRight': w}
POSES = {'neutral': ({}, 0, 0), 'left': ({}, 20, 0), 'right': ({}, -20, 0), 'table': ({}, 0, -15), 'blink': (B, 0, 0),
         'down05': (D_(0.5), 0, -26), 'down1': (D_(1.0), 0, -26), 'down055': (D_(0.55), 0, -26), 'blink_down': ({**B, **D_(1.0)}, 0, -26), 'blink_down055': ({**B, **D_(0.55)}, 0, -26),
         'dn105': (D_(0.4), 0, -10.5), 'dn21': (D_(0.8), 0, -21), 'dn21w1': (D_(1.0), 0, -21)}
fc = ec + Vector((0, 0, -0.012))
common.cam_persp(fc, (0, 1, 0), 0.555, 22)
for ln, (col, en, rot) in LIGHTS.items():
    bg[0].default_value = (col, col, col, 1); sun.data.energy = en; sun.rotation_euler = rot; fill.hide_render = ln != 'urp'
    sun.data.color = (1, 0.89, 0.75) if ln == 'urp' else (1, 1, 1)
    # the game has reflectionIntensity 0; Blender's eye would mirror the grey world, so switch the eye's specular off in this light
    for m in FN.data.materials:
        if m.name.startswith('rb_eye'): m.node_tree.nodes['Principled BSDF'].inputs['Specular IOR Level'].default_value = 0.0 if ln == 'urp' else 0.5
    for pn, (keys, yaw, pitch) in POSES.items():
        for k in FN.data.shape_keys.key_blocks[1:]: k.value = keys.get(k.name, 0.0)
        y, p = math.radians(yaw), math.radians(pitch)
        tgt.location = ec + Vector((math.sin(y) * math.cos(p), -math.cos(y) * math.cos(p), math.sin(p))); bpy.context.view_layer.update()
        common.render(f'{pre}_{ln}_{pn}.png')
print('EYELIGHT done')
