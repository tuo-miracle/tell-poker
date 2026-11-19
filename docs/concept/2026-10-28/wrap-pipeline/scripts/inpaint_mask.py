import json, sys, numpy as np, cv2
tex, mj, out, dbg = sys.argv[1:5]
im = cv2.imread(tex); h, w = im.shape[:2]; T = json.load(open(mj))
mask = np.zeros((h, w), np.uint8)
for t in T:
    cv2.fillPoly(mask, [np.array([[u * w, (1 - v) * h] for u, v in t]).round().astype(np.int32)], 255)
mask = cv2.dilate(mask, np.ones((5, 5), np.uint8))
res = cv2.inpaint(im, mask, 12, cv2.INPAINT_TELEA)
cv2.imwrite(out, res)
ov = im.copy(); ov[mask > 0] = (0, 255, 0)
cv2.imwrite(dbg, cv2.resize(np.hstack([ov, res]), (2048, 1024)))
print('MASKPX', int((mask > 0).sum()))
