# 標準寫實人頭模型調查：給 Tripo 角色做 wrap 的底頭選擇

日期：2026-10-29
目的：找一顆可商用（Meta Quest Store 上架、參賽）、有獨立眼球與眼皮、有口腔／牙齒／舌頭、有 ARKit 52 或同等表情系統、面數適合 Quest 3 的標準頭，用來 wrap 到 Tripo AI 生成的角色臉型上。
工具鏈假設：Blender 5.2 腳本處理，Unity 6 + glTFast 匯入，跑在 Quest 3。

## 讀法說明

- 【文件】代表官方 repo、授權檔或官方文件明寫的內容。
- 【實測】代表我這次把檔案下載下來，用 Blender 5.2.1（headless）或 Python 實際開檔數出來、渲染出來的結果。實測資料在本機 scratchpad，不在 repo 裡。
- 【推測】代表我的判斷，不是文件寫的，也沒實測。
- 查不到就寫「查不到」，並列出查過的來源。
- 授權部分我只轉述授權檔文字，不是法律意見。

## 1. 結論摘要

| 候選 | 授權 | 眼球／眼皮／口腔 | ARKit | 頭部面數 | 取得 |
|---|---|---|---|---|---|
| Microsoft Rocketbox（`*_facial.fbx`） | MIT | 有／有（形狀鍵＋骨架）／有（牙、舌） | 52 個全齊，另有 15 visemes、48 FACS | 約 3.4k 三角形（含雙眼），全身 8.7k | 直接下載 |
| MakeHuman／MPFB2 | 程式碼 GPLv3，資產與輸出 CC0 | 有（proxy）／有（形狀鍵）／有（proxy） | 52 個全齊（faceunits01 資產包，CC0） | 頭頸約 8k 三角形，另加眼、牙、舌 proxy | 需裝外掛加資產包（約 270 MB） |
| USC ICT-FaceKit（Light） | MIT | 有／有（形狀鍵）／有（牙、齦、舌、口腔） | 53 個（ARKit 命名，少 tongueOut，兩組 L/R 拆分，另多 cheekRaiser） | 全部 52.2k 三角形；僅臉皮 18.5k | 直接下載（OBJ） |
| HeadBox（Rocketbox 配套） | 無授權檔（GitHub license 欄位為 null） | 不適用 | 不適用 | 不適用 | 不建議用 |
| MetaHuman | Epic 自訂條款，2025-06 起可用於 Unity | 有 | 有 | 偏高（未查） | 需註冊 Epic，官方條款頁需登入 |
| Character Creator（Reallusion） | 商業 EULA | 有 | 有（付費） | 未查 | 付費；有 mass distribution 與角色客製限制 |
| three.js `facecap.glb` | 查不到 | 有眼球與牙 | 52 個 | 頭 5.0k 三角形 | 直接下載但授權不明 |

推薦前 2 名：

1. Microsoft Rocketbox（用 `_facial.fbx`）。
2. MakeHuman／MPFB2（CC0 資產＋faceunits01）。

ICT-FaceKit 排第 3，是「想要最高保真度、且願意自己做減面」時的備案。理由見第 7 節。

信心等級：授權條文與面數、形狀鍵數量為高（都是讀原始授權檔、實際開檔數的）；推薦排序為中（取決於你要的 wrap 保真度與你願意花多少裝配功夫，這部分是我的判斷）。

## 2. Microsoft Rocketbox（含 HeadBox）

### 2.1 授權

- 授權：MIT License，Copyright (c) 2020 Microsoft。【文件】
  - 授權檔：https://github.com/microsoft/Microsoft-Rocketbox/blob/master/LICENSE.md
  - README 寫「The library of avatars is now released under MIT License」，更新紀錄寫「12/2020: Updated license to MIT.」：https://github.com/microsoft/Microsoft-Rocketbox/blob/master/README.md
  - GitHub API 的 license 欄位也回 `mit`。【實測】
- 能不能商用：MIT 條文允許 use、copy、modify、merge、publish、distribute、sublicense、sell。【文件】
- 要不要標示：條件是「copies or substantial portions」要附上版權聲明與授權聲明。【文件】把它放進 app 的第三方授權頁／credits 是最保守的做法。這一句是我的【推測】，MIT 對「編譯進遊戲的資料檔」是否算 copy 沒有官方解釋。
- 能不能改作後再散布：可以（modify、merge、distribute 都在條文內）。
- 要小心的一點：README 內連到的原始發表文章網址 slug 寫的是「available-for-research-and-academic-use」，那是 2020 年初的條款；後來改為 MIT。以 repo 內的 LICENSE.md 為準。【文件，但是兩處說法有時間差】
- 另有 issue 問「Assets/Animations 是否也屬 MIT」，沒有 Microsoft 人員回覆：https://github.com/microsoft/Microsoft-Rocketbox/issues/24 。我們不用動畫，不受影響。
- repo 已封存（archived，最後推送 2022-10-02），不會再有更新或答覆。【實測，GitHub API】

