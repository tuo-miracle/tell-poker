# 全面寫實化計畫（2026-10-29 使用者決定）

> 使用者決定：角色全部換寫實；Tripo 牌桌只當備用虛擬桌的材質參考。
> 工作分支：`feat/realistic-hank-preview`（worktree `vr-poker-hank`）。主 checkout 不動。
> 期限：上機工作 11/10 前完成（使用者 11/11–15 不在）。

## 名單
- 遊戲現有：3 位對手（Rocco、Pip、Vex）＋荷官 Dot。
- 角色池（未實作）：Marcus、Danny、Reginald、Jace、Erik、Nora、Hank、Leo。設定圖在 `docs/concept/2026-10-28/turnaround-p*.png`。
- 已有 3D 模型：Hank（已換臉、已上機）、荷官（Tripo，未換臉）。其餘 7 位要由使用者用 Tripo 生成（我們沒有 Tripo API key）。

## 第 1 輪（現在，平行）
- [x] A. Blender：修 Hank 的虹膜（換深色）與膚色（臉罩對齊身體、淡化接縫）→ 新 `hank-wrap.glb`
- [x] B. Blender：荷官換臉（女性 Rocketbox 頭）→ `dealer-3d/wrap/dealer-wrap.glb`，驗證圖。這一步順便驗證流程能不能套到第二個角色、套到女性臉上。
- [x] C. Unity（TDD）：轉頭看鄰座時降低頭部比例、提高眼睛比例（目前約 60°）
- [x] D. Unity：備用虛擬桌換成皮革桌緣＋綠色絨布材質（參考 `table-3d/table-turnaround.png`）

## 第 2 輪（A、B 完成後）
- [x] 新 Hank GLB 換進 Unity，荷官取代 Dot（寫實版），上機截圖，量 fps
- [ ] 把換臉流程整理成可重用的腳本（角色名稱參數化），寫下每個角色的操作步驟

## 第 3 輪（等使用者提供 Tripo 模型）
- 2026-11-02 收到 4 個：asian man in turtleneck → Marcus、black male human → Jace、bearded man → Erik、old male figure → Reginald（對應待渲染確認）。還缺 Danny、Nora、Leo。
- [x] 3a. 換臉流程參數化（一支腳本吃角色名稱與設定），先用 Marcus 跑通
- [x] 3b. Jace、Erik、Reginald 換臉（每個角色各自判斷眼睛、膚色，不照抄 Hank／荷官的參數）
  - 結果（2026-11-02）：流程在 `docs/concept/2026-10-28/wrap-pipeline/`（荷官重建位元組相同）。GLB sha1：Marcus 10fb31de9684、Jace 588eeb246734、Reginald 038bf7327411、Erik af45607316f3。總表 `round3-summary.jpg`。殘留：Marcus 鏡腳殘段、Jace 閉眼淡圈、Reginald 左眉小台階、Erik 眉上方塊接縫、下排牙只露邊。
- [x] 3c. 4 位接進 Unity 預覽、上機截圖、量 fps（同桌最多 4 位寫實）
  - 結果（2026-11-02）：GLB Marcus 54fba3324d04、Jace 13b398128582、Reginald d4b62ff9896e、Erik 68ff8a5a7e16。陣容 `debug.tell.lineup`（預設 Marcus,Hank,Jace）。寫實側邊座位往後/往玩家移（SeatSpec）。貼圖經 `RealisticBake` 轉 ASTC（換 GLB 後要重跑 bake）：GL 記憶體 1196→775 MB、APK 152→101 MB。寫實蒙皮改 Bone4（Android 預設 2 骨）。全寫實一桌 72.6 fps（最低 72）。PlayMode 833/0、EditMode 817/0、core 814/0。
  - 殘留：Reginald 看左下巴下小暗缺口（Reginald 頭限 22°、胸 25° 已緩解）；Erik 右頰、Jace 太陽穴側面接縫；Marcus 鏡腳鋸齒；寫實角色仍用原座位卡通角色的聲音台詞。
- [ ] 其餘 3 位（Danny、Nora、Leo）等模型
- [ ] 角色池功能（選對手 UI、規則參數、Gemini 台詞）

## 停損
- 同一步驟第 3 次失敗就停下回報。
- 荷官換臉若臉型明顯不像原模型 → 停，回報。

