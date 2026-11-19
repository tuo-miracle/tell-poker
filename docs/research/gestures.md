# Quest 3/3S MR 德州撲克：手勢研究筆記

研究日期：2026-10-12。只做研究，沒有改任何專案檔案。

標記說明：
- **【文件】**：來源直接寫的（附編號）
- **【推測】**：我根據來源推出來的設計判斷，沒有直接證據，要實機驗證
- **【查不到】**：搜了但沒找到可靠來源

來源列表在文末（S1–S33）。

---

## 0. 總表：手勢 → 偵測方式 → 意圖條件 → 主要風險

| 手勢 | 偵測方式（建議） | 意圖條件（何時算數） | 主要風險 | 優先度 |
|---|---|---|---|---|
| 挑籌碼（捏起 N 枚） | HandGrab pinch（S6）抓籌碼堆的「虛擬把手」；數量用捏的位置與停留時間決定（例如手指在堆上的高度對到第 k 枚），**不是**靠逐枚物理碰撞 | 捏合成立後，手離開堆一段距離（例如 > 3 cm）才確定 N，並在手上方顯示數字（PokerStars VR 的做法，S16） | 指尖誤差約 1 cm（S20），籌碼厚度約 3 mm【推測：實體籌碼常見規格】，逐枚選取不可行；pinch 時其他手指捲曲會降低精度（S9） | **先做** |
| 用拇指分開一疊 | 不建議做成真實物理；可做成「捏住後拇指往旁邊滑＝減少一枚」的離散手勢，用 JointVelocityActiveState（S5）偵測拇指橫移 | 每次滑動 −1 枚，有冷卻時間 | 拇指在手掌下方時被遮擋（S3、S8）；誤差比籌碼厚度大很多 | 延後 |
| 丟／滑／放籌碼進底池＝下注 | 被抓的籌碼 Grabbable 帶 Throw when unselected（S6）；釋放後追蹤落點 | 籌碼「越過下注線且已放開且靜止（或離手 > 0.3 s）」才計入；計入前有 1–2 秒可撤回窗（把籌碼拿回線內就取消）【推測】；最後由「確認」動作（例如手離開下注區或敲桌）鎖定 | 釋放速度有雜訊；FMM 會增加抖動（S2）；丟太大力籌碼散落 → PokerStars VR 的做法是落地後系統自動收進底池（S16） | **先做** |
| 推 All-in | 雙手掌 ShapeRecognizer（手指張開、掌心朝前或朝下）＋ TransformRecognizer ＋ 雙手 JointVelocityActiveState 往前（S5），用 Sequence 串起來 | 雙手同時在自己籌碼堆後方 → 同向往前移動超過 X cm、越過下注線 → 停住；觸發後跳出明確的「ALL-IN」確認並保留撤回窗 | 雙手若靠太近會互相遮擋，Meta 明說重疊越多精度越低（S3）；「推」與「丟籌碼」可能混淆 → 需要「雙手都在籌碼堆旁」這個門檻 | **先做**（PokerStars VR 也有這個 push 手勢，S15） |
| 咪牌（手掌蓋牌、拇指掀角） | **不要靠手指關節精準偵測**。做法：手掌在牌上方（hover 區）＋ 掌心朝下（TransformRecognizer PalmDown，S5）→ 進入「咪牌模式」；掀角量由手腕相對牌的高度或手腕俯仰角連續驅動 | 牌角翹起只有自己看得到；手離開或掌心翻開就放回 | **最高風險**：掌心朝下時手指被手掌擋住，Meta 明說要避開「手掌擋住手指」的姿勢（S9、S10）；手太靠近桌面時追蹤精度不明【查不到官方數據】 | 先做簡化版 |
| 翻牌（攤牌） | 抓牌用 HandGrab 的預設姿勢吸附（Demeo 的做法，S18）；翻面用 JointRotationActiveState 偵測手腕旋前／旋後（S5） | 攤牌階段才開放；手腕旋轉超過約 120° 且牌在桌面上方才算翻開【推測】 | PokerStars VR 玩家抱怨「想看牌時不小心把牌翻給別人看」（S17）→ 翻牌只在攤牌階段開放 | **先做** |
| 生氣摔牌＝棄牌 | 抓著牌時，手往下的速度超過閾值（JointVelocityActiveState，world down，S5）＋ 在桌面平面附近放開；或抓著牌時大力往前丟 | 抓著自己的牌 + 往下或往前的速度 > 閾值 + 放開 → 棄牌；強度依峰值速度分級，觸發不同的音效、粒子與牌的彈跳 | 30 Hz 預設頻率對快速動作採樣不足（S1）；FMM 能到 60 Hz，但會增加抖動、在低光下退化（S1）；手出鏡頭後追蹤會掉 → 速度要用釋放前幾幀的峰值，不能只用釋放那一幀 | **先做**（但不要依賴 FMM） |
| 把牌蓋回去／推向前＝棄牌 | 牌被抓著或被手推著，移動到「棄牌區」（下注線外側），或手掌壓在牌上往前推 | 牌越過棄牌線＋放開；有 1.5 s 撤回窗【推測】 | 與「推籌碼」混淆 → 用「手上有沒有牌」來區分 | **先做** |
| 敲桌＝過牌 | 手腕往下的速度尖峰 + 手位置接近桌面平面（MRUK TABLE 平面，S24）+ 手勢為拳頭或指尖朝下 | 一次（或兩次）往下的速度尖峰，且在 Δt 內手停在桌面高度 ±2 cm；只在輪到自己且沒有下注時有效 | 指尖接觸判定誤差約 1 cm（S20、S21），只能靠速度尖峰，不能靠接觸；敲桌也可能是無意間的動作 → 用「輪到你」的提示當閘門（S10） | **先做** |
| 秀一張牌／籌碼花式 | — | — | PokerStars VR 也說秀單張要練習（S16）；籌碼花式連 PokerStars VR 都沒做（S16） | 延後 |

