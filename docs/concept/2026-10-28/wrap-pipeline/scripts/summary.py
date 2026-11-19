import sys, os, json
from PIL import Image, ImageDraw, ImageFont
F, out, ref, origdir, verify = sys.argv[1:6]
font = ImageFont.truetype('/System/Library/Fonts/STHeiti Medium.ttc', 26); small = ImageFont.truetype('/System/Library/Fonts/STHeiti Light.ttc', 20)
C = 420; W = 4 * C
J = lambda n: os.path.join(F, n + '.png')
rows = [
 ('1 眨眼 eyeBlink L+R=1：PASS（完全閉合、無眼球穿出；一側眼皮顏色略淺）', [(J('blink_close'), '特寫 正面'), (J('blink_low'), '特寫 仰角'), (J('blink_side'), '特寫 側'), (J('blink_1m'), '1 m')]),
 ('2 張嘴 jawOpen=1：PASS（見牙、口腔暗、無空洞；開口幅度中等）', [(J('jaw_close'), '特寫 正面'), (J('jaw_low'), '特寫 仰角'), (J('jaw_side'), '特寫 側'), (J('jaw_1m'), '1 m')]),
 ('3 眼骨頭 ±20°：PASS（眼球在眼眶內轉、眼皮未被穿透；上眼皮蓋住部分虹膜）', [(J('eyes_left20'), '左 20°'), (J('eyes_right20'), '右 20°'), (J('eyes_left20_low'), '左 20° 仰角'), (J('eyes_left20_halfblink'), '左 20°＋半眨')]),
 ('4 表情：PASS 自然無破圖，但幅度小（微笑多被八字鬍遮住）', [(J('smile_close'), '微笑'), (J('frown_close'), '皺眉 browDown'), (J('surprise_close'), '驚訝'), (J('smile_1m'), '微笑 1 m')]),
 ('5 中性臉 vs 原 Hank：PASS 認得出（眼睛較細、虹膜較淺）', [(ref, '原始概念圖'), (os.path.join(origdir, 'neutral_close.png'), '原模型'), (J('neutral_close'), '新臉'), (J('neutral_1m'), '新臉 1 m')]),
 ('6 接縫：特寫 FAIL（額頭/太陽穴可見淡邊、下顎角少量鋸齒）／1 m PASS', [(J('seam_close_q'), '特寫 3/4'), (J('seam_close_side'), '特寫 側'), (J('neutral_1m_q'), '1 m 3/4'), (J('headturn_yaw'), '轉頭30°＋張嘴')]),
 ('7 全身：PASS（帽子、耳朵、脖子未損）', [(J('body_front'), '正面'), (J('body_side'), '側面'), (J('body_back'), '背面'), (J('headturn_tilt'), '歪頭＋眨眼')]),
]
v = json.load(open(verify)); fm = v['meshes']['HankFace']
real = sum(m['tris'] for n, m in v['meshes'].items() if not n.startswith('Icosphere'))
txt8 = ['8 GLB 重新匯入：PASS',
        f"形狀鍵 {fm['shape_keys']}（ARKit 名稱，例：{', '.join(fm['keys'][:3])} … {fm['keys'][-1]}）",
        f"骨頭 {v['bones']}（原 65 ＋ {', '.join(v['eye_bones'])}，父骨 mixamorig:Head）",
        f"三角形 {real}（Hank 本體 {[m['tris'] for n, m in v['meshes'].items() if n.startswith('tripo')][0]} ＋ 新臉 {fm['tris']}）",
        '（匯入時多出的 80 三角形 Icosphere 是 glTF 匯入器的骨頭顯示形狀，原檔也會出現，不是模型的一部分）',
        f"材質 {', '.join(fm['mats'])}；貼圖 3 張 2048 JPEG"]
H_head = 44; H_lab = 30
H = 70 + len(rows) * (H_head + C + H_lab) + 230
S = Image.new('RGB', (W, H), 'white'); d = ImageDraw.Draw(S)
d.text((16, 18), 'Hank 臉部 wrap 驗收總覽（Rocketbox Male_Adult_03 → Hank）2026-10-29', fill='black', font=font)
y = 70
for title, ims in rows:
    col = (200, 40, 40) if 'FAIL' in title else (20, 110, 40)
    d.rectangle((0, y, W, y + H_head - 4), fill=(240, 240, 240)); d.text((12, y + 8), title, fill=col, font=font); y += H_head
    for i, (p, lab) in enumerate(ims):
        im = Image.open(p).convert('RGB'); im.thumbnail((C, C)); S.paste(im, (i * C + (C - im.width) // 2, y + (C - im.height) // 2))
        d.text((i * C + 8, y + C + 3), lab, fill='black', font=small)
    y += C + H_lab
d.rectangle((0, y, W, y + H_head - 4), fill=(240, 240, 240)); d.text((12, y + 8), txt8[0], fill=(20, 110, 40), font=font); y += H_head + 6
for t in txt8[1:]: d.text((20, y), t, fill='black', font=small); y += 30
S.save(out, quality=88); print('SUMMARY', S.size)
