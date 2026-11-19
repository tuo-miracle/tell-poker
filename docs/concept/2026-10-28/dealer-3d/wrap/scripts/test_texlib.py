import numpy as np, sys, os
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import texlib as tl

def test_lab_reference_values():
    lab = tl.srgb8_to_lab(np.array([[255, 255, 255], [255, 0, 0], [0, 0, 0]]))
    assert np.allclose(lab[0], [100, 0, 0], atol=0.05)
    assert np.allclose(lab[1], [53.24, 80.09, 67.20], atol=0.05)
    assert np.allclose(lab[2], [0, 0, 0], atol=0.05)

def test_delta_e2000_sharma_pairs():
    # Sharma, Wu, Dalal (2005) test data, pairs 1 and 17
    a = np.array([[50, 2.6772, -79.7751], [50, 2.5, 0]]); b = np.array([[50, 0, -82.7485], [73, 25, -18]])
    assert np.allclose(tl.delta_e2000(a, b), [2.0425, 27.1492], atol=1e-3)

def test_seam_bands_split_by_distance():
    face = np.zeros((40, 40), bool); face[:, :20] = True; other = ~face
    fb, ob = tl.seam_bands(face, other, 2, 6)
    cols_f = np.unique(np.nonzero(fb)[1]); cols_o = np.unique(np.nonzero(ob)[1])
    assert cols_f.min() >= 13 and cols_f.max() <= 18, cols_f
    assert cols_o.min() >= 21 and cols_o.max() <= 26, cols_o

def test_raster_vertex_field_interpolates_in_uv():
    tris_uv = np.array([[[0.1, 0.1], [0.9, 0.1], [0.1, 0.9]]]); vals = np.array([[1.0, 2.0, 3.0]])
    img, cov = tl.raster_vertex_field(tris_uv, vals, 100)
    # uv (0.1,0.1) -> pixel x=10, y=(1-0.1)*100=90 ; centroid value 2
    assert abs(img[90 - 1, 10 + 1] - 1.0) < 0.1 and cov[50, 30]
    cx, cy = int(100 * (0.1 + 0.8 / 3)), int(100 * (1 - (0.1 + 0.8 / 3)))
    assert abs(img[cy, cx] - 2.0) < 0.1 and not cov[5, 95]

def test_iris_repaint_dark_pupil_brown_iris_untouched_outside():
    rng = np.random.default_rng(0)
    im = np.full((200, 200, 3), 200, np.uint8); yy, xx = np.mgrid[:200, :200]; r = np.hypot(xx - 100, yy - 100)
    im[r < 40] = (120, 150, 130)  # pale greenish iris (BGR)
    im[r < 15] = (30, 30, 30); im[(r > 20) & (r < 24) & (xx > 100)] = (250, 250, 250)  # pupil + baked catchlight
    im = np.clip(im.astype(int) + rng.integers(-5, 6, im.shape), 0, 255).astype(np.uint8)
    out = tl.repaint_iris(im, (100, 100), 40, 15)
    assert out[r < 13].mean() < 20                         # pupil black
    iris = out[(r > 18) & (r < 34)].reshape(-1, 3).mean(0)  # BGR
    assert iris[2] > iris[1] > iris[0] and iris.mean() < 110, iris   # brown: R > G > B, darker than before
    limb = out[(r > 37) & (r < 40)].mean(); mid = out[(r > 25) & (r < 30)].mean()
    assert limb < mid                                        # dark limbal ring
    assert out[(r > 24) & (r < 34) & (xx > 100)].max() < 200  # catchlight removed
    assert np.array_equal(out[r > 46], im[r > 46])            # sclera untouched

def test_seam_gains_match_border_and_feather_to_one():
    # 1-D strip: Hank (linear 0.24) for x<0, face (linear 0.30) for x>=0; rings = x / 5 mm
    xs = np.arange(0, 0.05, 0.005); face_pos = np.c_[xs, np.zeros_like(xs), np.zeros_like(xs)]
    ring = xs / 0.005; face_lin = np.full((len(xs), 3), 0.30)
    hx = -np.arange(0.001, 0.02, 0.002); hank_pos = np.c_[hx, np.zeros_like(hx), np.zeros_like(hx)]; hank_lin = np.full((len(hx), 3), 0.24)
    g = tl.seam_gains(face_pos, face_lin, ring, hank_pos, hank_lin, radius=0.012, src_rings=(0, 3), full=1, fade=4)
    assert np.allclose(g[ring <= 1], 0.24 / 0.30, atol=1e-6)   # full correction up to ring 1
    assert np.allclose(g[ring >= 5], 1.0)                      # untouched from ring 5
    assert np.all(np.diff(g[:, 0]) >= -1e-9)                   # monotone feather

def test_apply_gain_linear():
    im = np.full((4, 4, 3), 128, np.uint8); g = np.full((4, 4, 3), 0.5)
    out = tl.apply_gain_srgb(im, g)
    lin = tl.srgb8_to_linear(np.array([128.]))[0] * 0.5
    assert np.allclose(tl.srgb8_to_linear(out[0, 0].astype(float)), lin, atol=0.003)

if __name__ == '__main__':
    n = 0
    for k, f in list(globals().items()):
        if k.startswith('test_'): f(); n += 1; print('PASS', k)
    print('ALL', n)
