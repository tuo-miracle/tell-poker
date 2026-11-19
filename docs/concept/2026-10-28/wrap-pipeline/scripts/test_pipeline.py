import json, sys, os, tempfile
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import pipeline as pl
HERE = os.path.dirname(os.path.abspath(__file__)); CH = os.path.join(HERE, '..', 'characters')

# dealer_run.sh (2026-10-30), copied verbatim: the dealer config must reproduce exactly these inputs to run_wrap.sh
DEALER_ENV = {
    'INNER_JSON': '{"sclera_gain":0.6,"sclera_tint":[0.86,0.94,1.0]}',
    'SEAM_JSON': '{"vmin":140,"full":4,"fade":8,"radius":0.015,"radius_smooth":0.03}',
    'EYE_JSON': '{"pupil_frac":0.33,"iris_scale":1.282,"sclera":true,"catchlight":true,"iris":[95,58,32],"limbus":[35,22,14]}',
    'RIG_JSON': '{"lid_open_deg":10,"lid_falloff":[1.25,2.3,-0.20,-0.02],"lid_full_at":0.1,"lower_lid_deg":-2,"lower_lid_falloff":[1.25,2.3,-0.02,-0.18],"eye_dx_mm":0.75,"eye_scale":0.78,"eye_rough":0.2,"eyelook_lids_only":true,"normal_rings":3,"lookdown_lid_mm":3.8}',
    'LID_JSON': '{"mode":"detail","nose_r0":0.10,"nose_r1":0.22,"rim":[0.72,0.92],"rim_tint":[1.0,0.9,0.88],"lash":[0.85,0.3]}',
}
DEALER_FIT = '{"eye_r0":0.010,"eye_r1":0.025,"nose_r0":0.010,"nose_r1":0.022,"offset_rings":1.5,"eye_tol2":1.0,"eye_back0":0.02,"cut_poke_marg":0.5,"eye_pin_r":0.012}'

def test_dealer_config_reproduces_dealer_run():
    cfg = pl.load_config(os.path.join(CH, 'dealer.json'))
    env, argv = pl.wrap_command(cfg, '/rb', '/w', '/o')
    for k, v in DEALER_ENV.items(): assert json.loads(env[k]) == json.loads(v), k
    assert env['SEAM_VMIN'] == '140'
    assert argv[0].endswith('run_wrap.sh') and argv[1] == 'dealer'
    assert os.path.realpath(argv[2]) == os.path.realpath(os.path.join(HERE, '..', '..', 'dealer-3d', 'dealer-12k.glb'))
    assert argv[3:5] == ['/rb/Female_Adult_07/Female_Adult_07_facial.fbx', '/rb/Female_Adult_07/f007_head_color.tga']
    assert argv[5:7] == ['/w', '/o'] and float(argv[7]) == 0.90 and float(argv[8]) == 0.17
    assert json.loads(argv[9]) == json.loads(DEALER_FIT)

def test_unset_stages_are_left_out_of_env():
    cfg = {'name': 'x', 'char_glb': '/c.glb', 'template': {'id': 'Male_Adult_09', 'tga': 'm017_head_color.tga'},
           'face_cz': 0.9, 'face_size': 0.2, 'rig': {'eye_scale': 0.8}}
    env, argv = pl.wrap_command(cfg, '/rb', '/w', '/o')
    assert json.loads(env['RIG_JSON']) == {'eye_scale': 0.8}
    for k in ('INNER_JSON', 'EYE_JSON', 'LID_JSON', 'SEAM_JSON', 'SEAM_VMIN', 'UNITY_H', 'CONCEPT_PNG'): assert k not in env
    assert argv[3] == '/rb/Male_Adult_09/Male_Adult_09_facial.fbx' and json.loads(argv[9]) == {}

def test_paths_resolve_against_config_dir():
    d = tempfile.mkdtemp(); p = os.path.join(d, 'c.json')
    json.dump({'name': 'a', 'char_glb': 'sub/a.glb', 'out_dir': '../out', 'concept_png': 'p.png', 'template': {'id': 'T', 'tga': 't.tga'}}, open(p, 'w'))
    cfg = pl.load_config(p)
    assert cfg['char_glb'] == os.path.join(d, 'sub', 'a.glb') and cfg['out_dir'] == os.path.normpath(os.path.join(d, '..', 'out'))
    assert cfg['concept_png'] == os.path.join(d, 'p.png')