### 2.2 內容

【實測】數據來自我下載的 `Female_Adult_01_facial.fbx`（2.2 MB）與 `Male_Adult_01_facial.fbx`（2.0 MB），用 Blender 5.2.1 匯入。

- 檔案路徑：`Assets/Avatars/Adults/Female_Adult_01/Export/Female_Adult_01_facial.fbx`
- 直接下載網址（不需 LFS，不需註冊，檔案是真的 FBX 不是指標檔）：
  https://raw.githubusercontent.com/microsoft/Microsoft-Rocketbox/master/Assets/Avatars/Adults/Female_Adult_01/Export/Female_Adult_01_facial.fbx
- repo 內共 117 個 `_facial.fbx`（728 個 fbx 中）。【實測，git tree】
- 一個 SkinnedMesh：`f001_hipoly_81_bones_opacity`，4,734 頂點，8,732 三角形（全身含頭髮卡片）。材質三個：`f001_body`（4,630 tris）、`f001_head`（3,434 tris）、`f001_opacity`（668 tris，髮片）。
- 頭部：`f001_head` 材質共 3,434 tris，其中雙眼各 126 tris（獨立連通區塊，73 頂點，骨架權重 100% 在 `Bip01 REye`／`LEye`）。所以頭皮膚、頸、口腔約 3.2k tris，加雙眼約 3.4k。
- UV：1 層（`UVChannel_1`）。
- 骨架：80 根骨，臉部骨包括 `Head`、`REye`、`LEye`、`MJaw`、`MTongue`、`REyeBlinkTop/Bottom`、`LEyeBlinkTop/Bottom`、`MUpperLip`、`MBottomLip`、嘴角、臉頰、眉毛（內／中／外）。
- 形狀鍵共 176 個：
  - `AK_01`～`AK_52`：52 個，全齊。逐一對照 Apple ARKit 清單，含 `AK_25_JawOpen`、`AK_09_EyeBlinkLeft`、`AK_10_EyeBlinkRight`、`AK_27_MouthClose`、`AK_52_TongueOut`。
  - `AA_VI_00`～`14`：15 個 visemes（Oculus Lipsync 相容）。
  - `AU_*`：48 個 FACS。
  - `HB_*`：17 個（HeadBox 產生），`SR_*`：42 個（對應 SRanipal／Vive 臉部追蹤）。
- 形狀鍵確實有動：JawOpen 動到 786 個頂點（最大位移 1.72，FBX 單位），EyeBlinkLeft 631 個，MouthClose 811 個，TongueOut 590 個。
- 眼皮：沒有獨立眼皮網格，眼皮是頭皮膚的一部分，用 `EyeBlink` 形狀鍵／眼瞼骨閉合。我把 EyeBlinkLeft＋Right 設為 1 渲染，雙眼確實閉上。【實測，截圖在 scratchpad `r/img/rb_f01_blink.png`】
- 口腔：張嘴（JawOpen=1）渲染看得到上下排牙與舌頭。牙齒只是薄薄一排，舌頭是低面數。【實測，`r/img/rb_f01_jawopen.png`、`rbclose_jawtongue.png`；「薄薄一排」是我看圖的描述】
- 眼球：低面數球體，虹膜靠貼圖，沒有虹膜幾何。【實測 126 tris／眼；虹膜靠貼圖是推測】
- 貼圖：tga，每張約 12.6 MB（推算 2048×2048 RGB），有 color／normal／specular／normal_wrinkle。【實測檔案大小；解析度是由大小反推，【推測】】

### 2.3 HeadBox

- 是什麼：Maya Python 腳本，用臉部骨骼在 Maya 裡產生 blendshape，再轉移到別的 Rocketbox 角色；另附 Unity demo。【文件】https://github.com/openVRlab/Headbox
- 授權：repo 沒有 LICENSE 檔，GitHub API license 欄位為 null。README 說 Unity demo 依賴 OpenFace（「ACADEMIC OR NON-PROFIT ORGANIZATION NONCOMMERCIAL RESEARCH USE ONLY」）與 Oculus Lipsync，「can't provide any particular license」。【文件】
- 必要性：不需要。因為 Rocketbox 的 `_facial.fbx` 已經內含 HeadBox 產生的 blendshape（README：「These blendshapes have been released with the original library」）。我們只要拿 `_facial.fbx`。
- 結論：不要用 HeadBox repo 的任何東西。

