# Round summary: one row per character, concept head | neutral 1 m | blink 1 m | jaw open 1 m | mouth 0.5 m neutral | jaw open
# (from <out_dir>/renders; the 0.5 m shots come from mouth_shots.py).
# usage: python summary_sheet.py out.jpg title characters/a.json characters/b.json ...
import json, os, sys
from PIL import Image, ImageDraw, ImageFont
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__))); import pipeline as pl
out, title, cfgs = sys.argv[1], sys.argv[2], sys.argv[3:]
C = 360; FONT = ImageFont.truetype('/System/Library/Fonts/STHeiti Medium.ttc', 18)
def tile(p, box):
    im = Image.open(p).convert('RGB'); w, h = im.size; im = im.crop((int(box[0] * w), int(box[1] * h), int(box[2] * w), int(box[3] * h)))
    k = C / max(im.size); return im.resize((round(im.width * k), round(im.height * k)), Image.LANCZOS)
cols = ['設定圖', '中性 1 m', '眨眼 1 m', '張嘴 1 m', '嘴 0.5 m 中性', '嘴 0.5 m 張嘴']
sheet = Image.new('RGB', (C * len(cols) + 120, 60 + C * len(cfgs)), 'white'); d = ImageDraw.Draw(sheet)
d.text((8, 8), title, fill='black', font=FONT)
for j, t in enumerate(cols): d.text((120 + j * C + 6, 34), t, fill='black', font=FONT)
for i, cp in enumerate(cfgs):
    cfg = pl.load_config(cp); r = os.path.join(cfg['out_dir'], 'renders'); y = 60 + i * C
    d.text((8, y + C // 2), cfg['name'].capitalize(), fill='black', font=FONT)
    ims = [tile(cfg['concept_png'], cfg.get('concept_crop', [0.09, 0.02, 0.18, 0.22]))]
    ims += [tile(os.path.join(r, n + '.jpg'), (.3, .2, .7, .6)) for n in ('neutral_1m', 'blink_1m', 'jaw_1m')]
    ims += [tile(os.path.join(r, n + '.jpg'), (.15, .2, .85, .8)) for n in ('mouth50_neutral', 'mouth50_jaw')]
    for j, im in enumerate(ims): sheet.paste(im, (120 + j * C + (C - im.width) // 2, y + (C - im.height) // 2))
sheet.save(out, quality=88); print('SUMMARY', out)
