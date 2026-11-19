# Face-wrap pipeline (per-character configs)

One shared set of scripts (`scripts/`, grown from `dealer-3d/wrap/scripts/`) and one config per character (`characters/<name>.json`).

```
PY=<python with mediapipe, opencv, pillow> MP_MODEL=<face_landmarker.task> \
  python3 scripts/pipeline.py characters/<name>.json <RB_ROOT> <WORK_DIR> [OUT_DIR]
```
- `RB_ROOT/<template id>/` holds the Rocketbox `_facial.fbx` and head colour tga; missing files are downloaded from the
  Microsoft-Rocketbox GitHub repo (MIT).
- If `char_glb` does not exist it is made from `source_glb` with `decimate.py source out 12000 face` (face kept at full density).
- About 2–3 minutes per character. Output goes to `out_dir` (`<name>-3d/wrap/`): `<name>-wrap.glb`, `verify.json`,
  `renders/` (all acceptance renders + `compare_1m.jpg`: concept head | Tripo 1 m | neutral, blink, jaw open, look down,
  with IPD / pupil shift / eye-openness line).
- Rebuilding the dealer with `characters/dealer.json` gives a `dealer-wrap.glb` byte-identical to the shipped one
  (sha1 b50e464ee209…): every option added since is off by default.
- Hank is not migrated: his build (`hank-3d/wrap/scripts/build_v2.sh`) runs older steps (`lid_data`/`lid_fill`) on top of the
  v1 chain, so it cannot be reproduced by these scripts. `hank-3d/` was not touched.

## Config keys
`name`, `source_glb`, `char_glb`, `decimate`, `template {id, tga}`, `unity_height_m` (only for the mm report),
`concept_png` + `concept_crop` (x0,y0,x1,y1 fractions of the turnaround sheet), `out_dir`, `face_cz`/`face_size` (front render
centre and ortho size, GLB units), and per stage: `fit`, `rig`, `eye`, `lid`, `inner`, `seam`, `seam_vmin`, `glasses`, `glasses_build`.
Stage parameters are documented in `dealer-3d/wrap/scripts/README.md`; new ones:

