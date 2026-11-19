import numpy as np, sys, os
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import wraplib as wl
rng = np.random.default_rng(0)

def rot(ax, ang):
    ax = np.asarray(ax, float); ax /= np.linalg.norm(ax); K = np.array([[0,-ax[2],ax[1]],[ax[2],0,-ax[0]],[-ax[1],ax[0],0]])
    return np.eye(3) + np.sin(ang)*K + (1-np.cos(ang))*K@K

def test_umeyama_recovers_similarity():
    A = rng.normal(size=(50, 3)); R = rot((1, 2, 3), 0.7); s = 0.55; t = np.array([0.1, -2, 3])
    B = s * A @ R.T + t
    s2, R2, t2 = wl.umeyama(A, B)
    assert abs(s2 - s) < 1e-9 and np.allclose(R2, R) and np.allclose(t2, t)
    assert np.allclose(wl.apply_sim((s2, R2, t2), A), B)

def test_tps_interpolates_and_keeps_affine():
    C = rng.normal(size=(40, 3)); T = C + 0.1 * np.sin(C * 3)
    f = wl.TPS(C, T, lam=0.0)
    assert np.allclose(f(C), T, atol=1e-8)
    M = np.array([[1.1, .1, 0], [0, .9, .2], [0, 0, 1.2]]); b = np.array([1, 2, 3.])
    g = wl.TPS(C, C @ M.T + b, lam=1e-6)
    X = rng.normal(size=(20, 3)) * 3
    assert np.allclose(g(X), X @ M.T + b, atol=1e-5)

def test_tps_regularisation_is_monotone_and_bounded():
    C = rng.normal(size=(120, 3)) * 0.05; T = C + rng.normal(size=C.shape) * 0.003
    aff = wl.TPS(C, T, lam=1e6); r_aff = np.linalg.norm(aff(C) - T, axis=1).mean()
    prev = 0.0
    for lam in (1e-4, 1e-3, 3e-3, 1e-2, 3e-2, 1e-1, 1.0):
        r = np.linalg.norm(wl.TPS(C, T, lam=lam)(C) - T, axis=1).mean()
        assert r >= prev - 1e-9 and r <= r_aff + 1e-9, (lam, r, prev, r_aff); prev = r

def test_fit_sphere_ratio_recovers_centre_and_radius():
    d = rng.normal(size=(200, 3)); d /= np.linalg.norm(d, axis=1, keepdims=True)
    rho = rng.uniform(0.6, 1.5, 200); c = np.array([0.1, -0.2, 0.9]); r = 0.0075
    pts = c + d * (rho * r)[:, None]
    c2, r2 = wl.fit_sphere_ratio(pts, rho, c + 0.002, r * 1.3)
    assert np.allclose(c2, c, atol=1e-7) and abs(r2 - r) < 1e-8

def test_smooth_field_respects_constraints_and_fills():
    # chain graph 0-1-2-3-4, constraints at ends
    edges = np.array([[0, 1], [1, 2], [2, 3], [3, 4]])
    w = np.array([1, 0, 0, 0, 1.]); c = np.array([[0, 0, 0], [9, 9, 9], [9, 9, 9], [9, 9, 9], [4, 0, 0.]])
    d = wl.smooth_field(5, edges, w, c, alpha=1e-3)
    assert np.allclose(d[0], c[0], atol=1e-2) and np.allclose(d[4], c[4], atol=1e-2)
    assert np.allclose(d[:, 0], [0, 1, 2, 3, 4], atol=2e-2)

def test_ramp():
    assert np.allclose(wl.ramp(np.array([0, 1, 2, 3.]), 1, 3), [0, 0, 0.5, 1])

def test_inside_polygon():
    sq = np.array([[0, 0], [2, 0], [2, 2], [0, 2.]])
    assert list(wl.inside_polygon(np.array([[1, 1], [3, 1], [-0.1, 1], [1.9, 1.9]]), sq)) == [True, False, False, True]

