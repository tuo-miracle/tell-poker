# Round 3 前置檢查（2026-11-02）

四個新 Tripo 模型（~/Downloads，先各自複製到空目錄再搬進專案，未在下載目錄執行任何東西）。
總覽圖：`round3-preflight.jpg`（每列：Tripo 前／右／後／左 ＋ 臉部正面＋側面 ＋ 設定圖）。
每角色：`<name>-3d/<name>-source.glb`、`<name>-turnaround.png`、`<name>-face.png`、`inspect.log`、`decimate.log`、`<name>-12k.glb`。

| 角色 | 來源檔 | 對應設定圖 | 面數（三角形） | 骨架 | 貼圖 | 12k 版 |
|---|---|---|---|---|---|---|
| Marcus | asian man in turtleneck 3d model.glb | turnaround-p1-marcus.png ✔ | 47,406（+80 Icosphere） | Mixamo 54 骨 | 2048² basecolor | 11,998（臉部 2,381 保留） |
| Jace | black male human 3d model.glb | turnaround-p4-jace.png ✔ | 48,764（+80） | Mixamo 52 骨 | 2048² basecolor | 11,996（臉部 1,368 保留） |
| Erik | bearded man 3d model.glb | turnaround-p5-erik.png ✔ | 47,406（+80） | Mixamo 52 骨 | 2048² basecolor | 11,998（臉部 2,201 保留） |
| Reginald | old male figure 3d model.glb | turnaround-p3-reginald.png ✔ | 48,786（+80） | Mixamo 54 骨 | 2048² basecolor | 12,002（臉部 2,334 保留） |

- 骨頭名稱都是 `mixamorig:*`（Hips、Spine/Spine1/Spine2、Neck、Head、四肢與五指三節）。54 骨的兩個（Marcus、Reginald）多了
  `LeftToe_End`/`RightToe_End`；沒有眼骨（wrap 時加 `mixamorig:LeftEye/RightEye`）。沒有 shape key。
- 都是 1 m 高（GLB 正規化），Tripo 照例附一顆 2 m 的 Icosphere 輔助網格（各腳本會忽略／丟掉）。
- 焊接後非流形邊 40–76 條（與荷官同級，Tripo 常態）。
- 對應全部正確：Marcus 灰髮、細框眼鏡、深灰高領；Jace 黑人、短髮短鬍、黑夾克；Erik 紅鬍子、丸子頭、花襯衫、工裝褲；
  Reginald 灰鬍子、酒紅絲絨西裝、白襯衫。
- 降面：`wrap-pipeline/scripts/decimate.py source out 12000 face`（與荷官同一支，臉部不減面）。重跑荷官的降面與現有
  `dealer-12k.glb` 位元組相同。

## 需要特別處理的地方
- **Marcus 眼鏡**：Tripo 把鏡框和臉融成同一個網格（連通分量只有 1 個），下框與鼻樑直接黏在臉頰／鼻子上，無法乾淨切出來。
  做法：偵測鏡框頂點（往頭部中軸打射線、3 cm 內又撞到網格者＝薄殼）→ fit 時排除（不讓模板臉被拉到鏡框上）、
  bake 時把來自鏡框的貼圖像素標出來補色，最後依原鏡框輪廓重建一副乾淨的細金屬框（設定圖本來就是細金屬框）。
- **Erik／Reginald 鬍子**：鬍子是臉的幾何本身，換臉罩會蓋到下巴與嘴邊，排在最後做。
- **Jace**：臉部保留面數只有 1,368（其他人 2,200+），Tripo 原本臉就較簡化。

## 模板頭挑選（rank_templates.py，21 個 Rocketbox 成年男性）
排名檔：`<name>-3d/template-rank.txt`、`template-rank.jpg`（分數越低臉型越接近）。

| 角色 | 選用 | 形狀排名／分數 | 理由 |
|---|---|---|---|
| Marcus | Male_Adult_09 | 14 / 0.0507（第 1 名 M07 0.0317） | 唯一的東亞成年男性頭；眼角寬/臉寬 0.602 最接近 Marcus 0.597。warp 會吃掉整體臉型，眼皮形狀才是像不像的關鍵 |
| Jace | Male_Adult_12（預定） | 2 / 0.0273 | 黑人男性、臉型第 2 名（第 1 名 M07 為白人） |
| Reginald | Male_Adult_19（預定） | 1 / 0.057 | 臉型第 1 名，中年、有鬍、眉紋，族裔接近設定圖 |
| Erik | Male_Adult_16（預定） | 2 / 0.0618 | 白人、臉型第 2 名（第 1 名 M19 給 Reginald） |
