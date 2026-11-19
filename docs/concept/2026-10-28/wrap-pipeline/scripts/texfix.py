"""Pure-numpy texture fixes after the bake: seam colour match and a dark procedural iris."""
import numpy as np

def srgb_to_lin(x):
    x = np.asarray(x, float); return np.where(x <= 0.04045, x / 12.92, ((x + 0.055) / 1.055) ** 2.4)

def lin_to_srgb(x):
    x = np.clip(np.asarray(x, float), 0, None); return np.where(x <= 0.0031308, x * 12.92, 1.055 * x ** (1 / 2.4) - 0.055)

def _inliers(rgb, k=2.5):
    """Drop samples far from the band median (hair, collar) by a MAD test on each channel."""
    med = np.median(rgb, 0); mad = np.median(np.abs(rgb - med), 0) + 1e-3
    return rgb[(np.abs(rgb - med) <= k * 1.4826 * mad).all(1)]

def band_gain(face_rgb, other_rgb, lo=0.67, hi=1.5):
    """Per-channel linear gain that makes the face band's mean colour equal the outer band's (sRGB 0..1 in)."""
    f = srgb_to_lin(_inliers(np.asarray(face_rgb, float))).mean(0)
    o = srgb_to_lin(_inliers(np.asarray(other_rgb, float))).mean(0)
    return np.clip(o / np.maximum(f, 1e-6), lo, hi)

def apply_gain(img_u8, weight, gain):
    """img * (1 + w*(gain-1)) in linear light; weight is (H,W) in 0..1."""
    lin = srgb_to_lin(img_u8 / 255.0)
    g = 1 + weight[..., None] * (np.asarray(gain, float) - 1)
    return np.clip(np.round(lin_to_srgb(lin * g) * 255), 0, 255).astype(np.uint8)

def radial_profile(lum, c, rmax):
    """Median luminance per integer radius (the median ignores small baked highlights)."""
    yy, xx = np.mgrid[:lum.shape[0], :lum.shape[1]]
    r = np.hypot(xx - c[0], yy - c[1]).round().astype(int)
    return np.array([np.median(lum[r == k]) if (r == k).any() else 0.0 for k in range(rmax)])

def iris_radii(prof):
    """Pupil and iris radius from a radial luminance profile: dark pupil, darker iris, bright sclera peak.
    Iris edge = last radius before the sclera peak that is still below half-way between iris and sclera."""
    p = np.asarray(prof, float); pupil = p[:3].mean(); top = int(np.argmax(p))
    rp = int(np.argmax(p > pupil + 0.25 * (p[top] - pupil)))
    half = (p[rp:top].min() + p[top]) / 2 if top > rp else p[top]
    below = np.nonzero(p[rp:top] <= half)[0]
    ri = rp + int(below[-1]) + 1 if len(below) else top
    return rp, ri

def paint_iris(img_u8, c, rp, ri, iris=(70, 40, 22), limbus=(22, 13, 8), pupil=(6, 5, 5), detail=0.35):
    """Repaint the iris disc: near-black pupil, dark brown iris keeping some of the old fibre detail,
    a dark limbal ring at the edge, soft blend into the untouched sclera."""
    out = img_u8.astype(float); H, W = out.shape[:2]
    yy, xx = np.mgrid[:H, :W]; r = np.hypot(xx - c[0], yy - c[1])
    lum = out.mean(2); m = (r > rp) & (r < ri); base = lum[m].mean() if m.any() else 1.0
    tex = np.clip(1 + detail * (lum / max(base, 1) - 1), 0.75, 1.25)[..., None]
    col = np.array(iris, float) * tex
    t_lim = np.clip((r - 0.72 * ri) / (0.24 * ri), 0, 1)[..., None]  # 0 mid-iris -> 1 at the limbus
    col = col * (1 - t_lim) + np.array(limbus, float) * t_lim
    t_pup = np.clip((r - 0.9 * rp) / (0.2 * rp + 1e-6), 0, 1)[..., None]
    col = np.array(pupil, float) * (1 - t_pup) + col * t_pup
    a = np.clip((1.06 * ri - r) / (0.08 * ri), 0, 1)[..., None]  # 1 inside the iris, fades to sclera
    return np.clip(np.round(col * a + out * (1 - a)), 0, 255).astype(np.uint8)

def raster_vertex_field(tris_uv, vals, size):
    """Rasterise a per-corner scalar over UV triangles (barycentric interpolation). Returns (field, covered)."""
    img = np.zeros((size, size)); cov = np.zeros((size, size), bool)
    for T, v in zip(np.asarray(tris_uv, float), np.asarray(vals, float)):
        P = np.c_[T[:, 0] * size, (1 - T[:, 1]) * size]
        x0, y0 = np.floor(P.min(0)).astype(int); x1, y1 = np.ceil(P.max(0)).astype(int)
        x0, y0 = max(x0, 0), max(y0, 0); x1, y1 = min(x1, size - 1), min(y1, size - 1)
        if x1 < x0 or y1 < y0: continue
        yy, xx = np.mgrid[y0:y1 + 1, x0:x1 + 1]; q = np.c_[xx.ravel() + 0.5, yy.ravel() + 0.5]
        A = np.c_[P[1] - P[0], P[2] - P[0]]
        if abs(np.linalg.det(A)) < 1e-12: continue
        b = np.linalg.solve(A, (q - P[0]).T).T; l = np.c_[1 - b.sum(1), b]
        ins = (l >= -1e-3).all(1)
        img[yy.ravel()[ins], xx.ravel()[ins]] = l[ins] @ v; cov[yy.ravel()[ins], xx.ravel()[ins]] = True
    return img, cov

