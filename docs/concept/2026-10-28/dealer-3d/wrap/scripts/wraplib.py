"""Pure-numpy helpers for the head wrap: similarity fit, thin-plate RBF, smooth displacement field."""
import numpy as np

def umeyama(A, B):
    """Best similarity (s, R, t) with B ~= s * R @ A + t."""
    ma, mb = A.mean(0), B.mean(0); a, b = A - ma, B - mb
    U, S, Vt = np.linalg.svd(b.T @ a / len(A))
    D = np.eye(3); D[2, 2] = np.sign(np.linalg.det(U @ Vt))
    R = U @ D @ Vt; s = np.trace(np.diag(S) @ D) / (a ** 2).sum(1).mean()
    return s, R, mb - s * R @ ma

def apply_sim(sim, X):
    s, R, t = sim; return s * X @ R.T + t

class TPS:
    """3-D thin-plate RBF with affine part; lam = smoothing.
    Kernel phi(r) = -r: the 3-D biharmonic r is conditionally negative definite, so the sign is flipped
    to make +lam*I a proper (monotone) regulariser."""
    def __init__(self, C, T, lam=0.0):
        n = len(C); self.C = C
        K = -np.linalg.norm(C[:, None] - C[None], axis=2) + lam * np.eye(n)
        P = np.c_[np.ones(n), C]
        L = np.zeros((n + 4, n + 4)); L[:n, :n] = K; L[:n, n:] = P; L[n:, :n] = P.T
        rhs = np.zeros((n + 4, 3)); rhs[:n] = T
        sol = np.linalg.solve(L, rhs); self.w, self.a = sol[:n], sol[n:]
    def __call__(self, X):
        out = np.empty_like(X, dtype=float)
        for i in range(0, len(X), 4096):
            x = X[i:i + 4096]
            out[i:i + 4096] = -np.linalg.norm(x[:, None] - self.C[None], axis=2) @ self.w + np.c_[np.ones(len(x)), x] @ self.a
        return out

def smooth_field(n, edges, w, c, alpha=1.0):
    """argmin sum_i w_i|d_i-c_i|^2 + alpha*sum_edges |d_i-d_j|^2  (dense solve, n ~ few thousand)."""
    Lp = np.zeros((n, n)); i, j = edges[:, 0], edges[:, 1]
    np.add.at(Lp, (i, j), -1); np.add.at(Lp, (j, i), -1)
    np.add.at(Lp, (i, i), 1); np.add.at(Lp, (j, j), 1)
    A = np.diag(w) + alpha * Lp
    return np.linalg.solve(A + 1e-9 * np.eye(n), w[:, None] * c)

def ramp(x, a, b):
    """0 below a, 1 above b, smoothstep between."""
    t = np.clip((x - a) / (b - a), 0, 1); return t * t * (3 - 2 * t)

def inside_polygon(pts, poly):
    """Even-odd point-in-polygon for (N,2) points against (M,2) polygon."""
    x, y = pts[:, 0][:, None], pts[:, 1][:, None]
    x1, y1 = poly[:, 0][None], poly[:, 1][None]; x2, y2 = np.roll(poly[:, 0], -1)[None], np.roll(poly[:, 1], -1)[None]
    cond = (y1 > y) != (y2 > y)
    with np.errstate(divide='ignore', invalid='ignore'):
        xi = x1 + (y - y1) * (x2 - x1) / (y2 - y1)
    return ((cond & (x < xi)).sum(1) % 2) == 1

def edges_of(F):
    e = np.sort(np.concatenate([F[:, [0, 1]], F[:, [1, 2]], F[:, [2, 0]]]), 1)
    return np.unique(e, axis=0)

