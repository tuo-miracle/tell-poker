# Quest 頭盔錄影：穩定、變速與有成品佐證的製作方法

研究日期：2026-11-05。標記：【文件】＝官方或原始文件；【實測】＝我在本機實跑；【推測】＝我的推論，沒有直接證據。
所有實驗檔在 `/private/tmp/claude-501/-Users-laichanean-WebstormProject-claw-hand/dba936d2-a2e1-4020-9344-fc7606a601ab/scratchpad/trailer-research/stab-lab/`（腳本與量測輸出都在裡面；大型暫存影片已刪）。

---

## 0. 先講最重要的發現：這段素材的「晃」主要不是抖動，而是兩件別的事

測試素材 `hevc.mp4`（6 秒、1920×1080、HEVC）。實際量到：

1. **幀間隔是 36 fps 與 24 fps 交錯的**【實測】：180 個間隔中 108 個是 27.8 ms、71 個是 41.7 ms（ffprobe `frame=pts_time`）。若直接 `fps=30` 轉定幀，178 個輸出幀中有 53 幀是重複幀、另有 53 次跳過來源幀；顯示幀比理想時間落後平均 15.1 ms、最大 41.7 ms。這種不均勻節奏在橫向轉頭時會是頓挫（judder），人眼會讀成「晃、卡」。
2. **真正的高頻抖動很小，大部分位移是故意的轉頭**【實測】：以光流（LK + RANSAC）估全域位移，轉頭時每幀可達 51–113 px（1920 寬度）；扣掉平滑路徑後的殘差只有 1–4 px（靜止段 0.9–1.1 px，快轉頭段 9–11 px）。所以「把 vidstab 的 smoothing 開大」會把刻意的轉頭當成抖動去追，代價是大幅放大（見 §2）。
3. 時間戳是真的【實測】：以 LK 位移幅度對幀間隔做相關，對齊後 r = 0.89（n=89 個移動幀；未對齊的 lag 0 為 -0.60，因為 cv2 讀到 181 幀、ffprobe 列出 180 個 pts，差一幀，我用 lag +1 對齊）。意思是 VFR 的時間戳可信，內容確實是依時間移動，不能忽略時間戳硬當定幀播。

---

## A1. Meta 官方與 VR 預告的做法

| 項目 | 內容 | 來源 | 標記 |
|---|---|---|---|
| 頭部運動 | 「be sure to use slow head movements」；「as the camera operator, the best practices for POV capture still apply (such as moving slowly)」 | https://developers.meta.com/horizon/resources/video-capture-mr-vr/ | 文件 |
| 頭盔內建防震 | 建議 `Image stabilization: Low`；原文「if your footage is too shaky, the software can crop out a lot of the image area」 | 同上 | 文件 |
| 防震的代價 | 「higher image stabilization settings reducing shaking inherent to head-based video recording … at the cost of reduced field of view」 | https://uploadvr.com/quest-stabilization-rate-options/ | 文件（媒體報導） |
| 後製穩定 | 「Most modern video editing software can help stabilize your recording with one button.」，沒有給參數 | Meta 同頁 | 文件 |
| 錄影幀率 | 頭盔錄影 36 fps（variable）；MQDH 最高 2160p、60 fps（variable）、40 Mbps；PC VR 建議 1080p 60 fps Fixed、≥20 Mbps。與我量到的 36/24 fps 交錯一致 | Meta 同頁 | 文件＋實測 |
| 構圖 | 「frame shots according to the eye you selected」；物件要刻意放在使用者前方 | Meta 同頁 | 文件 |
| 商店預告片規格 | 30 秒到 2 分鐘；16:9；不得有 letterbox / pillarbox（2025-02-12 修訂） | https://developers.meta.com/horizon/resources/vrc-quest-asset-7/ | 文件 |
| 單人體驗的預告 | 「if you have a single player experience, you are probably limited to first person footage.」 | Meta 同頁 | 文件 |
| 裁切比例 | Meta 只說 16:9 / 9:16 的目標比例，**沒有**給「後製穩定該裁多少」的數字 | Meta 同頁 | 文件（缺口） |
| 慢動作／變速慣例 | **我在 Meta 官方頁面找不到任何變速或慢動作的建議**；Meta 部落格那篇 capture playbook 的細節在需下載的 PDF 內，我沒拿到 | https://developers.meta.com/horizon/blog/best-practices-vr-mr-app-captures-meta-quest-developers/ | 文件（缺口） |