---

## 1. Meta Interaction SDK 現成元件（最新版本：Unity v207.0，2026-09-22，S4）

v207 主要新增眼動（Gaze）互動，**另外新增了一個「MR Tabletop」範例場景**，示範混合實境中手與控制器的最佳做法，例如遊戲區域的擺放與方向（S4，【文件】）。我沒能看到這個場景的實際內容（GitHub README 沒列出場景，S29）→ **建議第一件事是把它拉下來看**。

| 元件 | 文件說的 | 對應撲克手勢 | 限制 |
|---|---|---|---|
| **Hand Grab**（HandGrabInteractor／Interactable） | 不用物理的抓取，專為手設計；分 Pinch 與 Palm 兩種，每根手指可設為 Required／Optional／Ignored；放開模式有 All Released 和 Any Released；可用 HandGrabPose 讓虛擬手吸附到預設姿勢（S6，2026-08-26 更新） | 挑籌碼（pinch）、拿牌、翻牌 | 物件的 scale 建議是 1（S6）；文件沒寫最小物件尺寸【查不到】 |
| **HandGrabUse** | Interactor 把每根手指的「使用強度」（0–1）傳給 interactable，可設死區 StrengthDeadzone（S7） | 咪牌時「拇指掀角」的連續量可以拿來試；捏籌碼時的力道 | v207 的 WhenHandGrabEnded 事件不會觸發，要改用 WhenStateChanged（S7，【文件】）；手掌朝下時拇指的強度可靠度未知【推測：會受遮擋影響】 |
| **Touch Hand Grab** | 用手指上的球體與物件碰撞體重疊來選取，手指會貼合物件表面；抓住時記下手指與拇指的距離，手指張開超過這個距離就放開；**標為實驗性**（S11、S12，2025-11-03） | 抓籌碼堆、骰子類的小物件；Demeo 用它抓棋子與骰子（S18） | 實驗性，可能有效能問題或視覺瑕疵；大物件可能「黏在手上」；凹形物件要拆成多個凸包（S11）；**不適合薄牌**——Demeo 的牌刻意不用碰撞體，改用「靠近偵測＋HandGrab 吸附姿勢」（S18） |
| **Distance Hand Grab** | 抓手臂碰不到的物件，預設把物件吸到手上（S13） | 撿起滑太遠的籌碼、撿掉到地上的牌 | 桌邊互動用不太到；可能和桌上的近距離抓取互搶目標【推測】 |
| **Throw** | Grabbable 的 Throw when unselected 預設為 true；有 Velocity factor 和 Angular velocity factor 可調；舊的 IThrowVelocityCalculator 已標為過時，改用 Grabbable（S6、S14）；v83 加了更多投擲客製選項與投擲範例場景（S2b） | 丟籌碼、摔牌 | 有人回報用 OneGrabPhysicsJointTransformer 用力揮動時物件會飛走（S31，論壇，沒有官方回覆） |
| **Poke** | 指尖跨越 ISurface 連續兩幀算選取，且只接受從表面上方來的按壓；搭配 SyntheticHand 與 HandPokeLimiterVisual 可把手腕鎖住，讓手指不穿過表面（S19） | 敲桌（用桌面平面當 ISurface）、按虛擬下注確認鈕 | 文件沒提無邊界的平面或桌面用法（S19）；Meta 只建議用食指 poke，其他手指因為自我遮擋精度較差（S10） |
| **Hand Pose Detection** | ShapeRecognizer：手指的 curl、flexion、abduction、opposition 規則；TransformRecognizer：PalmDown、PalmUp、WristUp 等方向；JointVelocityActiveState 和 JointRotationActiveState 分別偵測沿某軸的速度和角速度，都有 min time 與 threshold width（遲滯）；Sequence 把多個狀態依時間串起來，每一步有最短啟動時間與最長步驟時間；ActiveStateGroup 做 AND（S5，2026-03-13 更新） | 推 All-in、摔牌、敲桌、咪牌模式的進入條件 | 只能偵測形狀和速度，沒有「物件狀態」→ 意圖判定要自己寫一層規則 |
| **Synthetic Hands** | 可以覆寫並鎖住手腕與手指關節，平滑地在追蹤資料與目標姿勢之間切換；主要用途是 poke 限制與抓取姿勢（S19、S23） | 讓虛擬手停在桌面上（不穿桌）、咪牌時手掌貼在牌上 | 沒有現成的「手被桌面擋住」元件；要自己把桌面平面當限制來覆寫手腕位置【推測】。Meta 設計文件鼓勵在接觸時讓虛擬手和真實手分離（S10） |
| **Physics hands／手部碰撞體** | OVRSkeleton 的 Enable Physics Capsules 會為每根骨頭產生 capsule 碰撞體（S22）；ISDK 本身**沒有**找到正式的 physics-hands 元件【查不到】；PhysicsGrabbable 已併入 Grabbable（S31） | 用手背把籌碼掃進底池、推牌 | 這些 capsule 在執行期才產生（S22，舊論壇）；Hand Physics Lab 的評測顯示，物理手會造成「物件被彈飛」、手和真實手位置不同步、抖動（S25、S26） |
| **Microgestures**（補充） | 拇指在食指側邊點擊或滑動，透過 OpenXR XR_META_hand_tracking_microgestures 提供（S28） | 可當作「確認下注」或「撤回」的輔助輸入【推測】 | 和「咪牌時的拇指動作」可能衝突 |