def boundary_loops(F):
    """Ordered vertex loops along edges used by exactly one triangle."""
    e = np.sort(np.concatenate([F[:, [0, 1]], F[:, [1, 2]], F[:, [2, 0]]]), 1)
    u, c = np.unique(e, axis=0, return_counts=True); b = u[c == 1]
    nb = {}
    for a, c2 in b: nb.setdefault(a, []).append(c2); nb.setdefault(c2, []).append(a)
    seen, loops = set(), []
    for s in nb:
        if s in seen: continue
        loop, prev, cur = [s], None, s; seen.add(s)
        while True:
            nxt = [n for n in nb[cur] if n != prev and n not in seen]
            if not nxt: break
            prev, cur = cur, nxt[0]; seen.add(cur); loop.append(cur)
        loops.append(loop)
    return loops

def ring_distance(n, edges, seeds):
    """BFS hop count from seed vertices (inf if unreachable)."""
    adj = [[] for _ in range(n)]
    for a, b in edges: adj[a].append(b); adj[b].append(a)
    d = np.full(n, np.inf); fr = list(seeds)
    for s in fr: d[s] = 0
    k = 0
    while fr:
        k += 1; nf = []
        for v in fr:
            for w in adj[v]:
                if d[w] == np.inf: d[w] = k; nf.append(w)
        fr = nf
    return d

def fit_sphere_ratio(pts, rho, c0, r0, iters=30):
    """Centre c and radius r minimising sum(|p_i - c| - rho_i * r)^2 (Gauss-Newton)."""
    c, r = np.array(c0, float), float(r0)
    for _ in range(iters):
        v = pts - c; L = np.linalg.norm(v, axis=1) + 1e-20
        f = L - rho * r
        J = np.c_[-v / L[:, None], -rho]
        step = np.linalg.lstsq(J, -f, rcond=None)[0]
        c += step[:3]; r += step[3]
        if np.abs(step).max() < 1e-12: break
    return c, r

def char_names(name):
    """Blender names for a character's wrapped face: object, face material, baked colour image."""
    n = name.lower()
    return {'obj': n.capitalize() + 'Face', 'mat': n + '_face', 'img': n + '_face_color'}

def rot_between(a, b):
    """Rotation matrix taking unit vector a onto unit vector b (shortest arc)."""
    a = np.asarray(a, float) / np.linalg.norm(a); b = np.asarray(b, float) / np.linalg.norm(b)
    v = np.cross(a, b); c = float(a @ b)
    if 1 + c < 1e-8 or np.linalg.norm(v) < 1e-12:
        if c > 0: return np.eye(3)
        p = np.cross(a, [1, 0, 0] if abs(a[0]) < 0.9 else [0, 1, 0]); p /= np.linalg.norm(p)
        return 2 * np.outer(p, p) - np.eye(3)
    K = np.array([[0, -v[2], v[1]], [v[2], 0, -v[0]], [-v[1], v[0], 0]])
    return np.eye(3) + K + K @ K / (1 + c)

def _smooth(t):
    t = np.clip(t, 0, 1); return t * t * (3 - 2 * t)

def lid_lift(X, c, r, theta_deg, r_full=1.25, r_zero=2.3, z0=-0.45, z1=-0.28):
    """Open an upper lid: rotate points about the eye's horizontal (world X) axis through c so front points move up
    over the eyeball. Weight = 1 inside r_full*r, smooth to 0 at r_zero*r, times a smooth ramp on the height above the
    centre (0 below z0*r, 1 above z1*r). A rotation about c keeps every point's distance to c. Returns (Y, w)."""
    D = np.asarray(X, float) - c; rel = np.linalg.norm(D, axis=1) / r
    w = (1 - _smooth((rel - r_full) / (r_zero - r_full))) * _smooth((D[:, 2] / r - z0) / (z1 - z0))
    a = np.radians(theta_deg) * w; ca, sa = np.cos(a), np.sin(a)
    Y = D.copy(); Y[:, 1] = D[:, 1] * ca + D[:, 2] * sa; Y[:, 2] = -D[:, 1] * sa + D[:, 2] * ca
    return Y + c, w