反面證據：搜尋摘要裡有第三方文章建議「穩定若讓 MR 畫面看起來漂浮或不貼地就整個關掉」，但我只看到搜尋摘要、沒有讀到原文，只能當二手線索【推測】。關於「頭部鎖定 HUD 如何處理」，我在 Meta 官方頁面找不到任何指引。

## A2. 穩定工具比較

### 本機環境【實測】
`ffmpeg -filters` 有 `vidstabdetect`、`vidstabtransform`、`deshake`、`minterpolate`、`libvmaf`（ffmpeg 7.1）。不需要安裝任何東西。OpenCV 5.0.0、numpy 2.4.1 可用，無 scipy（我用 numpy 自己實作 Savitzky-Golay）。

### vidstab 預設值（`ffmpeg -h filter=...` 實跑輸出）
`vidstabdetect`：shakiness 預設 5（1–10）、accuracy 15、stepsize 6、mincontrast 0.25。
`vidstabtransform`：smoothing 預設 **15**（本機 ffmpeg 7.1 的說明；vid.stab README 寫的是 10，兩者不同，以本機為準）、optzoom 預設 1、zoom 0、zoomspeed 0.25、interpol bilinear、crop keep、relative 1。
vid.stab README（https://github.com/georgmartius/vid.stab）【文件】：只做平移＋旋轉；smoothing 調大（例如 30）會更平順但「limits pan/tilt acceleration」；**重複幀會被當成零位移，造成規律性頓挫，需先 mpdecimate**；改變幀數／幀序／速率的濾鏡必須在兩個 pass 完全一致；預設是高斯低通，也描述了 L1 最佳路徑（靜止／線性／拋物線段）。

### 我跑的實驗（輸入：以 `fps=30` 轉成 CFR 的 `cfr.mp4`，181 幀）
量測方法（殘餘晃動）：
- 路徑：相鄰幀全域位移累加。兩種估計器交叉驗證：LK 光流＋RANSAC（對移動角色較穩健）、`cv2.phaseCorrelate`（全畫面，不同演算法）。
- 指標：路徑減去 Savitzky-Golay(w=15, 3 次) 平滑路徑後的 RMS（px，1920 寬度換算）。w=15 只看 ≈1 Hz 以上的抖動，不把轉頭算進去。
- 注意：這個指標只在「抖動頻段」有意義；我一開始用的加速度 RMS 被轉頭本身主導，區分力很差，已放棄（`measure1.txt`、`measure2.txt` 留存）。

| 設定 | LK 殘餘 jitter(w15) | PC 殘餘 jitter(w15) | 放大／邊緣 |
|---|---|---|---|
| 原始 | 9.65 | 8.07 | — |
| vidstab smoothing=5, zoom=0 | 6.37 | 4.30 | 無放大，邊緣用前一幀填（keep） |
| vidstab smoothing=10, zoom=5, optzoom=0 | 4.86 | 1.88 | crop=black 測：**0/181 幀露邊** |
| **vidstab smoothing=15, zoom=5, optzoom=0** | 4.14 | 1.11 | crop=black 測：30/181 幀露邊、最大 56 px |
| vidstab smoothing=15, zoom=8, optzoom=0 | 4.29 | 1.15 | 8/181 幀露邊、最大 27 px |
| vidstab smoothing=20, zoom=8 | 3.63 | 1.00 | 31/181 幀露邊、最大 96 px |
| vidstab smoothing=15, optzoom=1（預設） | 3.85 | 1.23 | 靜態放大約 1.17 倍（目視） |
| vidstab smoothing=30, optzoom=1 | 3.24 | 1.22 | 放大約 1.45 倍，**不可用**（目視，人物被切到胸口） |
| ffmpeg `deshake`（rx=ry=32） | 14.87 | 13.16 | **比原始更糟**，且邊緣鏡射殘影可見 |
| 自製 Savitzky-Golay(w=15, 3 次)＋固定放大 1.06、修正量 tanh 限幅 | 5.44 | 5.60 | 無露邊，保留轉頭 |
| 自製 SG(w=25)＋放大 1.08 | 3.65 | 4.82 | 無露邊 |

