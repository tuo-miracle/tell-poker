import bpy, sys, math, mathutils
from mathutils import Matrix, Vector
argv = sys.argv[sys.argv.index('--') + 1:]
src, out = argv[0], argv[1]
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=src)
arm = [o for o in bpy.context.scene.objects if o.type == 'ARMATURE'][0]
mesh = [o for o in bpy.context.scene.objects if o.type == 'MESH' and len(o.data.polygons) > 1000][0]
for o in bpy.context.scene.objects:
    if o.type == 'MESH' and o is not mesh: o.hide_render = True
P = arm.pose.bones
def rot(name, axis, deg):
    """Rotate a bone about a WORLD axis through its head (armature space ~ world after import)."""
    pb = P['mixamorig:' + name]
    bpy.context.view_layer.update()
    w = arm.matrix_world
    head_w = w @ pb.head
    R = Matrix.Translation(head_w) @ Matrix.Rotation(math.radians(deg), 4, axis) @ Matrix.Translation(-head_w)
    pb.matrix = w.inverted() @ R @ w @ pb.matrix
    bpy.context.view_layer.update()
X, Y, Z = Vector((1, 0, 0)), Vector((0, 1, 0)), Vector((0, 0, 1))
# model faces -Y. Thighs forward (+90 about X lifts toward -Y), knees back down.
for s in ('Left', 'Right'):
    rot(s + 'UpLeg', X, -85)
    rot(s + 'Leg', X, 85)
rot('LeftArm', Y, 40); rot('RightArm', Y, -40)
rot('LeftArm', X, -45); rot('RightArm', X, -45)
rot('LeftForeArm', X, -50); rot('RightForeArm', X, -50)
rot('Spine1', X, -8); rot('Head', X, 6)
bpy.context.view_layer.update()
dg = bpy.context.evaluated_depsgraph_get()
pts = [mesh.matrix_world @ v.co for v in mesh.evaluated_get(dg).to_mesh().vertices]
mn = Vector([min(p[i] for p in pts) for i in range(3)]); mx = Vector([max(p[i] for p in pts) for i in range(3)])
ctr = (mn + mx) / 2; size = mx - mn
print('POSED BOUNDS', tuple(round(v, 2) for v in mn), tuple(round(v, 2) for v in mx))
sc = bpy.context.scene
sc.render.engine = 'BLENDER_EEVEE'
sc.render.resolution_x = sc.render.resolution_y = 900
sc.world = bpy.data.worlds.new('w'); sc.world.use_nodes = True
sc.world.node_tree.nodes['Background'].inputs[0].default_value = (0.78, 0.78, 0.78, 1)
sc.view_settings.view_transform = 'Standard'
for r, e in (((50, 0, 30), 3.0), ((60, 0, 200), 1.5)):
    l = bpy.data.objects.new('sun', bpy.data.lights.new('sun', 'SUN')); l.data.energy = e
    l.rotation_euler = [math.radians(a) for a in r]; sc.collection.objects.link(l)
cam = bpy.data.objects.new('cam', bpy.data.cameras.new('cam')); sc.collection.objects.link(cam); sc.camera = cam
cam.data.type = 'ORTHO'; cam.data.ortho_scale = max(size) * 1.15
d = max(size) * 4
for name, ang in (('front', 0), ('side', 90), ('three-quarter', 40)):
    a = math.radians(ang)
    cam.location = (ctr.x + math.sin(a) * d, ctr.y - math.cos(a) * d, ctr.z)
    cam.rotation_euler = (ctr - cam.location).to_track_quat('-Z', 'Y').to_euler()
    sc.render.filepath = f'{out}_{name}.png'; bpy.ops.render.render(write_still=True)
