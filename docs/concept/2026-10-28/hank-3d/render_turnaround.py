import bpy, sys, math, mathutils
argv = sys.argv[sys.argv.index('--') + 1:]
src, out = argv[0], argv[1]
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=src)
meshes = [o for o in bpy.context.scene.objects if o.type == 'MESH']
bpy.context.view_layer.update()
dg = bpy.context.evaluated_depsgraph_get()
pts = [o.matrix_world @ v.co for o in meshes for v in o.evaluated_get(dg).to_mesh().vertices]
mn = mathutils.Vector((min(p.x for p in pts), min(p.y for p in pts), min(p.z for p in pts)))
mx = mathutils.Vector((max(p.x for p in pts), max(p.y for p in pts), max(p.z for p in pts)))
ctr = (mn + mx) / 2; size = mx - mn
print('BOUNDS', tuple(round(v, 3) for v in mn), tuple(round(v, 3) for v in mx))
sc = bpy.context.scene
sc.render.engine = 'BLENDER_EEVEE_NEXT' if 'BLENDER_EEVEE_NEXT' in [e.identifier for e in bpy.types.RenderSettings.bl_rna.properties['engine'].enum_items] else 'BLENDER_EEVEE'
sc.render.resolution_x, sc.render.resolution_y = 900, 1600
sc.world = bpy.data.worlds.new('w'); sc.world.use_nodes = True
sc.world.node_tree.nodes['Background'].inputs[0].default_value = (0.78, 0.78, 0.78, 1)
sc.world.node_tree.nodes['Background'].inputs[1].default_value = 1.0
sc.view_settings.view_transform = 'Standard'
for rot, e in (((50, 0, 30), 3.0), ((60, 0, 200), 1.5)):
    l = bpy.data.objects.new('sun', bpy.data.lights.new('sun', 'SUN')); l.data.energy = e
    l.rotation_euler = [math.radians(a) for a in rot]; sc.collection.objects.link(l)
cam = bpy.data.objects.new('cam', bpy.data.cameras.new('cam')); sc.collection.objects.link(cam); sc.camera = cam
cam.data.type = 'ORTHO'; cam.data.ortho_scale = size.z * 1.12
d = max(size) * 3
# glTF import: -Y is the model's front in Blender
views = {'front': (0, -1), 'right': (1, 0), 'left': (-1, 0), 'back': (0, 1)}
for name, (x, y) in views.items():
    cam.location = (ctr.x + x * d, ctr.y + y * d, ctr.z)
    cam.rotation_euler = (mathutils.Vector((ctr.x, ctr.y, ctr.z)) - cam.location).to_track_quat('-Z', 'Y').to_euler()
    sc.render.filepath = f'{out}_{name}.png'
    bpy.ops.render.render(write_still=True)
cam.data.ortho_scale = size.z * 0.16
cam.location = (ctr.x, ctr.y - d, mx.z - size.z * 0.1)
cam.rotation_euler = (math.radians(90), 0, 0)
sc.render.resolution_x = sc.render.resolution_y = 1000
sc.render.filepath = f'{out}_face.png'
bpy.ops.render.render(write_still=True)
