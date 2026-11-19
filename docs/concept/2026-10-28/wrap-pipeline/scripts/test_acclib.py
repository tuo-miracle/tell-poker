import numpy as np, sys, os
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import acclib as al

def strip(n):
    """n quads along x as triangles: verts (2n+2), x = 0..n."""
    V = np.array([[x, 0.0, z] for x in range(n + 1) for z in (0.0, 1.0)])
    F = []
    for x in range(n):
        a, b, c, d = 2 * x, 2 * x + 1, 2 * x + 2, 2 * x + 3; F += [[a, c, b], [b, c, d]]
    return V, np.array(F)

def test_seeds_need_thin_and_inside_box():
    th = np.array([0.001, 0.001, 0.02, 0.001]); P = np.array([[0, 0, 0], [0.1, 0, 0], [0, 0, 0], [0, 0, 0.5]])
    s = al.thin_seeds(th, P, t_max=0.008, box=((-0.05, 0.05), (-1, 1), (-0.1, 0.1)))
    assert s.tolist() == [True, False, False, False]

def test_grow_region_stays_inside_allowed():
    V, F = strip(5); seed = np.zeros(len(V), bool); seed[0] = True
    allowed = V[:, 0] <= 3
    g = al.grow_region(seed, F, allowed)
    assert g[V[:, 0] <= 3].all() and not g[V[:, 0] > 3].any()

def test_grow_region_does_not_jump_gaps():
    V, F = strip(2); V2, F2 = strip(2); F2 = F2 + len(V)
    seed = np.zeros(2 * len(V), bool); seed[0] = True
    g = al.grow_region(seed, np.vstack([F, F2]), np.ones(2 * len(V), bool))
    assert g[:len(V)].all() and not g[len(V):].any()

def test_faces_inside_requires_all_verts():
    V, F = strip(2); m = V[:, 0] <= 1
    assert al.faces_inside(m, F).tolist() == [True, True, False, False]

def test_clearance_shift():
    assert al.clearance_shift(np.array([0.004, 0.002, 0.003]), margin=0.001) == 0.0
    assert abs(al.clearance_shift(np.array([0.004, -0.0005, 0.003]), margin=0.001) - 0.0015) < 1e-12

def ring_points(cx, cz, rx, rz, y, n=200, gap=None):
    a = np.linspace(0, 2 * np.pi, n, endpoint=False)
    if gap: a = a[(a < gap[0]) | (a > gap[1])]
    return np.c_[cx + rx * np.cos(a), np.full(len(a), y), cz + rz * np.sin(a)]

def test_lens_outline_recovers_ellipse():
    pts = ring_points(0.02, 0.92, 0.015, 0.01, -0.06)
    O = al.lens_outline(pts, nbins=24)
    assert O.shape == (24, 3)
    r = np.hypot((O[:, 0] - 0.02) / 0.015, (O[:, 2] - 0.92) / 0.01)
    assert np.abs(r - 1).max() < 0.03 and np.allclose(O[:, 1], -0.06)

def test_lens_outline_fills_missing_bottom():
    pts = ring_points(0.0, 0.0, 0.015, 0.01, -0.06, gap=(1.2 * np.pi, 1.8 * np.pi))  # bottom rim fused into the cheek
    O = al.lens_outline(pts, nbins=24, center=(0.0, 0.0))
    bottom = O[np.argmin(O[:, 2])]
    assert abs(bottom[0]) < 0.003 and -0.0115 < bottom[2] < -0.0085

def test_lens_outline_is_ordered_counterclockwise_from_plus_x():
    O = al.lens_outline(ring_points(0, 0, 0.01, 0.01, 0), nbins=8, center=(0, 0))
    ang = np.unwrap(np.arctan2(O[:, 2], O[:, 0]))
    assert (np.diff(ang) > 0).all()

def test_lens_outline_smooths_noise_and_puts_the_rim_on_a_plane():
    rng = np.random.default_rng(1)
    pts = ring_points(0.0, 0.0, 0.015, 0.010, 0.0, n=400)
    pts[:, 1] = -0.06 + 0.1 * pts[:, 2] + rng.normal(0, 0.001, len(pts))  # tilted lens, noisy depth
    pts[:, [0, 2]] *= (1 + rng.normal(0, 0.05, len(pts)))[:, None]          # noisy radius
    O = al.lens_outline(pts, 24, center=(0, 0), order=3, plane=True)
    assert np.abs(O[:, 1] - (-0.06 + 0.1 * O[:, 2])).max() < 2e-4
    r = np.hypot(O[:, 0] / 0.015, O[:, 2] / 0.010)
    assert np.abs(np.diff(np.r_[r, r[0]])).max() < 0.05  # no zigzag between neighbouring bins