### 2.4 wrap 適合度

- 優點：拓撲為遊戲用低面數，眼、口、牙、舌、骨架、52 形狀鍵都已綁好並實測可動；面數遠低於預算；MIT 條款簡單。
- 缺點（均為【推測】）：
  - 頭只有約 3.2k 三角形，眼周與嘴唇的網格密度低，wrap 時 Tripo 臉的細部形狀（鼻翼、唇形）會被平均掉，細節只能靠貼圖與法線貼圖補。
  - 是全身單一網格，要用 `Bip01 Head` 等頂點群組把頭切出來。
  - 風格偏寫實但低模，渲染圖看起來有明顯稜角。
- 已知 wrap／retopo 案例：搜尋「Rocketbox wrap／retopology／photo avatar」沒找到官方或社群的現成案例（查過 Frontiers 論文頁、GitHub 搜尋、一般網搜）。查不到。
- Blender 匯入：repo issue #2「Importing in Blender」顯示作者「沒有 Blender 的官方指引」。我自己用 Blender 5.2.1 直接匯入 FBX 成功，只出現 `User property type 'Short' is not supported` 警告。【實測】

## 3. MakeHuman／MPFB2

### 3.1 授權

- MPFB2 授權說明：https://github.com/makehumancommunity/mpfb2/blob/master/LICENSE.md 。【文件】
  - 程式碼：GPLv3（`LICENSE.CODE.md`）。
  - 資產（base mesh、proxy、targets、貼圖、衣服、rig、pose、expression）：CC0 1.0（`LICENSE.ASSETS.md`）。
  - 輸出（FBX、OBJ、截圖等）：「the MakeHuman team makes no claim whatsoever over output」，視為你的資料。
- 解讀：我們不散布 MPFB 的程式碼，只用它產出的網格，所以 GPL 不影響遊戲；CC0 資產不需標示。這句解讀是【推測】；「輸出不含程式邏輯」是 MakeHuman 團隊的「opinion」，不是法律保證。
- faceunits01 資產包：資產包索引頁寫「Functional packs: The assets are shared under CC0」。【文件】https://static.makehumancommunity.org/assets/assetpacks/index.html 。但 faceunits01 子頁面本身沒有授權欄位：https://static.makehumancommunity.org/assets/assetpacks/faceunits01.html 。
- 系統資產包（眼、牙、舌）：索引頁寫「System assets and materials for system assets, shared under CC0」。我下載後在 `eyes/low-poly/low-poly.mhclo` 檔頭看到「This asset was explicitly released as CC0 in september 2020」，版權人為 Data Collection AB、Joel Palmius、Jonas Hauquier。【實測】
- 注意：另有一區 CC-BY 的材質包（System eye materials 03 等），不要裝那一區就沒標示義務。【文件，索引頁】
- 商標：MakeHuman™ 是商標，CC0 不放棄商標權（CC0 第 4a 條）。不要把「MakeHuman」當成遊戲的宣傳名義。【文件，CC0 條文】

### 3.2 內容

【文件】MPFB2 與 MakeHuman 共用同一個 base mesh（hm08）：https://static.makehumancommunity.org/mpfb/faq/differences_between_mpfb2_and_makehuman.html

【實測】我下載 `base.obj`（MPFB2 repo `src/mpfb/data/3dobjs/base.obj`）與兩個資產包，用 Python／Blender 數：

