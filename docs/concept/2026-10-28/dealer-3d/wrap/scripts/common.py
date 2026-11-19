import bpy, math, json
from mathutils import Vector

def setup_scene(W=1024, bg=0.8, engine='BLENDER_EEVEE', sun=True):
    sc = bpy.context.scene; sc.render.engine = engine
    sc.render.resolution_x = sc.render.resolution_y = W
    w = bpy.data.worlds.new('w'); sc.world = w; w.use_nodes = True
    w.node_tree.nodes['Background'].inputs[0].default_value = (bg, bg, bg, 1)
    w.node_tree.nodes['Background'].inputs[1].default_value = 1.0
    sc.view_settings.view_transform = 'Standard'
    # shadow terminator: with a hard sun, low-poly faces with smooth normals self-shadow in blocky patches (EEVEE
    # shadow-terminator artifact, like a Unity light with zero normal bias). Offset like a normal bias; applies to
    # every mesh in the scene, original and wrapped alike.
    for o in bpy.data.objects:
        if o.type == 'MESH': o.shadow_terminator_geometry_offset = 0.5; o.shadow_terminator_normal_offset = 0.5
    if sun:
        l = bpy.data.objects.new('sun', bpy.data.lights.new('sun', 'SUN')); l.data.energy = 1.6
        l.rotation_euler = (math.radians(50), 0, math.radians(25)); sc.collection.objects.link(l)
    return sc

def cam_ortho(center, size, direction=(0, 1, 0), dist=3.0, name='cam'):
    """camera at center - direction*dist, looking along direction (world). Ortho of given size."""
    sc = bpy.context.scene
    c = bpy.data.objects.new(name, bpy.data.cameras.new(name)); sc.collection.objects.link(c); sc.camera = c
    c.data.type = 'ORTHO'; c.data.ortho_scale = size; c.data.clip_end = 100
    d = Vector(direction).normalized(); c.location = Vector(center) - d * dist
    c.rotation_euler = look_rot(d)
    return c

def look_rot(d):
    from mathutils import Matrix
    z = -d; x = Vector((0, 0, 1)).cross(z).normalized(); y = z.cross(x)
    return Matrix((x, y, z)).transposed().to_euler()

def cam_persp(center, direction, dist, fov_deg, name='camp'):
    sc = bpy.context.scene
    c = bpy.data.objects.new(name, bpy.data.cameras.new(name)); sc.collection.objects.link(c); sc.camera = c
    c.data.type = 'PERSP'; c.data.angle = math.radians(fov_deg); c.data.clip_start = 0.01
    d = Vector(direction).normalized(); c.location = Vector(center) - d * dist
    c.rotation_euler = look_rot(d)
    return c

def render(path):
    bpy.context.scene.render.filepath = path; bpy.ops.render.render(write_still=True)

def tex_material(name, img_path_or_image, rough=0.6):
    m = bpy.data.materials.new(name); m.use_nodes = True
    nt = m.node_tree; bsdf = nt.nodes['Principled BSDF']
    tx = nt.nodes.new('ShaderNodeTexImage')
    tx.image = img_path_or_image if not isinstance(img_path_or_image, str) else bpy.data.images.load(img_path_or_image)
    nt.links.new(tx.outputs[0], bsdf.inputs['Base Color']); bsdf.inputs['Roughness'].default_value = rough
    return m
