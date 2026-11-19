# Face wrap scripts (parameterised from Hank's)

Per character: `run_wrap.sh NAME CHAR_GLB TEMPLATE_FBX TEMPLATE_HEAD_TGA WORK_DIR OUT_DIR FACE_CZ FACE_SIZE [FIT_JSON]`
with env `PY` (venv with mediapipe/opencv/pillow) and `MP_MODEL` (face_landmarker.task). About 1 minute end to end.

Choosing the template: `rb_prep.py` + `render_front.py` + `landmarks.py` for each candidate, then
`rank_templates.py char_front_lm.json grid.jpg TAG=lm.json:front.png ...` (lower score = closer face shape).

Dealer (2026-10-30): `dealer_run.sh RB_DIR WORK_DIR OUT_DIR` holds the exact parameters (fit, sclera, seam match, lid lift).

Seam colour: `seam_data.py` + `seam_match.py` (Hank v2 method), reported with `seam_renders.py` + `seam_stats.py` (CIEDE2000).
Eyes: `eye_check.py GLB out.json [prefix] [eye_tex_info.json]` measures iris/pupil visibility at a level look-at 1 m ahead.

Options added for the dealer (all off by default, so Hank's results are unchanged):
- fit.py `cut_poke_marg` (cut body skin poking in front of the mask), `front_off`/`crop_close` (crop depth / notch closing; tried, not used)
- lid_tex.py `mode: detail` (template lid detail on the character's own tone) and `nose_r0/nose_r1` (flatten the nostril shadow painted in the Tripo texture)
- rig.py `normal_rings` (blend border normals toward the body), `lower_lid_deg`, plus Hank's `eye_sphere_centre`, `eye_pitch_deg`, `lid_open_deg`, `eyelook_lids_only`
- common.setup_scene sets shadow-terminator offsets on every mesh (hard-sun self-shadow artifact on low-poly faces)

Eye appearance (v4): eye_tex.py `pupil_frac`, `iris_scale`, `sclera`, `catchlight`; lid_tex.py `rim`, `rim_tint`, `lash`; rig.py `eye_scale`, `eye_dx_mm`, `eye_rough`;
fit.py `eye_back0`. `eye_sheet.py GLB prefix` renders neutral/smile/gaze left and right at 1 m and close-up, lit and dim.

Eye spacing (v5): fit.py `eye_pin_r` holds the lid rims on the character's landmarks during shrinkwrap (without it the rims drifted 3.7 mm outward per side);
fit.py logs `lid rim shrink drift`. `eye_spacing.py tag=lm.json ...` reports inter-pupil and inner-corner ratios.

eyeLookDown (v6): rig.py `lookdown_lid_mm` rebuilds the upper-lid part of the key from the eyeBlink end pose along an arc round the eye centre.
Millimetres are in GLB units (character 1 m tall). `gazedown_sheet.py GLB prefix` renders it. Morph keys add, so blink + eyeLookDown must be limited at runtime.

Tests: `python test_wraplib.py`, `python test_texfix.py`, `python test_texlib.py`.