def test_report_env_from_config():
    cfg = {'name': 'm', 'char_glb': '/c.glb', 'template': {'id': 'T', 'tga': 't.tga'}, 'unity_height_m': 1.75, 'concept_png': '/x/p1.png',
           'concept_crop': [0.12, 0.04, 0.175, 0.19]}
    env, _ = pl.wrap_command(cfg, '/rb', '/w', '/o')
    assert env['UNITY_H'] == '1.75' and env['CONCEPT_PNG'] == '/x/p1.png' and env['CONCEPT_CROP'] == '0.12,0.04,0.175,0.19'
    env, _ = pl.wrap_command({**cfg, 'unity_height_m': None, 'concept_png': None}, '/rb', '/w', '/o')
    assert 'UNITY_H' not in env and 'CONCEPT_PNG' not in env

def test_glasses_config_excludes_frames_from_the_fit_and_adds_them_back():
    cfg = {'name': 'm', 'char_glb': '/c/m-12k.glb', 'template': {'id': 'T', 'tga': 't.tga'}, 'fit': {'eye_pin_r': 0.01},
           'glasses': {'t_grow': 0.006}, 'glasses_build': {'tube_r': 0.0007}}
    env, argv = pl.wrap_command(cfg, '/rb', '/w', '/o')
    assert argv[2] == '/c/m-12k.glb' and env['GLASSES_JSON'] == '/w/glasses.json' and json.loads(env['GLASSES_BUILD_JSON']) == {'tube_r': 0.0007}
    assert json.loads(argv[9]) == {'eye_pin_r': 0.01, 'target_exclude_json': '/w/glasses.json'}
    assert pl.glasses_command(cfg, '/w')[-5:] == ['/c/m-12k.glb', '/w/char_front_lm.json', '/w/char_front.json', '/w/glasses.json', '{"t_grow": 0.006}']
    env, argv = pl.wrap_command({k: v for k, v in cfg.items() if k != 'glasses'}, '/rb', '/w', '/o')
    assert 'GLASSES_JSON' not in env and 'GLASSES_BUILD_JSON' not in env and json.loads(argv[9]) == {'eye_pin_r': 0.01}

def test_template_normal_map_goes_to_rig_and_skin_detail_to_env():
    cfg = {'name': 'm', 'char_glb': '/c.glb', 'template': {'id': 'T', 'tga': 't.tga', 'normal': 'n.tga'},
           'rig': {'eye_scale': 0.9, 'normal_strength': 0.5}, 'skin_detail': {'k': 0.5}}
    env, _ = pl.wrap_command(cfg, '/rb', '/w', '/o')
    assert json.loads(env['RIG_JSON']) == {'eye_scale': 0.9, 'normal_strength': 0.5, 'normal_map': '/rb/T/n.tga'}
    assert json.loads(env['SKIN_DETAIL_JSON']) == {'k': 0.5}
    assert pl.template_urls(cfg['template'])[2].endswith('/T/Textures/n.tga')
    env, _ = pl.wrap_command({'name': 'm', 'char_glb': '/c.glb', 'template': {'id': 'T', 'tga': 't.tga'}}, '/rb', '/w', '/o')
    assert 'RIG_JSON' not in env and 'SKIN_DETAIL_JSON' not in env

def test_template_urls():
    fbx, tga = pl.template_urls({'id': 'Male_Adult_09', 'tga': 'm017_head_color.tga'})[:2]
    base = 'https://raw.githubusercontent.com/microsoft/Microsoft-Rocketbox/master/Assets/Avatars/Adults/Male_Adult_09/'
    assert fbx == base + 'Export/Male_Adult_09_facial.fbx' and tga == base + 'Textures/m017_head_color.tga'

def test_unity_mm():
    assert abs(pl.unity_mm(0.0368, 1.75) - 64.4) < 1e-9
    assert pl.unity_mm(0.0368, None) is None

if __name__ == '__main__':
    n = 0
    for k, f in list(globals().items()):
        if k.startswith('test_'): f(); n += 1; print('PASS', k)
    print('ALL', n)
