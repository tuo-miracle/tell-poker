# 1 m acceptance sheet: concept-art head | original Tripo 1 m | wrapped 1 m neutral, blink, jaw open, look down; IPD line.
# usage: python compare_sheet.py out.jpg title concept.png|- orig_1m.png neutral.png blink.png jaw.png lookdown.png [ipd.json] [crop x0,y0,x1,y1]
import json, sys
from PIL import Image, ImageDraw, ImageFont
out, title, concept, orig, *wrap = sys.argv[1:9]; rest = sys.argv[9:]
ipd = json.load(open(rest[0])) if rest and rest[0] != '-' else None
crop = [float(v) for v in rest[1].split(',')] if len(rest) > 1 else [0.09, 0.02, 0.18, 0.22]
C = 420
FACE_40 = (.3, .2, .7, .6)      # final_renders 1 m shots (40 deg fov): the face
FACE_22 = (.02, .0, .98, .96)  # gazedown_sheet front shots (22 deg fov, centred 1 cm higher): about the same framing (measured)
def tile(p, label, box=FACE_40):
    im = Image.open(p).convert('RGB'); w, h = im.size
    im = im.crop((int(box[0] * w), int(box[1] * h), int(box[2] * w), int(box[3] * h)))
    k = C / max(im.size); im = im.resize((round(im.width * k), round(im.height * k)), Image.LANCZOS); t = Image.new('RGB', (C, C + 28), 'white'); t.paste(im, ((C - im.width) // 2, 28))
    ImageDraw.Draw(t).text((6, 6), label, fill='black', font=FONT); return t
try: FONT = ImageFont.truetype('/System/Library/Fonts/STHeiti Medium.ttc', 18)
except OSError: FONT = ImageFont.load_default()
tiles = ([tile(concept, '設定圖', crop)] if concept != '-' else []) + [tile(orig, '原 Tripo 1 m')]
tiles += [tile(p, l, b) for p, l, b in zip(wrap, ['wrap 中性 1 m', '眨眼 1 m', '張嘴 1 m', '看下方 1 m'], [FACE_40] * 3 + [FACE_22])]
sheet = Image.new("RGB", (C * len(tiles), C + 116), "white")
for k, t in enumerate(tiles): sheet.paste(t, (k * C, 50))
d = ImageDraw.Draw(sheet); d.text((8, 12), title, fill='black', font=FONT)
if ipd:
    s = f"IPD 原 {ipd['ipd_src_mm_unity']:.1f} mm → wrap {ipd['ipd_wrap_mm_unity']:.1f} mm（差 {ipd['ipd_diff_mm_unity']:+.1f} mm，Unity 尺度）" \
        f"  瞳孔位移 R {ipd['pupil_shift_mm_unity']['R']} L {ipd['pupil_shift_mm_unity']['L']} mm  {'OK' if ipd['ok'] else '超過 3 mm'}" \
        + (f"  眼睛開度(高/寬) 原 R {ipd['open_src']['R']:.2f} L {ipd['open_src']['L']:.2f} → wrap R {ipd['open_wrap']['R']:.2f} L {ipd['open_wrap']['L']:.2f}" if 'open_src' in ipd else '')
    d.text((8, C + 86), s, fill='black', font=FONT)
sheet.save(out, quality=88); print('SHEET', out)