---

## 2. 手部追蹤弱點與對策

### 2.1 文件寫的（Meta 官方）
- **追蹤頻率**：預設 30 Hz，FMM 最高 60 Hz（S1、S2）。
- **FMM 的代價**：「FMM 的最佳化可能增加抖動，影響直接觸碰或打字等高精度互動」；「快速動作依賴短曝光，所以低光下追蹤品質會退化」；官方強烈建議先不開 FMM 測試（S1）。
- **FMM 的相容性**：不能和 Multimodal 同時開；會停用 Dynamic Object Tracker 和 MRUK 鍵盤追蹤（S1）。
- **Hands 2.4（v83）**：手重新進入視野時更快被偵測到、動作上採樣、快速動作濾波器（S2b）；Wide Motion Mode 在手離開視野時用身體追蹤推估合理的手部姿勢，可以讓投擲動作更符合軌跡（S2b）。
- **Hands 2.2**：一般延遲最多降低 40%，快速動作時最多降低 75%（S30）。
- **遮擋**：手的大部分被擋住時，虛擬手會消失；信心值低於門檻時不顯示（S3）。
- **雙手重疊**：「一隻手蓋住另一隻手越多，精度越低」；建議盡量減少重疊、偏好單手互動（S3）。Hands 2.0 宣稱支援交叉雙手、拍手等手疊手的動作（S32），但官方的限制頁仍然把重疊列為弱點（S3）。
- **自我遮擋**：手指被其他手指擋住、手指朝外指，都屬於自我遮擋（S3）；手勢設計頁明確要求避開「手掌擋住手指」的姿勢（S9、S10）。
- **快速動作**：往遠離使用者方向的動作，比和視線平行的動作追蹤得好（S3）。
- **小物件／目標**：觸碰目標最小 22×22 mm、間距至少 12 mm（S10）。