def test_boundary_loops_and_rings():
    # 3x3 grid of quads split into tris, centre quad removed -> outer loop (12 verts... 16-vertex grid) and inner hole loop
    import itertools
    idx = lambda x, y: y * 4 + x; F = []
    for x, y in itertools.product(range(3), range(3)):
        if (x, y) == (1, 1): continue
        a, b, c, d = idx(x, y), idx(x + 1, y), idx(x + 1, y + 1), idx(x, y + 1); F += [[a, b, c], [a, c, d]]
    loops = sorted(wl.boundary_loops(np.array(F)), key=len)
    assert [len(l) for l in loops] == [4, 12]
    ring = wl.ring_distance(16, wl.edges_of(np.array(F)), loops[1])
    assert ring[idx(0, 0)] == 0 and ring[idx(1, 1)] == 1

def test_char_names():
    n = wl.char_names('dealer')
    assert n == {'obj': 'DealerFace', 'mat': 'dealer_face', 'img': 'dealer_face_color'}
    assert wl.char_names('Hank')['obj'] == 'HankFace' and wl.char_names('Hank')['mat'] == 'hank_face'

def test_rot_between_maps_a_to_b():
    for a, b in (((0, -1, -0.15), (0.02, -1, -0.3)), ((1, 0, 0), (1, 0, 0)), ((0, 1, 0), (0, -1, 1e-9))):
        a = np.array(a, float) / np.linalg.norm(a); b = np.array(b, float) / np.linalg.norm(b)
        R = wl.rot_between(a, b)
        assert np.allclose(R @ a, b, atol=1e-7) and np.allclose(R @ R.T, np.eye(3), atol=1e-9) and abs(np.linalg.det(R) - 1) < 1e-9

def test_lid_lift_rotates_upper_lid_over_the_eye_and_falls_off():
    c = np.array([0.0, 0.0, 0.0]); r = 1.0
    lid = np.array([0, -1.1, -0.1]); brow = np.array([0, -1.3, 1.6]); low = np.array([0, -1.1, -0.7]); far = np.array([0, -3.0, 0.5])
    X = np.array([lid, brow, low, far])
    Y, w = wl.lid_lift(X, c, r, 20.0, r_full=1.25, r_zero=2.3, z0=-0.45, z1=-0.28)
    assert np.allclose(np.linalg.norm(Y - c, axis=1), np.linalg.norm(X - c, axis=1))   # stays on its shell: no new intersection
    assert Y[0, 2] > X[0, 2] + 0.3 and abs(w[0] - 1) < 1e-9                              # lid edge goes up, full weight
    assert 0 < w[1] < 1 and np.allclose(Y[2:], X[2:])                                    # brow partial; lower lid and far skin untouched
    a = np.degrees(np.arctan2(Y[0, 2], -Y[0, 1]) - np.arctan2(X[0, 2], -X[0, 1])); assert abs(a - 20) < 1e-6

def test_compensate_keys_keeps_closing_keys_absolute_and_opening_keys_relative():
    B = np.zeros((3, 3)); d = np.array([[0, 0, 0.3], [0, 0, 0.3], [0, 0, 0.0]]); B2 = B + d
    blink = np.array([[0, 0, -0.6], [0, 0, -0.1], [0, 0, -0.2]])   # closes: v0 beyond |d|, v1 partly
    wide = np.array([[0, 0, 0.2], [0, 0, 0.0], [0, 0, 0.0]])
    K = wl.compensate_keys(B, B2, {'blink': blink, 'wide': wide})
    assert np.allclose(K['blink'][0], blink[0])                     # absolute end position kept -> still closes
    assert np.allclose(K['blink'][1], blink[1] + d[1] * (1 - 0.1 / 0.3))
    assert np.allclose(K['blink'][2], blink[2])                      # no lift there: unchanged
    assert np.allclose(K['wide'], wide + d)                          # opening key keeps its delta from the new basis
    K2 = wl.compensate_keys(B, B2, {'blink': blink}, full_at=0.3)
    assert np.allclose(K2['blink'][1], blink[1])                     # 0.1 >= 0.3*0.3: counts as a full close

