# Seam/eye measurement renders from an exported GLB: per view an ID pass (face skin / Hank / eyes+inner),
# an albedo pass (texture colour only), a lit pass (same light as final_renders) and a dim flat pass
# (ambient only, 0.25 grey world; stands in for URP Lit in a dim passthrough room).
import bpy, sys, os, math
sys.path.insert(0, os.path.dirname(__file__)); import common
from mathutils import Vector
argv = sys.argv[sys.argv.index('--') + 1:]
glb, outdir = argv[0], argv[1]
os.makedirs(outdir, exist_ok=True)
bpy.ops.wm.read_factory_settings(use_empty=True); bpy.ops.import_scene.gltf(filepath=glb)
for o in list(bpy.data.objects):
    if o.type == 'MESH' and o.name.startswith('Icosphere'): bpy.data.objects.remove(o)
arm = [o for o in bpy.data.objects if o.type == 'ARMATURE'][0]; pb = arm.pose.bones
meshes = [o for o in bpy.data.objects if o.type == 'MESH']
FN = [o for o in meshes if o.data.shape_keys][0] if len(meshes) > 1 else None  # None: the original (uncut) Hank
H = max((o for o in meshes if o is not FN), key=lambda o: len(o.data.polygons))  # body (not glasses)
for o in meshes:
    if o not in (FN, H): o.hide_render = True  # glasses: not part of the seam measurement
if len(argv) > 2: ec = Vector(map(float, argv[2].split(',')))  # same cameras as another run
else:
    eL = arm.matrix_world @ pb['mixamorig:LeftEye'].head; eR = arm.matrix_world @ pb['mixamorig:RightEye'].head; ec = (eL + eR) / 2
print('SEAMRENDERS ec', ','.join('%.6f' % x for x in ec))
face_c = ec + Vector((0, 0, -0.022))
VIEWS = {'front': (face_c, 0.13, (0, 1, 0)), 'q': (face_c + Vector((0.01, 0, 0)), 0.11, (-0.7, 1, 0)),
         'side': (face_c, 0.13, (-1, 0.25, 0)), 'eyes': (ec + Vector((0, 0, 0.004)), 0.06, (0, 1, 0))}
OBJS = [o for o in (FN, H) if o]
orig = {o.name: list(o.data.materials) for o in OBJS}

def flat(name, rgb):
    m = bpy.data.materials.new(name); m.use_nodes = True; nt = m.node_tree; nt.nodes.clear()
    e = nt.nodes.new('ShaderNodeEmission'); e.inputs[0].default_value = (*rgb, 1); out = nt.nodes.new('ShaderNodeOutputMaterial')
    nt.links.new(e.outputs[0], out.inputs[0]); return m

def albedo(m0):
    m = m0.copy(); nt = m.node_tree
    tx = [n for n in nt.nodes if n.type == 'TEX_IMAGE'][0]; out = [n for n in nt.nodes if n.type == 'OUTPUT_MATERIAL'][0]
    e = nt.nodes.new('ShaderNodeEmission'); nt.links.new(tx.outputs[0], e.inputs[0]); nt.links.new(e.outputs[0], out.inputs[0]); return m

def set_mats(mode):
    for o in OBJS:
        for i, m0 in enumerate(orig[o.name]):
            if mode == 'id': m = flat('id', (1, 0, 0) if o is FN and i == 0 else (0, 1, 0) if o is H else (0, 0, 1))
            elif mode == 'albedo': m = albedo(m0)
            else: m = m0
            o.data.materials[i] = m

sc = common.setup_scene(1024, 0.45)
sun = [o for o in sc.objects if o.type == 'LIGHT'][0]
bg = sc.world.node_tree.nodes['Background'].inputs
for vn, (c, size, d) in VIEWS.items():
    for o in [o for o in sc.objects if o.type == 'CAMERA']: bpy.data.objects.remove(o)
    common.cam_ortho(c, size, d, 3.0)
    for mode in ('id', 'albedo', 'lit', 'dim'):
        set_mats('id' if mode == 'id' else 'albedo' if mode == 'albedo' else 'pbr')
        sun.hide_render = mode != 'lit'
        bg[0].default_value = (0, 0, 0, 1) if mode in ('id', 'albedo') else (0.45, 0.45, 0.45, 1) if mode == 'lit' else (0.25, 0.25, 0.25, 1)
        sc.render.filter_size = 0.01 if mode == 'id' else 1.5
        common.render(os.path.join(outdir, f'{vn}_{mode}.png'))
print('SEAMRENDERS done')
