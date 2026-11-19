#!/usr/bin/env bash
# Prepare one Rocketbox candidate head for ranking: crop/prep blend, front render, MediaPipe landmarks (same steps as run_wrap.sh step 1).
# usage: PY=... MP_MODEL=... prep_template.sh FACIAL_FBX HEAD_TGA OUT_DIR
set -euo pipefail
SC=$(cd "$(dirname "$0")" && pwd); FBX=$1; TGA=$2; O=$3
B=${BLENDER:-/Applications/Blender.app/Contents/MacOS/Blender}; PY=${PY:?set PY}; MP=${MP_MODEL:?set MP_MODEL}
mkdir -p $O
"$B" -b --factory-startup -P $SC/rb_prep.py -- $FBX $TGA $O/rb_head.blend > $O/rb_prep.log 2>&1
EZ=$(grep '^RB ' $O/rb_prep.log | sed -E "s/.*'L': \[[^,]*, [^,]*, ([0-9.]+)\].*/\1/")
RCZ=$(python3 -c "print(round($EZ - 0.025, 4))")
"$B" -b --factory-startup -P $SC/render_front.py -- $O/rb_head.blend $O/rb_front 0 0 $RCZ 0.30 > $O/rb_front.log 2>&1
$PY -I $SC/landmarks.py $O/rb_front.png $MP $O/rb_front_lm.json 2>/dev/null | tail -1
