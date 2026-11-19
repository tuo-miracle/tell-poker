import json, sys
import mediapipe as mp
from mediapipe.tasks.python import vision, BaseOptions
from PIL import Image, ImageDraw
img_path, model, out = sys.argv[1], sys.argv[2], sys.argv[3]
det = vision.FaceLandmarker.create_from_options(vision.FaceLandmarkerOptions(base_options=BaseOptions(model_asset_path=model), num_faces=1))
im = Image.open(img_path).convert('RGB')
res = det.detect(mp.Image.create_from_file(img_path))
if not res.face_landmarks: sys.exit('NO FACE')
W, H = im.size
pts = [(p.x * W, p.y * H) for p in res.face_landmarks[0]]
json.dump(pts, open(out, 'w'))
d = ImageDraw.Draw(im)
for x, y in pts: d.ellipse((x - 2, y - 2, x + 2, y + 2), fill=(0, 255, 0))
im.save(out.replace('.json', '_overlay.png')); print('LANDMARKS', len(pts))