def gain_weight(ring, r0, r1):
    """Feather: colour correction fades in linearly from ring r0 (border, tucked under the body) to r1."""
    return np.clip((np.asarray(ring, float) - r0) / (r1 - r0), 0, 1)

def border_outliers(img01, ring, cov, max_ring=1.5, th=0.1):
    """Pixels near the mask border that are much brighter than the skin median (bake rays that hit the collar)."""
    lum = img01.mean(2); med = np.median(lum[cov])
    return cov & (ring < max_ring) & (lum > med + th)

def _blur(x, s, cov=None):
    """Gaussian blur; with a coverage mask, a normalised blur that ignores texels outside the UV islands."""
    import cv2
    x = np.asarray(x, np.float32)
    if cov is None: return cv2.GaussianBlur(x, (0, 0), s)
    c = np.asarray(cov, np.float32); c3 = c[..., None] if x.ndim == 3 else c
    return cv2.GaussianBlur(x * c3, (0, 0), s) / np.maximum(cv2.GaussianBlur(c, (0, 0), s), 1e-4)[(..., None) if x.ndim == 3 else ...]

def detail_transfer(base_u8, tmpl_u8, sigma=8.0, cov=None, k=None):
    """base's low frequencies + template's detail above `sigma` px (lashes, crease), scaled by k (scalar or per pixel);
    tone comes only from base."""
    b, t = base_u8.astype(np.float32), tmpl_u8.astype(np.float32)
    k = 1.0 if k is None else np.asarray(k, np.float32)[..., None]
    return np.clip(np.round(_blur(b, sigma, cov) + k * (t - _blur(t, sigma, cov))), 0, 255).astype(np.uint8)

def flatten_lowfreq(img_u8, w, sigma_detail=4.0, sigma_base=40.0, cov=None):
    """Inside weight w, swap the mid-scale tone (between sigma_detail and sigma_base) for the broad tone: removes
    painted-in shadows (baked AO) while keeping fine detail."""
    x = img_u8.astype(np.float32); new = x - _blur(x, sigma_detail, cov) + _blur(x, sigma_base, cov)
    w = np.asarray(w, np.float32)[..., None]
    return np.clip(np.round(x * (1 - w) + new * w), 0, 255).astype(np.uint8)

def paint_sclera(img_u8, c, ri, rmax, white=(208, 201, 192), edge=(178, 160, 150), detail=0.2):
    """Repaint the eyeball outside the iris (template sclera is cool, dark and red at the edge): soft warm off-white
    falling to a pinker tone at the disc edge, keeping a little of the old vein detail. Beyond rmax untouched."""
    out = img_u8.astype(np.float32); H, W = out.shape[:2]
    yy, xx = np.mgrid[:H, :W]; r = np.hypot(xx - c[0], yy - c[1])
    t = np.clip((r - ri) / (rmax - ri), 0, 1)[..., None]; t = t * t
    col = np.array(white, np.float32) * (1 - t) + np.array(edge, np.float32) * t
    lum = out.mean(2); ref = _blur(lum, 12.0); col = col * np.clip(1 + detail * (lum / np.maximum(ref, 1) - 1), 0.85, 1.05)[..., None]
    a = (np.clip((r - ri) / 2.0, 0, 1) * np.clip((rmax - r) / 4.0, 0, 1))[..., None]
    return np.clip(np.round(col * a + out * (1 - a)), 0, 255).astype(np.uint8)

def add_catchlight(img_u8, c, ri, offset=(0.3, -0.2), radius=0.13, strength=0.85, col=(255, 252, 245)):
    """One small soft highlight on the iris, up and to the image right of the pupil."""
    out = img_u8.astype(np.float32); H, W = out.shape[:2]
    yy, xx = np.mgrid[:H, :W]; r = np.hypot(xx - (c[0] + offset[0] * ri), yy - (c[1] + offset[1] * ri)) / (radius * ri)
    a = (strength * np.clip(1.5 - r, 0, 1) ** 2 * (3 - 2 * np.clip(1.5 - r, 0, 1)))[..., None]
    return np.clip(np.round(out * (1 - a) + np.array(col, np.float32) * a), 0, 255).astype(np.uint8)

def rim_mask(tmpl_u8, min_ratio=1.8):
    """Template's saturated red inner-lid/socket texels: red well above green and not bright (skin is not, lashes are not)."""
    t = tmpl_u8.astype(np.float32); return (t[..., 0] > 60) & (t[..., 0] / np.maximum(t[..., 1], 1) > min_ratio)

def surround_fill(img_u8, w, radius=8, th=0.05):
    """Fill the region where w > th from the colour round it (Telea inpaint): the skin tone a closed lid should carry,
    instead of the painted-on eye that was baked there."""
    import cv2
    m = (np.asarray(w) > th).astype(np.uint8) * 255
    return cv2.inpaint(np.ascontiguousarray(img_u8), m, radius, cv2.INPAINT_TELEA)

def luminance_detail(base_u8, tmpl_u8, sigma=3.0, cov=None, k=0.5):
    """Add the template's fine luminance detail (pores, creases above `sigma` px) to base inside cov, the same amount in
    every channel so the template's own skin tone does not leak in."""
    t = np.asarray(tmpl_u8, np.float32).mean(2); d = k * (t - _blur(t, sigma, cov))
    out = np.asarray(base_u8, np.float32) + d[..., None]
    if cov is not None: out = np.where(np.asarray(cov, bool)[..., None], out, base_u8)
    return np.clip(np.round(out), 0, 255).astype(np.uint8)
