# Tell 比賽 demo 影片剪輯配方（2:30–2:50）

研究日期：2026-11-05。產出者：trailer-research 研究員。本檔不改任何專案檔。

標記說明：**【親眼】** = 我下載影片、跑 ffmpeg、並用 Read 看接觸表／抽幀後得到的；**【數出】** = ffmpeg 指令量測；**【文章】** = 網頁／規則文字所述；**【推測】** = 我的判斷，沒有直接證據。

---

## 0. 先講三個會改變你們決策的發現

1. **使用者要參加的很可能是「Meta VR Start Developer Competition 2026」**（Devpost 規則頁顯示 Entry period 2026-09-24 至 2026-11-18，與使用者 11/10 前完成、11/11–15 不在的時程吻合；我無法 100% 確認使用者報名的是哪個比賽，標【推測】）。它的規則比 2025 版多了對影片與體驗的明確要求（見第 2 節）：**「show real gameplay that honestly represents the experience. Lead with the story and what makes your project distinct. Don't lean on AI-generated video to carry the pitch.」**，以及 **「Passthrough should be purposeful: the player's real room meaningfully changes the experience」**，以及 **必須 hands-first（全程可用手完成，controller 可有可無）**。Tell 用手勢下注＋語音，方向吻合，但影片必須把這點演出來。
2. **2025 競賽得獎作品的 submission video 大多不是「快剪預告片」**：28 支下載到的得獎影片，平均鏡頭長度中位數 14.4 秒（th0.3），其中 21/28 平均鏡頭長 ≥ 8 秒；對照 13 支商業／得獎預告片，平均鏡頭長中位數 2.1 秒，且 0/13 超過 8 秒。也就是：**評審看的是「清楚、誠實的實機畫面」，不是剪輯炫技**。商業預告的快剪節奏來自不同目的（賣給大眾），不能直接套。
3. **得獎 submission 幾乎都有一層「極簡字幕」把畫面講清楚**（至少 16/28），而 Tell 的核心賣點（雙向讀心）在原始第一人稱畫面裡看不出來，**必須靠字幕與標註把「看不見的東西」變成看得見**。這是 Tell 影片最大的設計課題。

---

## 1. 影片清單與節奏量測

量測方法（我實際跑的指令）：

- 下載：`yt-dlp --js-runtimes node -f "bv*[height<=360]+ba/b[height<=360]/worst" --merge-output-format mp4 -- <url>`，放在 `/private/tmp/claude-501/-Users-laichanean-WebstormProject-claw-hand/dba936d2-a2e1-4020-9344-fc7606a601ab/scratchpad/trailer-research/vids/`（商業預告）與 `.../win/`（得獎 submission）。
- 鏡頭偵測：`ffmpeg -i f -an -vf "select='gt(scene,0.3)',metadata=print:file=-" -f null -`，另跑 0.2 當敏感度對照。腳本在 `/private/tmp/claude-501/-Users-laichanean-WebstormProject-claw-hand/dba936d2-a2e1-4020-9344-fc7606a601ab/scratchpad/trailer-scripts/analyze.py`。
- **限制**：scene 偵測對淡入淡出（crossfade）、連續運鏡、第一人稱頭部晃動都不準。例如 Inkphony submission 在 0.3 只偵測到 2 個鏡頭、0.2 偵測到 34 個（它用大量慢溶接）。所以請把「鏡頭數」當量級，不要當精確值；兩個門檻都列出。
- 音訊：用 `astats` 每秒 RMS。**只能證明「有沒有聲音、大小聲」，不能分辨是音樂還是遊戲音效**，所以下面「音樂走向」只講音量輪廓。

### A 組：Meta Horizon Start Developer Competition 2025 得獎作品的 submission video（共下載 28 支，4 支私人／下架無法取得）

