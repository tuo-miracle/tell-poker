# Template skin detail on the baked face: Tripo textures are smooth (the faces read as plastic next to Hank), the template's
# photographed skin is aligned with the mask by construction (the mask keeps its UVs). Adds its fine luminance detail
# (texfix.luminance_detail) over the face skin triangles of seam.json.
# usage: python skin_detail.py face.png template_head.png seam.json out.png ['{"k":0.5,"sigma":3,"ring0":0.5,"ring1":4}']
import json, sys, os, numpy as np, cv2
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__))); import texfix as tf
face, tmpl, sj, out = sys.argv[1:5]; P = json.loads(sys.argv[5]) if len(sys.argv) > 5 else {}
A = cv2.imread(face); h = A.shape[0]; T = cv2.resize(cv2.imread(tmpl), (h, h), interpolation=cv2.INTER_AREA)
S = json.load(open(sj)); ring, cov = tf.raster_vertex_field(np.array(S['tri_uv']), np.array(S['tri_ring']), h)
k = P.get('k', 0.5) * tf.gain_weight(ring, P.get('ring0', 0.5), P.get('ring1', 4.0))  # fade in from the mask border: no detail edge
res = tf.luminance_detail(A, T, P.get('sigma', 3.0), cov, k)
if P.get('normal_in'):  # the same fade on the template normal map (flat at the border, so the lit edge does not show)
    from PIL import Image  # TGA: OpenCV cannot read it
    N = cv2.resize(np.array(Image.open(P['normal_in']).convert('RGB'))[..., ::-1], (h, h), interpolation=cv2.INTER_AREA).astype(np.float32)
    f = (k / max(P.get('k', 0.5), 1e-6))[..., None] if P.get('normal_feather', True) else 1.0
    N = np.round(N * f + np.array([255, 128, 128], np.float32) * (1 - f))  # BGR flat normal (0.5, 0.5, 1)
    cv2.imwrite(P['normal_out'], N.astype(np.uint8)); print('SKINDETAIL normal ->', P['normal_out'])
cv2.imwrite(out, res); print('SKINDETAIL px', int(cov.sum()), 'k', P.get('k', 0.5), 'sigma', P.get('sigma', 3.0))
