import numpy as np, sys, os
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import texfix as tf
rng = np.random.default_rng(0)

def test_srgb_roundtrip():
    x = np.linspace(0, 1, 101)
    assert np.allclose(tf.lin_to_srgb(tf.srgb_to_lin(x)), x, atol=1e-9)
    assert abs(tf.srgb_to_lin(np.array(0.5)) - 0.2140) < 1e-3

def test_band_gain_matches_means_in_linear_space():
    other = np.full((200, 3), 0.6); face = tf.lin_to_srgb(tf.srgb_to_lin(other) * np.array([0.8, 0.9, 1.0]))
    g = tf.band_gain(face, other)
    assert np.allclose(g, [1 / 0.8, 1 / 0.9, 1.0], atol=1e-6)

def test_band_gain_ignores_outliers_and_clips():
    other = np.full((300, 3), 0.6); other[:20] = [0.25, 0.1, 0.05]  # hair pixels in the outer band
    face = np.full((300, 3), 0.6)
    assert np.allclose(tf.band_gain(face, other), 1.0, atol=1e-6)
    assert np.all(tf.band_gain(np.full((10, 3), 0.1), np.full((10, 3), 0.9)) <= 1.5)

def test_apply_gain_weighted():
    img = np.full((4, 4, 3), 128, np.uint8); w = np.zeros((4, 4)); w[:, 2:] = 1.0
    out = tf.apply_gain(img, w, np.array([2.0, 1.0, 1.0]))
    assert (out[:, :2] == 128).all() and (out[:, 2:, 1:] == 128).all()
    assert abs(tf.srgb_to_lin(out[0, 3, 0] / 255.) - 2 * tf.srgb_to_lin(128 / 255.)) < 0.01

def synth_eye(n=200, c=(100, 100), rp=12, ri=35):
    yy, xx = np.mgrid[:n, :n]; r = np.hypot(xx - c[0], yy - c[1])
    img = np.where(r < rp, 10, np.where(r < ri, 110, 220)).astype(float)
    return np.repeat(img[..., None], 3, 2).astype(np.uint8)

def test_iris_radii_from_profile():
    img = synth_eye(); prof = tf.radial_profile(img.mean(2), (100, 100), 80)
    rp, ri = tf.iris_radii(prof)
    assert abs(rp - 12) <= 1.5 and abs(ri - 35) <= 1.5

def test_iris_radii_ignores_specular_highlights():
    img = synth_eye(); img[80:92, 108:122] = 255; img[103:110, 113:126] = 250  # baked highlight squares in the iris
    prof = tf.radial_profile(img.mean(2), (100, 100), 80)
    rp, ri = tf.iris_radii(prof)
    assert abs(rp - 12) <= 1.5 and abs(ri - 35) <= 1.5, (rp, ri)

def test_paint_iris_makes_dark_iris_ring_and_pupil():
    img = synth_eye(); out = tf.paint_iris(img, (100, 100), 12, 35)
    lum = out.mean(2)
    assert lum[100, 100] < 20                      # pupil black
    assert lum[100, 100 + 22] < 80                 # iris clearly dark
    assert out[100, 122, 0] > out[100, 122, 2]     # brown: red > blue
    assert lum[100, 100 + 34] < lum[100, 100 + 22]  # limbal ring darker than mid-iris
    assert lum[100, 100 + 60] > 200                # sclera untouched

def test_raster_vertex_field():
    tris_uv = np.array([[[0.1, 0.1], [0.9, 0.1], [0.1, 0.9]]]); vals = np.array([[0.0, 3.0, 3.0]])
    img, cov = tf.raster_vertex_field(tris_uv, vals, 100)
    assert cov[50, 30] and not cov[5, 95]
    assert abs(img[89, 11] - 0.0) < 0.2            # uv (0.1,0.1) -> row (1-0.1)*100
    cx, cy = int(100 * (0.1 + 0.8 / 3)), int(100 * (1 - (0.1 + 0.8 / 3)))
    assert abs(img[cy, cx] - 2.0) < 0.15

def test_feathered_gain_weight():
    w = tf.gain_weight(np.array([0, 1, 2, 5.]), 0, 2)
    assert np.allclose(w, [0, 0.5, 1, 1])

def test_border_outliers_only_bright_near_border():
    img = np.full((10, 10, 3), 0.5); img[0, 0] = 0.95; img[5, 5] = 0.95; img[1, 1] = 0.1  # collar, highlight, shadow
    ring = np.full((10, 10), 5.0); ring[:2, :2] = 0.5; cov = np.ones((10, 10), bool)
    m = tf.border_outliers(img, ring, cov, max_ring=1.5, th=0.1)
    assert m[0, 0] and not m[5, 5] and not m[1, 1] and m.sum() == 1

def test_detail_transfer_keeps_base_low_freq_and_template_detail():
    A = np.full((64, 64, 3), 180, np.uint8)                       # character skin, flat
    T = np.full((64, 64, 3), 120, np.uint8); T[30:32, :] = 20      # darker template skin with a lash line
    out = tf.detail_transfer(A, T, sigma=4.0)
    assert abs(int(out[5, 5, 0]) - 180) <= 3                       # template's darker tone does not come through
    assert int(out[31, 5, 0]) < 150                                # the lash line does

