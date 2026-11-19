# Render previews of a fitted blend: args out_prefix mode(clay|tex) size view(front|side|back|q) then name:key=v,key=v ...
import bpy, sys, os, json
sys.path.insert(0, os.path.dirname(__file__)); import common
from mathutils import Vector
argv = sys.argv[sys.argv.index('--') + 1:]
import wraplib as wl; FN_NAME = wl.char_names(argv.pop(0))['obj']
prefix, mode, size, view = argv[0], argv[1], float(argv[2]), argv[3]
looks = argv[4:]
for o in list(bpy.data.objects):
    if o.type == 'MESH' and len(o.data.polygons) < 200 and o.name.startswith('Ico'): bpy.data.objects.remove(o)
FN = bpy.data.objects[FN_NAME]; arm = FN.parent
info = json.loads(FN['eyes'])
c = (Vector(info['L']['center']) + Vector(info['R']['center'])) / 2
if mode == 'clay':
    m = bpy.data.materials.new('clay'); m.use_nodes = True; m.node_tree.nodes['Principled BSDF'].inputs['Base Color'].default_value = (0.3, 0.45, 0.8, 1) if os.environ.get('BLUE') else (0.55, 0.6, 0.65, 1)
    m2 = bpy.data.materials.new('clay2'); m2.use_nodes = True; m2.node_tree.nodes['Principled BSDF'].inputs['Base Color'].default_value = (0.8, 0.5, 0.4, 1)
    m3 = bpy.data.materials.new('clay3'); m3.use_nodes = True; m3.node_tree.nodes['Principled BSDF'].inputs['Base Color'].default_value = (0.9, 0.9, 0.85, 1)
    for o in bpy.data.objects:
        if o.type == 'MESH':
            for i in range(len(o.data.materials)): o.data.materials[i] = (m2 if i == 0 else m3) if o is FN else m
if mode == 'sdist':
    m = bpy.data.materials.new('sd'); m.use_nodes = True; nt = m.node_tree
    a = nt.nodes.new('ShaderNodeAttribute'); a.attribute_name = os.environ.get('ATTR', 'sdist')
    mr = nt.nodes.new('ShaderNodeMapRange'); mr.inputs[1].default_value = float(os.environ.get('AMIN', -0.1)); mr.inputs[2].default_value = float(os.environ.get('AMAX', 0.1))
    cr = nt.nodes.new('ShaderNodeValToRGB'); cr.color_ramp.elements[0].color = (0, 0.2, 1, 1); cr.color_ramp.elements[1].color = (1, 0.1, 0, 1)
    e = cr.color_ramp.elements.new(0.5); e.color = (0.9, 0.9, 0.9, 1)
    nt.links.new(a.outputs['Fac'], mr.inputs[0]); nt.links.new(mr.outputs[0], cr.inputs[0])
    nt.links.new(cr.outputs[0], nt.nodes['Principled BSDF'].inputs['Base Color'])
    FN.data.materials[0] = m
    for o in bpy.data.objects:
        if o.type == 'MESH' and o is not FN: o.hide_render = True
if os.environ.get('MAG1'):
    mg = bpy.data.materials.new('mag'); mg.use_nodes = True; mg.node_tree.nodes['Principled BSDF'].inputs['Base Color'].default_value = (1, 0, 1, 1)
    FN.data.materials[1] = mg
if os.environ.get('HIDEEYES'):
    import bmesh as _bm
    b = _bm.new(); b.from_mesh(FN.data); pa = b.verts.layers.int.get('part'); b.verts.ensure_lookup_table()
    _bm.ops.delete(b, geom=[v for v in b.verts if v[pa] > 0], context='VERTS'); b.to_mesh(FN.data); b.free()
if os.environ.get('TEX'):
    for n in FN.data.materials[0].node_tree.nodes:
        if n.type == 'TEX_IMAGE': n.image = bpy.data.images.load(os.environ['TEX'])
sc = common.setup_scene(1024, 0.45, sun=not os.environ.get('NOSUN'))
if os.environ.get("HIDEFACE"): FN.hide_render = True
if os.environ.get("ONLYFACE"): [setattr(o, "hide_render", True) for o in bpy.data.objects if o.type == "MESH" and o is not FN]
dirs = {'front': (0, 1, 0), 'side': (-1, 0, 0), 'back': (0, -1, 0), 'q': (-0.6, 1, -0.1), 'low': (0, 1, 0.35)}
cz = c.z - size * 0.12 if size < 0.5 else c.z
center = Vector((c.x, c.y, cz)) if size < 0.5 else Vector((c.x, 0, size / 2 * 0.95))
center.z += float(os.environ.get('DZ', 0)); center.x += float(os.environ.get('DX', 0))
for spec in looks:
    name, _, kv = spec.partition(':')
    vals = dict((k, float(v)) for k, v in (x.split('=') for x in kv.split(',') if x))
    for k in FN.data.shape_keys.key_blocks[1:]: k.value = vals.get(k.name, 0.0)
    for b in ('mixamorig:LeftEye', 'mixamorig:RightEye'):
        if b in arm.pose.bones:
            pb = arm.pose.bones[b]; pb.rotation_mode = 'XYZ'; pb.rotation_euler = (0, 0, 0)
            ax = os.environ.get('EYEAX', 'Z'); ang = __import__('math').radians(vals.get('eyeYaw', 0))
            pb.rotation_euler = {'X': (ang, 0, 0), 'Y': (0, ang, 0), 'Z': (0, 0, ang)}[ax]
    bpy.context.view_layer.update()
    if 'eyeYaw' in vals:
        for b in ('mixamorig:LeftEye',):
            M = arm.matrix_world @ arm.pose.bones[b].matrix; print('EYEDIR', vals['eyeYaw'], tuple(round(x, 3) for x in (M.to_3x3() @ Vector((0, 1, 0)))))
    for o in [o for o in sc.objects if o.type == 'CAMERA']: bpy.data.objects.remove(o)
    common.cam_ortho(center, size, dirs[view], 3.0)
    common.render(f'{prefix}_{name}.png')
