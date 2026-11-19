#!/usr/bin/env bash
# Face-wrap pipeline for one character, end to end (Blender + Python only).
# usage: run_wrap.sh NAME CHAR_GLB TEMPLATE_FBX TEMPLATE_HEAD_TGA WORK_DIR OUT_DIR FACE_CZ FACE_SIZE [FIT_JSON]
#   FACE_CZ / FACE_SIZE: centre height and ortho size of the character's front face render (character is 1 m tall).
# env: BLENDER (Blender binary), PY (python with mediapipe, opencv, pillow), MP_MODEL (face_landmarker.task),
#      LID_JSON (optional lid_tex.py params, e.g. '{"d0":1.4,"d1":2.0}'),
#      EYE_JSON (eye_tex.py params), INNER_JSON (inner_tex.py params, e.g. sclera_gain/sclera_tint), SEAM_JSON (seam_match.py params), SEAM_VMIN (skin value floor for seam_stats.py, raise for dark red hair),
#      RIG_JSON (rig.py params; default aims each pupil level at a point 1 m ahead and lays the eye bone through it)
set -euo pipefail
NAME=$1; GLB=$2; FBX=$3; TGA=$4; W=$5; OUT=$6; CZ=$7; SZ=$8; FITJ=${9:-'{}'}
SC=$(cd "$(dirname "$0")" && pwd)
B=${BLENDER:-/Applications/Blender.app/Contents/MacOS/Blender}; PY=${PY:?set PY}; MP=${MP_MODEL:?set MP_MODEL}
L=$W/logs; mkdir -p $W $L $OUT/renders
bl() { local log=$1; shift; "$B" -b --factory-startup "$@" > $L/$log.log 2>&1 || { echo "FAILED $log"; tail -20 $L/$log.log; exit 1; }
       if grep -q '^Traceback' $L/$log.log; then echo "ERROR in $log"; grep -A8 '^Traceback' $L/$log.log | head -30; exit 1; fi; }
echo "== 1 template + landmarks"
bl rb_prep -P $SC/rb_prep.py -- $FBX $TGA $W/rb_head.blend
EZ=$(grep '^RB ' $L/rb_prep.log | sed -E "s/.*'L': \[[^,]*, [^,]*, ([0-9.]+)\].*/\1/")
RCZ=$(python3 -c "print(round($EZ - 0.025, 4))")
bl rb_front -P $SC/render_front.py -- $W/rb_head.blend $W/rb_front 0 0 $RCZ 0.30
bl char_front -P $SC/render_front.py -- $GLB $W/char_front 0 0 $CZ $SZ
$PY -I $SC/landmarks.py $W/rb_front.png $MP $W/rb_front_lm.json 2>/dev/null | tail -1
$PY -I $SC/landmarks.py $W/char_front.png $MP $W/char_front_lm.json 2>/dev/null | tail -1
echo "== 2 fit"
bl fit -P $SC/fit.py -- $NAME $GLB $W/rb_head.blend $W/rb_front_lm.json $W/rb_front.json $W/char_front_lm.json $W/char_front.json $W/fit.blend "$FITJ"
grep -E '^FIT (sim|tps resid|shrink|front gap|cut)' $L/fit.log | cut -c1-160
echo "== 3 bake + lid inpaint"
bl bake -P $SC/bake.py -- $NAME $W/fit.blend $GLB $W/face_bake.png $W/bake.blend "{\"lm\":\"$W/char_front_lm.json\",\"cam\":\"$W/char_front.json\"}"
$PY -I $SC/inpaint_mask.py $W/face_bake.png $W/face_bake_lidmask.json $W/face_inpaint.png $W/lidmask_dbg.jpg
echo "== 4 mouth/eye texture (template), dark iris"
$PY -I -c "from PIL import Image; Image.open('$TGA').convert('RGB').save('$W/rb_head_color.png')"
bl islands $W/bake.blend -P $SC/inner_islands.py -- $NAME $W/inner_islands.json
$PY -I $SC/inner_tex.py $W/rb_head_color.png $W/inner_islands.json $W/rb_inner_mod.png "${INNER_JSON:-{\}}"
$PY -I $SC/eye_tex.py $W/rb_inner_mod.png $W/inner_islands.json $W/rb_inner_eye.png $W/eyetex_dbg.jpg "${EYE_JSON:-{\}}"
echo "== 5 template lids, seam colour match (Hank v2 method: local linear gains at the border, feathered inward)"
bl seam $W/bake.blend -P $SC/seam_bands.py -- $NAME $W/face_inpaint.png $W/seam.json "{\"lm\":\"$W/char_front_lm.json\",\"cam\":\"$W/char_front.json\"}"
$PY -I $SC/lid_tex.py $W/face_inpaint.png $W/rb_head_color.png $W/seam.json $W/face_lid.png "${LID_JSON:-{\}}"
bl seam_data -P $SC/seam_data.py -- $NAME $W/bake.blend $W/seam_data.json $W/char_tex.png
grep '^SEAMDATA' $L/seam_data.log
$PY -I $SC/seam_match.py $W/seam_data.json $W/char_tex.png $W/face_lid.png $W/face_seam.png "${SEAM_JSON:-{\}}"
$PY -I $SC/skin_match.py $W/face_seam.png $W/seam.json $W/face_final.png "" '{"no_gain": true}'
TEX=$W/face_final.png
RIGJ=$(python3 -c "import json,sys; d={'eye_sphere_centre': True, 'eye_pitch_deg': 0, 'eye_conv_m': 1.0}; d.update(json.loads(sys.argv[1])); d['eye_tex_json']=sys.argv[2]; print(json.dumps(d))" "${RIG_JSON:-{\}}" $W/rb_inner_eye.json)
echo "  rig params $RIGJ"
echo "== 6 rig + export"
bl rig -P $SC/rig.py -- $NAME $W/bake.blend $TEX $OUT/$NAME-wrap.glb $W/rig.blend $W/rb_inner_eye.png "$RIGJ"
grep -E '^RIG (eye|lid|lower)' $L/rig.log | cut -c1-200
bl seam_renders -P $SC/seam_renders.py -- $OUT/$NAME-wrap.glb $W/seamr
$PY -I $SC/seam_stats.py $W/seamr $W/seam_stats.json $W/seamr_overlay ${SEAM_VMIN:-35} | grep '^SEAM'
bl verify -P $SC/verify_glb.py -- $OUT/$NAME-wrap.glb $OUT/verify.json
grep '^VERIFY {' $L/verify.log
echo "== 7 eye check (pupil/iris visible at a level look-at 1 m ahead, blink closure, eyeball poke-through)"
bl eyecheck -P $SC/eye_check.py -- $OUT/$NAME-wrap.glb $W/eye_check.json $W/eyecheck $W/rb_inner_eye.json
grep -o 'EYECHECK.*' $L/eyecheck.log | cut -c1-600
echo "== 8 renders"
bl eyesheet -P $SC/eye_sheet.py -- $OUT/$NAME-wrap.glb $W/es
bl final -P $SC/final_renders.py -- $OUT/$NAME-wrap.glb $W/final
bl headturn -P $SC/headturn.py -- $OUT/$NAME-wrap.glb $W/final
bl orig -P $SC/final_renders.py -- $GLB $W/orig neutral_close,neutral_1m,neutral_1m_q,body_front,body_side,body_back,seam_close_q,seam_close_side
echo "DONE $OUT/$NAME-wrap.glb"
