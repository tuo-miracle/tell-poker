import sys, os
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import measure as ms

def lm_with(p468, p473, p33=(0, 0), p263=(0, 0)):
    L = [(0.0, 0.0)] * 478; L[468] = p468; L[473] = p473; L[33] = p33; L[263] = p263; return L

def test_ipd_units_uses_ortho_scale():
    cam = {'s': 0.2, 'W': 1000}
    assert abs(ms.ipd_units(lm_with((400, 500), (600, 500)), cam) - 0.04) < 1e-12

def test_eye_report_compares_source_and_wrap():
    cam = {'s': 0.2, 'W': 1000}
    r = ms.eye_report(lm_with((400, 500), (600, 500), (350, 500), (650, 500)), cam,
                      lm_with((395, 502), (610, 502), (350, 500), (650, 500)), cam, height_m=1.75)
    assert abs(r['ipd_src'] - 0.04) < 1e-12 and abs(r['ipd_wrap'] - 0.043) < 1e-12
    assert abs(r['ipd_diff_mm_unity'] - 0.003 * 1.75 * 1000) < 1e-9
    assert abs(r['pupil_shift_mm_unity']['R'][0] - (-0.001 * 1750)) < 1e-9
    assert r['ok'] is False  # 5.25 mm > 3 mm

def test_eye_report_ok_within_3mm():
    cam = {'s': 0.2, 'W': 1000}
    r = ms.eye_report(lm_with((400, 500), (600, 500)), cam, lm_with((401, 500), (601, 500)), cam, height_m=1.8)
    assert r['ok'] is True

def test_eye_open_ratio():
    L = [(0.0, 0.0)] * 478
    L[33], L[133], L[159], L[145] = (0, 0), (40, 0), (20, -6), (20, 6)      # right eye: 12 / 40
    L[362], L[263], L[386], L[374] = (60, 0), (100, 0), (80, -5), (80, 5)   # left eye: 10 / 40
    o = ms.eye_open(L)
    assert abs(o['R'] - 0.3) < 1e-12 and abs(o['L'] - 0.25) < 1e-12

def test_eye_report_includes_openness():
    cam = {'s': 0.2, 'W': 1000}; L = lm_with((400, 500), (600, 500), (350, 500), (650, 500))
    r = ms.eye_report(L, cam, L, cam)
    assert set(r['open_src']) == {'R', 'L'} and set(r['open_wrap']) == {'R', 'L'}

if __name__ == '__main__':
    n = 0
    for k, f in list(globals().items()):
        if k.startswith('test_'): f(); n += 1; print('PASS', k)
    print('ALL', n)