- base mesh：19,158 頂點、18,486 個四邊形（含 helper 與 joint 群組），其中 `body` 群組 13,378 個四邊形，全身單一網格。UV 座標 21,334 個。全是四邊形，沒有三角形。
- 頭頸部：以頸部關節以上的四邊形數估算約 4,280 個四邊形，約 8.5k 三角形。這個數字是我用切高度的方式估的，【推測】，誤差在數百內。
- 眼：`eyes/low-poly` 96 頂點、172 tris；`eyes/high-poly` 1,064 頂點、2,040 tris。眼睛是獨立 proxy，base mesh 的眼窩是空的（我渲染 base mesh 看到眼窩是洞）。【實測】
- 牙：`teeth_base` 3,868 頂點、7,120 tris（共 6 種牙型，面數相同）。偏重，需要減面或自己換。【實測】
- 舌：`tongue01` 226 頂點、448 tris。【實測】
- 睫毛、眉毛 proxy 另有，眉毛 192 tris。【實測】
- 骨架：標準 rig 有 default、default_no_toes、game_engine、game_engine_with_breast、mixamo、mixamo_unity、cmu_mb、openpose、rigify。【實測，repo tree】。這些 rig 的臉部骨細節我沒有逐一查，查不到（沒開檔確認 `game_engine` rig 含不含眼瞼、下巴、舌骨）。base mesh 的 joint 群組含 `joint-jaw`、`joint-l-upperlid`、`joint-l-lowerlid`、`joint-tongue-1..4`、`joint-l-eye`，所以預設 rig 有這些關節。
- ARKit：faceunits01 資產包（0.2 MB zip，52 個 `.target`），作者 Mika Suominen。【文件／實測】
  - 下載：https://files.makehumancommunity.org/functional/faceunits01.zip （備援 files2）。我實際下載，zip 內 52 個 target 檔加 1 個 json。
  - 名稱逐一對照 ARKit 52：全齊，含 `jawOpen`、`eyeBlinkLeft`、`eyeBlinkRight`、`mouthClose`、`tongueOut`，沒有 `_L/_R` 拆分。
  - 我把 `jawOpen`（2,543 個頂點位移）與 `eyeBlinkLeft/Right`（783 個）套到 base mesh 渲染：下巴張開、眼皮閉合，都有效。【實測，`r/img/mh_jawopen.png`、`mh_blink.png`】張嘴時 base mesh 本身沒有牙與舌，要靠 proxy。
  - 品質提醒：MPFB 2.0.16 發行說明寫「some face shapes may still need polish」。【文件】https://static.makehumancommunity.org/mpfb/releases/release_2016.html
  - target 格式：每行 `頂點索引 dx dy dz`，MakeHuman 座標是 Y 向上，單位是公寸（我實測轉換用 ×0.1，Y→Z）。要在 Blender 腳本裡自己載入的話，不必裝 MPFB。【實測】
- MPFB 的 `faceunits_arkit` 匯出選項說明是「Load arkit-style faceunits. If baking shapekeys, this will be done after the bake」。【文件，`exportops/properties/faceunits_arkit.json`】

### 3.3 取得

- MPFB2 從 Blender extension 平台安裝，需要 Blender 4.2 以上。【文件】https://static.makehumancommunity.org/mpfb/docs/getting_started.html 。我沒有在 Blender 5.2.1 裝過 MPFB，所以 5.2 相容性查不到（repo 最後推送是 2026-10-24，是活躍專案，【實測，GitHub API】，但這不代表 5.2 沒問題）。
- 系統資產包 267 MB：https://files.makehumancommunity.org/asset_packs/makehuman_system_assets/makehuman_system_assets_cc0.zip 。
- 難度：中。要裝外掛、裝兩個資產包，並且自己裝配 proxy（眼、牙、舌）。如果不裝外掛，也可以像我這次，用腳本直接讀 `base.obj`＋`.target`，proxy 的貼合要自己處理 `.mhclo`（用重心座標綁在 base mesh 上）。

### 3.4 wrap 適合度（均為【推測】）

- 拓撲：全四邊形，邊流為動畫設計，眼、嘴周圍有環狀邊流，適合形變。
- 頭頸約 8k 三角形比 Rocketbox 密一倍多，wrap 後能保留較多臉型細節。
- 已知 wrap／retopo 案例：沒有查到專門「把 MakeHuman 頭 wrap 到 AI 生成臉」的案例。查不到。
- 風險：牙需減面；ARKit 形狀品質是社群作品；眼睛、牙、舌是分開的 proxy，要自己維持它們跟臉的相對位置，形狀鍵由 `interpolate_targets` 轉到 proxy（MPFB 內建），自己寫腳本就得自己處理。

## 4. USC ICT-FaceKit

### 4.1 授權

- MIT License，Copyright (c) 2020 USC Institute for Creative Technologies。【文件】
  - 授權檔：https://github.com/USC-ICT/ICT-FaceKit/blob/master/LICENSE
  - README 寫「ICT-FaceKit is released under the MIT license.」
- 不確定的地方：
  - issue #20「About model distribution」問「模型能否商用，還是 MIT 只涵蓋 Python 腳本」，沒有任何維護者回覆：https://github.com/USC-ICT/ICT-FaceKit/issues/20 。授權檔本身是 repo 層級的 MIT，沒有區分程式碼與資料，所以字面上涵蓋整個 repo，但作者沒有就資料（由光場掃描真人所建的 PCA）另外說明。這一點我會標為「字面上可商用，作者沒有確認過」。
  - Full model（200+ 身分模式、反照率模式、FBX face rig）「will be released under a different USC specific license」，要寄信給 kallebladin@gmail.com 或 haase@ict.usc.edu 詢問。我們不用 Full。issue #23 也在問怎麼取得 Full，不確定有沒有答案。【文件】