重點【實測】：
- `shakiness` 5 與 8 在這段素材上輸出**完全相同**（同樣的 smoothing/zoom 下兩項指標逐位相同），不必調高。
- `smoothing=10, zoom=5` 是在「零露邊」前提下最好的：LK 殘餘 9.65 → 4.86（-50%）、PC 殘餘 8.07 → 1.88（-77%）。`smoothing=15, zoom=8` 殘餘更低（4.29 / 1.15）但有 8 幀露邊最大 27 px。
- 用預設 `optzoom=1` 或把 smoothing 開到 30 會在轉頭幀把畫面放大 1.17–1.45 倍，因為 vidstab 的置中移動平均對刻意轉頭的加減速跟不上、算出很大的修正量。**這是這類素材最大的坑**。
- `optzoom=0` ＋ 固定 `zoom=5`，並用 `crop=black` 先測一次是否露邊，再決定最後用 `keep`（預設，邊緣是前一幀殘影）或加大 zoom。
- 補充：`interpol=bicubic` 與 `bilinear` 在指標上差距極小（4.14 vs 4.30 LK），沒有果凍感，因為 vidstab 與我的自製法都只做剛體（平移＋旋轉），不是逐像素變形。此素材為 VR 渲染畫面，沒有滾動快門，所以「果凍效應」本來就不存在；若改拍真實相機才需要考慮【推測】。
- 剛體穩定對「角色在動」沒有可見副作用【實測】：我抽了幀 75、141 的並排圖，角色與桌面沒有被扭曲。
- 對字幕泡泡：本素材的對話泡泡是場景內物件（跟著角色，不是頭部鎖定 HUD）【目視】。穩定處理不會扭曲泡泡，但**放大會把泡泡切掉**：來源幀 141 的泡泡已被畫面上緣切到（「Need a minute, champ?」只露半截），5% 放大只會更糟。解法是用 Remotion 重畫字幕，不要依賴錄影裡的泡泡【推測】。
- **頭部鎖定 HUD 沒有測**：這段素材沒有 HUD。理論上整幀剛體變換會讓 HUD 跟著晃動反而「變晃」（HUD 原本在畫面固定，穩定後被補償運動帶著走）【推測】，需要用有 HUD 的素材驗證。
- Gyroflow【文件】：「cameras without a built-in gyro … cannot be used with Gyroflow on their own」（https://docs.gyroflow.xyz/app/getting-started/supported-cameras）；v1.5.4 起有「very basic」的無陀螺儀光流模式，原文「this is not a proper feature yet. May work for simple cases … will probably fail for more tricky movements」（https://github.com/gyroflow/gyroflow/releases/tag/v1.5.4）。Quest 錄影檔沒有陀螺儀資料，所以 Gyroflow 不適用；它的光流模式還需要鏡頭 profile，我沒有實跑，**不建議**。
- L1 最佳路徑【文件】：Grundmann、Kwatra、Essa, CVPR 2011（https://research.google/pubs/auto-directed-video-stabilization-with-robust-l1-optimal-camera-paths/）。路徑由靜止／等速／等加速度段組成，理論上最適合「刻意轉頭 + 小抖動」。vid.stab 本身也宣稱內建 L1 求解器，但 ffmpeg 的 `vidstabtransform` 沒有暴露這個選項（我在 `-h filter` 輸出中找不到），所以要用 L1 得自己用 OpenCV 做。我只實作了 Savitzky-Golay（同樣保留轉頭曲率）作為替代，沒有實作 L1，所以 L1 的效果沒有量測。

