import bpy, sys, math, json
from mathutils import Vector
argv = sys.argv[sys.argv.index('--') + 1:]
src, out = argv[0], argv[1]
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=src)
arm = [o for o in bpy.context.scene.objects if o.type == 'ARMATURE'][0]
for o in bpy.context.scene.objects:
    if o.type == 'MESH' and len(o.data.polygons) < 1000: o.hide_render = True
w = arm.matrix_world
head = w @ arm.pose.bones['mixamorig:Head'].head
top = w @ arm.pose.bones['mixamorig:HeadTop_End'].head
c = (head + top) / 2; s = (top - head).length * 1.6
sc = bpy.context.scene; sc.render.engine = 'BLENDER_EEVEE'
W = 1024; sc.render.resolution_x = sc.render.resolution_y = W
sc.world = bpy.data.worlds.new('w'); sc.world.use_nodes = True
sc.world.node_tree.nodes['Background'].inputs[0].default_value = (0.8, 0.8, 0.8, 1)
sc.world.node_tree.nodes['Background'].inputs[1].default_value = 1.2
sc.view_settings.view_transform = 'Standard'
cam = bpy.data.objects.new('cam', bpy.data.cameras.new('cam')); sc.collection.objects.link(cam); sc.camera = cam
cam.data.type = 'ORTHO'; cam.data.ortho_scale = s
cam.location = (c.x, c.y - 3, c.z); cam.rotation_euler = (math.radians(90), 0, 0)
sc.render.filepath = out + '.png'; bpy.ops.render.render(write_still=True)
json.dump({'cx': c.x, 'cy': c.y, 'cz': c.z, 's': s, 'W': W}, open(out + '.json', 'w'))
print('FACECAM', c, s)