- 標示：MIT 要附授權聲明（同 Rocketbox 的【推測】）。CREDITS.md 列出貢獻者：https://github.com/USC-ICT/ICT-FaceKit/blob/master/CREDITS.md

### 4.2 內容

來源：README https://github.com/USC-ICT/ICT-FaceKit ；我另外下載 `FaceXModel/generic_neutral_mesh.obj`、`jawOpen.obj`、`eyeBlink_L.obj` 實測。

- 檔案格式：OBJ（每個表情一個完整網格的 OBJ，不是差量）；Light 版沒有 FBX rig、沒有骨架。【文件】Blender 資料夾只有 Python 腳本。【實測，repo tree】
- 網格：26,719 頂點、26,384 個多邊形（25,836 四邊形＋548 三角形）、52,220 三角形。UV 座標 28,911 個。【實測】
- 各部分（README 的表格，和我數的三角形數）【文件＋實測】：

| 部件 | 多邊形 | 三角形 |
|---|---|---|
| 臉（Face） | 9,230 | 18,460 |
| 頭頸（Head and Neck） | 1,914 | 3,828 |
| 口腔（Mouth socket） | 2,082 | 4,164 |
| 眼窩（左右） | 808 | 1,616 |
| 牙齦與舌（Gums and tongue） | 2,972 | 5,808 |
| 牙（32 顆） | 4,490 | 8,696 |
| 眼球（左右，各分鞏膜與虹膜） | 3,196 | 6,264 |
| 淚液、眼混合、眼遮擋 | 612 | 1,224 |
| 睫毛條 | 1,080 | 2,160 |

- 眼皮：沒有獨立眼皮網格，眼皮是臉皮膚的一部分，用 eyeBlink 形狀閉合。我渲染 `eyeBlink_L.obj`，單眼閉合有效。【實測，`r/img/ict_blinkL.png`】
- 口腔：口腔、牙、牙齦、舌都有；渲染 `jawOpen.obj` 可見舌頭與口腔內部。【實測，`r/img/ict_jawopen.png`】
- 表情：README 寫 53 個，命名依 ARKit 並用 `_L`／`_R`，且把 `browInnerUp` 與 `cheekPuff` 各拆成左右。【文件】
  - 我數資料夾：55 個表情 OBJ＋2 個 `PupilDilate`。多出的 2 個是 `cheekRaiser_L/R`，非 ARKit 名稱。
  - 含 `jawOpen`、`eyeBlink_L/R`、`mouthClose`。缺 `tongueOut`。【實測】
  - 另一個第三方專案也記錄「合併到 51 個精確 ARKit 名（缺 tongueOut）」：https://github.com/mrchipset/aniface-demo （0 星，個人專案，僅供參考，可信度低）。
- 已知問題：
  - issue #27／#22：`eyeLook*` 形狀鍵沒有移動眼球頂點，只動眼皮與周圍；眼球轉動要自己用旋轉驅動。https://github.com/USC-ICT/ICT-FaceKit/issues/27
  - issue #5：`mouthClose` 是 `jawOpen` 的修正形狀，單獨用會看起來怪（維護者 kbladin 回覆）。https://github.com/USC-ICT/ICT-FaceKit/issues/5
  - 另有 100 個身分 PCA 模式（`identity000`～`099.obj`），我們用不到。

### 4.3 取得

- 直接下載，不需註冊，不需編譯：repo 的 `FaceXModel/` 資料夾，每個檔約 2.5 MB，整個 repo 約 228 MB（含 100 個身分 OBJ）。【實測，GitHub API size 227,693 KB】
- 只要 `generic_neutral_mesh.obj`＋53 個表情 OBJ＋`vertex_indices.json` 就夠用。

### 4.4 wrap 適合度

- 優點：它本來就是為「把掃描註冊到共同拓撲」設計的；參考專案 `FLAME_TO_HACK`（把 FLAME 轉到 ICT 拓撲）說明這個拓撲被研究圈當標準：https://github.com/aoru45/FLAME_TO_HACK 。【文件，專案描述】臉部區域頂點密度高（臉 9.2k 四邊形），形狀細節捕捉最好。有可用的第三方 Blender 流程參考：https://github.com/mrchipset/aniface-demo （MIT，包含 ICT→Blender 匯入、寫入形狀鍵、瘦身匯出的流程與踩坑筆記；僅供參考）。
- 缺點：
  - 面數遠超預算：臉皮 18.5k tris、全部 52.2k tris。要降到 10k 以內，需要把臉皮減到約 1/2，牙、牙齦、眼球也要大幅減，而且減面時 53 個形狀鍵必須同步保持。【推測：做法是在中立網格上減面，再用最近點／資料傳輸把每個形狀鍵的偏移量轉到減面後的網格，這會損失一些精度】
  - 上述第三方專案記錄，剔除眼球、牙、牙齦、睫毛後仍有 14,062 頂點。
  - 沒有骨架。要自己做下巴與眼球骨。
  - 資料授權沒有被作者確認（見 4.1）。