## 第 2 輪結果（2026-10-30，第六次上機）
- 模型：Hank.glb ca61feabb68b、Dealer.glb b50e464ee209；未 commit；頭盔上是預覽版。
- 72.6 fps（最低 72，無真人手）；PlayMode 786／0 失敗、EditMode 817／0、core 814／0。
- 暫時凍結這兩個角色。未解：Hank 看桌面時虹膜灰藍反光（原因未驗證）、荷官男聲（ElevenLabs key 失效，設定已改 Sarah，音檔未生成）、截圖黑條（疑似 app 外）、看鄰座轉臉 46–50°。
- 待使用者：新 ElevenLabs key、其餘 7 位 Tripo 模型、Pip/Vex 亮度、重開頭盔。

## 第 4 輪（2026-11-02 使用者回饋：桌太小擠在一起、荷官與玩家重疊；男角女聲、荷官男聲）
使用者決定：家用標準桌（外框 2.13×1.07 m，84×42 吋，長圓形、荷官缺口在遠端長邊中間）；座位左右對稱並**加一位對手**：
左端 Pip 座、荷官左側 Rocco 座、荷官（遠端長邊中央）、荷官右側新座、右端 Vex 座、玩家在近端長邊中央；每個寫實角色有自己的聲音。
- [x] 4a. 桌子與座位（Unity，TDD）：絨布尺寸、長圓形虛擬桌、5 個座位、虛擬桌放置、桌面擺放常數、SeatingPlanner、相關測試更新
- [x] 4b. 第 4 位對手：遊戲邏輯支援 5 人、新座位與個性（暫定「老手」：緊凶、話少、冷幽默），台詞，預設陣容 Marcus,Hank,Reginald,Jace
- [x] 4c. 每個寫實角色自己的聲音：Unity SpeechBank/SpeechChannel、voice proxy、bake 都改成依角色；Hank 沿用 Callum 不重錄
- [x] 4d. 拿到新 ElevenLabs key 後重錄（荷官 Sarah 2439 字＋Marcus、Jace、新座位角色），先估額度再跑
- [x] 4e. 上機驗證：座位不重疊、轉頭看得到兩端、72 fps、聲音性別正確
  - 結果（2026-11-03）：絨布 1.89×0.83 長圓、外框 2.13×1.07、荷官缺口 0.5 m；5 座相鄰身體距離約 0.72 m，兩端約 ±55°。只有寫實預覽開 5 人（正式卡通版維持 3 人）。新座位 Silas（Veteran）由 Reginald 坐。教學支援 5 人（按鈕在倒數第二座）。泡泡改 `BubblePlacement.Place`（不蓋頭、歸屬說話者）。聲音：Marcus=Eric、Hank=Callum、Jace=Liam、Reginald/Silas=Bill、Erik=Brian、荷官=Sarah；bake 6,340 字元。中繼已部署（伺服器 IP 改 192.168.1.249，部署腳本補 restart api）；`build-quest.sh` 支援 `SECRETS_DIR`（worktree 建置指向主 checkout 的 .secrets）。實機 72 fps、每人播自己的聲音、`preview-r4d.apk` 在頭盔上、伺服器收到該 session 遙測。PlayMode 882/0、EditMode 874/0、core 871/0。
  - 殘留：Vex 座泡泡偏低；卡通正式版只在 Editor 驗扶手；教學只在實機驗到第一步；遊戲內名字仍用座位名（Pip/Rocco/Silas/Vex）而非寫實角色名；即時對話未實際對頭盔講話驗證。

## 第 5 輪（2026-11-03 使用者：「左下角還有 Fold, Call 啥的按鈕全部移除」「選單也幫我美化」）
- [x] 5a. 移除手勢備援按鈕（Editor 截圖先確認左下角就是 FallbackAssist／AssistButtonsView，`Logs/scratch/round5/before/assist_buttons_seated.png`）
  - 刪：`GameController.Assist.cs`、`AssistButtonsView.cs`、`FallbackAssist.cs` 與 4 個測試檔；GameController／Hints／Meta 的呼叫點；選單 Settings 頁的「Assist buttons」項目、`UxSettings.AssistButtons`、UxController 的套用。
  - 舊存檔裡的 `AssistButtons` 欄位照樣讀得進來（JsonUtility 略過），再存一次就消失（`SettingsStoreTests.OldSaveWithTheRemovedAssistButtonsField…`）。
  - 新測試 `NoAssistButtonsTests`：兩次敲桌失敗＋卡住 30 秒都不出按鈕、沒有 `[Assist]` log。教學、示範手、手勢本身不動。
  - 遙測白名單的 `[Assist]` tag 留著（`DevTelemetry.cs` 在語音那邊的範圍，不再有人印這個 tag，無害）。