def compensate_keys(B, B2, K, full_at=1.0):
    """Shape keys after moving the basis from B to B2 (d = B2 - B). The part of a key's motion that runs against d
    (closing the lid) cancels the lift: where it reaches |d| the key keeps its absolute end position, so blinks still
    close; motion along d or none keeps the key's delta from the new basis (eyeWide opens further). Closing motion of
    full_at*|d| or more already counts as a full close (keeps the absolute end pose)."""
    d = B2 - B; n = np.linalg.norm(d, axis=1); u = d / np.maximum(n, 1e-12)[:, None]
    out = {}
    for name, A in K.items():
        against = np.einsum('ij,ij->i', -(A - B), u)
        a = np.where(n > 1e-12, np.clip(against / np.maximum(full_at * n, 1e-12), 0, 1), 0)
        out[name] = A + d * (1 - a)[:, None]
    return out

def fit_sphere(X):
    """Least-squares sphere through points (works on a cap, unlike the vertex mean)."""
    X = np.asarray(X, float); A = np.c_[2 * X, np.ones(len(X))]; sol = np.linalg.lstsq(A, (X ** 2).sum(1), rcond=None)[0]
    c = sol[:3]; return c, float(np.sqrt(sol[3] + c @ c))

def smooth_shell_displacement(X, Y, edges, c, iters=3, lam=0.5):
    """Laplacian-smooth the displacement Y - X over the mesh edges (only where something moved, plus neighbours), then
    put every point back at its original distance from c, so a lid moved over the eyeball stays on its shell."""
    X = np.asarray(X, float); D = np.asarray(Y, float) - X; n = len(X)
    act = np.linalg.norm(D, axis=1) > 1e-12
    act[edges[:, 0]] |= act[edges[:, 1]] & False  # placeholder (kept explicit below)
    nb = np.zeros(n, bool); nb[edges[act[edges[:, 1]], 0]] = True; nb[edges[act[edges[:, 0]], 1]] = True; act = act | nb
    deg = np.zeros(n); np.add.at(deg, edges[:, 0], 1); np.add.at(deg, edges[:, 1], 1)
    for _ in range(iters):
        S = np.zeros_like(D); np.add.at(S, edges[:, 0], D[edges[:, 1]]); np.add.at(S, edges[:, 1], D[edges[:, 0]])
        L = S / np.maximum(deg, 1)[:, None] - D; D = D + lam * L * act[:, None]
    Z = X + D; r0 = np.linalg.norm(X - c, axis=1); r1 = np.linalg.norm(Z - c, axis=1)
    return c + (Z - c) * (r0 / np.maximum(r1, 1e-12))[:, None]

def scale_about(X, p, s):
    """Uniform scale of points X about pivot p."""
    return p + s * (np.asarray(X, float) - p)

def lid_arc_lerp(A, B, c, f):
    """Move points from A toward B by fraction f along an arc about c (direction slerp, radius lerp): part-way lid poses
    stay on the eyeball's shell, where a straight blend (chord) would cut into it."""
    u, v = np.asarray(A, float) - c, np.asarray(B, float) - c
    ru, rv = np.linalg.norm(u, axis=1, keepdims=True), np.linalg.norm(v, axis=1, keepdims=True)
    a, b = u / np.maximum(ru, 1e-12), v / np.maximum(rv, 1e-12)
    th = np.arccos(np.clip((a * b).sum(1, keepdims=True), -1, 1)); s = np.sin(th)
    small = s < 1e-9; s = np.where(small, 1.0, s)
    d = np.where(small, a + f * (b - a), (np.sin((1 - f) * th) * a + np.sin(f * th) * b) / s)
    return c + d * (ru + f * (rv - ru))

def fraction_for_travel(A, B, c, target, iters=40):
    """Fraction f with median |lid_arc_lerp(A, B, c, f) - A| == target (clamped to 0..1)."""
    lo, hi = 0.0, 1.0
    for _ in range(iters):
        f = (lo + hi) / 2
        if np.median(np.linalg.norm(lid_arc_lerp(A, B, c, f) - A, axis=1)) < target: lo = f
        else: hi = f
    return (lo + hi) / 2
