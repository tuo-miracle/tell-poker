"""Texture helpers for the wrap (system python, numpy + cv2): colour difference, seam bands, UV rasterising, iris repaint."""
import numpy as np, cv2

def srgb8_to_lab(rgb):
    """sRGB 0-255 (N,3, RGB order) -> CIE Lab (D65)."""
    c = np.asarray(rgb, float) / 255.0
    c = np.where(c <= 0.04045, c / 12.92, ((c + 0.055) / 1.055) ** 2.4)
    M = np.array([[0.4124564, 0.3575761, 0.1804375], [0.2126729, 0.7151522, 0.0721750], [0.0193339, 0.1191920, 0.9503041]])
    xyz = c @ M.T / np.array([0.95047, 1.0, 1.08883])
    f = np.where(xyz > (6 / 29) ** 3, np.cbrt(xyz), xyz / (3 * (6 / 29) ** 2) + 4 / 29)
    return np.stack([116 * f[..., 1] - 16, 500 * (f[..., 0] - f[..., 1]), 200 * (f[..., 1] - f[..., 2])], -1)

def delta_e2000(lab1, lab2):
    L1, a1, b1 = np.moveaxis(np.asarray(lab1, float), -1, 0); L2, a2, b2 = np.moveaxis(np.asarray(lab2, float), -1, 0)
    C1, C2 = np.hypot(a1, b1), np.hypot(a2, b2); Cm = (C1 + C2) / 2
    G = 0.5 * (1 - np.sqrt(Cm ** 7 / (Cm ** 7 + 25.0 ** 7)))
    a1p, a2p = (1 + G) * a1, (1 + G) * a2; C1p, C2p = np.hypot(a1p, b1), np.hypot(a2p, b2)
    h1 = np.degrees(np.arctan2(b1, a1p)) % 360; h2 = np.degrees(np.arctan2(b2, a2p)) % 360
    dL = L2 - L1; dC = C2p - C1p
    dh = h2 - h1; dh = np.where(dh > 180, dh - 360, np.where(dh < -180, dh + 360, dh)); dh = np.where(C1p * C2p == 0, 0, dh)
    dH = 2 * np.sqrt(C1p * C2p) * np.sin(np.radians(dh / 2))
    Lm = (L1 + L2) / 2; Cmp = (C1p + C2p) / 2
    hs = h1 + h2; hm = np.where(np.abs(h1 - h2) > 180, np.where(hs < 360, hs + 360, hs - 360) / 2, hs / 2); hm = np.where(C1p * C2p == 0, hs, hm)
    T = 1 - 0.17 * np.cos(np.radians(hm - 30)) + 0.24 * np.cos(np.radians(2 * hm)) + 0.32 * np.cos(np.radians(3 * hm + 6)) - 0.20 * np.cos(np.radians(4 * hm - 63))
    Sl = 1 + 0.015 * (Lm - 50) ** 2 / np.sqrt(20 + (Lm - 50) ** 2); Sc = 1 + 0.045 * Cmp; Sh = 1 + 0.015 * Cmp * T
    Rt = -2 * np.sqrt(Cmp ** 7 / (Cmp ** 7 + 25.0 ** 7)) * np.sin(np.radians(60 * np.exp(-((hm - 275) / 25) ** 2)))
    return np.sqrt((dL / Sl) ** 2 + (dC / Sc) ** 2 + (dH / Sh) ** 2 + Rt * (dC / Sc) * (dH / Sh))

def seam_bands(face, other, near, far):
    """Pixels of `face` / `other` whose distance (px) to the other region is in [near, far]."""
    d_f = cv2.distanceTransform((~other).astype(np.uint8), cv2.DIST_L2, cv2.DIST_MASK_PRECISE)
    d_o = cv2.distanceTransform((~face).astype(np.uint8), cv2.DIST_L2, cv2.DIST_MASK_PRECISE)
    return face & (d_f >= near) & (d_f <= far), other & (d_o >= near) & (d_o <= far)

def raster_vertex_field(tris_uv, vals, res):
    """Rasterise per-corner values over UV triangles (barycentric). tris_uv (T,3,2), vals (T,3[,C]).
    Pixel centre (x+.5)/res, v = 1-(y+.5)/res. Returns (img, covered)."""
    vals = np.asarray(vals, float); C = vals.shape[2] if vals.ndim == 3 else 1; vals = vals.reshape(len(vals), 3, C)
    img = np.zeros((res, res, C)); cov = np.zeros((res, res), bool)
    P = np.stack([np.asarray(tris_uv)[..., 0] * res - 0.5, (1 - np.asarray(tris_uv)[..., 1]) * res - 0.5], -1)
    for p, v in zip(P, vals):
        x0, y0 = np.floor(p.min(0)).astype(int); x1, y1 = np.ceil(p.max(0)).astype(int)
        x0, y0 = max(x0, 0), max(y0, 0); x1, y1 = min(x1, res - 1), min(y1, res - 1)
        if x1 < x0 or y1 < y0: continue
        yy, xx = np.mgrid[y0:y1 + 1, x0:x1 + 1]; q = np.stack([xx, yy], -1).reshape(-1, 2).astype(float)
        T = np.array([p[1] - p[0], p[2] - p[0]]).T
        if abs(np.linalg.det(T)) < 1e-12: continue
        l12 = np.linalg.solve(T, (q - p[0]).T).T; l = np.c_[1 - l12.sum(1), l12]
        ok = (l >= -1e-6).all(1)
        if not ok.any(): continue
        ys, xs = yy.ravel()[ok], xx.ravel()[ok]
        img[ys, xs] = l[ok] @ v; cov[ys, xs] = True
    return (img[..., 0] if C == 1 else img), cov

