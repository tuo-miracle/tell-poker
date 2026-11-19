# Turnaround: front/right/back/left at identical framing, stitched side by side into one PNG.
# Adapted from hank-3d/render_turnaround.py: frames on the real mesh (ignores tiny helper meshes like Icosphere),
# same ortho scale + centre for all four views, optional camera elevation, optional face close-up.
# usage: blender -b -P render_turnaround.py -- src.glb out.png [elev_deg] [face_out.png]
import bpy, sys, math, numpy as np
from mathutils import Vector
argv = sys.argv[sys.argv.index('--') + 1:]
src, out = argv[0], argv[1]
elev = float(argv[2]) if len(argv) > 2 else 0.0
face_out = argv[3] if len(argv) > 3 else None
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=src)
sc = bpy.context.scene
meshes = [o for o in sc.objects if o.type == 'MESH' and len(o.data.polygons) > 1000]
for o in sc.objects:
    if o.type == 'MESH' and o not in meshes: o.hide_render = True
bpy.context.view_layer.update()
dg = bpy.context.evaluated_depsgraph_get()
pts = np.array([tuple(o.matrix_world @ v.co) for o in meshes for v in o.evaluated_get(dg).to_mesh().vertices])
mn, mx = Vector(pts.min(0)), Vector(pts.max(0)); ctr = (mn + mx) / 2; size = mx - mn
print('BOUNDS', tuple(round(v, 3) for v in mn), tuple(round(v, 3) for v in mx))
sc.render.engine = 'BLENDER_EEVEE'
sc.world = bpy.data.worlds.new('w'); sc.world.use_nodes = True
sc.world.node_tree.nodes['Background'].inputs[0].default_value = (0.78, 0.78, 0.78, 1)
sc.world.node_tree.nodes['Background'].inputs[1].default_value = 1.0
sc.view_settings.view_transform = 'Standard'
for rot, e in (((50, 0, 30), 3.0), ((60, 0, 200), 1.5)):
    l = bpy.data.objects.new('sun', bpy.data.lights.new('sun', 'SUN')); l.data.energy = e
    l.rotation_euler = [math.radians(a) for a in rot]; sc.collection.objects.link(l)
cam = bpy.data.objects.new('cam', bpy.data.cameras.new('cam')); sc.collection.objects.link(cam); sc.camera = cam
cam.data.type = 'ORTHO'; cam.data.clip_end = 1000
# one frame that fits every view: horizontal extent = bounding-circle diameter in XY, vertical = height (+ elevation)
horiz = math.hypot(size.x, size.y)
e = math.radians(elev)
vert = size.z * math.cos(e) + horiz * math.sin(e)
W = 800; Hpx = max(400, int(W * vert / horiz)) if vert < horiz else int(W * 1.8)
sc.render.resolution_x, sc.render.resolution_y = W, Hpx
aspect_h_over_w = Hpx / W
cam.data.sensor_fit = 'AUTO'
cam.data.ortho_scale = max(horiz, vert / aspect_h_over_w) * 1.06 * (1 if W >= Hpx else aspect_h_over_w)
d = max(size) * 4
views = [('front', (0, -1)), ('right', (1, 0)), ('back', (0, 1)), ('left', (-1, 0))]  # glTF front = -Y in Blender
files = []
for name, (x, y) in views:
    cam.location = (ctr.x + x * d * math.cos(e), ctr.y + y * d * math.cos(e), ctr.z + d * math.sin(e))
    cam.rotation_euler = (ctr - cam.location).to_track_quat('-Z', 'Y').to_euler()
    f = f'{out[:-4]}_{name}.png'; sc.render.filepath = f; bpy.ops.render.render(write_still=True); files.append(f)
def stitch(paths, dst):
    ims = [bpy.data.images.load(p) for p in paths]
    w, h = ims[0].size
    arrs = [np.array(im.pixels[:]).reshape(h, w, 4) for im in ims]
    gap = np.ones((h, 8, 4)); gap[..., :3] = 0.6
    row = np.concatenate(sum([[a, gap] for a in arrs], [])[:-1], axis=1)
    o = bpy.data.images.new('stitch', row.shape[1], h, alpha=True)
    o.pixels = row.ravel(); o.filepath_raw = dst; o.file_format = 'PNG'; o.save()
stitch(files, out)
import os
for f in files: os.remove(f)
if face_out:
    head = next(o for o in sc.objects if o.name.endswith('Head') and o.type == 'ARMATURE') if False else None
    arm = next(o for o in sc.objects if o.type == 'ARMATURE')
    hb = arm.matrix_world @ arm.pose.bones['mixamorig:Head'].head
    top = sc.objects['mixamorig:HeadTop_End'].matrix_world.translation
    c = (hb + top) / 2; s = (top - hb).length * 1.6
    sc.render.resolution_x = sc.render.resolution_y = 1024
    cam.data.ortho_scale = s
    cam.location = (c.x, c.y - 3, c.z); cam.rotation_euler = (math.radians(90), 0, 0)
    fs = [f'{face_out[:-4]}_f.png', f'{face_out[:-4]}_s.png']
    sc.render.filepath = fs[0]; bpy.ops.render.render(write_still=True)
    cam.location = (c.x + 3, c.y, c.z); cam.rotation_euler = (math.radians(90), 0, math.radians(90))
    sc.render.filepath = fs[1]; bpy.ops.render.render(write_still=True)
    stitch(fs, face_out)
    for f in fs: os.remove(f)
    print('FACECAM', tuple(round(v, 4) for v in c), round(s, 4))
