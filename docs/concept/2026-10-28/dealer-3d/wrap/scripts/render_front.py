# Render orthographic front view of a head; write png + camera json (pixel->world mapping).
import bpy, sys, os, json
sys.path.insert(0, os.path.dirname(__file__)); import common
from mathutils import Vector
argv = sys.argv[sys.argv.index('--') + 1:]
src, out, cx, cy, cz, size = argv[0], argv[1], *map(float, argv[2:6])
extra = argv[6:]  # key=value shape key settings
if src.endswith('.blend'): bpy.ops.wm.open_mainfile(filepath=src)
else:
    bpy.ops.wm.read_factory_settings(use_empty=True); bpy.ops.import_scene.gltf(filepath=src)
for o in bpy.context.scene.objects:
    if o.type == 'MESH' and len(o.data.polygons) < 1000: o.hide_render = True
for kv in extra:
    k, v = kv.split('=')
    for o in bpy.context.scene.objects:
        if o.type == 'MESH' and o.data.shape_keys and k in o.data.shape_keys.key_blocks: o.data.shape_keys.key_blocks[k].value = float(v)
sc = common.setup_scene(1024, 0.8, sun=False)
sc.world.node_tree.nodes['Background'].inputs[1].default_value = 1.2
common.cam_ortho((cx, cy, cz), size, (0, 1, 0), 3.0)
common.render(out + '.png')
json.dump({'cx': cx, 'cy': cy - 3, 'cz': cz, 's': size, 'W': 1024}, open(out + '.json', 'w'))