- [x] 5b. 手腕選單美化（只改外觀，項目與順序不變）：黃銅細框、深色皮革底、墨綠絨布面、黃銅標題牌、象牙色按鈕、深咖啡色墨水、襯線字 Libre Baskerville（SIL OFL，`Resources/Fonts/`，附授權檔）粗體；
  停留暖金、按下琥珀 0.25 s、示範中淺綠；字 ≥ 1.3°（`WristMenuView.MinLabelDegrees`）；全部不透明 Unlit、不加燈與陰影。手腕按鈕、掌上按鈕改黃銅色。
  - 截圖：前 `Logs/scratch/round5/before/before_sheet.png`、後 `Logs/scratch/round5/after/after_sheet.png`。
  - 寫實預覽版開發用 `adb shell setprop debug.tell.menu 1|settings|howtoplay|0` 叫出／關掉選單（只有場景裡有 RealisticPreview 才讀）。
- 測試：PlayMode 867／862 過／0 失敗／5 略過；EditMode 874／826／0／48；core 871／823／0／48。
- 實機（preview-r5.apk，SECRETS_DIR，已刪 VoiceClient.json）：`token set`；fps 72–73（開機頭幾秒 1、68、71）。
  頭盔追蹤丟失（房間太暗，系統跳「Finding position in room」），App 畫面沒出來，**選單的頭盔截圖沒拍到**；拍的 3 張（只有 passthrough）已從頭盔刪除，使用者的舊截圖沒動。

## 第 6 輪 b（2026-11-03 使用者：一手結束的揭曉「美術太難看了，而且文字很長，難懂」——不刪，改短改好看）
- [x] A 攤牌標題：一行白話（Strong hand／Bluffing／On a draw／Unsure／Gave up／No action），拿掉第二行（Acted the opposite 等；`HandReveal.Detail` 只留在資料裡）。
- [x] B 讀人揭曉卡：「Rocco read you: weak — right／— wrong!」，沒攤牌只寫「Rocco read you: weak」；教學「Rocco read you: nothing」；Fool Rocco「weak — wrong!／strong — right／a trick — right」。推理鏈只留在 `[Read]` log。
- [x] 外觀：`WorldLabel.Plaque`（黃銅細框、墨綠底、象牙色 Libre Baskerville 粗體；新 shader `Poker/TintedText` 讓淺色字可以畫；底板不透明 Unlit），攤牌標題、讀人揭曉卡、Fool Rocco 揭曉卡都用它。
- 測試：`RevealTextTests`（長度、單行、無推理鏈、每種情況、外觀、字 ≥ 1.3°）先紅後綠。PlayMode 871／865 過／0 失敗／6 略過；EditMode 874／826／0／48；core 871／823／0／48。
- 前後對照：`Logs/scratch/round6/after/before_after_sheet.jpg`（一般牌局 A、B、教學、Fool Rocco）。
- [x] 使用者同意並要求放大：攤牌牌子 0.0045→0.0058（約 1.65–1.74°）、讀人揭曉卡 0.0036→0.0044（約 1.76°）、Fool Rocco 揭曉卡 0.0042→0.0046；揭曉卡位置改在底池上方 16 cm（26 cm 會蓋到荷官下巴），Fool Rocco 共用同一點。
  新測試 `FiveHanded_EveryPlaque_IsBigEnough_AndCoversNoFaceCardOrOtherPlaque`（≥ 1.6°、不蓋臉／底牌／公共牌、不互相重疊）先紅（荷官兩側 1.28°、揭曉卡蓋到荷官的臉）後綠。
  PlayMode 872／866 過／0 失敗／6 略過；EditMode 874／826／0／48。
- [x] 建 `Builds/Quest/preview-r7.apk`（107,152,072 bytes，SECRETS_DIR，已刪 VoiceClient.json，已重建卡通預設場景）。**還沒裝**（使用者可能在錄影）。

