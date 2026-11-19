# Side-by-side before/after sheets (labels: env LA, LB): python3 before_after.py out_dir name:before.png:after.png ...
import sys, os
from PIL import Image, ImageDraw, ImageFont
out = sys.argv[1]; os.makedirs(out, exist_ok=True)
font = ImageFont.truetype('/System/Library/Fonts/Helvetica.ttc', 28)
for spec in sys.argv[2:]:
    name, a, b = spec.split(':')
    A, Bm = Image.open(a).convert('RGB'), Image.open(b).convert('RGB')
    S = Image.new('RGB', (A.width + Bm.width + 8, A.height), 'white'); S.paste(A, (0, 0)); S.paste(Bm, (A.width + 8, 0))
    d = ImageDraw.Draw(S)
    for x, t in ((10, os.environ.get('LA', 'before (v1)')), (A.width + 18, os.environ.get('LB', 'after (v2)'))):
        d.rectangle((x - 4, 6, x + 160, 42), fill=(0, 0, 0)); d.text((x, 10), t, fill='white', font=font)
    S.save(os.path.join(out, name + '.jpg'), quality=90)
print('BEFOREAFTER', len(sys.argv) - 2)
