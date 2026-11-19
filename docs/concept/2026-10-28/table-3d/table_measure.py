# Measure the table: heights of upward-facing surfaces (felt vs rail), footprint at felt height, outline shape.
import bpy, sys, numpy as np
argv = sys.argv[sys.argv.index('--') + 1:]
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=argv[0])
o = [o for o in bpy.context.scene.objects if o.type == 'MESH'][0]
me = o.data; me.calc_loop_triangles()
n = len(me.polygons)
c = np.empty(n * 3); me.polygons.foreach_get('center', c); c = c.reshape(-1, 3)
nm = np.empty(n * 3); me.polygons.foreach_get('normal', nm); nm = nm.reshape(-1, 3)
ar = np.empty(n); me.polygons.foreach_get('area', ar)
up = nm[:, 2] > 0.95
P = lambda *a: print('MEAS', *a, flush=True)
h, edges = np.histogram(c[up, 2], bins=78, range=(0, 0.39), weights=ar[up])
for i in np.argsort(h)[::-1][:8]: P('up-facing area %.4f m2 at z %.3f-%.3f' % (h[i], edges[i], edges[i + 1]))
# central column: highest up-facing surface near the centre
cen = up & (np.abs(c[:, 0]) < 0.1) & (np.abs(c[:, 1]) < 0.05)
P('centre up-facing z', np.round(np.percentile(c[cen, 2], [0, 50, 100]), 4) if cen.any() else None)
# radial profile along +X and +Y: top z vs position
V = np.empty(len(me.vertices) * 3); me.vertices.foreach_get('co', V); V = V.reshape(-1, 3)
for axis, name in ((0, 'X(length)'), (1, 'Y(width)')):
    other = 1 - axis
    band = np.abs(V[:, other]) < 0.01
    xs = V[band, axis]; zs = V[band, 2]
    prof = []
    for b in np.arange(0, 0.52, 0.02):
        m = (xs >= b) & (xs < b + 0.02)
        if m.any(): prof.append('%.2f:%.3f' % (b, zs[m].max()))
    P('top profile along', name, ' '.join(prof))
# felt extent: up-facing faces whose z is within 5mm of the centre felt height
fz = np.median(c[cen, 2])
felt = up & (np.abs(c[:, 2] - fz) < 0.005)
P('felt z %.4f extent x %.4f..%.4f y %.4f..%.4f' % (fz, c[felt, 0].min(), c[felt, 0].max(), c[felt, 1].min(), c[felt, 1].max()))
# outline at mid-height (is it oval/rect?) : max radius per angle at top
ang = np.degrees(np.arctan2(V[:, 1], V[:, 0]))
for a in (0, 30, 45, 60, 90):
    m = np.abs(ang - a) < 1.5
    r = np.hypot(V[m, 0], V[m, 1]).max(); P('outline radius at %d deg: %.3f' % (a, r))
# tighter felt extent: perfectly flat (normal z > 0.999) faces within 1 mm of the centre felt height
felt2 = (nm[:, 2] > 0.999) & (np.abs(c[:, 2] - fz) < 0.001)
P('felt(tight) extent x %.4f..%.4f y %.4f..%.4f area %.4f' % (c[felt2, 0].min(), c[felt2, 0].max(), c[felt2, 1].min(), c[felt2, 1].max(), ar[felt2].sum()))
for x0 in (0.0, 0.2):
    m = felt2 & (np.abs(c[:, 0] - x0) < 0.02); P('felt y-range at x=%.1f: %.4f..%.4f' % (x0, c[m, 1].min(), c[m, 1].max()))