### 2.2 研究數據
- Quest 2 研究：指尖平均位置誤差約 1.1 cm、關節角度誤差 9.6°、延遲 38 ms（另一版本寫 45 ms）（S20；數字來自搜尋摘要，我沒能讀到全文，因為 PMC 和 bioRxiv 都擋了請求）。
- 另一篇論文引用 Quest Hand Tracking 2.0 分辨「靠近表面時有沒有觸碰」的解析度約 9.8 mm（S21 的搜尋摘要，屬於間接引用）。
- SurfaceXR（2026-03）：指出以頭戴相機做表面輸入，受限於「不可靠的手部追蹤與不準的表面平面估計」（S21b）。

### 2.3 對策（以下多為【推測】，依據標在括號內）
1. **桌面當物理參考**：用 MRUK 取得 TABLE 平面（S24），或讓玩家「雙手平放在桌上」做一次校準（量手腕高度→得到桌面高度，減去偏移）。之後：
   - 虛擬手的手腕與指尖高度夾在 `max(tracked_y, table_y + 手厚度)` → 視覺上不穿桌（依據 S10「接觸表面時讓虛擬手和實體手分離」，以及 S19 的 poke limiting 做法）。
   - 任何「碰到桌面」的判定都用「速度尖峰 + 高度落在 table ±2 cm」，不靠單一幀的接觸（依據 S20、S21 的誤差約 1 cm）。
   - 手的高度訊號做低通濾波＋遲滯（ISDK 內建 threshold width 就是遲滯，S5）。
2. **雙手 All-in**：要求兩手間距 > 15 cm【推測值】，避免重疊（S3）。
3. **小物件**：不追求「逐枚物理籌碼」，改成「堆＝一個可抓的物件＋數量狀態」；抓取範圍比視覺模型大（Demeo 用比手更大的 box collider 對應看得到的手，S18）。
4. **快速動作（摔牌）**：用釋放前 N 幀（例如 100–150 ms）的峰值速度判斷；追蹤遺失時沿用最後的速度推估（依據 S3 快速動作會掉追蹤、S2b WMM）。FMM 先關，等實測真的常掉追蹤再開（S1 的官方建議）。
5. **手掌朝下**：咪牌不依賴手指關節，只用手腕或手掌的位置與朝向（PalmDown 屬 TransformRecognizer，追蹤的是整隻手的姿態，比手指關節穩【推測】）。
6. **光線**：啟動時提醒玩家把環境打亮（S1、S3、S18）。Demeo 開發者也提到陽光窗、白牆、暗處會造成問題（S18）。

---

## 3. 已上架 VR 撲克與物理手遊戲

