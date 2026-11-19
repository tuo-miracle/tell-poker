# Acceptance renders from an exported GLB (re-imported), so they also prove the export.
import bpy, sys, os, json, math
sys.path.insert(0, os.path.dirname(__file__)); import common
from mathutils import Vector
argv = sys.argv[sys.argv.index('--') + 1:]
glb, outdir = argv[0], argv[1]
only = set(argv[2].split(',')) if len(argv) > 2 and argv[2] else None
os.makedirs(outdir, exist_ok=True)
bpy.ops.wm.read_factory_settings(use_empty=True); bpy.ops.import_scene.gltf(filepath=glb)
for o in list(bpy.data.objects):
    if o.type == 'MESH' and o.name.startswith('Icosphere'): bpy.data.objects.remove(o)
arm = [o for o in bpy.data.objects if o.type == 'ARMATURE'][0]
meshes = [o for o in bpy.data.objects if o.type == 'MESH']
FN = [o for o in meshes if o.data.shape_keys][0] if any(o.data.shape_keys for o in meshes) else None
sc = common.setup_scene(1024, 0.45)
pb = arm.pose.bones
if 'mixamorig:LeftEye' in pb:
    eL = arm.matrix_world @ pb['mixamorig:LeftEye'].head; eR = arm.matrix_world @ pb['mixamorig:RightEye'].head
else:  # original Hank: approximate eye level from head bone
    h = arm.matrix_world @ pb['mixamorig:Head'].head; eL = eR = h + Vector((0, -0.04, 0.045))
ec = (eL + eR) / 2
def set_pose(keys=None, yaw=0.0):
    if FN:
        for k in FN.data.shape_keys.key_blocks[1:]: k.value = (keys or {}).get(k.name, 0.0)
    for b in ('mixamorig:LeftEye', 'mixamorig:RightEye'):
        if b in pb: pb[b].rotation_mode = 'XYZ'; pb[b].rotation_euler = (0, 0, math.radians(yaw))  # bone Z is the vertical axis (X would pitch the eye)
    bpy.context.view_layer.update()
def shot(name, center, size=None, direction=(0, 1, 0), persp=None, W=1024):
    if only and name not in only: return
    sc.render.resolution_x = sc.render.resolution_y = W
    for o in [o for o in sc.objects if o.type == 'CAMERA']: bpy.data.objects.remove(o)
    if persp: common.cam_persp(center, direction, persp[0], persp[1])
    else: common.cam_ortho(center, size, direction, 3.0)
    common.render(os.path.join(outdir, name + '.png'))
face_c = ec + Vector((0, 0, -0.022)); eye_c = ec + Vector((0, 0, 0.004)); mouth_c = ec + Vector((0, 0, -0.04))
B = {'eyeBlinkLeft': 1, 'eyeBlinkRight': 1}
set_pose(); shot('neutral_close', face_c, 0.13); shot('neutral_eyes', eye_c, 0.06)
shot('neutral_1m', face_c, persp=(0.555, 40)); shot('neutral_1m_q', face_c, direction=(-0.6, 1, 0), persp=(0.555, 40))
shot('seam_close_q', face_c + Vector((0.01, 0, 0)), 0.11, direction=(-0.7, 1, 0)); shot('seam_close_side', face_c, 0.13, direction=(-1, 0.25, 0))
shot('seam_close_top', face_c + Vector((0, 0, 0.02)), 0.09, direction=(0, 1, -0.45)); shot('seam_close_low', face_c, 0.11, direction=(0.3, 1, 0.5))
if FN:
    set_pose(B); shot('blink_close', eye_c, 0.06); shot('blink_low', eye_c, 0.06, direction=(0, 1, 0.4)); shot('blink_side', eye_c, 0.07, direction=(-0.8, 1, 0))
    shot('blink_1m', face_c, persp=(0.555, 40))
    set_pose({'jawOpen': 1}); shot('jaw_close', mouth_c, 0.07); shot('jaw_low', mouth_c, 0.07, direction=(0, 1, 0.45)); shot('jaw_high', mouth_c, 0.07, direction=(0, 1, -0.35))
    shot('jaw_side', mouth_c, 0.08, direction=(-0.8, 1, 0)); shot('jaw_1m', face_c, persp=(0.555, 40))
    for nm, yaw in (('eyes_left20', 20), ('eyes_right20', -20)):
        set_pose(yaw=yaw); shot(nm, eye_c, 0.06); shot(nm + '_low', eye_c, 0.06, direction=(0, 1, 0.35))
    set_pose({'eyeBlinkLeft': 0.6, 'eyeBlinkRight': 0.6}, yaw=20); shot('eyes_left20_halfblink', eye_c, 0.06)
    for nm, k in (('smile', {'mouthSmileLeft': 1, 'mouthSmileRight': 1}), ('frown', {'browDownLeft': 1, 'browDownRight': 1}),
                  ('surprise', {'browInnerUp': 1, 'browOuterUpLeft': 1, 'browOuterUpRight': 1})):
        set_pose(k); shot(nm + '_close', face_c, 0.13); shot(nm + '_1m', face_c, persp=(0.555, 40))
set_pose()
H = 1.0
for nm, d in (('body_front', (0, 1, 0)), ('body_side', (-1, 0, 0)), ('body_back', (0, -1, 0))):
    shot(nm, Vector((0, 0, 0.5)), 1.1, direction=d)
print('FINAL done')