# brown-hazel iris, BGR, by normalised radius t (0 = pupil edge, 1 = limbus)
IRIS_STOPS = [(0.0, (46, 86, 120)), (0.3, (40, 70, 100)), (0.6, (32, 52, 76)), (0.85, (24, 34, 50)), (1.0, (15, 17, 22))]

def repaint_iris(im, center, r_iris, r_pupil, pupil_scale=1.0, stops=IRIS_STOPS, feather=1.1):
    """Recolour an iris in a BGR texture: keep its fibre detail (luma ratio to a blurred copy, catchlights clipped),
    replace the hue with a radial brown-hazel ramp, dark limbal ring, solid black pupil. Outside r_iris*feather untouched."""
    out = im.copy(); cx, cy = center
    R = int(np.ceil(r_iris * feather)) + 2
    x0, y0 = int(cx) - R, int(cy) - R; sub = im[y0:y0 + 2 * R + 1, x0:x0 + 2 * R + 1].astype(np.float32)
    yy, xx = np.mgrid[y0:y0 + 2 * R + 1, x0:x0 + 2 * R + 1]; r = np.hypot(xx - cx, yy - cy)
    L = sub.mean(2); iris = (r > r_pupil) & (r < r_iris)
    med = np.median(L[iris]); hot = (iris & (L > 1.3 * med)).astype(np.uint8)
    Lf = cv2.inpaint(np.clip(L, 0, 255).astype(np.uint8), cv2.dilate(hot, np.ones((5, 5), np.uint8)), 4, cv2.INPAINT_TELEA).astype(np.float32)
    detail = np.clip(Lf / (cv2.GaussianBlur(Lf, (0, 0), max(r_iris / 8, 1.0)) + 1e-3), 0.65, 1.3)
    rp = r_pupil * pupil_scale
    t = np.clip((r - rp) / (r_iris - rp), 0, 1)
    ts = np.array([s[0] for s in stops]); cs = np.array([s[1] for s in stops], np.float32)
    col = np.stack([np.interp(t, ts, cs[:, k]) for k in range(3)], -1) * detail[..., None]
    a_pupil = np.clip(rp + 0.75 - r, 0, 1.5) / 1.5
    col = col * (1 - a_pupil[..., None]) + 6 * a_pupil[..., None]
    w = np.clip((r_iris * feather - r) / (r_iris * (feather - 1)), 0, 1)  # 1 inside the iris, fades over the limbus
    new = sub * (1 - w[..., None]) + col * w[..., None]
    out[y0:y0 + 2 * R + 1, x0:x0 + 2 * R + 1] = np.clip(new, 0, 255).astype(im.dtype)
    return out

def srgb8_to_linear(x):
    c = np.asarray(x, float) / 255.0; return np.where(c <= 0.04045, c / 12.92, ((c + 0.055) / 1.055) ** 2.4)

def linear_to_srgb8(x):
    x = np.clip(x, 0, 1); return np.clip(np.where(x <= 0.0031308, x * 12.92, 1.055 * x ** (1 / 2.4) - 0.055) * 255 + 0.5, 0, 255).astype(np.uint8)

def apply_gain_srgb(im, gain):
    """Multiply an 8-bit sRGB image by a per-pixel, per-channel gain in linear light."""
    return linear_to_srgb8(srgb8_to_linear(im) * gain)

def seam_gains(face_pos, face_lin, ring, hank_pos, hank_lin, radius, src_rings=(1, 4), full=1.0, fade=4.0, clip=(0.7, 1.4),
               radius_face=None, radius_smooth=None):
    """Per-vertex linear-light gain that makes the face match Hank across the seam.
    Border vertices (ring 0): gain = mean Hank colour within `radius` / mean face colour (rings in src_rings) within
    `radius_face` (default `radius`), smoothed along the border over `radius_smooth` (default `radius`). Every face vertex takes the gain of its nearest border vertex, at full
    strength up to ring `full`, smoothstep-faded to 1 over the next `fade` rings."""
    face_pos, hank_pos = np.asarray(face_pos, float), np.asarray(hank_pos, float)
    rf = radius_face or radius; rs = radius_smooth or radius
    bi = np.nonzero(ring <= 0.5)[0]; src = (ring >= src_rings[0]) & (ring <= src_rings[1])
    gb = np.ones((len(bi), 3)); okb = np.zeros(len(bi), bool)
    for k, i in enumerate(bi):
        mh = np.linalg.norm(hank_pos - face_pos[i], axis=1) < radius; mf = src & (np.linalg.norm(face_pos - face_pos[i], axis=1) < rf)
        if mh.sum() >= 3 and mf.sum() >= 3:
            gb[k] = np.clip(hank_lin[mh].mean(0) / np.maximum(face_lin[mf].mean(0), 1e-4), *clip); okb[k] = True
    sm = np.ones_like(gb)
    for k, i in enumerate(bi):  # smooth along the border; borders without samples borrow from neighbours
        m = okb & (np.linalg.norm(face_pos[bi] - face_pos[i], axis=1) < rs)
        if m.any(): sm[k] = gb[m].mean(0)
    near = np.argmin(np.linalg.norm(face_pos[:, None] - face_pos[bi][None], axis=2), axis=1)
    t = np.clip((ring - full) / fade, 0, 1); w = 1 - t * t * (3 - 2 * t)
    return 1 + (sm[near] - 1) * w[:, None]