| stage | key | what |
|---|---|---|
| fit | `eye_sym` (`"min"`/`"mean"`), `eye_sym_level` | same eyeball size both sides (min: never larger than its lid fit), centres mirrored; level=false keeps each eye's height (Reginald) |
| fit | `target_exclude_json` | set by the pipeline for glasses: frame vertices are left out of the landmark rays and the shrinkwrap target |
| fit | `top_cut`, `oval_scale`, `cut_poke_marg: null` | existing options; used for Marcus/Erik forehead edge, Jace forehead, Erik beard volume |
| rig | `blink_gain` | scales the skin part of the eyeBlink keys (lids stopped short of each other after the fit) |
| template | `normal` | the template's head normal map (same UVs as the mask); rig `normal_strength`. With `skin_detail` it is faded flat toward the mask border |
| (top) | `skin_detail {k, sigma, ring0, ring1}` | `skin_detail.py`: the template's photographed skin detail (luminance only) on the smooth Tripo bake, faded in from the border — the plastic look in the headset |
| fit | `cut_facing` | cut only body faces whose normal faces forward at least this much (keeps the under-chin/throat skin; Reginald) |
| rig | `inner_floor_pct` | lift the mouth bag's lowest vertices (below that height percentile) to it: its bottom poked out under Reginald's chin as an orange patch when the head turned against the neck |
| eye | `sclera_white`, `sclera_edge` | paint colours of the sclera (a dimmer white for a less blank look) |
| rig | `neck_ramp {z0, z1}` | Tripo weights shirt collars to Neck/Head (Reginald's collar tore at a 35° turn): below z1 the Neck+Head share fades into Spine2 (none left at z0) |
| rig | `basis_keys`, `basis_keep` | bake part of a key into the rest pose and every key except `basis_keep` (new characters: mouthClose 0.12–0.25, jawOpen kept): the fitted lips left a thin gap at rest and the upper teeth showed as a white line |
| rig | `jaw_inner_lag` | jawOpen moves the lower teeth/tongue/mouth floor that much less than the lips, so they show in the opening |
| inner | `mouth_gain`, `mouth_dark` | lift the mouth-interior texture (the cavity rendered black); `mouth_dark` lists the island sizes to darken (default = Hank's M03 walls 47/36/8; on M09 those are the tongue, so the new characters use `[]`) |
| lid | `lid_base: "skin"`, `lid_detail`, `lid_gain`, `fill_radius` | closed-lid tone from the skin round the eye (Telea fill) with only the template's fine detail on top, instead of the baked painted eye / template colour (Jace, Reginald too light, Erik too dark) |
| lid | `nose_r0`/`nose_r1` (existing) | Marcus: wider radius also flattens the moustache-like shadow painted on the upper lip |
| eye | `pupil_set_frac` | fixed pupil size for templates whose iris is near-black (detection grabs the whole iris: M12, M19) |
| glasses | `box`, `grow_box`, `bridge_box` … | `glasses_extract.py`: frame vertices (thin shells hit again within 3 cm toward the head axis), rim outlines round the pupils, temples |
| glasses_build | `tube_r`, `margin`, `shape: "oval"`, `exp`, `drop_body_frame`, `drop_box` | `add_glasses.py`: rebuilt metal frames, kept ≥ margin from the face in neutral / wide / brows up / smile+squint, bridge arched over the nose |

## For the Unity stage
- Marcus's `Glasses` mesh: set the renderer's Cast Shadows to Off. The dotted ring under his eyes in the headset is the frames'
  shadow, not texture (`marcus-3d/wrap/renders/rim_residue_check.jpg`: gone with the shadow off); glTF cannot carry the flag.
- Known, not fixed (stop-loss): Erik's right-cheek line and Jace's temple speck at a head turn (body faces at the mask edge),
  a dark notch under Reginald's chin when he looks left in the game (face +50°).
- Body meshes are Tripo's own; Jace's light patches on the neck/collar are painted in the Tripo source texture (also on the
  47k source), slightly more faceted after decimation.

## Texture budget (headset round)
Each new character GLB carries four 2048² images: body colour (Tripo), face colour, face normal, mouth/eye (template),
JPEG in the GLB: 2.6–3.2 MB per GLB (Hank 3.4 MB with three images). In Unity the face normal adds one 2048² texture per
character: about 2.5 MB each at ASTC 6x6 with mips, about 10 MB for all four; `rig.normal_size: 1024` would cut that to a quarter.

## Added scripts
`pipeline.py` (config → run), `prep_template.sh` (one Rocketbox candidate for `rank_templates.py`), `measure.py` (IPD and eye
openness from MediaPipe on orthographic front renders), `compare_sheet.py`, `compare_glb.py` (vertex/key/bone/texture diff of two
GLBs), `components.py`, `glasses_extract.py`, `glasses_inpaint.py`, `add_glasses.py`, `acclib.py` (glasses geometry helpers),
`write_credits.py`, `summary_sheet.py` (one row per character: concept | neutral | blink | jaw open at 1 m | mouth at 0.5 m neutral / open), `mouth_shots.py` (0.5 m mouth close-ups) and `turn_shots.py` (neck ±35° at 1 m), both run in step 9; `pose_shots.py` renders any bone poses from a json; `pose_unity.py` renders the poses unity-r1 captured in the game (`<name>_pose.json`, glTF-local quaternions). `run_wrap.sh` gained step 9 (gaze-down sheet, IPD, comparison sheet, renders copied to `OUT/renders`).

## Template choice
`prep_template.sh` for each candidate, then
`python rank_templates.py <work>/char_front_lm.json grid.jpg TAG=<cand>/rb_front_lm.json:<cand>/rb_front.png ...`;
pick by shape score **and** look (sex, age, ethnicity: the lids carry most of the likeness after the landmark warp).
Each character's `wrap/CREDITS.md` records the choice.

## Tests
`python test_pipeline.py`, `test_measure.py`, `test_acclib.py`, plus the inherited `test_wraplib.py`, `test_texfix.py`, `test_texlib.py`
(run with the mediapipe venv's python, `-I`).

## Known pitfalls (in addition to dealer-3d/wrap/scripts/README.md)
- Tripo glasses are fused into the face: they cannot be cut out cleanly (rims merge into the cheeks and the nose).
- Tripo's painted pupils can sit off-centre in the eye (Jace: looking outward). Judge IPD together with the eye corners.
- fit.py's vertex-graph dilation (`a[idx] |= b` with repeated indices) under-grows; left as is so Hank/dealer results stay
  identical. `acclib.grow_region` uses `np.logical_or.at`.
