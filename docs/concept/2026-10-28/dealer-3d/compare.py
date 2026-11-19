# Before/after decimation comparison: same camera for both GLBs; each row = [before | after], textured + wireframe-free.
# Row 1: full front view. Row 2: close-up (dealer: face; table: near rail corner from above).
# usage: blender -b -P compare.py -- before.glb after.glb out.png elev_deg mode(dealer|table)
import bpy, sys, math, os, numpy as np
from mathutils import Vector
argv = sys.argv[sys.argv.index('--') + 1:]
A, Bf, out, elev, mode = argv[0], argv[1], argv[2], float(argv[3]), argv[4]
def load(src):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.gltf(filepath=src)
    sc = bpy.context.scene
    for o in sc.objects:
        if o.type == 'MESH' and len(o.data.polygons) <= 1000: o.hide_render = True
    sc.render.engine = 'BLENDER_EEVEE'
    sc.world = bpy.data.worlds.new('w'); sc.world.use_nodes = True
    sc.world.node_tree.nodes['Background'].inputs[0].default_value = (0.78, 0.78, 0.78, 1)
    sc.view_settings.view_transform = 'Standard'
    for rot, e in (((50, 0, 30), 3.0), ((60, 0, 200), 1.5)):
        l = bpy.data.objects.new('sun', bpy.data.lights.new('sun', 'SUN')); l.data.energy = e
        l.rotation_euler = [math.radians(a) for a in rot]; sc.collection.objects.link(l)
    cam = bpy.data.objects.new('cam', bpy.data.cameras.new('cam')); sc.collection.objects.link(cam); sc.camera = cam
    cam.data.type = 'ORTHO'; cam.data.clip_end = 100
    return sc, cam
def shot(sc, cam, ctr, scale, elev_deg, path, res=900, yaw=0):
    e = math.radians(elev_deg); a = math.radians(yaw); d = 5
    cam.data.ortho_scale = scale; sc.render.resolution_x = sc.render.resolution_y = res
    cam.location = (ctr[0] + d * math.cos(e) * math.sin(a), ctr[1] - d * math.cos(e) * math.cos(a), ctr[2] + d * math.sin(e))
    cam.rotation_euler = (Vector(ctr) - cam.location).to_track_quat('-Z', 'Y').to_euler()
    sc.render.filepath = path; bpy.ops.render.render(write_still=True)
shots = []
for i, src in enumerate((A, Bf)):
    sc, cam = load(src)
    if mode == 'dealer':
        full = ((0, 0, 0.5), 1.05, 0)
        arm = next(o for o in sc.objects if o.type == 'ARMATURE')
        h = arm.matrix_world @ arm.pose.bones['mixamorig:Head'].head
        t = sc.objects['mixamorig:HeadTop_End'].matrix_world.translation
        close = (tuple((h + t) / 2), (t - h).length * 1.6, 0)
    else:
        full = ((0, 0, 0.17), 1.1, elev)
        close = ((0.38, -0.2, 0.37), 0.3, 35)
    p1 = f'{out[:-4]}_{i}a.png'; p2 = f'{out[:-4]}_{i}b.png'
    shot(sc, cam, *full, p1); shot(sc, cam, *close, p2, yaw=0 if mode == 'dealer' else 30)
    shots.append((p1, p2))
def px(p):
    im = bpy.data.images.load(p); w, h = im.size; return np.array(im.pixels[:]).reshape(h, w, 4)
gap = lambda h: np.concatenate([np.full((h, 10, 3), 0.3), np.ones((h, 10, 1))], 2)
rows = [np.concatenate([px(shots[0][k]), gap(900), px(shots[1][k])], 1) for k in (1, 0)]  # pixel rows go bottom-up
hg = np.concatenate([np.full((10, rows[0].shape[1], 3), 0.3), np.ones((10, rows[0].shape[1], 1))], 2)
img = np.concatenate([rows[0], hg, rows[1]], 0)
o = bpy.data.images.new('cmp', img.shape[1], img.shape[0], alpha=True); o.pixels = img.ravel()
o.filepath_raw = out; o.file_format = 'PNG'; o.save()
for s in shots:
    for p in s: os.remove(p)