| 遊戲 | 手部追蹤？ | 籌碼與牌的設計 | 抱怨 | 稱讚 |
|---|---|---|---|---|
| **PokerStars VR → Vegas Infinite**（Lucky VR） | vrdb 頁面（2026-10-12 更新）列出輸入方式為「Hands, Controllers」（S27）。**怎麼用手部追蹤玩，我查不到任何官方說明**；YouTube 上有一部〈PokerStars VR Hand tracking update〉，但抓不到日期和內容（S33） | 沒有按鈕或滑桿；從籌碼堆拿籌碼時會跳出數字顯示拿了多少；往前丟就是下注，籌碼落地後系統自動收進底池；雙手推整疊＝All-in；把牌拿起來看，舉高別人也看不到；heads-up 時可秀一張或兩張（S15、S16） | 想看牌時不小心把牌翻給別人看（S17）；用控制器抓牌或抓籌碼要「用力捏」才算數（S34，2019 Steam）；手會抖動（Rift S，Steam 回報）；控制方式要花時間學；手局拖很久（S16） | 籌碼手感好；幾手之後控制就不彆扭；社交與「讀人」的樂趣（S16）；開發者說花了很多力氣在抓籌碼、多角度看牌、All-in 推（S15） |
| **POKER VR w/ Roulette, Blackjack & Slots**（Mega Particle） | 支援 Hands 與 Controllers；不支援 MR（S35） | 查不到手勢細節 | 查不到 | 4.4 星，約 1 萬則評價（S35） |
| **Poker Club** | 【查不到】：找不到同名的 Quest VR 撲克遊戲；同名作品應該是主機或 PC 遊戲，不是 VR | — | — | — |
| **Demeo**（MR 桌遊，最接近的參考） | 有，且支援 MR（S18） | 棋子用 TouchHandGrab；骰子用球形碰撞體，手指會貼合；**牌不用碰撞體**，改用靠近偵測＋HandGrab 吸附預設姿勢；牌桌用 table 標籤的 anchor 擺放與共享（S18） | 手靠近臉時追蹤較差；陽光窗、白牆、暗處（S18） | 棋子用比手大的碰撞體，減少放錯位置（S18） |
| **Hand Physics Lab** | 有（S25、S26） | 物理手 | 「與其說是直接操作物件，不如說是小心地比手畫腳」；燈泡被彈飛；手指抖動；手會卡在管子裡；轉頭看別處時追蹤掉了；細小方塊最難操作（S25、S26） | 第一次看到自己手指的「哇」時刻；捏一下就能操作的遙控車很靈敏（S26） |

**對我們的啟示【推測】**：
- PokerStars VR 的核心設計（數字浮現、丟到哪裡都會被收進底池、push 進 All-in）已經被驗證，值得直接沿用。
- 他們最常被抱怨的是「不小心秀牌」，所以秀牌或翻牌要用階段來限制。
- Hand Physics Lab 證明「完全物理手」在 Quest 上容易讓人挫折。撲克的動作應該用「動畫或規則驅動＋物理點綴」，不要用純物理。

---

## 4. 每個手勢的實作建議（細節）

通用架構【推測】：
```
追蹤層（ISDK IHand + 桌面平面濾波）
 → 特徵層（PalmDown、Pinch、手腕速度與角速度、離桌高度、手上持有物）
 → 手勢狀態機（Sequence／自訂 FSM，每個手勢有 arm → active → commit → undo-window）
 → 遊戲意圖（Bet(x)、Call、Fold、Check、AllIn、Peek）→ 撲克規則引擎驗證（輪到誰、最小加注）
```
- **閘門**：手勢只在「輪到你」的時候才可能提交（Meta 建議用閘門避免誤觸，S10）。
- **兩段式提交**：動作完成後進入 1–2 秒的「待確認」狀態（顯示倒數圈）。這段時間內反向動作就撤回，倒數結束就鎖定。
- **回饋**（S9、S10：沒有觸覺時必須有視覺和聲音）：
  - 進入可抓範圍：籌碼或牌描邊亮起
  - 抓起：在手上方顯示 "×5 = 500"
  - 越線：下注線發光，並發出低音提示
  - 提交：籌碼清脆的「堆疊」聲
  - 撤回：反向音效
- **摔牌強度**：峰值速度分三級，分別觸發不同的音量、牌的彈跳高度、桌面震動粒子。這是「帶情緒」的關鍵，而且不影響判定結果。