### 推薦（對這類素材）
1. 最簡單可靠：`vidstabdetect=shakiness=5:accuracy=15` → `vidstabtransform=smoothing=10:zoom=5:optzoom=0:interpol=bicubic`。數字見上表。
2. 想更穩：smoothing=15、zoom=8；但先用 `crop=black` 檢查露邊幀數。
3. 絕對不要：`deshake`、`optzoom=1/2` 搭配 smoothing ≥ 20、smoothing=30。
4. 順序：**先在原生幀序（`-fps_mode passthrough`）做穩定，再做幀率轉換**。理由是 vid.stab README 明講重複幀會被當零位移造成規律頓挫；兩個 pass 的輸入需一致。我有跑 passthrough 版（`nat.trf`，180 幀輸出），殘餘指標與 CFR 版相同（LK 4.16 vs 4.14），但這個指標是以幀序計算、看不出重複幀造成的時間性頓挫，所以「先穩定再轉幀率」的優勢是依文件，不是我量到的。
5. 比穩定更有效的事【文件＋實測】：錄的時候慢慢轉頭（Meta 官方第一條建議），並在剪輯時避開 50 px/幀以上的快速轉頭段。

## A3. 變速規則與幀率處理

### 可變幀率素材怎麼處理
- 直接 `fps=30`：53/178 重複幀、53 次跳幀【實測】，不建議。
- Remotion 端：Remotion 官方文件指出舊的 FPS 轉換器範例不適用於媒體標籤；我找不到 Remotion 官方文件直接處理 VFR 素材【文件缺口】。實務做法是先在 ffmpeg 轉成 CFR 再丟進 Remotion。

### 補幀品質比較（同一段素材）
殘餘幀間運動粗糙度（越低越平滑；理想值＝依時間戳正規化的原生序列 0.096；整個指標不夠精細，只能看「排序」）【實測】：

| 方法 | 輸出 fps | 粗糙度 |
|---|---|---|
| 理想（依時間戳正規化的原生幀） | — | 0.096 |
| `fps=30` | 30 | 0.220 |
| `framerate=30`（混合） | 30 | 0.206 |
| `minterpolate=30 mci aobmc bidir vsbmc` | 30 | 0.194 |
| `fps=60`（重複幀） | 60 | 0.756 |
| `minterpolate=60 mode=blend` | 60 | 0.739 |
| `minterpolate=60 mci aobmc bidir vsbmc` | 60 | 0.416 |

- 目視證據【實測】：minterpolate mci 在快速轉頭幀（輸出幀 281、282）出現**人物邊緣重影**，對話泡泡文字被抹糊成不可讀。這和字幕泡泡共存是硬傷。圖：`stab-lab/fr/mci.png`。
- RIFE（rife-ncnn-vulkan 20221029 macOS 版，MIT 授權，`gh release download 20221029 -R nihui/rife-ncnn-vulkan`）【實測】：在這台 Apple M1 Max 上，61 幀 1080p 補成 120 幀（`-m rife-v4.6 -n 120`）耗時 22.8 秒（約 5 幀/秒），所以整段 6 秒素材約 70 秒，可行。快速轉頭段（來源幀 138–150）補出的中間幀沒有重影、泡泡文字清楚。圖：`stab-lab/fr/rife_fast.png`。
- RIFE 的限制【實測／推測】：它固定做 2 倍中點補幀，不理會 24/36 fps 交錯造成的不等間隔，所以時間上仍是近似。要嚴格依時間戳取樣，需要自己拆成多段指定 timestep（`-n` 與 `-x`/模型支援，我沒有嘗試）。
- 目前沒有依這個素材驗證「穩定 → RIFE → 變速」整條管線，只分別驗證過各步。

