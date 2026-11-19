# 實作計畫與進度

> spec：`docs/specs/2026-10-12-mr-poker-design.md`。2026-10-12 更新進度（main 075b4c2）。

## M0（10/13–10/14）— 完成
- [x] 新 repo、spec、研究、Mono 測試管道
- [x] Unity 6.3＋OpenXR 1.18＋Meta 207＋URP 專案；Quest APK 建置成功
- [x] 頭盔連線（Quest 3，adb 已授權）
- [x] APK 裝進頭盔：看到 passthrough 與真實桌面
- [x] 頭盔內手部錄製工具可用（132 段錄製已入庫）

## M1 原型關卡（10/15–10/24）— 功能完成，驗收進行中
- [x] Core 規則引擎完成（含 side pot、TDA；評估器窮舉驗證）
- [x] 牌桌吸附真實桌面（MRUK）＋虛擬桌備案＋雙手校準（高度與朝向）
- [x] 手勢：捏籌碼、丟籌碼下注、敲桌過牌、推／滑／拿到棄牌堆棄牌、咪牌、All-in、攤牌翻牌
- [x] AI 角色：決策＋破綻＋眼神＋被嚇反應
- [x] 一對一完整一手
- [x] 手勢回放正確率 ≥ 90%：v2 錄製 36/40（90%）；v1 錄製 53/92（39 段因錄製時桌上位置不同而略過）
- [ ] 實機手感調校：敲桌（輕敲、張開手敲）仍不穩；咪牌／捏籌碼／All-in／棄牌剛放寬，待實機確認
- [ ] 驗收：使用者親玩 10 手，判斷「讀心有爽感、手勢準」（讀心尚未正式評估）

## M2 完整遊戲（10/25–11/03）— 大多已提前完成
- [x] 3 角色＋荷官 Dot
- [x] 完整規則、All-in 與攤牌
- [x] 荷官帶第一手（實機未驗證）
- [x] 約 8 分鐘一局、結算、存檔、回訪問候（節奏實機未驗證）
- [x] 台詞字幕、音效（GameAudio）
- [x] 看對手下注堆顯示金額與跟注額（使用者追加）
- [ ] 「全程零文字」：目前有金額標籤與字幕，與原規格不一致，需決定

## M3 打磨（11/04–11/10）— 未開始（部分提前）
- [ ] 美術與音效打磨
- [ ] 4 個角色同場時實機量測 ≥ 60 fps（只量過一對一：72 fps）
- [ ] 3 個以上房間測試
- [x] 虛擬桌備案
- [ ] FoV 檢查
- [~] 無障礙：高對比、字幕大小、慣用手、手勢靈敏度已有
- [ ] Fast Motion Mode A/B 結論

## 緩衝（11/11–11/15）
- [ ] 確認開發者是否不在

## M4 提交（11/16–11/18）— 未開始
- [ ] 實機錄影 < 3 分鐘
- [ ] Devpost 文案
- [ ] release channel 上架

## Live table talk（2026-10-14，worktree livetalk）
實機回饋：AI 只在你下注「之後」講一句，不像真人。改成你還在想、還在伸手時就開口，而且讀到的東西真的影響決策。
- [x] Gestures：PlayerHabits 即時訊號串流（回合開始、思考 3/7/12 s、再看牌、伸向籌碼、縮回、捏著籌碼、放回、伸向牌、盯著某個 AI、玩家開口）＋合成手部測試
- [x] AI：LiveTalkDirector（一次一人、整桌間隔 3 s、角色冷卻、每個觸發每個角色洗牌袋、不蓋過玩家講話與荷官資訊、教學不講；講不講＝破綻，依可信度）＋ LiveLines 台詞
- [x] AI：讀人改版（每回合的訊號 → 信念 → 有感但有上限的勝率偏移；被騙過就打折）＋自我對弈前後數據
- [x] Game：GameController.LiveTalk.cs 接上（log、手結束「Rocco read: … → …」）；Fool Rocco 改用即時系統、保留三手劇情
- [x] 台詞匯出、bake（≤ 6,000 字含重試）、verify
- [x] 全部測試（core、EditMode、PlayMode、proxy）、commit

### Review（2026-10-14）
- 訊號：合成手部測試＋真實 idle 錄製防誤報（16 段：「手放到牌上」1 個、籌碼類 20 個；調整前 57 個）。手掌判斷籌碼區與牌區（捏合點在手張開時會落在籌碼／牌上）。
- 導演：77 句（1,601 字；新 bake 70 句，其他與既有台詞同一句共用音檔）。P(有牌｜開口)：Pip 82%、Rocco 66%、Vex 48%。
- 讀人：上限 ±0.15；自我對弈（6 種子 × 20,000 手）誠實玩家 −13.5 ± 5.8 BB/100、演假破綻 AI 不學 +25.4 ± 6.9、會學 −4.0 ± 7.4。
- ElevenLabs：1,562 字（含 2 次重試）；verify 428/428 過（26 warn，新的 2 個：pip.live.held.1、vex.live.cards.2）。
- 未在 Quest 上驗證：即時台詞的頻率與時機手感、真實手部資料下伸手／縮回的誤報率（v1 idle 錄製時桌上沒畫籌碼）。