| 影片 | URL | 得獎／入圍狀況（來源） | 長度 | 鏡頭數 / 平均秒 / 中位秒 (th0.3) | 鏡頭數 / 平均秒 (th0.2) | 第一刀秒數 | 前10秒鏡頭數 |
|---|---|---|---|---|---|---|---|
| Meta Horizon Start Developer Competition - GeoPo (geoportal) | https://youtu.be/diRcGpDhMl8 | Best Android App Leveraging Features Unique to Meta Quest；來源：devpost.com/software/geoportal 頁面「Winner, …」字樣（我用 curl 取得） | 2:25 | 7 / 20.73 / 9.5 | 11 / 13.19 | 12.83 | 1 |
| OnTheGoVR - Meta Horizon Start Developer 2025 Su (syncedvr) | https://youtu.be/snMBNi9eN0o | Best Android App for Travel Mode；來源：devpost.com/software/syncedvr 頁面「Winner, …」字樣（我用 curl 取得） | 2:44 | 21 / 7.8 / 4.4 | 23 / 7.13 | 12.23 | 1 |
| MarketView VR (marketview-vr) | https://youtu.be/XFBR9wV8kLA | Best Android Utility App；來源：devpost.com/software/marketview-vr 頁面「Winner, …」字樣（我用 curl 取得） | 1:30 | 1 / 89.8 / 89.8 | 1 / 89.8 | 無 | 1 |
| Tiny Golf Trailer / Meta Horizon Start Developer (tiny-golf) | https://youtu.be/e1_YPNKMR0E | Best Casual Game；來源：devpost.com/software/tiny-golf 頁面「Winner, …」字樣（我用 curl 取得） | 0:46 | 13 / 3.55 / 3.61 | 19 / 2.43 | 5.57 | 2 |
| Le Dino Labo – Build Dinosaurs in Mixed Reality  (realcast-game-working-title) | https://youtu.be/OciTlQDsUHU | Best Casual Game Honorable Mention；來源：devpost.com/software/realcast-game-working-title 頁面「Winner, …」字樣（我用 curl 取得） | 0:49 | 2 / 24.34 / 24.34 | 2 / 24.34 | 32.9 | 1 |
| Little Critters Advanced Hand Tracking Update fo (little-critters) | https://youtu.be/i2qJPLwJRSw | Best Casual Game Runner-up；來源：devpost.com/software/little-critters 頁面「Winner, …」字樣（我用 curl 取得） | 2:50 | 13 / 13.08 / 4.93 | 31 / 5.49 | 6.2 | 3 |
| DreamSpace- Mixed Reality / Best Entertainment E (dreamspace-6g8rzm) | https://youtu.be/ogtDlO9M6jA | Best Entertainment Experience；來源：devpost.com/software/dreamspace-6g8rzm 頁面「Winner, …」字樣（我用 curl 取得） | 3:50 | 9 / 25.56 / 27.37 | 11 / 20.91 | 40.43 | 1 |
| (m)ORPH Mixed Reality Music Experience・モーフー音楽 (Q (m-orph-mixed-reality-music-experience) | https://youtu.be/tDy0m9KRpzg | Best Entertainment Experience Honorable Mention；來源：devpost.com/software/m-orph-mixed-reality-music-experience 頁面「Winner, …」字樣（我用 curl 取得） | 3:01 | 16 / 11.32 / 2.38 | 20 / 9.06 | 14.93 | 1 |
| photon video (photon-bm9p31) | https://youtu.be/5WpxaLfLWOo | Best Entertainment Experience Runner-up；來源：devpost.com/software/photon-bm9p31 頁面「Winner, …」字樣（我用 curl 取得） | 0:34 | 14 / 2.41 / 1.59 | 18 / 1.88 | 2.5 | 3 |
| Magic Wonders Showcase — Multiplayer Duels, Smar (magic-wonders) | https://youtu.be/NTN3Vbj88MY | Best Experience Built with React Native；來源：devpost.com/software/magic-wonders 頁面「Winner, …」字樣（我用 curl 取得） | 2:24 | 2 / 71.86 / 71.86 | 2 / 71.86 | 30.2 | 1 |
| The Nanauts by James C. Kane - Hack-a-thon Trail (nanauts) | https://youtu.be/7r-29fp4Nbg | Best Immersive Experience Built with Immersive Web SDK；來源：devpost.com/software/nanauts 頁面「Winner, …」字樣（我用 curl 取得） | 1:18 | 6 / 13.01 / 11.23 | 14 / 5.58 | 4.17 | 3 |
| Dun Jun - Action-arcade hand interaction-based V (dunjun) | https://youtu.be/BpONkM3Co7A | Best Immersive Experience Built with Immersive Web SDK；來源：devpost.com/software/dunjun 頁面「Winner, …」字樣（我用 curl 取得） | 1:42 | 7 / 14.5 / 2.1 | 7 / 14.5 | 35.13 | 1 |
| Awesome Hand - Demo movie (awesome-hand) | https://youtu.be/LB8Yeluiems | Best Implementation of Hand Interactions；來源：devpost.com/software/awesome-hand 頁面「Winner, …」字樣（我用 curl 取得） | 2:48 | 1 / 167.76 / 167.76 | 1 / 167.76 | 無 | 1 |
| HandSurvivor Presentation - Meta Horizon Start D (handsurvivor) | https://youtu.be/8RnPp3ORdL8 | Best Implementation of Hand Interactions；來源：devpost.com/software/handsurvivor 頁面「Winner, …」字樣（我用 curl 取得） | 1:22 | 8 / 10.28 / 8.73 | 15 / 5.48 | 24.93 | 1 |
| Meta Start Hackathon Submission for Pocket Lands (pocket-lands) | https://youtu.be/VEUu8n77FVg | Best Implementation of Hand Interactions；來源：devpost.com/software/pocket-lands 頁面「Winner, …」字樣（我用 curl 取得） | 3:00 | 1 / 179.98 / 179.98 | 1 / 179.98 | 無 | 1 |
| Chores.gg: turn chores into a game (ping-t6exlb) | https://youtu.be/Sm9pWQHiZU4 | Best Implementation of Passthrough Camera Access (PCA) with AI；來源：devpost.com/software/ping-t6exlb 頁面「Winner, …」字樣（我用 curl 取得） | 0:48 | 7 / 6.83 / 6.27 | 9 / 5.31 | 3.03 | 2 |
| Flows XR (weavexr) | https://youtu.be/fbFu2ehXxFU | Best Implementation of Passthrough Camera Access (PCA) with AI；來源：devpost.com/software/weavexr 頁面「Winner, …」字樣（我用 curl 取得） | 2:14 | 5 / 26.76 / 26.03 | 5 / 26.76 | 23.57 | 1 |
| Language IRL - Demo December 2025 (see-and-speak-spanish) | https://youtu.be/1RC030TLCys | Best Implementation of Passthrough Camera Access (PCA) with AI；來源：devpost.com/software/see-and-speak-spanish 頁面「Winner, …」字樣（我用 curl 取得） | 1:09 | 7 / 9.86 / 5.47 | 12 / 5.75 | 34.63 | 1 |
| Inkphony Meta Start Competition (inkphony) | https://youtu.be/T6NyCAvU0sk | Best Lifestyle Experience；來源：devpost.com/software/inkphony 頁面「Winner, …」字樣（我用 curl 取得） | 2:57 | 2 / 88.43 / 88.43 | 34 / 5.2 | 68.0 | 1 |
| IMMERROCK - Exercise overhaul and celebrations U (immerrock) | https://youtu.be/98HlGIi8KMs | Best Lifestyle Experience Honorable Mention；來源：devpost.com/software/immerrock 頁面「Winner, …」字樣（我用 curl 取得） | 2:42 | 3 / 53.84 / 7.21 | 3 / 53.84 | 154.23 | 1 |
| The Tarot Experience VR  - Developer Comp (the-tarot-experience-vr-ai-edition) | https://youtu.be/DnuDY5wk3rE | Best Lifestyle Experience Runner-up；來源：devpost.com/software/the-tarot-experience-vr-ai-edition 頁面「Winner, …」字樣（我用 curl 取得） | 2:59 | 1 / 178.78 / 178.78 | 3 / 59.59 | 無 | 1 |
| Saber Punks Meta Horizon Start Developer Competi (saber-punks) | https://youtu.be/nYZlCRHhAB4 | Best Social Game；來源：devpost.com/software/saber-punks 頁面「Winner, …」字樣（我用 curl 取得） | 1:10 | 12 / 5.81 / 6.2 | 24 / 2.9 | 9.48 | 2 |
| PACK ATTACK the MR MULTIPLAYER game! - Meta star (pack-attack) | https://youtu.be/YkWO2qdyeME | Best Social Game Honorable Mention；來源：devpost.com/software/pack-attack 頁面「Winner, …」字樣（我用 curl 取得） | 1:16 | 13 / 5.81 / 3.73 | 21 / 3.6 | 9.27 | 2 |
| FALCON - Meta Horizon Start Developer Competitio (falcon-shgxn9) | https://youtu.be/CwuS1JffbFs | Best Social Game Runner-up；來源：devpost.com/software/falcon-shgxn9 頁面「Winner, …」字樣（我用 curl 取得） | 1:57 | 3 / 38.89 / 49.46 | 8 / 14.58 | 66.71 | 1 |
| HandCraft XR: Winter Edition - Trailer (handcraft-xr-winter-edition) | https://youtu.be/4KbKIA9H5_Y | Judge’s Choice；來源：devpost.com/software/handcraft-xr-winter-edition 頁面「Winner, …」字樣（我用 curl 取得） | 1:13 | 17 / 4.3 / 2.0 | 20 / 3.65 | 2.0 | 4 |
| OpenSoundLab: Demo for Meta Hackathon 2025 (opensoundlab) | https://youtu.be/230hncihTxI | Judge’s Choice；來源：devpost.com/software/opensoundlab 頁面「Winner, …」字樣（我用 curl 取得） | 5:00 | 21 / 14.3 / 3.67 | 25 / 12.01 | 215.53 | 1 |
| Behind the scenes of Final Throwdown / Game Nigh (sumo-boxing) | https://youtu.be/RI3DOfg7vhY | Judge’s Choice；來源：devpost.com/software/sumo-boxing 頁面「Winner, …」字樣（我用 curl 取得） | 3:00 | 12 / 15.0 / 15.6 | 14 / 12.86 | 12.5 | 1 |
| horizon devcomp submission (lasertag-zombies-update) | https://youtu.be/FXE2ThA0RgE | Judge’s Choice；來源：devpost.com/software/lasertag-zombies-update 頁面「Winner, …」字樣（我用 curl 取得） | 1:36 | 9 / 10.63 / 9.7 | 20 / 4.78 | 7.33 | 2 |

### B 組：商業／得獎預告片（作為對照）

| 影片 | URL | 得獎／入圍狀況（來源） | 長度 | 鏡頭數 / 平均秒 / 中位秒 (th0.3) | 鏡頭數 / 平均秒 (th0.2) | 第一刀秒數 | 前10秒鏡頭數 |
|---|---|---|---|---|---|---|---|
| Batman: Arkham Shadow / Become The Knight / Meta Quest 3/3S | https://youtu.be/JGKM5dzR1mM | Batman: Arkham Shadow：The Game Awards 2024 Best VR/AR 得主（WebSearch 結果：mixed-news、bo3.gg） | 1:10 | 33 / 2.12 / 1.04 | 45 / 1.56 | 8.34 | 2 |
| Demeo / Launch Trailer | https://youtu.be/TurA7Jt284g | Demeo：VR Awards 2021 Game of the Year（WebSearch 結果引述，未開原始頁） | 2:20 | 27 / 5.18 / 2.72 | 44 / 3.17 | 10.24 | 1 |
| Demeo / Reign of Madness Release Trailer | https://youtu.be/-9hTamNgjTs | Demeo 資料片預告；同上（Demeo 的獎） | 1:50 | 38 / 2.89 / 2.03 | 50 / 2.19 | 0.53 | 3 |
| DreamSpace / Official Trailer / New Environment Release / Me | https://youtu.be/2RAfGdwwMV8 | DreamSpace 官方預告；同一款在 2025 競賽為 Best Entertainment Experience 冠軍（Devpost 頁面） | 1:15 | 19 / 3.94 / 2.3 | 19 / 3.94 | 1.67 | 2 |
| Eleven Table Tennis MRC Trailer | https://youtu.be/hWJ3q4lyl8M | 未確認得獎（Eleven Table Tennis，僅作 MR 外接攝影機合成參考） | 0:29 | 29 / 1.01 / 0.93 | 30 / 0.97 | 1.23 | 11 |
| Laser Dance / Early Access Trailer / Meta Quest 3 + 3S | https://youtu.be/eQq6l48HZt0 | 未確認得獎（Meta 頻道，MR 實拍） | 1:00 | 8 / 7.5 / 5.42 | 27 / 2.22 | 0.04 | 4 |
| Little Critters Launch Trailer | https://youtu.be/Xdg9N02WfDM | Little Critters 上市預告；同一款在 2025 競賽為 Best Casual Game Runner-up（Devpost 頁面） | 0:39 | 19 / 2.03 / 1.6 | 28 / 1.38 | 3.77 | 3 |
| PokerStars Presents: Vegas Infinite | https://youtu.be/6B_K1rJJNZQ | 未確認得獎（撲克題材參考） | 1:22 | 71 / 1.16 / 0.8 | 89 / 0.92 | 5.04 | 7 |
| PokerStars VR - Launch Trailer / PS VR2 | https://youtu.be/ymCEHWQbvac | 未確認得獎（PS VR2 發行預告，撲克題材參考） | 1:03 | 45 / 1.4 / 1.07 | 50 / 1.26 | 2.0 | 10 |
| Spatial Ops / Official Trailer / Meta Quest Platform | https://youtu.be/QnGoEaHqDbE | 未確認得獎（Meta 官方頻道 Spatial Ops 預告） | 1:22 | 47 / 1.75 / 1.2 | 55 / 1.49 | 0.7 | 8 |
| Starship Home / Full Reveal Trailer / Meta Quest 3 | https://youtu.be/M4--FBM_fEs | 未確認得獎（Meta 頻道，MR 實拍） | 1:27 | 36 / 2.4 / 1.84 | 41 / 2.11 | 10.01 | 1 |
| The Midnight Walk - Reveal Trailer / PS5 & PS VR2 Games | https://youtu.be/QnBE7mU6ZsI | The Midnight Walk：The Game Awards 2025 Best VR/AR 得主（WebSearch 結果） | 1:35 | 16 / 5.91 / 1.87 | 22 / 4.3 | 2.0 | 3 |
| Walkabout Mini Golf: Official Main Trailer | https://youtu.be/wyRjHU7vNEg | 未確認得獎（Walkabout Mini Golf 官方主預告） | 0:56 | 27 / 2.07 / 1.8 | 33 / 1.69 | 2.43 | 5 |

### C 組：其他分析過但不歸入上兩組

| 影片 | URL | 得獎／入圍狀況（來源） | 長度 | 鏡頭數 / 平均秒 / 中位秒 (th0.3) | 鏡頭數 / 平均秒 (th0.2) | 第一刀秒數 | 前10秒鏡頭數 |
|---|---|---|---|---|---|---|---|
| Inkphony – The World Is Your Instrument | https://youtu.be/tM70BJWCPc8 | Inkphony 冠軍的後製行銷片（2026-04 上傳，非 submission video） | 2:04 | 18 / 6.87 / 4.07 | 20 / 6.18 | 2.67 | 4 |
| The Tarot Experience VR - Trailer 2024 | https://youtu.be/7uFcGGuciCQ | The Tarot Experience VR 2024 預告（Best Lifestyle Runner-up 為 AI Edition 的 submission，此片較舊） | 0:46 | 9 / 5.08 / 3.8 | 12 / 3.81 | 3.8 | 3 |
| A look around the Pocket Lands in the living room | https://youtu.be/aJjNGSpT8IY | Pocket Lands 發佈後行銷片（2025-12-21 上傳） | 2:12 | 4 / 32.95 / 13.86 | 5 / 26.36 | 103.9 | 1 |
| Skully Survivors VR – Hands-first update (Meta VR Start Deve | https://youtu.be/1lYNf_mY8FQ | Skully Survivors VR：2026 競賽的 submission（2026-11-02 上傳），結果未出 | 2:20 | 16 / 8.73 / 2.15 | 38 / 3.68 | 7.1 | 3 |

### 彙總（各組中位數）

| 組 | 支數 | 長度中位 | 平均鏡頭秒 (th0.3) | 中位鏡頭秒 (th0.3) | 平均鏡頭秒 (th0.2) | 第一刀秒數 | 前10秒鏡頭數 |
|---|---|---|---|---|---|---|---|
| A 得獎 submission | 28 | 2:05 | 14.4 | 9.1 | 10.5 | 13.9 | 1 |
| B 商業預告 | 13 | 1:15 | 2.1 | 1.8 | 1.7 | 2.0 | 3 |
**怎麼讀這張表**：A 組的第一刀中位數 13.9 秒、前 10 秒中位數 1 個鏡頭，代表得獎 submission 多半「一鏡到底先講清楚」；B 組第一刀中位數 2.0 秒、前 10 秒中位數 3 個鏡頭。A 組有 4 支達 3:00 以上（OpenSoundLab 5:00、DreamSpace 3:50、Sumo/Final Throwdown 3:00 整、MORPH 3:01）仍得獎，但規則寫「should be less than three minutes」「Judges are not required to watch beyond three minutes」，不是自動 DQ，也不是邀請你超時。

### 逐支觀察（前 10 秒、標題、字幕、結尾；皆【親眼】看接觸表／抽幀）

| 影片 | 前 10 秒 | 標題出現 | 字幕樣式 | 結尾 |
|---|---|---|---|---|
| Chores.gg（PCA 特別獎，47.8s，7 鏡頭） | 0–3s 戴頭盔的鏡頭（頭盔拿在手上、白閃、腳）→ 3s 進入穿透房間，浮動 UI 加 `chores.gg` 彩色 logo → 手指點 Start → 9s 已經在做家事 | 約 2.4–3s，疊在真實房間的 UI 上（不是黑底標題卡） | 幾乎沒有後製字幕；用「世界中的 UI 彈字」（LEVEL 47、+4 XP、washing the dishes）當字幕，全部貼在實物旁 | 約 43–48s 黑底 logo 約 5 秒，無音樂尾巴（音量從 -44 dB 降到靜音） |
| HandSurvivor（Hand Interactions 特別獎，1:22） | 0–8s 已有字幕「DEFEND THE NEXUS」 | 結尾 1:09 大 logo + 「By Tim & Denis」（約 12 秒黑底） | 底部置中全大寫、白字、深色圓角底板、每句約 4–8 秒、3–6 個字、全部講「功能」 | 黑底 logo 約 13 秒 |
| Inkphony submission（Best Lifestyle 冠軍，2:57） | 第一人稱桌面長鏡頭，手拿起蘋果，無字幕 | 取樣畫面中未見標題卡 | 左下小白底黑字標籤（New handtracking implementation／Plug into the music tool!／Unlock endless instruments!），每句持續數秒（取樣間隔 8.8 秒，無法精確到秒） | 取樣畫面中未見結尾卡 |
| Inkphony 行銷片（2:04，2026-04 上傳） | 真人坐桌前拿樂譜，大字「Invisible／Abstract／Hard to grasp」（問題陳述）；然後戴頭盔，轉入第一人稱 | 2.7s 角落小 logo，1:57 結尾大 logo | 粗體白字黑描邊大字，一詞一屏 | logo + 口號 |
| Tiny Golf（Best Casual Game 冠軍，46s，13 鏡頭） | 0s 就在真實客廳：腳翹在椅子上、桌面上長出小球場，手指捏 | 只有結尾 43.8s 綠底 logo | **完全沒有字幕**，只靠示範 | 綠底 logo 約 2 秒 |
| Language IRL（PCA 特別獎，1:09） | 房間牆上的卡片 + 吉祥物，「Language IRL」字樣與吉祥物在 0–24s 疊在房間畫面上（應用內 UI） | 0s | 不靠後製字幕，用應用程式內卡片 | 回到房間的結算畫面 |
| Little Critters submission（Casual Runner-up，2:50） | 0s logo + 真人小孩站在房間（真人第三人稱） | 0s | 幾乎無字幕 | 取樣畫面中未見結尾卡 |
| Little Critters 上市預告（0:39） | 0s 真人小孩＋房間；1.9s 彈出「THE LITTLE CRITTERS ARE COMING!」綠色潑漆字 | 結尾 logo 卡約 7 秒 | 彈出式潑漆字，3–5 字，與遊戲美術同風格 | 「OUT NOW」 |
| Spatial Ops 官方預告（Meta 頻道，1:22，47 鏡頭） | 0.7s 就切：POV 射擊 → 真人在客廳揮槍（第三人稱）→ 反覆交錯 | 1:13（全片 89% 處）才出現 | 傳送門風格字幕「TURN ANY SPACE INTO A BATTL…」「PULSE-POUNDING ACTION IN MIXED REALITY」，一次只一句 | 標題＋ Resolution logo |
| Laser Dance（Meta 頻道，1:00，8 鏡頭／27 鏡頭） | 0s 起就是真人第三人稱＋雷射特效合成；Meta Quest logo 疊在最前面 | 0:51（85% 處） | 無字幕，純靠肢體表演 | 標題＋日期＋裝置圖 |
| Starship Home（Meta 頻道，1:27） | 0–8s 工作室 logo，8.6s 起真人坐在自家沙發，腳翹起 | 1:17 | 無字幕 | 標題＋「Available for Preorder Today!」 |
| Eleven Table Tennis MRC（0:29，29 鏡頭） | 0s 真人戴頭盔，之後被合成進虛擬房間（外接攝影機＋綠幕） | 結尾 logo 約 3 秒 | 無字幕 | logo |
| Demeo 上市預告（2:20） | 0–6s 工作室 logo，10s 才有第一刀；真人玩家桌旁的實拍 | 1:37 | 單詞字卡「Descend into the dungeons／Survive together／Dungeon crawler／All rolled」，筆觸發光的襯線字（字型名稱為【推測】），一次一張 | 「DOWNLOAD NOW」 |
| PokerStars VR（PS VR2 發行預告，1:03，45 鏡頭） | 0–3s ESRB 評級卡（平台要求） | 0:57 | 底部小字法律聲明 | PlayStation 標語 |
| Vegas Infinite（1:22，71 鏡頭） | 0s logo，5s 起每約 0.8 秒一刀 | 0:00 與結尾 | 單字字幕「BE／MAKE／PLAY A BUNCH」 | 平台 QR code |

> 以上「標題出現秒數」是我從接觸表（取樣間隔約 3–7 秒）估的，誤差約 ±3 秒。

### 2025 得獎的 submission 裡，對 Tell 最有參考價值的幾支

- **Chores.gg（PCA 特別獎）**：47.8 秒、7 個長鏡頭，家裡不同房間（臥室、廚房、客廳）連續示範，**用「換不同房間」傳達「在你的房間裡」**。
- **Tiny Golf（Best Casual 冠軍）**：46 秒、真實客廳、**零字幕零標題卡直到結尾**，全靠手指動作讓人看懂。
- **HandCraft XR（Judge's Choice）／Le Dino Labo（Casual 榮譽獎）**：底部字幕寫「利益」而不是「功能」（"Build a tiny festive world… with just your hands"、"Build a dinosaur… bone by bone."、"Relax. Create. Enjoy."）。
- **Final Throwdown／Sumo Boxing（Judge's Choice）**：真人第三人稱＋ 「PROTOTYPE FOOTAGE」標籤，**誠實標示原型**，符合 2026 規則「honestly represents」的方向。

---

## 2. 比賽規則與評審標準（【文章】，皆為官方規則頁原文）

### 2026 版（Meta VR Start Developer Competition 2026）
來源：https://start-developer-competition-26.devpost.com/rules （我用 `curl -sL -A <browser UA>` 取得 HTML 並轉文字，存為 `rules26.txt`；WebFetch 對該站回 403，curl 可用）。

- 日期：Entry period 2026-09-24 10:00 PT 至 **2026-11-18 12:00 PT**。
- 影片：「Demonstration video of your Project that should be **less than three (3) minutes** and showcase footage of your Project **as viewed on a Meta Quest device** or via XR Simulator or another equivalent emulator. The video must be uploaded to and made publicly visible on **YouTube or Vimeo** … Judges are not required to watch beyond three minutes.」
- 語言：整個作品須英文，或附英文字幕；非英文須提供英文翻譯。
- 評審：Stage One pass/fail（是否符合主題、是否用到必要功能）；Stage Two 四項**等權重各 25%**：Innovation & Creativity／Experience Design／Technical Implementation／Polish & Presentation。評審團含 Meta 人員或第三方，**「AI tools may also be used to assist in judging, but final decisions will be made by human judges」**（【推測】：AI 輔助看影片時，清楚的字幕比純畫面更容易被讀到）。
- 與影片直接相關的句子（Polish & Presentation 欄）：「high-quality submission materials (text, images, video) that effectively communicate the project's value. **On your video/trailer: show real gameplay that honestly represents the experience. Lead with the story and what makes your project distinct. Don't lean on AI-generated video to carry the pitch.**」
- Experience Design 欄：「Passthrough should be purposeful: the player's real room meaningfully changes the experience, not a scene that plays the same with passthrough off.」另有 FoV-aware design（重要 UI 放在窄視野內）。
- Technical Implementation 欄：min 60 fps；使用穿透者須「correct use of scene understanding (MRUK), anchoring content to detected planes, surfaces, and room geometry, and holding up in rooms the developer never tested」。
- 對 Tell 有機會的獎項：**Best First Five Minutes**（「how fast the experience explains itself, teaches controller-free interaction without a wall of text, and delivers a payoff before the user takes the headset off」）、**Boldest Original Concept**（「couldn't exist anywhere else… would fall apart on any other platform」）、**Best Accessibility Forward**（含「voice alternatives for hand interactions」，Tell 的語音下注／對話可算）、**Best Agentic Interaction**（AI 是體驗核心、voice-first）、Judge's Choice。
- 資格：必須 hands-first，「fully usable with hands, end-to-end」（來源：https://www.uploadvr.com/meta-launches-1-million-competition-for-hands-first-apps-ahead-of-vr-glasses/ ，WebFetch 擷取）。
- 我**沒有**查到 2026 版有「影片需幾支」「必須有旁白」之類更細規定；上面是全部。

### 2025 版對照
來源：https://start-developer-competition.devpost.com/rules 。影片規定同上（<3 分鐘、Quest 實機或模擬器、YouTube 公開）；四項等權重 25% 相同；2025 版 Polish & Presentation 只寫「video quality meets the stated submission requirements」，沒有 2026 版那句「honestly represents」。

---

## 3. 專家與官方文章的建議（【文章】）

| 來源 | 重點（附原句或轉述） |
|---|---|
| Derek Lieu，*More Trailer Editing Is Not Better Trailer Editing*（https://derek-lieu-yrd2.squarespace.com/blog/2023/4/23/more-trailer-editing-is-not-better-trailer-editing） | 「MORE editing does not mean BETTER editing」；「There is no average shot length that makes your trailer objectively better or worse」；「Don't cut fast because you think it will make it look more like a 'real' trailer」；寧可「simple editing with clear and intriguing gameplay」也不要「flashy editing with confusing gameplay」；沒有口白與過場動畫的遊戲，剪輯「can (and probably should be) much more simple」。 |
| Derek Lieu，*Elevating Your Editing Baseline*（https://derek-lieu-yrd2.squarespace.com/blog/2020/7/14/elevating-your-editing-baseline） | 對白時要壓低音樂，沒壓是「one of the surest signs of a less experienced editor」；避開 cross zoom／cube spin 等「video filter festival」，「The effects draw attention away from the game!」；字卡與畫面要一致（See & Don't Say：寫「BOSS BATTLES」就要接 boss 畫面）；資訊不要塞滿，把每個鏡頭削到「until removing any more would make it lose its meaning」。 |
| Derek Lieu，*The Trailer Line of Rising Action*（https://www.derek-lieu.com/blog/2018/9/16/the-trailer-line-of-rising-action） | 六段：Cold Open（鉤子）→ Introduction（前提）→ Escalation（複雜化）→ Twist（轉折）→ Climax → Button（結尾一擊或笑點）；「For a 90 to 150 second trailer, these sections don't need to be long」；用「accents」（短促的視覺或聲音亮點）維持節奏，且 accent 要與剛說的話有關；不要從最弱排到最強的平線，要有高低起伏。 |
| Derek Lieu，*The Matrix Is a Trailer Editor's Dream*（https://www.derek-lieu.com/blog/2017/9/10/the-matrix-is-a-trailer-editors-dream） | Cold open 要「hit the ground running」且自帶脈絡；全程高能量的預告「never feels high-energy」。 |
| Derek Lieu，*Professional Video Editing Tidbits*（https://derek-lieu-yrd2.squarespace.com/blog/2024/5/5/professional-video-editing-tidbits） | 音訊不要硬切，片段頭尾加短淡化；遊戲片段間用約 4 格轉場；對白起點壓低音樂約 4 格、對白結束後約 20–30 格慢慢回復音量。 |
| Derek Lieu 影片《How to Make a Movie Trailer - Editing 3-Act Trailer Structure》章節（用 `yt-dlp --skip-download --print description` 取得） | 章節：0:00 簡介、0:19 Line of Rising Action、0:44 Cold Open、1:29 Act 1、2:17 Act 2、3:18 Act 3、4:31 The Button、5:13 Recap。**我只拿到章節，沒有聽內容**。 |
| Meta，*Capture MR and VR Apps for Publishing*（https://developers.meta.com/horizon/resources/video-capture-mr-vr/） | 單人遊戲通常只能第一人稱；頭盔內「Image stabilization」設 **Low**；剪輯時可一鍵穩定，但重度穩定會裁掉很多畫面；「be sure to use slow head movements」；原生擷取 36 fps 可變、20 Mbps；Meta VR Developer Hub 最高 2160p 60 fps；用 Quest 3 擷取 MR 品質最好。 |
| Meta，*Why Mixed Reality Capture is Effective: Q&A with Scout House*（https://developers.meta.com/horizon/blog/why-mixed-reality-capture-is-effective-qa-with-scout-house/） | 2020-12-21 至 2021-01-11 的 A/B 測試：MRC 預告（顯示玩家本人）勝過純 POV 預告，OhShape 購買轉換 +30%、Holopoint +25%（**資料已過時，且是商店頁預告，不是比賽評審**）；建議「Open with MRC footage」；MRC 機位鎖在三腳架，後製加模擬運鏡增加動感。 |
| Road to VR，*VR Design Unpacked: The Formula for Great VR Game Trailers*（https://roadtovr.com/inside-xr-design-make-great-vr-trailer-without-mixed-reality-capture/2/） | 三原則「have a hook, show don't tell, tell a story」；用**平滑的旁觀者鏡頭**錄（等於 steadicam）；60 fps 錄製且遊戲要穩 60+；暗場景要提亮；以 4K/60/100 Mbps 錄製再下壓；手在鏡頭前造成比平面遊戲更多晃動。 |
| Meta Developer Blog 專欄 *Bringing a Trailer to Life for VR with Defense Grid 2*（https://developers.meta.com/horizon/blog/guest-blog-bringing-a-trailer-to-life-for-vr-with-defense-grid-2/） | 把頭盔綁在腳架／搖臂上做平穩運鏡（約 80% 素材用搖臂）；2020 年前後文章，**需要外部器材，Tell 目前沒有**。 |

**查不到的**：Meta 官方的「比賽影片」專屬製作指南（只有商店預告規格，如 Trailer 不超過 2 分鐘，且 Tell 比賽影片不是商店預告）；Derek Lieu 關於「前 10 秒」的明確文字（我只找到泛用的「前 10 秒很重要」文章，不是他說的，所以不引用）。

---

## 4. 共通手法排序（支數）

分母：A = 28 支 2025 得獎 submission；B = 13 支商業／得獎預告。支數是我看接觸表（取樣間隔約 3–12 秒）＋量測得到的，**可能漏掉短暫畫面**，請當「約略下限」。

| 排名 | 手法 | A（28） | B（13） | 證據類型 | 對 Tell 的意義 |
|---|---|---|---|---|---|
| 1 | 全程有連續音訊墊（中位 RMS 高於 -35 dB） | 26 | 13 | 【數出】（分不出音樂或遊戲音） | 不要有「靜音的畫面」；A 組僅 MarketView、Lasertag 近乎靜音／低音量 |
| 2 | 結尾標題／logo 卡（或開場 logo） | ≥18 | 13 | 【親眼】 | 一定要有，長度 B 組約 4–13 秒 |
| 3 | 以「玩家自己的真實房間」為畫面（穿透 POV） | 19 | 6 | 【親眼】 | 這是 Tell 的本錢，且 2026 規則要求穿透要「有意義」 |
| 4 | 平均鏡頭長 ≥ 8 秒（長鏡頭為主） | 21（th0.2：15） | 0 | 【數出】 | 比賽 submission 偏長鏡頭；商業預告偏快剪 |
| 5 | 極簡字幕（約 3–8 字、講功能或利益） | ≥16 | 8（Walkabout／Spatial Ops／Demeo 兩支／Vegas Infinite／Midnight Walk／Batman 場景字／Little Critters 彈字） | 【親眼】 | Tell 必須用字幕把「雙向讀心」說出來 |
| 6 | 穿插真人第三人稱（玩家在房間裡的實拍） | ≥5（Inkphony、Pack Attack、Lasertag、Final Throwdown、Little Critters） | ≥6（Demeo 上市、Eleven、Spatial Ops、Starship Home、Laser Dance、Little Critters 上市） | 【親眼】 | 需要外部攝影機；Tell 目前沒有，見第 8 節替代手法 |
| 7 | 第一個畫面就是實機（不先放工作室 logo／評級卡） | 多數（未逐一數；Saber Punks、Dunjun、Magic Wonders 以 logo 開場） | 7/13；另 6/13 先放工作室 logo、評級卡或平台 logo（Demeo 兩支、Starship Home、Vegas Infinite、PokerStars、Midnight Walk） | 【親眼】 | 比賽影片沒有品牌要介紹，開場 6–8 秒放 logo 是浪費（Demeo 上市預告 10 秒才有第一刀） |
| 8 | 結尾音量下降（最後 10 秒平均比全片中位低 10 dB 以上，下限取 -60 dB） | 11 | 8 | 【數出】 | 不是必要手法（A 組多數不降），但商業預告偏好「收」的感覺 |

### Meta 自己做的 MR 預告用什麼手法（B 組 Meta 頻道：Spatial Ops、Starship Home、Laser Dance）
- 三支全部以**真人在自家房間第三人稱**為主軸（Laser Dance 全片 60 秒皆為真人＋雷射合成，**標題卡到 51 秒才出現**）。這證實 Meta 官方思路是「讓人看到玩家在自己的房間裡」。Tell 沒有外接攝影機，所以必須用第 8 節的替代手法達到同樣訊息。

---

## 5. 反面證據（哪些成功影片不符合上面的配方）

1. **完全不剪也能得獎**：Pocket Lands（Hand Interactions 特別獎）3:00 只有 1 個鏡頭；The Tarot Experience VR（Lifestyle Runner-up）2:59 只有 1 個鏡頭（th0.2 為 3）；Awesome Hand（Hand Interactions 特別獎）2:48 只有 1 個鏡頭；Inkphony 冠軍 2:57 在 th0.3 只有 2 個鏡頭（【數出】）。
2. **完全沒有字幕也能得獎**：Tiny Golf（冠軍）、Pocket Lands、Tarot、Awesome Hand、Little Critters、Dunjun 取樣畫面中沒見到後製字幕（【親眼】，可能漏掉）。
3. **畫面有明顯瑕疵仍得獎**：OpenSoundLab、Awesome Hand、Dunjun、Magic Wonders 是直式內容放進橫式畫面（兩側黑邊或紫色背景）；Magic Wonders 是手機 App 螢幕錄影；Photon 大量使用 2D 影片素材；4 支超過 3:00（【親眼】）。
4. **快剪的商業預告有效**：B 組 9/13 平均鏡頭 < 3 秒；Meta 的 A/B 測試也顯示 MRC 預告在商店頁轉換率勝過純 POV（2020–2021 資料，【文章】）。但那是賣給大眾的商店預告，不是比賽評審。
5. **重要警語（因果）**：得獎不代表影片造成得獎。比賽同時評 APK（Stage Two 的 Technical Implementation 25%、Experience Design 25% 都看 app 本身）。A 組只能證明「什麼樣的影片不會讓好 app 出局」，不能證明「什麼影片會讓 app 贏」。【推測】
6. **我沒做的對照**：Devpost 圖庫共 73 個作品（`slugs.txt`），32 個得獎，另外 41 個沒得獎作品我沒有下載分析，所以**無法說明「沒得獎的影片有什麼共同缺點」**。
7. **2026 規則更嚴**：2025 得獎影片是在舊規則下評的；2026 加入「honestly represents」「purposeful passthrough」「hands-first」，A 組不一定符合新標準，不能全盤照抄。

---

## 6. 建議的 2:30–2:50 逐段結構（目標 2:42，可彈性）

設計依據：Derek Lieu 六段結構（【文章】）＋ A 組「長鏡頭＋極簡字幕」（【親眼】）＋ 2026 規則的 First Five Minutes／honest gameplay／purposeful passthrough（【文章】）。**秒數與鏡頭長度是我的設計（【推測】）**，不是從某支影片抄的；A 組的鏡頭長度偏長、B 組偏短，我取中間：說明段 6–10 秒、升溫段 3–5 秒、高潮段 1.5–3 秒。

| 時間 | 段落（Lieu 名稱） | 內容目的 | 鏡頭長度 | 音樂狀態 | 字幕文字類型 |
|---|---|---|---|---|---|
| 0:00–0:06 | Cold Open | 一個自帶脈絡的鉤子：你自己的房間、牌桌已擺在真實桌面上、對手抬眼盯著你剛推出籌碼的手。**不放 logo、不放黑底** | 1 個鏡頭，約 6 秒（不剪） | 無旋律；低頻脈動＋遊戲環境音，整體約 -25 dB（A 組前 10 秒平均 RMS 中位數 -24 dB、B 組 -21 dB，皆【數出】，相對值而已） | 無字幕，或一句 ≤6 字的懸念句 |
| 0:06–0:14 | Title | 標題疊在穿透畫面上出現（Chores.gg 約 2.4–3 秒、Little Critters 約 2 秒就彈出標題，不是黑底標題卡）＋一句「承諾」 | 1–2 個鏡頭，各約 4 秒 | 節奏型脈動進場，約 -20 dB | 標題字＋一句利益型承諾（≤8 字） |
| 0:14–0:40 | Introduction（也是 First Five Minutes 證明） | 證明「不用控制器、不用教學文字牆」：放置牌桌→坐下→發牌→第一次手勢下注 | 3–4 個鏡頭，各 6–8 秒 | 節奏穩定，約 -18 dB | 功能→利益型短字幕（3–6 字、4–6 秒一句），例如「手勢下注」類 |
| 0:40–1:10 | Escalation A：它讀你 | 看不見的東西變看得見：你做出小動作，牌手反應；用標註說明「它注意到了什麼」 | 3 個鏡頭，各 8–10 秒 | 稍升，約 -16 dB；對白時壓低音樂（Lieu） | 因果句字幕（你…，它…）＋細線標註；標註持續 1.5–2 秒 |
| 1:10–1:40 | Escalation B：你讀它 | 反向：近看對手表情與手，你自己判斷；對你說話（語音）也是亮點 | 3–4 個鏡頭，各 7–9 秒 | 同上，對白起點壓低約 4 格、對白後 20–30 格回復 | 對白字幕（與功能字幕**不同樣式**，見第 7 節）；說話必須同時有聲音＋字幕 |
| 1:40–2:15 | Twist | 轉折：你唬牌、對手識破（或被你騙過）；語音互動；注碼升高 | 5–7 個鏡頭，各 3–5 秒 | 逐步推高至全片最大，約 -14 dB | 對白字幕為主，功能字幕停掉 |
| 2:15–2:30 | Climax | 攤牌＋反應，剪速最快 | 5–8 個鏡頭，各 1.5–3 秒 | 最高點；然後在攤牌瞬間「斷音一拍」（accent，【推測】） | 幾乎無字幕 |
| 2:30–2:42 | Button + End card | 最後一句對白或一個反應當結尾（Lieu：button），再接 logo＋一句話＋「Quest 3 · hands-only」類平台資訊 | 1 個鏡頭約 4 秒＋ end card **約 5–8 秒**（B 組 4–13 秒；Chores.gg 約 5 秒、Little Critters 約 7 秒） | 最後 3–5 秒淡出到靜音（B 組 8/13、A 組 11/28 的最後 10 秒明顯變小聲，【數出】） | 標題／logo／平台字樣 |

**彈性**：要壓到 2:30 就縮短 Twist（砍到 25 秒）；要拉到 2:50 就延長 Escalation B。**不要超過 2:59**；規則只保證評審看到 3:00。關鍵的獎項訴求（Boldest Original Concept、Best Accessibility Forward 的語音）務必在 2:00 前出現，因為評審「不必」看完。【推測】

**影片裡必須讓評審看見的 2026 評分點**（逐條對應規則）：
- 穿透有意義：至少一個鏡頭讓真實家具（桌面、椅子）與牌桌／牌手互動（遮擋、擺放在真實平面上），而不是「關掉穿透也一樣」。
- Hands-first：全片找一個沒有控制器的畫面（手勢下注），並讓字幕說出「no controllers」這件事。**Skully Survivors 的做法**：開頭 0 秒就打出「No controllers. Just your hands.」（【親眼】，該片標題標示為 2026 競賽作品）。
- Honest：不插入 AI 生成影片；若有原型功能，像 Final Throwdown 那樣標「PROTOTYPE FOOTAGE」。

---

## 7. 字體與動畫質感規範

**語言（硬性）**：2026 規則要求「Projects must be entirely in the English language or include English subtitles」，非英文材料須附英文翻譯。**影片內所有字幕一律英文**；若牌手說中文，需另附英文字幕。（【文章】）

觀察到的樣式（【親眼】）：

| 影片 | 樣式 |
|---|---|
| HandSurvivor | 底部置中、全大寫、白字、半透明深色圓角底板，3–6 字，每句約 4–8 秒 |
| HandCraft XR | 底部置中、句首大寫、白字帶陰影，寫「利益」句（"Relax. Create. Enjoy."） |
| Inkphony submission | 左下、小白底黑字標籤（不搶畫面） |
| Falcon | 底部青綠底板＋白色全大寫 |
| Little Critters 上市預告 | 彈出的綠色潑漆底字，與遊戲美術同一風格 |
| Spatial Ops | 科幻等寬字加光暈，傳送門動畫進場 |
| MORPH | 細緻淺色字，慢淡入淡出，置中偏下 |
| Photon | 動態字（kinetic type）逐詞出現，是 A 組裡動畫最重的 |

**建議規範（【推測】，依上面觀察與 Lieu 建議整理，並非引用）：**

1. 一套字體家族（無襯線，建議兩種字重：Regular／Bold）。全片最多 3 種文字樣式：(a) 功能字幕、(b) 對白字幕、(c) 標註／標籤。三種外觀要明顯不同，避免評審分不出「誰在說話」與「這是系統說明」。
2. 功能字幕：底部置中，**單行、≤ 8 個英文字、停留 4–6 秒**（HandSurvivor／HandCraft 實測範圍）；深色半透明圓角底板確保在任何真實房間背景上可讀。
3. 對白字幕：白字無底板、加細描邊，**與聲音同步**出現，標明說話者；音樂在對白時壓低（Lieu）。
4. 標註（顯示「牌手注意到什麼」）：細線＋短標籤，優先貼在世界座標上（Chores.gg 的「LEVEL 47」「+4 XP」就是貼在實物旁的世界內字），避免全螢幕遮擋。
5. 動畫：只用淡入淡出（約 6–12 格）與輕微位移；**禁止** cross zoom、cube spin、翻頁（Lieu：「video filter festival」）。標題最多做 1 秒的縮放＋淡入；end card 靜止 5–8 秒。
6. 配色：取遊戲本身的 1 個主色當強調色；Little Critters、Demeo、Spatial Ops 的字卡皆與遊戲美術同調（【親眼】）。
7. 安全區：文字距畫面邊緣至少 5%（手機觀看，【推測】；通用做法，本次未查證）；FoV 窄的畫面中字幕不要壓住牌手的臉。
8. 字幕易讀速度：通用字幕準則約每秒 17–20 字元（【推測／通用準則，未在本次查證】）。
9. 「See & Don't Say」：字幕寫什麼，接下來的鏡頭就要出現什麼（Lieu）。

---

## 8. 只有第一人稱素材時的替代手法

目標：用頭盔 POV 傳達「在你的房間裡」「它在讀你／你在讀它」，而沒有外接攝影機。

### 8.1 傳達「在你的房間裡」
- **換位置與物件**（【親眼】Chores.gg）：臥室→廚房→客廳→寵物碗，每個地方一個 5–7 秒長鏡頭，證明「它在你的任何房間」。Tell 可變化：不同桌面、不同椅子位置、不同光線。（需要實拍多個房間，【推測】）
- **真實物件與虛擬物件互動**（【親眼】Tiny Golf：球場放在真實腳墊旁、手指捏取；Pocket Lands：世界放在真實桌面上）。Tell：真實咖啡杯、手遮住籌碼、牌手坐在真實椅子的位置。對應 2026 規則「passthrough should be purposeful」。
- **開場「戴頭盔」鏡頭**：Chores.gg 0–3 秒、Eleven 0 秒、Little Critters 0 秒用外部鏡頭拍戴頭盔／真人站在房間。**這需要另一支攝影機**；如果能借一支手機手持拍 3–5 秒，就能補到。Meta 文件也說「單人遊戲通常只能第一人稱」，所以只有 POV 是正常的，不需要硬補。
- 如果使用者有手機可以由別人手持拍玩家：可以只拍 2–3 秒做開場或收尾，成本低，但**不是必要條件**（A 組最多 23/28 沒有真人第三人稱，以我看到的下限推算）。

### 8.2 防止第一人稱的晃動感
- 【文章 Meta】頭盔內 Image stabilization 設為 Low；動作「use slow head movements」；剪輯端穩定化可用，但重度穩定會裁掉大量畫面。
- 【文章 Road to VR】盡量用平滑的旁觀者視角；60 fps 錄製（遊戲要穩定高於 60）；暗場景提亮；錄高解析度再下壓。
- 【文章 Meta】原生擷取是 36 fps 可變／20 Mbps；經 Meta VR Developer Hub 可到 60 fps／最高 2160p。**這與 2026 規則的「min 60 fps」相關**：影片若看得出掉幀，等於自曝。
- 【親眼】Tiny Golf、Pocket Lands、Chores.gg 的長鏡頭畫面穩定、頭部動作慢，沒有可見的後製穩定痕跡（我沒有用工具量測晃動，只是看畫面）。
- 【推測】Tell 本來是坐姿牌桌，天然比站立遊戲穩；拍攝時坐定、只用頸部慢轉；每個鏡頭起手 0.5 秒穩定再開始動作；剪切點選在動作（手伸出去）中而非頭部轉動中。

### 8.3 把「看不見的東西」變成看得見
- 標註覆蓋層：用細線、小標籤指出牌手注意到的訊號（呼吸、視線、手）；每個標註停留 1.5–2 秒；一個鏡頭最多 2 個標註。（【推測】；precedent：HandSurvivor 以字幕標示「custom hand gestures」；Photon 以動態字帶出概念）
- 誠實標示：若使用開發者偵錯畫面（視線射線、特徵點），加「Developer view」標籤，比照 Final Throwdown 的「PROTOTYPE FOOTAGE」做法（【親眼】），符合 2026 的「honestly represents」。
- 玩家反應用聲音呈現：自己的語音、呼吸、籌碼聲，字幕同步（Tell 的「說話＝聲音＋字幕」原則）。

### 8.4 不靠快剪製造節奏
- 用「畫面內動作」切：伸手下注、翻牌、牌手抬眼；Lieu：「cut at a pace appropriate to the game」。
- 用 accent：一次籌碼落桌、一聲低音、一個字幕閃現（Lieu 的 accents 概念），不要用轉場特效。
- 長鏡頭中途用字幕或聲音換拍，而不是硬切。

---

## 9. 常見會扣分的錯誤（附依據）

| 錯誤 | 依據 |
|---|---|
| 影片過長或關鍵內容在 3:00 之後 | 規則：「Judges are not required to watch beyond three minutes」（【文章】） |
| 不是真實實機畫面、用 AI 生成影片撐場 | 2026 規則 Polish & Presentation 欄明文（【文章】） |
| 穿透只是背景，關掉也一樣 | 2026 Experience Design 欄（【文章】） |
| 影片中看得出掉幀、當機、錯誤 | Technical Implementation：min 60 fps、「solid and bug-free」（【文章】） |
| 非英文且沒英文字幕 | 規則（【文章】） |
| 影片不是公開的 YouTube／Vimeo 連結 | 規則（【文章】）；注意 A 組有 4 支（ZNeVG0tUZiQ、A2s7b85T348、VQT4SE5cFgw、qsvlQcJF09A）現在已私人或下架，我下載失敗（【數出】）——比賽結束後別把它設成私人 |
| 快剪遮住玩法、特效太多 | Lieu：「flashy editing with confusing gameplay」「The effects draw attention away from the game!」 |
| 對白時沒壓低音樂 | Lieu：「one of the surest signs of a less experienced editor」。Tell 以對話為核心，這條特別重要 |
| 字卡寫「功能清單」而非利益；資訊塞太滿 | Lieu（Baseline、More Trailer Editing 兩篇） |
| 字幕與畫面不一致 | Lieu：See & Don't Say |
| 影片太暗（頭盔亮度在平面螢幕上顯暗） | Road to VR：「Make sure your footage looks bright enough to be plenty visible on a flat screen」 |
| 頭部動作過快造成晃動 | Meta：「be sure to use slow head movements」 |
| 工作室 logo／評級卡佔用開場 6–8 秒 | 【親眼】Demeo 上市預告（10 秒才有第一刀）、Starship Home、PokerStars；比賽影片沒有這個需要，是【推測】 |
| 直式畫面放進橫式、兩側黑邊 | A 組 4 支（OpenSoundLab、Awesome Hand、Dunjun、Magic Wonders）有此現象但仍得獎，故為「可避免的減分」，非致命；【推測】 |
| 展示 hands-first 卻讓控制器入鏡 | 規則要求「fully usable with hands, end-to-end」；【推測】評審會看影片是否真的沒用控制器 |

---

## 10. 沒做到的事與不確定點

1. **沒聽聲音**：我是文字模型，音樂走向只能用 RMS 音量輪廓推測；分不出音樂與遊戲音；音樂風格、混音品質未評估。
2. **鏡頭偵測誤差**：ffmpeg scene 偵測會漏淡入淡出與連續運鏡，也會把第一人稱的劇烈晃動誤判成切換。已用兩個門檻對照，但數字是量級。
3. **接觸表取樣稀疏**：取樣間隔 3–12 秒，所以「有沒有字幕」「有沒有第三人稱」是下限；標題出現秒數誤差約 ±3 秒。
4. **畫質 360p**：看不出字級大小（px）、字體名稱，字體屬【推測】。
5. **沒分析沒得獎的作品**（41 件），所以沒有「失敗影片有什麼特徵」的資料；A 組有選擇性偏誤（只看得獎者），且得獎主要是由 app 本身決定。
6. **4 支得獎影片已私人或下架**（ZNeVG0tUZiQ、A2s7b85T348、VQT4SE5cFgw、qsvlQcJF09A），沒有分析。
7. **日期異常**：HandSurvivor 的 Devpost 內嵌影片（8RnPp3ORdL8）YouTube 上傳日顯示 2026-10-15，標題卻寫「Competition 2025」，推測是重新上傳或重新編輯；我沒有確認。
8. **B 組得獎狀況部分只靠 WebSearch 結果摘要**（Demeo 的 VR Awards 2021、Batman 與 Midnight Walk 的 The Game Awards），**我沒有逐一開原始頁**；其餘 B 組影片標「未確認得獎」。
9. **Derek Lieu 影片本身我沒看／聽**，只取得章節與部落格文字；他的 YouTube 內容細節未引用。
10. **使用者報名的比賽是 2025 還是 2026 版**：我依日期推測為 2026（deadline 2026-11-18），未向使用者確認。
11. **Meta 官方的「比賽影片」專屬製作指南查不到**；我引用的是商店預告與 MR 擷取文件，用途不完全相同。
12. 我**沒有**對 Tell 本身的畫面做任何檢視；第 6 節時間表需由主對話依實際素材調整。

## 附：檔案位置
- 量測原始資料：`/private/tmp/claude-501/-Users-laichanean-WebstormProject-claw-hand/dba936d2-a2e1-4020-9344-fc7606a601ab/scratchpad/trailer-research/out/*.json`（B、C 組）與 `.../out_win/*.json`（A 組）
- 接觸表：同目錄 `*_sheet.jpg`（20 格）、`*_first10.jpg`（前 10 秒逐秒）、`*_last8.jpg`（最後 8 秒）
- 規則全文文字：`.../trailer-research/rules26.txt`（2026）、`.../fe8445c1622a5c101f5600c5b781cc0b.html.txt`（2025）
- 得獎者 Devpost 對照：`.../trailer-research/dp_summary.txt`
- 下載的影片僅供分析，放在 `.../trailer-research/vids/` 與 `.../trailer-research/win/`，未轉傳。