## 第 6 輪 c（2026-11-04 使用者實機錄影 preview-r7 的兩個 bug）
- [x] 1. All-in 提前揭曉。根因：翻牌圈 all-in 後，攤牌、轉牌、河牌、分池、一手結束這批事件一起到。動畫兩三秒就跑完，荷官的話（「We have an all-in!」×2、「The turn.」、「And the river.」、「Pot goes to …」）排在後面才講。另外「Rocco read you」揭曉卡和結算看板都在 EnterHandOver 那一步，同一幀出現。
  - 修法：
    - all-in 跑牌時，每條街等荷官講完（`DealerVoice.Busy`，最多 4 s）才發牌，發完停 0.6 s。
    - 揭曉卡改在 HandEnded 那批事件裡先出（`_liveRevealDone` 防重複）。
    - all-in 跑牌或這一局最後一手時，等荷官講完分池那句才進 EnterHandOver（結算）。一般的手節奏不變。
  - 測試：
    - `AllInRunoutOrderTests.AllInOnTheFlop_DealerCallsTheRunout_BeforeAnyResultShows`：紅時第 5 張牌 611、牌子 644、看板 710，「The turn.」620 才開始講，看板前沒講分池。綠時 turn 620 → river 810 → 第 5 張牌 926 → 牌子／分池 1003 → 看板 1103。
    - `LiveTalkFlowTests.LastHand_TheReadRevealComesBeforeTheSummary`：紅時揭曉卡與看板都在 354，綠時 354 → 423。
- [ ] 2. Tells read 永遠 0：**照指示停下回報，沒改程式**。
  - 定義（`ReadJudge`）：AI 下注或加注後玩家回應。AI 是價值注時，玩家棄牌才算對；AI 是虛張或半虛張時，玩家跟或加才算對；拿不定的注不算。跟盯人、被盯回、輸贏都無關。
  - 模擬（5 人桌，GameSession＋GameStats，6 局）：
    - 一直跟：18 次對／90 次（72 次是價值注）。
    - 一直棄：49／57。
    - 隨機：61／121。
  - 結論：邏輯算得到，但 AI 的注八成是價值注。玩家常跟注，所以 0 of N 很常見，而且玩家想的意思（讀牌）和這個定義（棄掉價值注）不一樣。等使用者決定定義。
- 測試：PlayMode 874／868 過／0 失敗／6 略過；EditMode 874／826／0／48。
- [x] 建 `Builds/Quest/preview-r8.apk`（107,158,252 bytes，SECRETS_DIR，log 有「通行碼已寫入 VoiceClient.json」，建完已刪 VoiceClient.json 與 .meta，已重建卡通預設場景）。**沒裝**（使用者在錄影）。

## 第 6 輪 d（2026-11-04 lead 代使用者決定：已知問題自己處理完再找使用者驗證）
- [x] 1. 結算看板拿掉「Tells read」：`GameSummary.Lines()` 只剩 Hands won、Biggest pot，看板也只留 2 行（`_sumLines` 3→2，行距 4.6 cm 不變）。ReadsCorrect／ReadsTotal 照算，存檔照記。
  - 新增 `TableProps.SummaryRows` 給測試量排版。
  - 紅：GameStatsTests 還是 3 行；MultiwayGameTests 看板文字含「Tells read: none yet」。
  - [x] lead 決定一起拿掉：Dot 回訪開場白不再講「tells read N%」，一律講「Welcome back! N wins in M so far.」。這句原本就有預錄片段（welcome_back＋數字＋wins_in／win_in＋so_far），沒有新音檔，沒呼叫 ElevenLabs。
    - 測試：`Greeting_NoLongerQuotesTellsRead_AndIsStillVoiced`（含 `SpeechBank.CanSay("dot", …)`）先紅（實際是「…, tells read 62%.」）後綠；`ProgressStoreTests.Greeting_FirstTime_vs_Returning` 改成不含 %。
- [x] 2. `debug.tell.firstrun`（`Runtime/Table/FirstRunReplay.cs`，只在寫實預覽版的 Android 實機讀）。
  - 規則：一個值只生效一次，用掉的值記在 PlayerPrefs `Dev.FirstRunReplay.consumed`；要再來一次就換值（2、3…），設 0 清掉。
  - 效果：教學從頭、Dot 說 FirstHello；TableAnchorFlow `Begin(ignoreSaved)` 不找舊錨點、直接放置（出雙手手印），舊錨點不刪。
  - 保留：存檔（完成紀錄、統計）與 UX 設定都不動。
  - 紅：TutorialVersionTests 1 個、TableAnchorFlowTests 4 個（3 個 Decide 案例＋1 個錨點）。
- 測試（含開場白修改）：PlayMode 884／878 過／0 失敗／6 略過；EditMode 874／826／0／48；core 871／823／0／48。
- [x] 重建 `Builds/Quest/preview-r9.apk`（107,159,548 bytes，含開場白修改，SECRETS_DIR，log 有「通行碼已寫入」，已刪 VoiceClient.json 與 .meta，已重建卡通預設場景）。
