# Write <out_dir>/CREDITS.md for a wrapped character from its config (template source, why chosen, MIT text).
# usage: python write_credits.py characters/NAME.json "why this template (one paragraph)"
import json, os, sys
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__))); import pipeline as pl
cfg = pl.load_config(sys.argv[1]); why = sys.argv[2]; t = cfg['template']; fbx, tga = pl.template_urls(t)
mit = open(os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', '..', 'dealer-3d', 'wrap', 'CREDITS.md')).read()
mit = mit[mit.index('```\nMIT License'):]; mit = mit[:mit.index('```', 4) + 3]
glasses = ', glasses rebuilt as procedural metal frames from the Tripo frame outline' if 'glasses' in cfg else ''
md = f"""# Credits

## Microsoft Rocketbox Avatar Library (MIT License)

The face geometry (eyelids, mouth cavity, teeth, tongue, eyeballs), the 52 ARKit blendshapes, the eye/mouth-interior
texture and the eyelid texture detail in `{cfg['name']}-wrap.glb` are derived from the Microsoft Rocketbox avatar **{t['id']}**.

- Source: https://github.com/microsoft/Microsoft-Rocketbox
- File used: {fbx}
- Texture used: {tga}
- License: https://github.com/microsoft/Microsoft-Rocketbox/blob/master/LICENSE.md

### Why {t['id']}
{why}

{mit}

Modifications: head cropped to the face mask, non-ARKit shape keys removed, non-rigidly fitted to the character's face,
skin re-textured with the character's own colour map (baked), eyelid region re-coloured to the character's skin, irises
repainted, eyeballs scaled and placed, eyeball motion removed from the eyeLook* keys (the eye bones turn the eyes){glasses}.
Build: `../../wrap-pipeline/characters/{cfg['name']}.json` (see `../../wrap-pipeline/README.md`).

## {cfg['name'].capitalize()}

Body, hair, clothes and skin colour come from the project's Tripo AI generated model (`../{os.path.basename(cfg['source_glb'])}`).

## Tools (not shipped in the GLB)

Landmarks: MediaPipe FaceLandmarker (`face_landmarker.task`, Apache License 2.0).
"""
open(os.path.join(cfg['out_dir'], 'CREDITS.md'), 'w').write(md); print('CREDITS', os.path.join(cfg['out_dir'], 'CREDITS.md'))