| 手勢 | 判定規則草案【推測，數值需實測】 |
|---|---|
| 下注 X | 持有籌碼 → 釋放 → 籌碼中心越過下注線 → 0.3 s 內速度低於閾值 或 已經被收進底池 → 計入。總額不到跟注額 → 提示；剛好等於跟注額 → Call；超過 → Raise |
| 棄牌（蓋回或推出） | 手持牌（或掌心壓著牌）＋ 牌越過棄牌線 ＋ 放開 → 1.5 s 撤回窗 |
| 摔牌棄牌 | 持牌 ＋ 釋放前 150 ms 內往下或往前的峰值速度 > 1.5 m/s ＋ 釋放 → 立即棄牌（情緒動作不給撤回窗，但手局開始前可設定要不要確認） |
| 過牌 | 沒有人下注 ＋ 輪到你 ＋ 手腕往下的速度尖峰 > 0.6 m/s ＋ 手腕高度在桌面 3 cm 內 ＋ 手上沒有東西；敲兩下更保險（Sequence 可串兩步） |
| All-in | 雙手都在自己籌碼堆 15 cm 內 ＋ 雙手 PalmDown 或手掌朝前 ＋ 雙手往前的速度 > 0.3 m/s 持續 0.2 s ＋ 籌碼堆越線 → 彈出確認並保留 2 秒撤回 |
| 咪牌 | 手在自己的牌上方 10 cm 內 ＋ PalmDown 持續 0.3 s → 咪牌模式；掀角量 = f(手腕抬升高度, 手腕 pitch)，加低通濾波；手離開 → 牌放平 |

---

## 5. 籌碼與牌的物理

### 5.1 數百枚籌碼
- **【文件】** Unity Rigidbody 進入睡眠後就不參與物理計算，可以大幅降低 CPU；不要無謂地喚醒（S36）。
- **【文件】** `Graphics.RenderMeshInstanced` 用 GPU instancing 畫同一個 mesh；預設每次呼叫最多 511 個實例；和 SRP Batcher 互斥（SRP Batcher 優先）（S37）。
- **【推測】建議架構**：
  1. 「籌碼堆」是一個邏輯物件（顏色 × 數量），靜止時只有一個 Box／Capsule collider，渲染用 instancing。
  2. 手進入堆的 hover 區或被抓時，才把頂部的 N 枚生成成剛體（從物件池取）。
  3. 剛體籌碼睡著並落在某個堆附近時，合併回去。
  4. 底池收集用動畫（插值飛向底池），不做物理。這樣也符合 PokerStars VR「落地後自動收集」的設計（S16）。
  5. 同時活著的籌碼剛體上限約 30–50 枚【推測，要在 Quest 3S 上實測】。

### 5.2 牌的彎曲（咪牌）
- **【文件】** 在 vertex shader 位移頂點可以讓平面彎曲，但需要足夠細分的 mesh；只有 4 個頂點的 quad 彎不出曲線（S38）。頁面捲曲可以用「半個圓柱、軸垂直於移動方向」來近似（S39）。
- **【文件】** Blend shape 可以用 `SetBlendShapeWeight` 控制權重（S40）。
- **【推測】建議**：
  - 牌的 mesh 做 8×12 左右的細分。
  - 用 shader 實作「圓柱捲角」，參數有捲曲軸（對角線，從被掀的角出發）、半徑、進度 0–1；全部在 GPU 算，一個 float 就能驅動，和 instancing 相容。
  - Blend shape 只能做固定幾種捲法，彈性較差。
  - 碰撞體維持平的，不需要跟著彎（S38 也提到視覺彎曲不影響物理）。

### 5.3 聲音
- **【文件】** `Collision.relativeVelocity`／`impulse` 可以在 OnCollisionEnter 取得（S41）。
- **【推測】** 依相對速度選音色和音量：
  - 籌碼對籌碼：清脆的 clack
  - 籌碼對桌面：悶響
  - 牌對桌面：啪聲（摔牌時加重低頻）
  - 每種事件準備 3–5 個變體，加上音高隨機
  - 限制同時發聲的數量，避免一整堆倒下時爆音
  - 堆疊合併時播一個「整理」音
- 真實音效素材來源、錄音建議：【查不到】專業來源。