def test_fit_sphere_on_a_cap():
    d = rng.normal(size=(300, 3)); d /= np.linalg.norm(d, axis=1, keepdims=True); d = d[d[:, 1] < -0.1]  # front cap only
    c = np.array([0.02, 0.05, 0.9]); X = c + 0.009 * d
    c2, r2 = wl.fit_sphere(X)
    assert np.allclose(c2, c, atol=1e-9) and abs(r2 - 0.009) < 1e-9

def test_smooth_shell_displacement_softens_a_spike_and_keeps_radius():
    # ring of 12 points on a shell around c; one point displaced alone (a spike) along the shell
    c = np.zeros(3); t = np.linspace(0, 2 * np.pi, 12, endpoint=False); X = np.c_[np.cos(t), np.zeros(12), np.sin(t)] * 1.1
    E = np.array([[i, (i + 1) % 12] for i in range(12)])
    Y = X.copy(); Y[0] = [1.1 * np.cos(0.4), 0, 1.1 * np.sin(0.4)]
    Z = wl.smooth_shell_displacement(X, Y, E, c, iters=3, lam=0.5)
    assert np.allclose(np.linalg.norm(Z - c, axis=1), 1.1)                  # still on the shell: no new eye contact
    assert np.linalg.norm(Z[0] - X[0]) < np.linalg.norm(Y[0] - X[0])         # spike reduced
    assert np.linalg.norm(Z[1] - X[1]) > 0 and np.allclose(Z[6], X[6])       # spread to neighbours, far points untouched

def test_lid_lift_lower_lid_config_moves_lower_lid_down_only():
    c = np.zeros(3); upper = np.array([0, -1.1, 0.3]); lower = np.array([0, -1.1, -0.4]); mid = np.array([0, -1.1, 0.0])
    Y, w = wl.lid_lift(np.array([upper, lower, mid]), c, 1.0, -10.0, 1.25, 2.3, -0.10, -0.28)
    assert np.allclose(Y[0], upper) and np.allclose(Y[2], mid) and Y[1, 2] < lower[2] - 0.1 and abs(w[1] - 1) < 1e-9

def test_scale_about_keeps_the_pivot():
    X = np.array([[1.0, 0, 0], [3.0, 2, 0]]); Y = wl.scale_about(X, X[0], 0.5)
    assert np.allclose(Y[0], X[0]) and np.allclose(Y[1], [2, 1, 0])

def test_lid_arc_lerp_stays_on_the_shell_unlike_a_chord():
    c = np.zeros(3); a = np.array([[0, -1.0, 0.5]]); a = a / np.linalg.norm(a); b = np.array([[0, -1.0, -0.5]]); b = b / np.linalg.norm(b)
    X = wl.lid_arc_lerp(a, b, c, 0.5)
    assert abs(np.linalg.norm(X) - 1) < 1e-9 and abs(X[0, 2]) < 1e-9        # chord midpoint would be inside (r=0.894)
    assert np.allclose(wl.lid_arc_lerp(a, b, c, 0.0), a) and np.allclose(wl.lid_arc_lerp(a, b, c, 1.0), b)

def test_lid_arc_lerp_interpolates_radius_when_shells_differ():
    c = np.zeros(3); a = np.array([[0, -1.0, 0]]); b = np.array([[0, -2.0, 0]])
    assert np.allclose(wl.lid_arc_lerp(a, b, c, 0.5), [[0, -1.5, 0]])

def test_fraction_for_travel_hits_the_target():
    c = np.zeros(3); th = np.radians(np.array([30.0, 20.0]))
    a = np.c_[np.zeros(2), -np.cos(th), np.sin(th)]; b = np.c_[np.zeros(2), -np.cos(th), -np.sin(th)]
    f = wl.fraction_for_travel(a, b, c, 0.5)
    assert abs(np.median(np.linalg.norm(wl.lid_arc_lerp(a, b, c, f) - a, axis=1)) - 0.5) < 1e-3

if __name__ == '__main__':
    n = 0
    for k, f in list(globals().items()):
        if k.startswith('test_'): f(); n += 1; print('PASS', k)
    print('ALL', n)
