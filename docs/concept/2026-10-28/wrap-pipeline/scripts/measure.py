# Inter-pupil distance from MediaPipe iris centres (468 = image-left = character's right eye, 473) on orthographic
# front renders (render_front.py camera json: s = ortho size, W = pixels), source vs wrapped face.
# usage: python measure.py src_lm.json src_cam.json wrap_lm.json wrap_cam.json [unity_height_m]
import json, sys, numpy as np
PUPIL = {'R': 468, 'L': 473}

def ipd_units(L, cam):
    return float(np.linalg.norm(np.subtract(L[473], L[468]))) * cam['s'] / cam['W']

def eye_open(L):
    """Lid opening height / corner-to-corner width per eye (MediaPipe 159/145 over 33/133, 386/374 over 362/263)."""
    d = lambda a, b: float(np.linalg.norm(np.subtract(L[a], L[b])))
    return {'R': d(159, 145) / max(d(33, 133), 1e-9), 'L': d(386, 374) / max(d(362, 263), 1e-9)}

def world_xz(p, cam):  # image px -> (x, z) offsets in GLB units (image y grows downward)
    return np.array([p[0], -p[1]]) * cam['s'] / cam['W']

def eye_report(src, src_cam, wrap, wrap_cam, height_m=1.0, tol_mm=3.0):
    a, b = ipd_units(src, src_cam), ipd_units(wrap, wrap_cam); k = height_m * 1000
    shift = {s: ((world_xz(wrap[i], wrap_cam) - world_xz(src[i], src_cam)) * k).round(2).tolist() for s, i in PUPIL.items()}
    return {'ipd_src': a, 'ipd_wrap': b, 'ipd_src_mm_unity': a * k, 'ipd_wrap_mm_unity': b * k,
            'ipd_diff_mm_unity': (b - a) * k, 'pupil_shift_mm_unity': shift, 'ok': bool(abs(b - a) * k <= tol_mm),
            'open_src': eye_open(src), 'open_wrap': eye_open(wrap)}

if __name__ == '__main__':
    j = lambda p: json.load(open(p)); h = float(sys.argv[5]) if len(sys.argv) > 5 else 1.0
    print('IPD', json.dumps(eye_report(j(sys.argv[1]), j(sys.argv[2]), j(sys.argv[3]), j(sys.argv[4]), h)))