## 5. 其他候選

### 5.1 Blender Studio Human Base Meshes

- 授權：CC0，49 MB，Blender 4.2 以上。https://www.blender.org/download/demo-files/ 【文件】
- 內容：官方頁面沒說明是否含眼、牙、舌或形狀鍵，我沒有下載來看，查不到。這個資產包的性質是雕刻用基底網格（我記得沒有表情系統，但這是【推測】，未驗證）。
- 結論：不符合 (c)，不列入。

### 5.2 three.js `facecap.glb`

- 內容：glTF，頭 5,032 tris、2,694 頂點，含眼球（各 960 tris）與牙（1,090 tris），52 個 morph target，名稱為 ARKit（`_L`／`_R`），含 `jawOpen`、`eyeBlink_L/R`、`mouthClose`、`tongueOut`。【實測，直接解析 glb JSON】
- 檔案：https://github.com/mrdoob/three.js/blob/dev/examples/models/gltf/facecap.glb 。
- 授權：查不到。three.js 範例頁只寫「model by Face Cap」連到 https://www.bannaflak.com/face-cap ，該頁只提供 iOS App，沒有模型授權聲明。我也查過 three.js repo 內的 readme。three.js 本身是 MIT，但範例資產不一定同授權。
- 結論：授權不明，不能用。（風格也偏卡通低模。）

### 5.3 Eisko 免費頭

- 網路新聞說是 CC-BY 且非商用，綁定的 rig 版有 154 個 blendshape，不是 ARKit 52。這是二手資訊（CG Channel 報導，我沒有讀到授權原文）。https://www.cgchannel.com/2017/11/download-eiskos-free-hi-res-model-of-a-human-head/ 。如果屬實，不能商用，不列入。

### 5.4 Ready Player Me

- 官方文件有 ARKit morph target：https://docs.readyplayer.me/ready-player-me/api-reference/avatars/morph-targets
- 二手報導：Netflix 於 2025-12 收購，公開服務 2026-01-31 下線。https://genies.com/blog/ready-player-me-shutdown 。
- 授權條款原文我沒有讀到，查不到。服務已下線，不納入。

### 5.5 MetaHuman（只說明授權）

- 官方條款頁 https://www.metahuman.com/en-US/license 需要 Epic 登入，我無法直接讀。
- 二手報導（CG Channel，2025-06-04）：2025 年起 MetaHuman 可用於 Unity、Godot 等其他引擎，不收版稅；年營收低於 100 萬美元免費；超過需買座席授權。https://www.cgchannel.com/2025/06/you-can-now-sell-metahumans-or-use-them-in-unity-or-godot/ 另外 Digital Production 也有報導，並提到禁止拿來當 AI 訓練資料：https://digitalproduction.com/2025/06/05/metahumans-graduate-ready-for-unity-godot-and-the-fab-cash-register/
- 沒有任何來源提到 Quest 平台；我沒有找到官方 FAQ 的原文（網搜結果摘要有引用「Non-Engine Products」字樣，但未能在官方頁面確認）。
- 結論：理論上可用，但來源是二手，條款文字沒親自讀到；且 MetaHuman 網格面數高，Quest 需另外減面（【推測】）。

### 5.6 Reallusion Character Creator（只說明授權）

- 原始 EULA 頁面我讀到 404，沒有讀到原文。論壇上 Reallusion 工作人員的回覆說：Export License 可將內容轉成 FBX 等格式，嵌入個人、商業、工業或教育專案的遊戲與應用程式。https://forum.reallusion.com/PrintTopic335621.aspx
- 同一批論壇文章說大量發行的遊戲需要事先取得 Reallusion 的 mass distribution 許可；管理員說不允許把角色、衣物當成遊戲內角色客製系統的素材。https://forum.reallusion.com/506573/EULA-Question-InGame-Character-Creation-Mass-Distribution-License 。這些貼文日期約 2015～2023，現行條款可能不同。
- 沒有任何來源提到 Quest。
- 結論：付費，條款有 mass distribution 與角色客製限制，且資訊是論壇貼文。優先序在 ICT、Rocketbox、MakeHuman 之後。

