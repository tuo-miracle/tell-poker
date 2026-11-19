"""Accessory (glasses) split helpers: thin-shell seeds, region growing on the vertex graph, clearance."""
import numpy as np

def thin_seeds(thickness, pos, t_max, box):
    """Vertices whose inward ray hits the other side within t_max (thin shells such as frames) and inside the box."""
    pos = np.asarray(pos, float); inb = np.ones(len(pos), bool)
    for k, (lo, hi) in enumerate(box): inb &= (pos[:, k] >= lo) & (pos[:, k] <= hi)
    return (np.asarray(thickness) < t_max) & inb

def grow_region(seed, faces, allowed):
    """Flood fill from seed over face edges, only through allowed vertices."""
    g = np.asarray(seed, bool) & allowed; F = np.asarray(faces)
    E = np.concatenate([F[:, [0, 1]], F[:, [1, 2]], F[:, [2, 0]]])
    while True:
        n = g.copy(); np.logical_or.at(n, E[:, 0], g[E[:, 1]]); np.logical_or.at(n, E[:, 1], g[E[:, 0]]); n &= allowed
        if (n == g).all(): return g
        g = n

def faces_inside(vmask, faces):
    return np.asarray(vmask)[np.asarray(faces)].all(1)

def clearance_shift(sd, margin):
    """Forward shift that puts every accessory vertex at least margin in front of the face (sd: signed distance, + = in front)."""
    return float(max(0.0, margin - np.min(sd)))

def lens_outline(pts, nbins=24, center=None, order=None, plane=False):
    """Closed rim centreline (nbins x 3, counter-clockwise from +x in the x-z plane) from frame points: per angle bin
    round the centre the median radius and depth; bins with no points (rim fused into the skin) filled from a 2nd-order
    Fourier fit of radius and depth over the bins that have points (an ellipse-like lens, not a straight chord).
    order: replace every bin's radius by a Fourier fit of that order (smooth rim); plane: depth from a plane fitted to the points."""
    pts = np.asarray(pts, float); c = np.asarray(center if center is not None else pts[:, [0, 2]].mean(0), float)
    d = pts[:, [0, 2]] - c; ang = np.arctan2(d[:, 1], d[:, 0]) % (2 * np.pi); rad = np.hypot(d[:, 0], d[:, 1])
    k = np.round(ang / (2 * np.pi / nbins)).astype(int) % nbins
    r = np.full(nbins, np.nan); y = np.full(nbins, np.nan)
    for b in range(nbins):
        m = k == b
        if m.any(): r[b] = np.median(rad[m]); y[b] = np.median(pts[m, 1])
    t = np.arange(nbins) * 2 * np.pi / nbins; ok = np.isfinite(r)
    fourier = lambda q: np.c_[[np.ones(nbins)] + [f(j * t) for j in range(1, q + 1) for f in (np.cos, np.sin)]].T
    B = fourier(2)
    for a in (r, y):
        a[~ok] = B[~ok] @ np.linalg.lstsq(B[ok], a[ok], rcond=None)[0]
    if order:
        Bo = fourier(order); r = Bo @ np.linalg.lstsq(Bo, r, rcond=None)[0]
    O = np.c_[c[0] + r * np.cos(t), y, c[1] + r * np.sin(t)]
    if plane:
        A = np.c_[np.ones(len(pts)), pts[:, 0], pts[:, 2]]; cf = np.linalg.lstsq(A, pts[:, 1], rcond=None)[0]
        O[:, 1] = cf[0] + cf[1] * O[:, 0] + cf[2] * O[:, 2]
    return O

