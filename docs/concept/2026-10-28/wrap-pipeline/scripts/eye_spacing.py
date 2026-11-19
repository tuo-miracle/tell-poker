# Eye spacing from MediaPipe landmarks (478 points, iris centres 468/473).
# usage: python eye_spacing.py tag=lm.json ...  -> ratios per tag
import json, sys, numpy as np
def spacing(L):
    L = np.asarray(L, float); d = lambda a, b: float(np.linalg.norm(L[a] - L[b]))
    ipd, inner, outer, fw = d(468, 473), d(133, 362), d(33, 263), d(234, 454)
    ew = (d(33, 133) + d(362, 263)) / 2
    return {'ipd/face': ipd / fw, 'ipd/eye': ipd / ew, 'inner/eye': inner / ew, 'outer/face': outer / fw, 'eye/face': ew / fw}
if __name__ == '__main__':
    for a in sys.argv[1:]:
        t, _, p = a.partition('='); print('SPACING', t, {k: round(v, 3) for k, v in spacing(json.load(open(p))).items()})