---

## 6. 沒做到／不確定
- **Vegas Infinite 的手部追蹤細節**：只知道 vrdb 列出支援 Hands（S27），怎麼咪牌、怎麼拿籌碼都查不到；YouTube 影片抓不到內容。
- **Poker Club**：查不到 Quest VR 版本。
- **MR Tabletop 範例（v207）**：只知道它存在（S4），沒看到內容。
- **ISDK 正式的 physics hands 元件**：沒找到；Meta 論壇的深度解析頁回 403。
- **手掌貼近桌面時的追蹤品質**：沒有官方數據，只有約 1 cm 的一般誤差研究（S20），而且該研究的全文沒讀到（摘要數字來自搜尋結果）。
- **Reddit 上的玩家抱怨**：搜尋沒有回傳 Reddit 結果，只有 Steam 討論區。
- **表格中的數值**（速度閾值、距離、時間窗）全是推測，必須實機調整。

---

## 來源
- S1 Meta, Fast Motion Mode (Unity) — https://developers.meta.com/horizon/documentation/unity/fast-motion-mode/
- S2 Meta, Hands technology (design) — https://developers.meta.com/horizon/design/hands-technology/
- S2b Meta blog, What's New with Hand Tracking in v83 — https://developers.meta.com/horizon/blog/whats-new-hand-tracking-v83/
- S3 Meta, Hands Limitations & Mitigations — https://developers.meta.com/horizon/design/hands-limitations-mitigations/
- S4 Meta XR Interaction SDK package page (v207.0 release notes, 2026-09-22) — https://developers.meta.com/horizon/downloads/package/meta-xr-interaction-sdk/
- S5 Meta, ISDK Hand Pose Detection (2026-03-13) — https://developers.meta.com/vr/documentation/unity/unity-isdk-hand-pose-detection/
- S6 Meta, ISDK Hand Grab Interactions (2026-08-26) — https://developers.meta.com/horizon/documentation/unity/unity-isdk-hand-grab-interaction/
- S7 Meta API ref, HandGrabUseInteractable v207 — https://developers.meta.com/horizon/reference/interaction/v207/class_oculus_interaction_hand_grab_hand_grab_use_interactable
- S8 Meta, ISDK overview (2026-10-07) — https://developers.meta.com/horizon/documentation/unity/unity-isdk-interaction-sdk-overview/
- S9 Meta, Hands Best Practices (2026-09-14) — https://developers.meta.com/horizon/design/hands-best-practices/
- S10 Meta, Hands Interaction Types (2026-08-17) — https://developers.meta.com/horizon/design/hands-interaction-types/ ；Gesture design — https://developers.meta.com/horizon/design/hands-gesture-design/ ；Hands UI best practices — https://developers.meta.com/horizon/design/hands-ui-best-practices/
- S11 Meta, Touch Hand Grab Interactions — https://developers.meta.com/horizon/documentation/unity/unity-isdk-touch-hand-grab-interaction/
- S12 Meta API ref, TouchHandGrabInteractor v207 — https://developers.meta.com/vr/reference/interaction/v207/class_oculus_interaction_touch_hand_grab_interactor
- S13 Meta, Distance Hand Grab — https://developers.meta.com/horizon/documentation/unity/unity-isdk-distance-hand-grab-interaction/
- S14 Meta API ref, IThrowVelocityCalculator (obsolete) — https://developers.meta.com/horizon/reference/interaction/v201/interface_oculus_interaction_throw_i_throw_velocity_calculator
- S15 Meta/Oculus blog, All In: PokerStars VR on Rift (2018-11-08) — https://www.meta.com/blog/all-in-pokerstars-vr-now-available-on-rift/
- S16 Casino.org, PokerStars VR: Is It Worth Your Time? (2021-02-21) — https://www.casino.org/blog/pokerstars-vr/
- S17 Steam, Vegas Infinite discussion (accidental card flip) — https://steamcommunity.com/app/886250/discussions/1/1637536330476151492
- S18 Meta blog, Demeo hand tracking + MR (2024-07-19) — https://www.meta.com/blog/demeo-hand-tracking-mixed-reality-mr/
- S19 Meta, ISDK Poke Interaction (2025-11-04) — https://developers.meta.com/horizon/documentation/unity/unity-isdk-poke-interaction/
- S20 Quest 2 hand-tracking accuracy framework (bioRxiv / PMC) — https://www.biorxiv.org/content/10.1101/2022.02.18.481001v1.full ；https://pmc.ncbi.nlm.nih.gov/articles/PMC10830632/ （只看到搜尋摘要）
- S21 Schneider et al., Accuracy Evaluation of Touch Tasks in Commodity VR/AR (SUI 2021) — https://arxiv.org/abs/2109.10607 ；S21b Xu et al., SurfaceXR (2026) — https://arxiv.org/abs/2603.19529
- S22 Meta, Unity hand tracking hands setup (Physics Capsules) — https://developers.meta.com/horizon/documentation/unity/unity-handtracking-hands-setup/
- S23 Meta API ref, SyntheticHand v207 — https://developers.meta.com/horizon/reference/interaction/v207/class_oculus_interaction_input_synthetic_hand
- S24 Meta, MRUK basic sample — https://developers.meta.com/horizon/documentation/unity/unity-sample-mruk-basic
- S25 Road to VR, Hand Physics Lab review — https://roadtovr.com/hand-physics-lab-review/
- S26 UploadVR, Hand Physics Lab review (2021-04-01) — https://uploadvr.com/hand-physics-lab-review/
- S27 vrdb, PokerStars – Vegas Infinite — https://vrdb.app/game/2370815932930055
- S28 Meta, Unity Microgestures — https://developers.meta.com/horizon/documentation/unity/unity-microgestures/
- S29 GitHub, oculus-samples/Unity-InteractionSDK-Samples — https://github.com/oculus-samples/Unity-InteractionSDK-Samples
- S30 Meta blog, Hands 2.2 (2023-08-21) — https://developers.meta.com/vr/blog/hand-tracking-22-response-time-meta-quest-developers/
- S31 Meta forum, ISDK physics issue on grab — https://communityforums.atmeta.com/discussions/Questions_Discussions/meta-xr-interaction-sdk-physics-issue-on-grab/1361069 ；API ref HandGrabInteractable v201 — https://developers.meta.com/horizon/reference/interaction/v201/class_oculus_interaction_hand_grab_hand_grab_interactable
- S32 Meta blog, Hand Tracking API Gets an Upgrade (2.0) — https://developers.meta.com/horizon/blog/presence-platforms-hand-tracking-api-gets-an-upgrade/
- S33 YouTube, PokerStars VR Hand tracking update — https://www.youtube.com/watch?v=oAedcpM3IYU （只拿到標題）
- S34 Steam, Support for finger tracking (2019) — https://steamcommunity.com/app/886250/discussions/0/1637542851358132014/
- S35 vrdb, POKER VR w/ Roulette, Blackjack & Slots — https://vrdb.app/game/2257223740990488
- S36 Unity Manual, Rigidbody sleeping — https://docs.unity3d.com/Manual/physics-optimization-cpu-rigidbody-sleeping.html
- S37 Unity ScriptRef, Graphics.RenderMeshInstanced — https://docs.unity3d.com/cn/2023.2/ScriptReference/Graphics.RenderMeshInstanced.html
- S38 Unity Discussions, How to make plane look curved — https://discussions.unity.com/t/how-to-make-plane-look-curved/47337
- S39 Solar2D forum, Page curl effect — https://forums.solar2d.com/t/page-curl-effect-using-graphics-2-0-and-shaders/335388
- S40 Unity Manual, Blend Shapes — https://docs.unity3d.com/2022.1/Documentation/Manual/BlendShapes.html
- S41 Unity ScriptRef, Collision — https://docs.unity.cn/ScriptReference/Collision.html
- S42 Hand Tracking 2.1 blog — https://developers.meta.com/horizon/blog/hand-tracking-improvements-v2-1
- S43 Ultraleap XR guidelines, Virtual objects — https://docs.ultraleap.com/xr-guidelines/Components/virtual-objects.html