def mirror_average(R, L):
    """Average a right rim with the mirrored left one (x -> -x) so the rebuilt pair is symmetric about x = 0.
    Both are lens_outline rings with the same even bin count; bin k on the right mirrors bin n/2 - k on the left."""
    R, L = np.asarray(R, float), np.asarray(L, float); n = len(R); flip = np.array([-1.0, 1.0, 1.0])
    idx = (n // 2 - np.arange(n)) % n
    R2 = (R + L[idx] * flip) / 2
    return R2, R2[idx] * flip

def tube(C, r, nseg=8, closed=True):
    """Triangle mesh of a round tube of radius r along polyline C (closed ring, or open with end caps)."""
    C = np.asarray(C, float); n = len(C)
    nxt = np.roll(C, -1, 0) if closed else np.vstack([C[1:], 2 * C[-1] - C[-2]])
    prv = np.roll(C, 1, 0) if closed else np.vstack([2 * C[0] - C[1], C[:-1]])
    T = nxt - prv; T /= np.linalg.norm(T, axis=1, keepdims=True)
    up = np.where(np.abs(T[:, [1]]) > 0.9, [[0.0, 0, 1]], [[0.0, 1, 0]])
    N = np.cross(T, up); N /= np.linalg.norm(N, axis=1, keepdims=True); B = np.cross(T, N)
    a = np.arange(nseg) * 2 * np.pi / nseg
    V = (C[:, None] + r * (np.cos(a)[None, :, None] * N[:, None] + np.sin(a)[None, :, None] * B[:, None])).reshape(-1, 3)
    F = []
    for i in range(n if closed else n - 1):
        i2 = (i + 1) % n
        for j in range(nseg):
            j2 = (j + 1) % nseg; q = (i * nseg + j, i * nseg + j2, i2 * nseg + j2, i2 * nseg + j)
            F += [(q[0], q[1], q[2]), (q[0], q[2], q[3])]
    if not closed:
        c0, c1 = len(V), len(V) + 1; V = np.vstack([V, C[0], C[-1]]); last = (n - 1) * nseg
        F += [(c0, (j + 1) % nseg, j) for j in range(nseg)] + [(c1, last + j, last + (j + 1) % nseg) for j in range(nseg)]
    return V, np.array(F)

def oval_outline(O, nbins=32, exp=2.5):
    """Clean lens rim from an extracted one: a superellipse (exp 2 = ellipse, larger = squarer) spanning its x/z extent,
    on the plane fitted to its depth."""
    O = np.asarray(O, float); x0, x1 = O[:, 0].min(), O[:, 0].max(); z0, z1 = O[:, 2].min(), O[:, 2].max()
    t = np.arange(nbins) * 2 * np.pi / nbins; c, s = np.cos(t), np.sin(t)
    x = (x0 + x1) / 2 + (x1 - x0) / 2 * np.sign(c) * np.abs(c) ** (2 / exp)
    z = (z0 + z1) / 2 + (z1 - z0) / 2 * np.sign(s) * np.abs(s) ** (2 / exp)
    cf = np.linalg.lstsq(np.c_[np.ones(len(O)), O[:, 0], O[:, 2]], O[:, 1], rcond=None)[0]
    return np.c_[x, cf[0] + cf[1] * x + cf[2] * z, z]

def neck_ramp(w, z, z0, z1):
    """w: per-vertex [Neck, Head, Spine2] weights. Below z1 the Neck+Head share fades linearly to nothing at z0 and goes to
    Spine2: Tripo collars weighted to the neck tear when the head turns; a hard split tears between collar and neck skin."""
    w = np.array(w, float); t = np.clip((np.asarray(z, float) - z0) / (z1 - z0), 0, 1)[:, None]
    moved = w[:, :2] * (1 - t); w[:, :2] -= moved; w[:, 2] += moved.sum(1)
    return w

def frame_paint_mask(img, zone, sat_max=0.2, black=12):
    """Texels in the zone round the old frames that carry frame paint (grey/silver: low saturation) or were not baked (black)."""
    f = np.asarray(img, float); mx, mn = f.max(2), f.min(2)
    sat = (mx - mn) / np.maximum(mx, 1)
    return np.asarray(zone, bool) & ((sat < sat_max) | (mx < black))
