#!/bin/sh
# v2 (2026-10-29): darker brown-hazel irises with visible pupils, face-mask seam matched to Hank's skin.
# Runs after the v1 chain (rb_prep -> fit -> bake -> inpaint_mask -> inner_islands); OUT holds its outputs.
# usage: [PY=python-with-cv2] build_v2.sh OUT_DIR
set -e
OUT=$1; S=$(dirname "$0"); B=/Applications/Blender.app/Contents/MacOS/Blender; PY=${PY:-python3}  # PY needs numpy + opencv
# eyes: Rocketbox iris (pale olive + baked catchlights) -> very dark brown, larger dark core, one small painted catchlight;
# sclera a dim warm off-white. Hank's socket is dark, so an iris as light as the dealer's still reads pale next to it.
$PY $S/inner_tex.py $OUT/rb_head_color.png $OUT/inner_islands.json $OUT/rb_inner_v2.png \
  '{"iris": [539.75, 1904.98, 40, 10], "pupil_scale": 1.3, "sclera_gain": 0.55, "sclera_tint": [0.86, 0.94, 1.0], "iris_stops": [[0.0, [18, 30, 46]], [0.35, [15, 25, 40]], [0.65, [12, 20, 32]], [0.88, [10, 15, 24]], [1.0, [8, 10, 14]]], "sclera_paint": {"white": [100, 112, 124], "edge": [72, 80, 96]}, "catchlight": {"offset": [0.3, -0.25], "radius": 0.11, "strength": 0.8}}'
# closed lids: replace what the bake left on the upper lids (painted eye, light patches) with skin from around them, plus a lash line
$B -b -P $S/lid_data.py -- $OUT/bake5.blend $OUT/lid_data.json
$PY $S/lid_fill.py $OUT/face_bake5_fixed.png $OUT/lid_data.json $OUT/face_lid.png $OUT/lid_dbg.jpg
# seam: feathered linear-light gain so the first visible face rings match Hank's skin next to the cut
$B -b -P $S/seam_data.py -- $OUT/bake5.blend $OUT/seam_data.json $OUT/hank_tex.png
$PY $S/seam_match.py $OUT/seam_data.json $OUT/hank_tex.png $OUT/face_lid.png $OUT/face_bake6.png
# rig: face roughness 0.9 (= Hank); eye roughness 0.2: URP has no reflections here, so a smooth eye keeps its colour and only a
# tight highlight, while a rough one lays a broad grey specular over the dark iris; eyes turned about the fitted eyeball sphere
# centre (bones there too, rest axis through the pupil) and moved 1.5 mm down into the lid opening, pupils 5 deg down at a point
# 1 m ahead; upper lids opened 18 deg, lower lids raised 8 deg (his squint), rim smoothed, shape keys re-based; eyeLook* keys keep
# only their lid motion (the eye bones turn the eyeball); eyeLookDown's upper lid goes about 52% of the way to the blink end pose along an arc round the eye (4.5 mm in the GLB, 8.1 mm at Unity scale 1.8)
$B -b -P $S/rig.py -- $OUT/bake5.blend $OUT/face_bake6.png $OUT/hank_wrap_v2.glb $OUT/rig7.blend $OUT/rb_inner_v2.png \
  '{"eye_rough": 0.2, "lookdown_mm": 4.5, "eye_sphere_centre": true, "eye_dz_mm": 1.5, "eye_pitch_deg": 5, "eye_conv_m": 1.0, "face_rough": 0.9, "lid_open_deg": 18, "lower_lid_deg": 8, "lid_full_at": 0.25, "lid_falloff": [1.4, 1.85, -0.40, -0.27], "lid_smooth": 2, "eyelook_lids_only": true}'
$B -b -P $S/verify_glb.py -- $OUT/hank_wrap_v2.glb $OUT/verify_v2.json