### 什麼時候用哪一種變速（來源不同、可信度不同）
- Remotion 官方文件【文件】：直接對 `playbackRate` 做 interpolate 行不通（每幀獨立計算），要累加已過的幀數去設 `trimBefore`（https://www.remotion.dev/docs/videos/accelerated-video）。這是 speed ramp 在 Remotion 的標準做法。
- HyperFrames 官方 skill【文件】（`skills/hyperframes/references/motion.md`）：montage 每 0.13–0.8 秒切一次；示範 UI 同一構圖停留 7–10 秒；鏡頭平均 1.8–3 秒；**payoff 前停 0.3–0.75 秒，payoff 以正常速度播放**；「要調快：內容加速、節拍同量縮短」。一個要讀的句子 hold ≈ 0.5 秒 + 每字 0.3 秒，最後一句至少 2 秒。
- 語音對嘴不該變速【推測／常識】：我找不到有數據的來源；可驗證的做法是只變速無對白的過場、讓有對白的片段維持 1.0×（HyperFrames 的 `data-playback-rate` 說明「picture and pitch-preserved sound」，只保證音高不變，不保證對嘴）。
- 慢動作：要有 RIFE 補幀（上面證據）才不會出現頓挫；若只用 `setpts` 放慢，會暴露 36/24 fps 交錯。

---

## B4. 有成品佐證的 AI／程式化影片工作流

**我無法觀看影片**，下面「品質證據」只包含我能實際讀到的原始碼、文件與 metadata，沒有任何一項是我看過成品畫面後的判斷。需要看成品的請使用者自己點。

### browser-use/video-use
- 倉庫：https://github.com/browser-use/video-use，MIT 授權，30,561 stars，2026-04-12 建立，最後推送 2026-10-08（`gh api` 實測）。
- 成品連結：README 提到官方 15 秒 demo（TikTok：https://www.tiktok.com/@browser_use/video/7639824093721758989），我無法下載或播放；SKILL.md 提到「the launch video shipped with this」但沒給 URL。**成品畫面我沒看過。**
- 可借鑑（我讀了 `SKILL.md` 全文，342 行）：
  - EDL JSON 流程：transcribe → pack → LLM 推理 → EDL → render → self-eval；EDL 欄位 `sources/ranges(start,end,beat,quote,reason)/grade/overlays/subtitles`。
  - 12 條硬規則，其中對本案最有用：**字幕最後才疊（避免被 overlay 蓋住）**、每段先抽出再無損 concat（避免二次編碼）、段落邊界 30 ms 音訊淡入淡出、overlay 用 `setpts=PTS-STARTPTS+T/TB` 對齊起點、整段輸出放獨立資料夾。
  - 自我驗片循環：在**渲染後**的成品上，每個剪點 ±1.5 秒做合成圖（影格 + 波形）；檢查視覺跳動、音訊爆音、字幕被蓋住、overlay 錯位；另外抽首 2 秒、尾 2 秒、2–3 個中間點；上限 3 輪，仍有問題就回報。
  - 音訊要量不要猜：`ffmpeg -i out.mp4 -af ebur128=peak=true -f null -` 看整合響度與 true peak；原文「You cannot listen: say so, and report the numbers.」
  - 對外發布的片要另外派一個 critic sub-agent，只給成品＋EDL，要求「roast, not praise」，輸出帶時間碼的排序問題與 5 個優先修正。
  - 音效：「~20 stock whooshes in 18s reads as generic; ~8 reads as designed」；每個效果對應畫面事件；效果起點要提前 attack 長度，讓峰值落在接觸幀；音樂在對白下壓低約 12–15 dB；最後 two-pass loudnorm 到 -14 LUFS、true peak ≤ -1 dBTP。
  - 字幕：2 字 UPPERCASE 為預設，< 0.35 秒的字幕改成長到 3 字，避免閃現。
- 可直接採用：自我驗片循環（含 critic）、音訊量測指令、字幕最後疊、音效數量上限。EDL 與轉錄（ElevenLabs Scribe）不適用本案（無語音素材，且要付費 API）。