def paint_sclera(im, center, r_iris, r_max, white=(142, 160, 172), edge=(98, 110, 130), detail=0.2):
    """Repaint the eyeball outside the iris (BGR): a warm off-white falling to a darker, pinker tone toward r_max, keeping a
    little of the old vein detail. The template sclera is cool and grey. Inside r_iris and beyond r_max untouched."""
    out = im.astype(np.float32); yy, xx = np.mgrid[:im.shape[0], :im.shape[1]]; r = np.hypot(xx - center[0], yy - center[1])
    t = np.clip((r - r_iris) / (r_max - r_iris), 0, 1)[..., None]; t = t * t
    col = np.array(white, np.float32) * (1 - t) + np.array(edge, np.float32) * t
    L = out.mean(2); col = col * np.clip(1 + detail * (L / np.maximum(cv2.GaussianBlur(L, (0, 0), 12.0), 1) - 1), 0.85, 1.05)[..., None]
    a = (np.clip((r - r_iris) / 2.0, 0, 1) * np.clip((r_max - r) / 4.0, 0, 1))[..., None]
    return np.clip(np.round(col * a + out * (1 - a)), 0, 255).astype(im.dtype)

def add_catchlight(im, center, r_iris, offset=(0.3, -0.25), radius=0.13, strength=0.85, col=(245, 250, 255)):
    """One small soft highlight on the iris (BGR), offset in iris radii, image y down."""
    out = im.astype(np.float32); yy, xx = np.mgrid[:im.shape[0], :im.shape[1]]
    r = np.hypot(xx - (center[0] + offset[0] * r_iris), yy - (center[1] + offset[1] * r_iris)) / (radius * r_iris)
    t = np.clip(1.5 - r, 0, 1); a = (strength * t * t * (3 - 2 * t))[..., None]
    return np.clip(np.round(out * (1 - a) + np.array(col, np.float32) * a), 0, 255).astype(im.dtype)

def urp_spec(N, L, V, perceptual_roughness, f0=0.04):
    """URP Lit direct specular (DirectBRDFSpecular) for a dielectric. Unit vectors; arrays broadcast on the last axis."""
    H = L + V; H = H / np.linalg.norm(H, axis=-1, keepdims=True)
    NoH = np.clip((N * H).sum(-1), 0, 1); LoH = np.clip((L * H).sum(-1), 0, 1)
    r = max(perceptual_roughness ** 2, 0.0078125); r2 = r * r; d = NoH * NoH * (r2 - 1) + 1
    return f0 * r2 / (d * d * np.maximum(0.1, LoH * LoH) * (4 * r + 2))

def urp_lit(albedo_lin, N, V, perceptual_roughness, lights, ambient):
    """Linear colour from URP Lit for a dielectric, no environment reflection: (diffuse + spec) * light * NdotL + ambient * diffuse."""
    diff = np.asarray(albedo_lin, float) * 0.96; out = ambient * diff
    for L, col in lights:
        ndl = np.clip((N * L).sum(-1, keepdims=True), 0, 1)
        out = out + (diff + urp_spec(N, L, V, perceptual_roughness)[..., None]) * col * ndl
    return out

def membrane_fill(im, mask, iters=2000):
    """Fill `mask` with a smooth membrane from the colours around it (Jacobi on the Laplace equation)."""
    ys, xs = np.nonzero(mask); y0, y1, x0, x1 = max(ys.min() - 1, 0), ys.max() + 2, max(xs.min() - 1, 0), xs.max() + 2
    sub = im[y0:y1, x0:x1].astype(np.float32); m = mask[y0:y1, x0:x1]
    sub[m] = sub[~m].mean(0)
    for _ in range(iters):
        s = (np.roll(sub, 1, 0) + np.roll(sub, -1, 0) + np.roll(sub, 1, 1) + np.roll(sub, -1, 1)) / 4; sub[m] = s[m]
    out = im.copy(); out[y0:y1, x0:x1][m] = np.clip(np.round(sub[m]), 0, 255).astype(im.dtype)
    return out