def test_mirror_average_makes_the_pair_symmetric():
    R = al.lens_outline(ring_points(-0.02, 0.92, 0.015, 0.010, -0.06), 24, center=(-0.02, 0.92))
    L = al.lens_outline(ring_points(0.022, 0.92, 0.013, 0.011, -0.058), 24, center=(0.022, 0.92))
    R2, L2 = al.mirror_average(R, L)
    m = L2 * [-1, 1, 1]  # mirrored left, same bin order as the right after reversal
    assert np.allclose(np.sort(R2[:, 0]), np.sort(m[:, 0])) and np.allclose(np.sort(R2[:, 2]), np.sort(m[:, 2]))
    assert abs(R2[:, 0].mean() + 0.021) < 1e-9 and abs(L2[:, 0].mean() - 0.021) < 1e-9  # centres kept symmetric about x = 0

def test_tube_closed_ring():
    t = np.linspace(0, 2 * np.pi, 12, endpoint=False); C = np.c_[np.cos(t), np.zeros(12), np.sin(t)]
    V, F = al.tube(C, 0.1, nseg=6, closed=True)
    assert V.shape == (72, 3) and F.shape == (144, 3)
    d = np.linalg.norm(V.reshape(12, 6, 3) - C[:, None], axis=2)
    assert np.allclose(d, 0.1)
    assert F.max() == 71 and len(np.unique(np.sort(F, 1), axis=0)) == 144

def test_tube_open_has_caps():
    C = np.c_[np.zeros(3), np.linspace(0, 1, 3), np.zeros(3)]
    V, F = al.tube(C, 0.05, nseg=6, closed=False)
    assert V.shape == (18 + 2, 3) and len(F) == 2 * 6 * 2 + 2 * 6

def test_oval_outline_keeps_extent_and_plane():
    O = ring_points(0.02, 0.92, 0.015, 0.010, 0.0, n=24); O[:, 1] = -0.06 + 0.1 * (O[:, 2] - 0.92)
    O[3, 2] += 0.002  # a bump that the oval should not follow
    V = al.oval_outline(O, nbins=32, exp=2.0)
    assert V.shape == (32, 3)
    assert abs(V[:, 0].min() - O[:, 0].min()) < 1e-3 and abs(V[:, 0].max() - O[:, 0].max()) < 1e-3
    assert np.allclose(V[:, 1], -0.06 + 0.1 * (V[:, 2] - 0.92), atol=2e-4)
    r = np.hypot((V[:, 0] - V[:, 0].mean()) / 0.015, (V[:, 2] - V[:, 2].mean()) / 0.010)
    assert np.abs(r - r.mean()).max() < 0.08  # exp 2 = an ellipse

def test_neck_ramp_moves_neck_head_weight_to_spine_below_z1():
    w = np.array([[0.6, 0.2, 0.2], [0.6, 0.2, 0.2], [0.6, 0.2, 0.2]])  # neck, head, spine2
    z = np.array([0.80, 0.84, 0.90])
    out = al.neck_ramp(w, z, z0=0.82, z1=0.86)
    assert np.allclose(out[0], [0, 0, 1.0]) and np.allclose(out[2], w[2])
    assert np.allclose(out[1], [0.3, 0.1, 0.6]) and np.allclose(out.sum(1), 1.0)

def test_frame_paint_mask_picks_grey_and_unbaked_texels_in_the_zone_only():
    img = np.zeros((4, 4, 3), np.uint8); img[:] = (90, 120, 170)
    img[0, 0] = (190, 195, 200); img[0, 1] = (0, 0, 0); img[3, 3] = (190, 195, 200)
    zone = np.zeros((4, 4), bool); zone[:2] = True
    m = al.frame_paint_mask(img, zone, sat_max=0.2)
    assert m[0, 0] and m[0, 1] and not m[3, 3] and not m[1].any()

if __name__ == '__main__':
    n = 0
    for k, f in list(globals().items()):
        if k.startswith('test_'): f(); n += 1; print('PASS', k)
    print('ALL', n)