### heygen-com/hyperframes
- 倉庫：https://github.com/heygen-com/hyperframes，Apache-2.0，79,116 stars，最後推送 2026-11-05（`gh api` 實測）；官方展示頁 https://hyperframes.heygen.com/examples。
- 展示頁我讀到的內容：只有兩個「重現既有影片」的範例（THE OBLIST 38.58 秒/60 fps、Srinika × Mercury 30.56 秒/25 fps），各有 reference/replica 兩張海報圖，沒有可播放的 URL；「Launch films」與「Talking-head edits」區塊的實際影片，我抓取的文字內容是空的。**成品品質我沒有直接證據。**
- 可借鑑（我讀了 `skills/hyperframes/SKILL.md` 與 `references/{motion,sound,review,captions}.md`）：
  - 流程：`npx hyperframes check --json`（lint）→ `snapshot --review`（每個場景起始與中點、每個節拍、每個剪點前後 ±0.2 秒、手機寬度 360 px 三張總覽圖）→ 逐項問題清單（P0/P1/P2）→ 另一個全新 agent 只看 STORYBOARD 與三張圖挑問題 → `REVIEW.md`。「Trust the numbers over your eye where they disagree」「Never score the film out of 10」。
  - 可量測的工藝警告：一行字顯示太短讀不完、超過 3 秒畫面沒變化、文字小於短邊的 2.6%、第一幀空白。
  - 安全區：關鍵內容在 90% action-safe，字幕與關鍵文字在 80% title-safe。
  - 節奏：見 A3。轉場「3–5 種、每種都有畫面上的原因」；硬切最多佔轉場的三分之一。
  - 字幕：2–3 字高能量、3–5 字對話、4–6 字平靜；一次只顯示一組；字幕不要從 0 秒開始（0.1–0.3 秒後再出）；每組結尾要硬性關閉。
- 可直接採用：Review 流程與 phone-width 檢查（和 Remotion 無關，純方法）；「defaults that look generated」清單（呼吸、漂浮、脈動循環、標題落地後持續滑動、每個元素都淡入上滑等）。
- 與本案的衝突：HyperFrames 是另一個渲染框架（HTML + GSAP），使用者環境已有 Remotion 4.0.540，沒有必要換。**成品我沒看過。**

### Remotion 官方資源
- 倉庫 https://github.com/remotion-dev/remotion（70,841 stars）、技能庫 https://github.com/remotion-dev/skills（6,380 stars，含 `remotion-best-practices` 與 `remotion-captions`、`remotion-create/video-layout.md` 等）。Claude Code 外掛：`claude plugin marketplace add remotion-dev/claude-code-plugin`（https://www.remotion.dev/docs/ai/claude-code-plugin；該頁沒有列規則，也沒有示範影片）。
- 展示頁 https://www.remotion.dev/showcase：我抓到的內容只有導覽，沒有條目，所以**無法列出成品**。
- GitHub Unwrapped：https://github.com/remotion-dev/github-unwrapped（1,328 stars；搜尋摘要說是 Next.js + Remotion + AWS Lambda，為 10,000+ 個使用者各自產生影片，「音樂只授權給該網站使用」）。我抓到的 https://www.githubunwrapped.com 內容只有標題，沒有樣片連結，**成品畫面沒看過**。這個案例對本案的價值只在「Remotion 可以穩定地大量輸出」，與頭盔錄影剪輯無關。
- 授權【文件】：Remotion 的免費授權適用於「an individual／for-profit organization with up to 3 employees／non-profit／evaluating」（https://github.com/remotion-dev/remotion/blob/main/LICENSE.md）。比賽若以公司名義參賽、公司超過 3 人，需要確認；個人參賽沒有問題。
- 反面證據：搜尋結果裡「Claude Code 做的 Remotion 影片」幾乎都是第三方部落格與論壇貼文，其中的數字（例如某則 demo 600 萬次觀看、30K 週安裝量）我沒有查證，**不要引用**。