def test_flatten_lowfreq_removes_a_soft_shadow_in_mask_only():
    yy, xx = np.mgrid[:80, :80]; shadow = 60 * np.exp(-((xx - 40) ** 2 + (yy - 40) ** 2) / (2 * 8 ** 2))
    img = np.clip(np.repeat((200 - shadow)[..., None], 3, 2), 0, 255).astype(np.uint8); img[40, 10:70] = 90  # fine detail line
    w = np.zeros((80, 80)); w[20:60, 20:60] = 1
    out = tf.flatten_lowfreq(img, w, sigma_detail=2.0, sigma_base=30.0)
    assert out[30, 40, 0] > img[30, 40, 0] + 15                    # shadow lifted inside the mask
    assert out[40, 40, 0] < out[37, 40, 0] - 30                    # the thin line survives
    assert (out[:, :15] == img[:, :15]).all()                      # outside the mask untouched

def test_masked_blur_ignores_outside_texels():
    x = np.zeros((40, 40, 3)); x[:, :20] = 100; cov = np.zeros((40, 40), bool); cov[:, :20] = True
    b = tf._blur(x, 5.0, cov)
    assert abs(b[20, 18, 0] - 100) < 1e-3                          # no darkening from the black outside the island

def test_paint_sclera_is_soft_off_white_and_leaves_iris_and_outside():
    img = synth_eye(); img[:] = np.where(img > 200, 120, img)      # dark grey sclera
    out = tf.paint_sclera(img, (100, 100), 35, 80)
    assert out[100, 100 + 50].mean() > 180 and out[100, 150, 0] >= out[100, 150, 2]   # light, slightly warm
    assert (out[100, 100 + 20] == img[100, 100 + 20]).all()        # iris untouched
    assert (out[0, 0] == img[0, 0]).all()                          # beyond the disc untouched

def test_add_catchlight_is_small_soft_and_up_right():
    img = np.full((200, 200, 3), 60, np.uint8)
    out = tf.add_catchlight(img, (100, 100), 35)
    d = out.mean(2) - 60; ys, xs = np.nonzero(d > 40)
    assert len(ys) and ys.mean() < 100 and xs.mean() > 100         # up and to the image right of the pupil
    assert len(ys) < 0.12 * np.pi * 35 ** 2                        # small
    assert d.max() > 120 and out[100, 100].mean() == 60            # bright, pupil centre untouched

def test_rim_mask_finds_template_red_not_skin_or_lashes():
    T = np.zeros((4, 3, 3), np.uint8); T[0] = (90, 30, 25); T[1] = (200, 150, 130); T[2] = (30, 25, 25); T[3] = (120, 45, 40)
    m = tf.rim_mask(T)
    assert m[0, 0] and m[3, 0] and not m[1, 0] and not m[2, 0]

def test_detail_transfer_per_pixel_strength():
    A = np.full((64, 64, 3), 180, np.uint8); T = np.full((64, 64, 3), 120, np.uint8); T[20:22, :] = 20; T[44:46, :] = 20
    k = np.ones((64, 64)); k[32:] = 0.2                            # upper lashes full, lower lashes faint
    out = tf.detail_transfer(A, T, sigma=4.0, k=k)
    assert out[21, 5, 0] < 130 and out[45, 5, 0] > 150

def test_surround_fill_replaces_masked_region_with_surrounding_skin():
    img = np.zeros((64, 64, 3), np.uint8); img[:] = (150, 110, 90); img[24:40, 20:44] = (40, 30, 25)  # painted-on eye
    w = np.zeros((64, 64)); w[22:42, 18:46] = 1.0
    out = tf.surround_fill(img, w, radius=6)
    assert np.abs(out[32, 32].astype(int) - [150, 110, 90]).max() <= 6
    assert (out[5, 5] == img[5, 5]).all()

def test_luminance_detail_adds_grey_detail_only_inside_cov():
    base = np.full((32, 32, 3), (150, 110, 90), np.uint8)
    T = np.full((32, 32, 3), (60, 120, 200), np.uint8); T[16, :] = (20, 80, 160)   # a dark line (crease) in a blue template
    cov = np.zeros((32, 32), bool); cov[:, :16] = True
    out = tf.luminance_detail(base, T, sigma=2.0, cov=cov, k=1.0)
    d = out[16, 5].astype(int) - base[16, 5].astype(int)
    assert d.max() < -10 and abs(d[0] - d[2]) <= 1          # darker, same amount in every channel (no blue tint)
    assert (out[:, 20:] == base[:, 20:]).all() and abs(int(out[5, 5, 0]) - 150) <= 3

def test_luminance_detail_per_pixel_strength():
    base = np.full((8, 8, 3), 120, np.uint8); T = np.full((8, 8, 3), 100, np.uint8); T[4, :] = 40
    k = np.zeros((8, 8)); k[:, 4:] = 1.0
    out = tf.luminance_detail(base, T, sigma=1.0, k=k)
    assert (out[4, :4] == 120).all() and out[4, 6, 0] < 110

if __name__ == '__main__':
    n = 0
    for k, f in list(globals().items()):
        if k.startswith('test_'): f(); n += 1; print('PASS', k)
    print('ALL', n)
