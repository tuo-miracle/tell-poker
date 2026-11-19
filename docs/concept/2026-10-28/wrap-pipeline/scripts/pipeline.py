# One face-wrap build per character config (characters/<name>.json) -> run_wrap.sh.
# usage: PY=... MP_MODEL=... python3 pipeline.py characters/NAME.json RB_ROOT WORK_DIR [OUT_DIR]
#   RB_ROOT/<template id>/ holds the template's _facial.fbx and head colour tga (downloaded from GitHub if missing).
#   If char_glb is missing and the config has source_glb + decimate, the decimated GLB is made first.
# Config keys: name, char_glb, [source_glb, decimate {target, face}], template {id, tga, [normal]}, face_cz, face_size,
#   unity_height_m (only for reporting mm), concept_png (turnaround sheet for the 1 m comparison), out_dir, and per-stage params fit/rig/eye/lid/inner/seam/seam_vmin
#   (see run_wrap.sh and the stage scripts; a stage left out runs on its defaults),
#   glasses (glasses_extract.py params: the Tripo frames are left out of the fit's target and rebuilt by add_glasses.py with glasses_build params),
#   skin_detail (skin_detail.py: template skin detail on the baked face).
import json, os, subprocess, sys, urllib.request
HERE = os.path.dirname(os.path.abspath(__file__))
RB_URL = 'https://raw.githubusercontent.com/microsoft/Microsoft-Rocketbox/master/Assets/Avatars/Adults/'
STAGE_ENV = {'inner': 'INNER_JSON', 'seam': 'SEAM_JSON', 'eye': 'EYE_JSON', 'rig': 'RIG_JSON', 'lid': 'LID_JSON'}
PATH_KEYS = ('char_glb', 'source_glb', 'out_dir', 'concept_png')

def load_config(path):
    cfg = json.load(open(path)); d = os.path.dirname(os.path.abspath(path))
    for k in PATH_KEYS:
        if cfg.get(k): cfg[k] = os.path.normpath(os.path.join(d, cfg[k]))
    return cfg

def template_files(tpl, rb_root):
    d = os.path.join(rb_root, tpl['id'])
    return (os.path.join(d, tpl['id'] + '_facial.fbx'), os.path.join(d, tpl['tga'])) + \
        ((os.path.join(d, tpl['normal']),) if tpl.get('normal') else ())

def template_urls(tpl):
    return (RB_URL + tpl['id'] + '/Export/' + tpl['id'] + '_facial.fbx', RB_URL + tpl['id'] + '/Textures/' + tpl['tga']) + \
        ((RB_URL + tpl['id'] + '/Textures/' + tpl['normal'],) if tpl.get('normal') else ())

def wrap_command(cfg, rb_root, work, out):
    """Environment additions and argv for run_wrap.sh."""
    env = {v: json.dumps(cfg[k]) for k, v in STAGE_ENV.items() if k in cfg}
    files = template_files(cfg['template'], rb_root)
    if len(files) > 2: env['RIG_JSON'] = json.dumps({**cfg.get('rig', {}), 'normal_map': files[2]})  # template normal map on the face
    if 'skin_detail' in cfg: env['SKIN_DETAIL_JSON'] = json.dumps(cfg['skin_detail'])
    if 'seam_vmin' in cfg: env['SEAM_VMIN'] = str(cfg['seam_vmin'])
    if cfg.get('unity_height_m'): env['UNITY_H'] = str(cfg['unity_height_m'])
    if cfg.get('concept_png'): env['CONCEPT_PNG'] = cfg['concept_png']
    if cfg.get('concept_crop'): env['CONCEPT_CROP'] = ','.join(str(v) for v in cfg['concept_crop'])
    fit = dict(cfg.get('fit', {}))
    if 'glasses' in cfg:  # Tripo frames fused into the face: fit to the skin only, rebuild clean frames after rigging
        env['GLASSES_JSON'] = fit['target_exclude_json'] = os.path.join(work, 'glasses.json')
        env['GLASSES_BUILD_JSON'] = json.dumps(cfg.get('glasses_build', {}))
    fbx, tga = files[:2]
    argv = [os.path.join(HERE, 'run_wrap.sh'), cfg['name'], cfg['char_glb'], fbx, tga, work, out,
            str(cfg.get('face_cz', 0.9)), str(cfg.get('face_size', 0.17)), json.dumps(fit)]
    return env, argv

def glasses_command(cfg, work, blender='blender'):
    return [blender, '-b', '--factory-startup', '-P', os.path.join(HERE, 'glasses_extract.py'), '--', cfg['char_glb'],
            os.path.join(work, 'char_front_lm.json'), os.path.join(work, 'char_front.json'), os.path.join(work, 'glasses.json'),
            json.dumps(cfg['glasses'])]

def unity_mm(glb_units, height_m):
    """GLB characters are 1 m tall; Unity scales them to height_m."""
    return None if height_m is None else glb_units * height_m * 1000

def fetch_template(tpl, rb_root):
    for path, url in zip(template_files(tpl, rb_root), template_urls(tpl)):
        if not os.path.exists(path):
            os.makedirs(os.path.dirname(path), exist_ok=True); urllib.request.urlretrieve(url, path)

def main(cfg_path, rb_root, work, out=None):
    cfg = load_config(cfg_path); out = out or cfg['out_dir']
    blender = os.environ.get('BLENDER', '/Applications/Blender.app/Contents/MacOS/Blender')
    if not os.path.exists(cfg['char_glb']):
        dec = cfg['decimate']; os.makedirs(work, exist_ok=True)
        with open(os.path.join(work, 'decimate.log'), 'w') as log:
            subprocess.run([blender, '-b', '--factory-startup', '-P', os.path.join(HERE, 'decimate.py'), '--', cfg['source_glb'],
                            cfg['char_glb'], str(dec['target'])] + (['face'] if dec.get('face') else []), stdout=log, stderr=log, check=True)
    fetch_template(cfg['template'], rb_root)
    if 'glasses' in cfg:  # frame outline needs the pupils: same front render + landmarks as run_wrap.sh step 1
        os.makedirs(work, exist_ok=True); fr = os.path.join(work, 'char_front')
        with open(os.path.join(work, 'glasses_extract.log'), 'w') as log:
            subprocess.run([blender, '-b', '--factory-startup', '-P', os.path.join(HERE, 'render_front.py'), '--', cfg['char_glb'], fr,
                            '0', '0', str(cfg.get('face_cz', 0.9)), str(cfg.get('face_size', 0.17))], stdout=log, stderr=log, check=True)
            subprocess.run([os.environ['PY'], '-I', os.path.join(HERE, 'landmarks.py'), fr + '.png', os.environ['MP_MODEL'], fr + '_lm.json'],
                           stdout=log, stderr=log, check=True)
            subprocess.run(glasses_command(cfg, work, blender), stdout=log, stderr=log, check=True)
    env, argv = wrap_command(cfg, rb_root, work, out)
    subprocess.run(['bash'] + argv, env={**os.environ, **env}, check=True)

if __name__ == '__main__':
    main(*sys.argv[1:])