### 結論（B4）
有成品佐證且可直接取用的是「方法」，不是「某支成品」：video-use 的自我驗片與音訊量測、HyperFrames 的 snapshot 檢視與工藝警告。我沒有任何一支成品的畫面證據。

## B5. 預告片剪輯的音樂與音效

### 方法
- 音樂當時鐘【文件】：HyperFrames `sound.md`：「Music only: the music is the clock. Land the payoff and the mark on its strong beats … cuts follow the motion, not the drums.」意思是 payoff 與標題對齊強拍，但一般的剪點仍然跟著畫面動作，不是每個拍子都切。節拍偵測用 `npx hyperframes beats`（我沒跑）。
- 進場與結尾【文件】：音樂從曲子最強的小節進，不要放安靜的前奏；在小節線上結束，結尾卡下面做短淡出。
- 效果只放有畫面原因的地方【文件】：whoosh 只在場景切換或大卡片快速移動；riser into impact 整支片只用一次，放在 payoff 或標誌；pop 用於徽章或數字落定。
- 時間對齊【文件】：音效峰值放在畫面接觸後一幀（光比聲快，聲音早到比晚到更不自然）；每個 hit 比音樂床高 3–6 dB；同一個音效準備 2–3 個 take 輪流用；不要用合成音調、鈴聲、叮聲。
- 靜默的作用【文件，廠商部落格，可信度中】：riser 尾端留約 0.5 秒靜默再落 impact（https://www.krotosaudio.com/ 的 trailer sound tips 的搜尋摘要，我沒讀原文）；Rare Form Audio 說 stopdown 讓之後的 drop 更大、沒有 build 的 drop「feels hollow」（https://www.rareformaudio.com/blog/trailer-music-drop-vs-stopdown，只看到搜尋摘要）。
- 響度【文件】：video-use 與 HyperFrames 都給出：混音約 -16 到 -14 LUFS、true peak ≤ -1 dBTP；音樂在旁白下約 0.14 音量（HyperFrames）或 -12 到 -15 dB（video-use）；音樂能量大多在 150 Hz 以下，在手機喇叭上等於沒有（HyperFrames）。
- **實際聽感無法驗證**：兩份文件都明說 agent 聽不到，只能量測。我也一樣。

### 可合法用於比賽作品的免費來源
本案條件：不能用 AI 生成畫面、不能出現品牌 logo；音樂要能合法用於比賽。**授權各有限制，比賽規則是否禁止第三方素材我沒有看到，請使用者自行對照。**

| 來源 | 授權原文（我實際抓到的） | 要點 | 風險 |
|---|---|---|---|
| Sonniss GDC Game Audio（音效，最新是 GDC 2024，9 包 23.5 GB；2026 版在 gdc.sonniss.com）https://sonniss.com/gameaudiogdc | 「All of the sounds are royalty free and commercially usable.」「No attribution is required and you can use them on an unlimited number of projects」「These sound effects are licensed for media production (games, film, TV, interactive projects) only.」「Use for AI/ML training is strictly prohibited」 | 適合做 hit、whoosh、impact；單獨轉售音效不可 | 檔案很大；只有音效，沒有音樂 |
| Freesound 的 CC0 檔案 https://freesound.org/help/faq/#licenses | CC0 全文：「You can copy, modify, distribute and perform the work, even for commercial purposes, all without asking permission.」（https://creativecommons.org/publicdomain/zero/1.0/） | 只下載標示 CC0 的；CC BY 要署名，CC BY-NC「you can't earn any money」不適合有獎金的比賽 | 每支都要逐一確認授權標籤 |
| Pixabay 音樂與音效 https://pixabay.com/service/license-summary/ | 「Use Content for free」「Use Content without having to attribute the author」「Modify or adapt Content into new works」；禁止：「You cannot sell or distribute Content … on a Standalone basis.」 | 免署名；不能單獨轉售；Content ID 與 YouTube 版權聲明，頁面「沒有提到」；另需自行確認 | 具名的法律效力只在 /service/terms/；該頁條款 7B 要求標示 AI 生成內容，而且我沒看到比賽用途的明確句子 |
| Kevin MacLeod / Incompetech https://incompetech.com/music/royalty-free/faq.html | 「Be sure to credit me.」「Licensed under Creative Commons: By Attribution 4.0」「Yes, AND you can monetize the videos.」「All of this music is copyrighted.」 | CC BY 4.0，必須在作品中標示作者與授權；明確可商用 | 要在影片或說明中署名；曲風偏通用 |
| Mixkit 音樂與音效 https://mixkit.co/license/ | 我抓到的頁面只有「Stock Music Free License」「Sound Effects Free License」名稱，**沒有條款原文** | — | 未驗證，不建議採用，除非使用者自行讀完授權 |

