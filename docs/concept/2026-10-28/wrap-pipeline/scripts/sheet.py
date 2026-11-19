import sys
from PIL import Image, ImageDraw
out, cols, cell = sys.argv[1], int(sys.argv[2]), int(sys.argv[3]); items = sys.argv[4:]
rows = (len(items) + cols - 1) // cols
s = Image.new('RGB', (cols * cell, rows * (cell + 18)), 'white'); d = ImageDraw.Draw(s)
for i, it in enumerate(items):
    p, _, lab = it.partition('::')
    im = Image.open(p).convert('RGB'); im.thumbnail((cell, cell))
    x, y = (i % cols) * cell, (i // cols) * (cell + 18); s.paste(im, (x, y)); d.text((x + 4, y + cell + 3), lab or p.split('/')[-1], fill='black')
s.save(out, quality=88)