### 5.7 Daz3D、Sketchfab

- Daz3D 與 Sketchfab 我沒有實際查證（只用了網搜，網搜結果沒有給出有 ARKit blendshape 的 CC0／CC-BY 寫實頭）。Sketchfab 網站是動態頁面，我沒有進去逐一翻。這一項是「沒做到」。

## 6. 取捨比較（針對「wrap 到 Tripo 臉型、跑 Quest 3」）

| 項目 | Rocketbox | MakeHuman／MPFB2 | ICT-FaceKit |
|---|---|---|---|
| 授權清爽度 | MIT，條款明確 | CC0（資產），最寬鬆 | MIT，資料授權作者沒確認 |
| 拿到就能用 | 是（FBX 已含眼、牙、舌、52 形狀鍵、骨架） | 否（要裝外掛＋資產包＋裝配 proxy） | 否（OBJ 差量，要自己建形狀鍵、骨架） |
| 頭部面數 | 約 3.4k（含眼） | 頭頸約 8.5k＋proxy | 臉皮 18.5k，全部 52.2k |
| 需不需要減面 | 不需要 | 牙需要（7.1k tris） | 一定要，且要保持 53 形狀鍵 |
| wrap 保真度（推測） | 低（密度低） | 中 | 高 |
| 眼球轉動 | 有眼骨 | 有眼關節（base rig） | 沒有，要自己加 |
| 風險 | 密度不夠，wrap 後臉部細節流失 | 外掛與 Blender 5.2 相容性未驗證，ARKit 形狀自述「may need polish」 | 減面工程大；授權文字面上 OK 但沒人回答 issue #20 |

## 7. 推薦排序與理由

### 第 1 名：Microsoft Rocketbox（`*_facial.fbx`）

- 需求 (a)(b)(c)(d) 一次齊全，而且我已經在 Blender 5.2.1 實測：眼球獨立、眼皮可閉、有牙與舌、ARKit 52 全齊且位移有效、頭部約 3.4k tris。
- 授權為 MIT，repo 與 README 一致，下載是直接 raw 連結，沒有註冊或編譯。
- 它是目前候選中唯一能「不加工就符合 Quest 預算」的。Quest 3 的面數與 draw call 壓力小，可以把省下來的預算放在貼圖與法線貼圖上。
- 風險：密度低，wrap 後 Tripo 臉的細節要靠貼圖補（【推測】）。建議先用 Rocketbox 做出可動的 wrap 原型，看效果夠不夠再決定要不要換。

### 第 2 名：MakeHuman／MPFB2（CC0 資產＋faceunits01）

- 授權最寬鬆（CC0），不需要標示，不會有 repo 授權文字模糊的問題。
- 頭頸約 8.5k tris 的全四邊形網格，密度比 Rocketbox 高、比 ICT 低很多，在預算邊緣。我實測 faceunits01 的 52 個 ARKit 形狀鍵可直接套到 base mesh 且有效。
- 代價：要自己裝配（外掛、兩個資產包、眼牙舌 proxy），牙齒 7.1k tris 太重要減面或換掉，Blender 5.2 相容性未驗證。

### 第 3 名：ICT-FaceKit

- 如果 Rocketbox 的 wrap 保真度不夠、MakeHuman 的臉型又不好用，ICT 是備案：拓撲密度與臉部細節最好。
- 但面數預算超出 5 倍，減面時要保持 53 個形狀鍵，沒有骨架，眼球轉動形狀鍵無效，而且資料授權沒有被作者確認。除非有明確需要，不建議當第一選擇。

### 下一步建議（【推測】）

1. 先把 Rocketbox `Female_Adult_01_facial.fbx` 或 `Male_Adult_01_facial.fbx` 的頭切出來（用 `Bip01 Head`、`Bip01 Neck` 等頂點群組），保留眼球與全部 52 個 AK 形狀鍵，其餘 124 個形狀鍵刪除以減少 glTF 體積。
2. 用 Blender 的 Surface Deform 或 Shrinkwrap＋手動標記點把頭 wrap 到 Tripo 臉，再把 Tripo 貼圖投影到 Rocketbox UV。
3. 匯出 glb，在 Unity 6 + glTFast 確認 morph target 名稱與數量保留。這一步我沒有做，glTFast 對 52 個以上 morph target 的處理屬「沒做到」。

## 8. 沒做到的事