我沒有查 YouTube Audio Library（授權限制多與 YouTube 平台綁定）、Free Music Archive、ccMixter。

反面提醒：
- 「免費」不等於「免版權聲明」。Pixabay 頁面沒有談 Content ID，所以若上 YouTube 仍可能被第三方誤判；建議在比賽提交前保留授權頁截圖與下載頁面 URL【推測】。
- CC BY 的 Kevin MacLeod 曲目需要署名；若比賽影片有「畫面不得出現其他文字」之類規則，要先確認署名放哪裡。

---

## 沒做到的事／不確定的點

1. 完全沒有看過任何 AI／程式化影片的成品畫面（video-use、HyperFrames、Remotion showcase、GitHub Unwrapped）；只讀了原始碼、文件與 metadata。Remotion showcase 與 GitHub Unwrapped 網站的條目內容我抓不到。
2. Meta 官方頁面沒有找到：後製穩定的裁切比例數字、慢動作／變速慣例、頭部鎖定 HUD 的處理；Meta capture playbook 的 PDF 內容我沒取得。
3. 測試只用一段 6 秒、VR 渲染畫面（非 MR 穿透），也沒有頭部鎖定 HUD。穿透畫面的視差、真實相機的滾動快門、HUD 與穩定的交互作用，都沒有實測。
4. 殘餘晃動指標有局限：以光流與相位相關估計全域位移，移動中的角色會干擾；兩種估計器的絕對數字不同（LK 4.14 vs PC 1.11），我只依「同一估計器內的相對變化」下結論。幀間粗糙度的指標在 60 fps 與 30 fps 之間不可直接比較，且 minterpolate 30 與 `fps=30` 的差距（0.194 vs 0.220）可能在雜訊範圍內。
5. 時間戳與位移的相關（r=0.89）是在差一幀對齊下得到的；cv2 解出 181 幀、ffprobe 列 180 個 pts，這個差一的原因我沒查明。
6. L1 最佳路徑沒有實作與量測；只實作 Savitzky-Golay 當替代。Gyroflow 的無陀螺儀光流模式沒有實跑。
7. RIFE 只在 2 秒與 13 幀兩個片段測試，沒有把整段 6 秒跑完，也沒有驗證與穩定、變速、Remotion 串接後的整條管線；它的 2 倍固定補幀與 24/36 fps 交錯的時間不均問題沒有解決。
8. 音樂：沒有實際下載或聆聽任何曲目；只核對授權頁面。Mixkit 授權原文沒取得。比賽主辦方對第三方素材的規定我沒看到。
9. 沒有跑 `npx hyperframes beats`，節拍偵測工具沒有實測；本機沒有 librosa。
10. 實驗檔：`/private/tmp/claude-501/-Users-laichanean-WebstormProject-claw-hand/dba936d2-a2e1-4020-9344-fc7606a601ab/scratchpad/trailer-research/stab-lab/`（`measure3.py` 是最後採用的量測腳本；`cstab.py` 是自製穩定器；`fr/` 是對照圖）。RIFE 二進位放在同層的 `rife-dl/`，沒有安裝任何全域工具。