## 打磨衝刺（2026-10-14 起，硬截止 11/10：之後開發者不在；11/16–18 只交件）
目標：得獎（評審玩一次、約 3 分鐘、多半不懂撲克、在陌生房間）。核心＝雙向讀心（已認可）。
實機量測 2026-10-14：4 角色同場 72 fps、Stale 0、GPU 76%（App 8.2 ms／13.9 ms）、42°C、等級 2/2。
- [x] 牌力提示＋教學 v4（模擬 76 s；第一手被讀、第二手騙倒 Rocco；其他手勢即時教）— 實機確認 v4 啟動、提示升級、[Playtest] log
- [x] 試玩流程表 docs/playtest/protocol.md
- [x] 手勢失敗兩次後浮出按鈕備援（分支 fallback：同一種失敗 2 次或 20 s 卡住 → 桌緣 Check／Call／Fold／最小下注；Settings 可關；待實機確認誤觸與按法）
- [x] 陌生房間：persistent anchor、避牆座位、虛擬桌高度與提示（錨點存取／MRUK 待實機驗證）〔分支 room：程式與測試完成，待實機驗證〕
- [x] 即時對話收尾：同一角色這回合講過即時台詞就不補長考台詞；即時台詞排隊 > 1.5 s 或玩家已出手就丟（分支 fallback）
- [x] 視覺打磨第一輪（示範手、FoV 審查）— 實機截圖：示範手 OK；說明文字擋到荷官／Vex → 第二輪（polish2）修正中
- [x] 音效：程式合成環境音、LUFS 混音、講話時壓低（待人耳聽）
- [x] 開發版遠端紀錄（實機確認已上傳伺服器）＋ TTS 月預算保護（15,000 點）
- [x] 交件準備：隱私頁（已上線 /privacy，聯絡信箱待填）、Devpost 草稿、影片分鏡、release 清單
- [x] 語音限流改每台裝置（每台 80／日、全體暫定 400／日，待確認 ElevenLabs 額度）
- [x] 清理：關閉明文 HTTP（TELL_ALLOW_HTTP=1 給本機開發）；SelfPlay 大樣本測試改 95% 信賴區間斷言，Mono 10 萬手、Unity 2.5 萬手（分支 fallback）

### 打磨 2（2026-10-14，分支 polish2；實機截圖：咪牌字幕浮在 Dot／Vex 身上）
- [x] 重現：低眼位（桌面上 0.2 m）從眼睛穿過字幕的視線打到 Vex、Dot 的身體 → 紅燈（GhostCaptionViewTests）
- [x] 示範手字幕：貼桌（立著或平躺）、離示範手 ≤ 10 cm；45 個檢查眼位（高 0.15–0.55 m、後 0.22–0.40 m、左右 ±3 cm）看都不碰角色區域；備援按鈕出現時讓開按鈕
- [x] 備援按鈕 9×6 cm、字斜立面向眼睛、放不下就折兩行：「Raise to 20」0.91° → 1.52–1.72°
- [x] Pip／Vex 往後（SideDepth 0.75 → 0.85，頭 1.09 → 1.16 m、31.8°）；籌碼堆不放在別人手邊（TableSpots 規則，≥ 12 cm）→ Rocco 的籌碼改放內側、Rocco −0.22 → −0.26（離底池 11.7 cm）；側邊座位籌碼往桌內 3 cm；牌堆放荷官內側；對面的攤牌標題抬高到 0.15 m
  - 試過把 Rocco 移到 −0.10：看牌桌中央的視線會掃到他的胸口（注視球 0.17 m）變成盯著他，JIT／教學測試抓到，退回
- [x] 咪牌示範片段開頭：裁掉從左邊掃過來的 0.75 s（GhostSpec.ApproachRadius）
- [x] core／EditMode／PlayMode 全綠

#### Review
- 雙手 All-in 的動作範圍蓋滿整個桌緣，貼桌的字幕找不到完全乾淨的位置：用逐時刻的手判斷，48 個時刻裡蓋到 4 個。
- 字放大 1.5 倍、或備援按鈕同時出現時，Pick up／Flip／Fold 的字幕會退而求其次（距離 ≤ 12.3 cm 或蓋到手的範圍一角），log 有 `[Ghost] caption … no clean spot`。
- 平躺的字幕在低眼位（0.15–0.25 m）會被壓扁，讀起來吃力；坐姿眼位 ≥ 1.2°。
- 實機待確認：字幕位置、按鈕字大小、座位感覺、第一次擺字幕的時間（Editor 約 15–30 ms，之後快取）。

### 選單瘦身（2026-10-15，分支 menuslim；評審只玩約 3 分鐘，舊選單約 30 項）
- [x] 紅燈：各頁項目清單（主頁 6＋教學中 Skip tutorial；How to play 9；Settings 7）、Move table here（放到面前＋手印）、Hints 一個開關管兩套提示、Text size 循環、Left-handed、存檔遷移、版面與字的視角
- [x] MenuModel／WristMenuView／GameMenuActions／UxSettings／SettingsStore；TablePlacer 公開「放到面前」（RecenterPlan 同一套）
- [x] Fool Rocco、靈敏度、Fast motion 不在玩家選單（程式保留；Fool Rocco 只在開發工具場景出現）
- [x] 文件：spec §4、agent-brief、playtest protocol、video.md
- [x] core／EditMode／PlayMode 全綠＋各頁截圖

#### Review
- 紅燈 34 個（UX PlayMode 124 個裡）→ 全綠；core 814 過、EditMode 817 過（48 個 Explicit／Ignored 跳過）、PlayMode 708 過（1 個 Explicit 跳過）。
- 全套 PlayMode 抓到一個測試互相影響：別的測試載入的 Main 場景留著，「全場景找 TablePlacer」會搬到那張桌子 → 改成只用這張牌桌上的 TablePlacer。
- 字：按鈕字約 2.1°（「Assist buttons: On」縮到 1.77°），舊版 1.71°；按鈕 12.4 × 5.0／3.8 cm、How to play 8 × 5 cm（舊版 3 cm 高）。
- 實機待確認：格子版面好不好戳、Move table here 在真實桌前會不會對齊到面前那張桌子。