- 沒有實際做 wrap，沒有驗證任何候選 wrap 到 Tripo 臉型的結果。保真度的排序是推測。
- 沒有在 Unity 6 + glTFast 匯入測試任何候選，沒有在 Quest 3 上量效能。
- 沒有在 Blender 5.2.1 裝 MPFB2，相容性查不到；沒有確認 `game_engine` 等 rig 是否含臉部骨；沒有實際裝配 MakeHuman 的眼、牙、舌 proxy（我只用了 base mesh 加 faceunits01 目標做形變渲染）。
- Rocketbox 只實測了 `Female_Adult_01_facial.fbx` 與 `Male_Adult_01_facial.fbx` 兩個；其餘 115 個沒逐一檢查，不同角色的面數與形狀鍵可能有差異。
- Rocketbox 臉部貼圖是否來自真人、有無肖像權疑慮，查不到（沒有文件說明）。
- ICT 的掃描資料取得時是否有受試者同意，README 與 repo 內查不到。
- MetaHuman 官方條款頁需要登入，沒有讀到原文；Reallusion EULA 官方頁讀到 404，只讀到論壇貼文；Ready Player Me 的條款原文沒讀到。
- Daz3D 與 Sketchfab 沒有實際查證；網搜沒有找到符合所有條件的 CC0／CC-BY 寫實頭，但這不等於不存在。
- 授權部分只是轉述條文與我的解讀，沒有請律師確認；MIT 對「編譯進遊戲的資料檔」是否要附標示，是我推測要保守處理。
- Meta Quest Store 是否對上架內容有額外第三方素材要求，沒有查。

## 9. 來源清單

官方 repo／授權檔：

- https://github.com/USC-ICT/ICT-FaceKit
- https://github.com/USC-ICT/ICT-FaceKit/blob/master/LICENSE
- https://github.com/USC-ICT/ICT-FaceKit/issues/20
- https://github.com/USC-ICT/ICT-FaceKit/issues/27
- https://github.com/USC-ICT/ICT-FaceKit/issues/5
- https://github.com/microsoft/Microsoft-Rocketbox
- https://github.com/microsoft/Microsoft-Rocketbox/blob/master/LICENSE.md
- https://github.com/microsoft/Microsoft-Rocketbox/issues/24
- https://github.com/openVRlab/Headbox
- https://github.com/makehumancommunity/mpfb2
- https://github.com/makehumancommunity/mpfb2/blob/master/LICENSE.md
- https://github.com/makehumancommunity/mpfb2/blob/master/LICENSE.ASSETS.md

官方文件：

- https://static.makehumancommunity.org/mpfb/docs/getting_started.html
- https://static.makehumancommunity.org/mpfb/releases/release_2016.html
- https://static.makehumancommunity.org/mpfb/faq/differences_between_mpfb2_and_makehuman.html
- https://static.makehumancommunity.org/assets/assetpacks/index.html
- https://static.makehumancommunity.org/assets/assetpacks/faceunits01.html
- https://static.makehumancommunity.org/assets/assetpacks/makehuman_system_assets.html
- https://www.blender.org/download/demo-files/
- https://docs.readyplayer.me/ready-player-me/api-reference/avatars/morph-targets

論文：

- Rocketbox 原論文（README 引用）：https://www.frontiersin.org/articles/10.3389/frvir.2020.561558/abstract （只引用，沒有逐字讀）

社群／二手：

- https://github.com/mrchipset/aniface-demo
- https://github.com/aoru45/FLAME_TO_HACK
- https://github.com/mrdoob/three.js/blob/dev/examples/webgl_morphtargets_webcam.html
- https://www.bannaflak.com/face-cap
- https://www.cgchannel.com/2025/06/you-can-now-sell-metahumans-or-use-them-in-unity-or-godot/
- https://digitalproduction.com/2025/06/05/metahumans-graduate-ready-for-unity-godot-and-the-fab-cash-register/
- https://www.metahuman.com/en-US/license （需登入，未讀到）
- https://forum.reallusion.com/PrintTopic335621.aspx
- https://forum.reallusion.com/506573/EULA-Question-InGame-Character-Creation-Mass-Distribution-License
- https://genies.com/blog/ready-player-me-shutdown
- https://www.cgchannel.com/2017/11/download-eiskos-free-hi-res-model-of-a-human-head/

實測檔案（本機，不入 repo）：

- 工作目錄：`/private/tmp/claude-501/-Users-laichanean-WebstormProject-claw-hand/dba936d2-a2e1-4020-9344-fc7606a601ab/scratchpad/`
- 腳本：`s/inspect_fbx.py`、`s/rb_detail.py`、`s/rb_render.py`、`s/mh_render.py`、`s/ict_render.py`
- 渲染圖：`r/img/`（`rb_f01_*`、`rbclose_*`、`mh_*`、`ict_*`）
