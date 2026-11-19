# Meta 過去比賽得獎模式 × MR 手勢德州撲克的得獎評估（2026-10-12）

> 標記規則：**[事實]** 有來源 URL；**[統計]** 我用爬下來的完整資料集算的（方法有寫）；**[分析]** 是我的判斷，不是事實。
> 原始資料：`pw/` 目錄（2025 比賽 Devpost 全部 653 頁專案、2024 黑客松全部 181 頁專案、本屆各頁 HTML 與文字檔）。

---

## 1. 本屆：Meta VR Start Developer Competition 2026

來源：Devpost 首頁 https://start-developer-competition-26.devpost.com/ 、規則 https://start-developer-competition-26.devpost.com/rules 、評審頁 https://start-developer-competition-26.devpost.com/details/judging 、特別獎頁 https://start-developer-competition-26.devpost.com/details/special-awards 、FAQ https://start-developer-competition-26.devpost.com/details/faqs 、Meta 部落格 https://developers.meta.com/blog/meta-connect-2026-vr-start-developer-competition/

### 1.1 時程 [事實]
- 參賽期間：2026-09-24 10:00 PT 到 2026-11-18 12:00 PT。評審期約 11-18 到 12-09，約 12-11 公布得獎（規則）。
- New Experience 組：專案必須在 2026-09-24 之後才構想並開始做，不能用既有的程式碼。

### 1.2 獎項與名額 [事實]（總獎金 100 萬美元，每件作品最多拿一個獎，同時符合多個獎項時頒金額最高的那個）
| 獎項 | 金額 | 名額 |
|---|---|---|
| Best New Gaming Experience | $100,000 | 1 |
| Best New Gaming Experience Runner-up | $50,000 | 1 |
| Best New Entertainment / Runner-up | $100,000 / $50,000 | 1 / 1 |
| Best New Productivity / Runner-up | $70,000 / $35,000 | 1 / 1 |
| Best Adapted Gaming / Runner-up | $100,000 / $50,000 | 1 / 1 |
| Best Adapted Entertainment / Runner-up | $100,000 / $50,000 | 1 / 1 |
| Best Adapted Productivity / Runner-up | $70,000 / $35,000 | 1 / 1 |
| Best Social & Multiplayer Experience | $25,000 | 1 |
| Best Agentic Interaction | $25,000 | 1 |
| Best Reason to Come Back | $25,000 | 1 |
| Best First Five Minutes | $25,000 | 1 |
| Best Accessibility Forward Experience | $25,000 | 1 |
| Boldest Original Concept | $25,000 | 1 |
| Judge's Choice | $20,000 | 2 |

→ 總共 **20 個得獎名額**。**Gaming × New 組只有 2 個主獎名額**；另外 6 個特別獎和 2 個 Judge's Choice，是跨賽道、跨組別所有作品一起競爭。

### 1.3 評分細項原文（四項各佔 25%）[事實，摘自規則與評審頁]
- **Innovation & Creativity**：「Measures originality, creative ambition, and relevance to the chosen track… High-scoring projects differentiate themselves, show developer ingenuity, and uniquely leverage Meta device capabilities (e.g., spatial depth, hands-first design, gaze, passthrough mode, scene understanding, persistent anchors) to drive repeat usage.」
- **Experience Design**：「…emphasis on seated, hands-first, or context-specific use. Strong entries have intuitive navigation, clear onboarding, and a habit-forming purpose.」「Passthrough should be purposeful: the player's real room meaningfully changes the experience」。評審頁原文還舉例：「**whether a table becomes the play surface**, walls host content, or UI adapts to the space, rather than a scene that plays identically with passthrough off.」另外也評 FoV-aware design。
- **Technical Implementation**：「…gaze interactions (Unity/Unreal ISDK v207+), hand tracking, passthrough, spatial anchoring, and FoV-aware design… must be performant on Meta Quest hardware (min 60 fps), solid and bug-free… For entries that use passthrough… correct use of scene understanding (MRUK), anchoring content to detected planes, surfaces… **holding up in rooms the developer never tested**.」
- **Polish & Presentation**：「UI/UX, art direction, and sound design… high-quality submission materials… show real gameplay that honestly represents the experience… Don't lean on AI-generated video to carry the pitch.」
- 評審流程：Stage One 是及格／不及格（主題、賽道、工具是否合適）；Stage Two 用上面四項評分。規則還寫「AI tools may also be used to assist in the judging, but final decisions will be made by human judges」。評審用 Quest 3／3S 試玩（FAQ）。

### 1.4 硬性要求與設計準則 [事實]
- 必須能「全程只用手」完成（hands-first，不能需要控制器）；必須遵守 Meta 的政策。
- 設計準則：坐著玩（airplane seat test，約 2 呎半徑）；10 分鐘內就要有滿足感（one bus stop test）；不能只是包一層第三方服務（take it away test）。
- Gaming 賽道原文：「Games designed for hands-first or eyes and hands. Puzzle, strategy, casual, social, narrative—genres that thrive seated, without controllers.」
- Meta 部落格的建議：「A tight vertical slice is better than a broad prototype with gaps, because judges play what you submit.」

### 1.5 特別獎定義（原文重點）[事實]
- Social & Multiplayer：shared presence、co-watching、cooperative、asymmetric multiplayer。
- **Agentic Interaction：「AI as a core part of the experience: spatial agents, voice-first interaction, procedural content, context-aware assistance.」**
- Reason to Come Back：progression、跨 session 看得見的成長、「a loop that pulls the user back tomorrow」。
- **First Five Minutes：「how fast it explains itself, teaches controller-free interaction without a wall of text, and delivers a payoff before the headset comes off.」**
- Accessibility Forward：手部互動的語音替代方案、只用眼睛導覽、高對比模式。
- **Boldest Original Concept：「the idea that couldn't exist anywhere else… Judged on originality and on whether the experience would fall apart on any other platform.」**

### 1.6 報名數、評審、題材偏好
- [事實] Devpost 顯示參加人數 **2,937**（2026-10-12 抓取時）。提交數還沒公開，作品集也還沒公開（「The hackathon managers haven't published this gallery yet」）。
- [事實] 官方首頁寫：去年「over 2,900 developers joined… more than 650 submissions」。
- [事實] 評審：Devpost 只列出 **Dilmer V**（Dilmer Valecillos，XR 教學 YouTuber；他也是 2024 Presence Platform Hackathon 的評審，見 §2.3）。規則說評審「may or may not be listed individually」，可以是 Meta 員工或外部人士。
- [事實] **沒有任何官方文字提到「已經做過很多次的類型」或排斥某些題材**。Meta 部落格也沒有（WebFetch 確認）。題材偏好只有 Gaming 賽道那句「puzzle, strategy, casual, social, narrative… seated」。
- [事實] 首頁的「參考去年得獎作品」連結，Hands 範例放的是 **Hand Physics Lab** 和 **Table Troopers**。Table Troopers 是 MR 手勢桌面戰略遊戲，把真實桌子變成戰場，Meta 的成功案例頁列出的教訓是「Design for player intent, not just raw gesture data」「Simplify mechanics rather than making input more complex」：https://developers.meta.com/vr/discover/success-stories/table-troopers/

---

## 2. Meta 過去比賽的得獎名單

### 2.1 Meta Horizon Start Developer Competition 2025（本屆的前身，最有參考價值）
- 來源：Meta 得獎公告 https://developers.meta.com/horizon/blog/meta-horizon-start-developer-competition-meet-the-winners/ ；Devpost https://start-developer-competition.devpost.com/ （作品集全部 28 頁都爬了，得到 **653 件作品，其中 32 件標示 Winner**）。
- [事實] 2,865 位參加者（Devpost）；$1.5M、32 個獎。賽道是 Casual Game／Social Game／Entertainment／Lifestyle，加上以 SDK 為主的特別獎。**這一屆沒有分 New 和 Adapted 組**。
- 人數欄是 Devpost 列出的成員帳號數，不一定是實際人數（工作室常常只用一個帳號）。

| 獎 | 作品 | 類型／核心機制（依作品頁 tagline 與描述） | Meta 技術 | Devpost 成員數 |
|---|---|---|---|---|
| Best Casual Game 冠軍 | Tiny Golf | MR 迷你高爾夫，用「捏」來打，可以自己做球場 | MR、手部、真實桌面 | 2 |
| Casual 亞軍 | Little Critters – hand tracking update | 已上架的 MR 塔防遊戲，這次做了手勢更新 | MR、手部、真實表面 | 1（工作室 Purple Yonder） |
| Casual 佳作 | Le Dino Labo | 一根一根骨頭組裝恐龍、按真實比例呈現（教育） | XR | 4（Realcast） |
| Best Social Game 冠軍 | Saber Punks | 已有的 PvP 格鬥遊戲，加入 MR 與同空間多人（colocation） | MR、room scan、colocation | 2 |
| Social 亞軍 | FALCON | 在 MR 或 VR 裡開無人機 | MR、多人 | 1（Wild Vision Games） |
| Social 佳作 | Pack Attack | MR 派對遊戲，頭顯玩家搭配手機玩家（非對稱） | MR、跨裝置 | 2 |
| Entertainment 冠軍 | DreamSpace | 把房間變成沉浸式 MR 空間 | MR、場景 | 2 |
| Entertainment 亞軍 | Photon | 3D／空間影片社群 | — | 2 |
| Entertainment 佳作 | (m)ORPH MR | MR 互動式音樂 | MR、手部、錨點、colocation | 1 |
| Lifestyle 冠軍 | Inkphony | 周圍的物體變成樂器，用來學作曲 | PCA＋AI、手部 | 3 |
| Lifestyle 亞軍 | The Tarot Experience VR – AI Edition | 塔羅牌占卜加上 AI 解讀 | AI | 1 |
| Lifestyle 佳作 | IMMERROCK | MR 學吉他 | MR | 2 |
| Hand Interactions ×3 | Hand Survivor / Pocket Lands / Awesome Hand | 純手勢的生存遊戲；在腿上大小的立體模型裡用手塑造 voxel 世界；用手掌與手指控制球路的解謎 | 手部追蹤 | 2 / 1 / 2 |
| PCA with AI ×3 | Language IRL / Chores.gg / Flows.xr | 用環境學語言；AI 把家務變成遊戲；XR AI 工作流程 | 相機存取（PCA）＋AI | 2 / 2 / 1 |
| IWSDK ×2 | The Nanauts / Dun Jun | MR AI 外星人（Llama 3.3、場景理解）；手勢地城生存 | WebXR | 1 / 2 |
| Spatial SDK ×2 | Compile Crops / Mise | 寫程式控制無人機的農場遊戲；AR 料理 | Spatial SDK | 2 / 0 |
| Android 類 ×4 | Magic Wonders / GeoPortal / MarketView VR / OnTheGoVR | 手勢施法、飛行追蹤、股票看盤、旅行時看電影 | Android | 2 / 1 / 1 / 2 |
| Judges' Choice ×6 | Into The Mind / OpenSoundLab / Lasertag / Final Throwdown / HandCraft XR / ModStorm Block Party | 大腦探索；空間模組合成器；colocation 雷射槍戰；colocation MR 對打派對；用手在 MR 搭聖誕市集；voxel 派對 | 多半是 MR＋手部或 colocation | 1/1/1/4/1/2 |

### 2.2 Meta Horizon Mobile Innovation Challenge 2025（Horizon Worlds 行動版，平台不同，只作題材參考）
- 來源：https://developers.meta.com/vr/blog/announcing-meta-horizon-mobile-innovation-winners/
- [事實] $1.5M、35 個獎。三個首獎：Ship It!（裝箱上卡車）、Gunship: Quackdown（砲艇射擊）、Spell Swiper（用手指畫形狀守城）。**「Hardy World Poker」（hardywest）拿到 Best World that Leveraged Noesis**，是技術類的特別獎。
- [分析] 這是 Meta 比賽中**唯一查到的撲克得獎作品**，但它是在 Horizon Worlds 平台、拿的是 UI 技術獎，不能直接類比 Quest 原生的 MR 比賽。它顯示的是 Meta 不會因為「撲克／模擬博弈」就排除作品，但這只是一個例子。

### 2.3 Meta Quest Presence Platform Hackathon 2024（Devpost，線上）
- 來源：Devpost https://metaquesthackathon.devpost.com/ （爬了全部作品集：**181 件作品、9 件得獎**，參加者 2,161）；Meta 公告 https://developers.meta.com/horizon/blog/presence-platform-2024-digital-hackathon-winners-meta-quest/
- [事實] 獎金總額 $30,985（每類冠軍 $5,000 加孵化計畫申請資格）。評審有 Dilmer、Valem、Justin Barnett、Rahel Demant、Dinesh Punni。參賽門檻是至少用一項 MR 能力和一個 Presence SDK。

| 獎 | 作品 | 類型／機制 | 技術 | 成員數 |
|---|---|---|---|---|
| Casual/Social 第 1 | Blobble | 撞球加泡泡龍，球在你的房間裡反彈 | Scene、ISDK 捏放 | 1 |
| Casual/Social 第 2 | Room Wars | 敵人破牆攻擊你的房間，城堡防禦 | MR、場景 | 1 |
| Casual/Social 第 3 | Super Eats | 跨平台多人 MR 料理遊戲 | MR、多人 | 6 |
| Hobbies 第 1 | Flaivor | MR AI 料理助理 | Passthrough、Voice SDK、OpenAI | 3 |
| Hobbies 第 2 | Orbital Beats | MR 音樂創作與視覺化 | MR | 1 |
| Hobbies 第 3 | Flux | MR 機器人／Arduino 實驗室 | MR | 3 |
| Utility 第 1 | Dreamscape | 用手或語音生成雪花球迷你世界 | 手部、Voice、生成式 AI | 1 |
| Utility 第 2 | Loop Jam | 異地一起即興演奏 | 多人 | 5 |
| Utility 第 3 | Busy Buds | MR 生產力工具 | MR | 2 |

### 2.4 Presence Platform Hackathon 2023（實體活動，Meta 加州與倫敦園區）
- 來源：https://developers.meta.com/horizon/blog/announcing-our-presence-platform-hackathon-results
- [事實] 2023-04-25 到 04-27，入選 150 多人，展示 33 件作品，兩地各 $50,000。得獎作品：**The Digital Wardrobe**（MR 虛擬鏡子試衣，用手勢換裝；Scene、眼動、身體、手部追蹤；4 人）、**SUBMERSED**（你的房間被水淹，用手貼膠帶堵破洞；Passthrough、空間錨點、手部；4 人）。

### 2.5 沒查到的部分
- Oculus Launch Pad／Oculus Start 的展示名單：**沒查**（時間優先給上面 4 個比賽）。
- 2021 XR Hackathon（https://developers.facebook.com/blog/post/2021/12/07/announcing-xr-hackathon-winners/）：只找到網址，**沒讀內容**。

---

## 3. 得獎模式分析

### 3.1 卡牌、撲克、賭場類作品的戰績 [統計＋事實]
- **2025 Start 比賽（653 件，全部作品頁全文搜尋 `poker|hold'em|casino|blackjack|gambl|bluff|chips`）**：**沒有任何撲克作品**。有提到 casino 的 3 件都不是撲克遊戲。最接近的是：
  - **Liar's Roulette**（1 人）：「Play cards with your bare hands using hand-tracking… A social card game where **body language exposes lies**」，靈感來自 Liar's Bar。**沒得獎。** https://devpost.com/software/liar-s-roulette
  - 其他桌上／卡牌類，全部沒得獎：Table Space（MR 桌遊沙盒，Adapted 更新）、TableConnect（MR 遠端實體桌遊）、Dueling Dimension（MR 集換式卡牌）、Cards of Destiny、Card Tower Defense、RogueGods（牌組構築）、Banners & Bastions（MR 桌上戰術）、**Table-Top-Tank（「Real table becomes the battlefield」＋手部追蹤）**、Mech Rivals（在餐桌上對戰）。
  - 有得獎的桌上作品：Tiny Golf（Casual 冠軍）、Pocket Lands（Hand Interactions）、HandCraft XR（Judges' Choice）。這三個的共通點是**「用手在桌上建造或操作一個小世界」**，不是卡牌類。
  - 只有 The Tarot Experience（塔羅牌加 AI）跟「牌」有關且得獎（Lifestyle 亞軍），但它的核心是 AI 占卜體驗，不是卡牌玩法。
- **2024 Presence 黑客松（181 件）**：撲克相關只有 **Dela AI**（AI 撲克荷官工具包，1 人），**沒得獎**。NoFold（XR 桌遊平台，4 人）也沒得獎。
- **Horizon Worlds Mobile 2025**：Hardy World Poker 拿到技術特別獎（見 §2.2）。
- [分析] 在 Quest 原生的 Meta 比賽中，**卡牌類（包含主打「手勢讀謊」的 Liar's Roulette）目前得獎紀錄是 0**。樣本很小，不能說評審討厭這個類型，但至少沒有正面的先例。

### 3.2 已知類型搬進 MR，還是全新機制？[分析，依 §2 名單判斷]
- 主賽道冠軍多半是「**熟悉的類型＋真實空間徹底改變玩法**」：迷你高爾夫（Tiny Golf）、撞球加泡泡龍（Blobble）、塔防（Room Wars、Little Critters）、格鬥加 colocation（Saber Punks）。所以「已知類型搬進 MR」**可以**得獎，**前提是房間或桌面真的改變了遊戲本身**，不只是背景換成 passthrough。
- 特別獎和 Judges' Choice 偏向「**只有手或只有 MR 才成立**」的機制：Awesome Hand（用手腕彎曲球路）、Pocket Lands、Inkphony（物體變樂器）、Lasertag（即時建出場景網格）。
- 撲克屬於第一類。但撲克的規則和節奏**不會因為真實桌面而改變**（不像高爾夫球會被真實障礙物彈開），這是它跟過去冠軍最大的不同。

### 3.3 共通點 [統計：2025 年 653 件的全文關鍵字比例，粗略統計，只代表「描述中有提到」]
| 特徵 | 全部作品 | 得獎作品 | 有提到這個特徵的作品得獎率 |
|---|---|---|---|
| MR／passthrough | 49% | 62% | 6.2% |
| 手部追蹤 | 42% | 53% | 6.2% |
| colocation／同空間共享 | 7% | 19% | **14.0%** |
| AI／LLM | 40% | 34% | 4.2% |
| 多人 | 36% | 44% | 6.0% |
| 提到 table | 7% | 9% | 6.5% |
| （對照）整體得獎率 | — | — | 4.9%（32/653） |
- 解讀 [分析]：MR 和手部是基本門檻（得獎作品略多，但差距不大）。**colocation（同一個房間一起玩）是唯一明顯拉高得獎率的特徵**。**AI 本身沒有加分**（提到 AI 的作品得獎率反而低於平均），只有 AI 確實是核心的作品才有用（PCA＋AI 是當年專門的特別獎）。
- 已上架作品的更新拿走很多 2025 主獎（Little Critters、Saber Punks、Photon、IMMERROCK、(m)ORPH、Tarot、Final Throwdown 等，依描述判斷）。**2026 把 New 和 Adapted 分組**，New 組不用再跟已上架的作品比完成度，這對從零開始的作品有利。

### 3.4 一人開發的得獎比例 [統計]
- 2025：Devpost 成員數為 1 的作品有 448 件，得獎 13 件（**2.9%**）；2 人以上的有 205 件，得獎 19 件（**9.3%**）。
  - 注意：成員數為 1 的得獎作品中，至少 Little Critters、FALCON、Into The Mind、OpenSoundLab 是工作室用單一帳號提交，所以**真正一個人開發的得獎率應該低於 2.9%**。
  - 確定是一個人做的得獎例子：The Nanauts（作者寫「completed all technical and creative work on my own in only three weeks」）、HandCraft XR（「Made it from scratch in just a month」）、Pocket Lands。
- 2024：一人作品 84 件，得獎 4 件（4.8%）；整體 9/181（5.0%）。

### 3.5 競爭強度
- 2025：2,865 人參加，653 件作品，32 個獎，**整體得獎率 4.9%**。
- 2026 [推估]：截至 10-12 已有 2,937 人參加。如果提交數跟去年差不多（約 650 件），20 個獎的整體得獎率**約 3%**。Gaming × New 組的作品數查不到；2025 年名稱或標語中有 game／play 字樣的作品就有 212 件（這是下限，不是賽道人數）。
- 2024 Presence 黑客松：181 件作品、9 個獎（5.0%），但獎金小很多。

---

## 4. 撲克作品的評估 [全部是分析]

前提：Quest 3／3S MR 德州撲克，主打用手做真實牌桌手勢（咪牌、丟籌碼、雙手推 All-in、敲桌過牌、摔牌），牌桌鋪在真實桌面上，AI 對手有個性、會「讀」玩家的手勢，不賣籌碼。

| 評分項 | 評估 | 理由 |
|---|---|---|
| Innovation & Creativity | **中偏弱** | 德州撲克是大家很熟的類型，Quest 上已經有 Vegas Infinite、PokerVerse 等作品（見 competitors.md）。「用手勢互動加上 AI 讀手勢」是賣點，但 2025 年 Liar's Roulette 的「body language exposes lies」幾乎是同一個概念，結果沒得獎。真實桌面也不會改變撲克的規則或策略。差異化主要靠「AI 會讀你的手」這點要做得夠驚豔。 |
| Experience Design | **中偏強（潛力）** | 評審頁的原文就舉了「a table becomes the play surface」當作 purposeful passthrough 的範例。撲克本來就是坐著玩、一手牌時間短，很符合 airplane 和 bus stop 兩個測試。風險有兩個：(1) 不會德州撲克的評審要先學規則，onboarding 很難不出現「a wall of text」；(2) 一整局打到結束遠超過 10 分鐘，要設計短賽制。 |
| Technical Implementation | **中（高變異）** | 評分寫了要 hand tracking、passthrough、MRUK 偵測桌面、空間錨點、60fps，這些都會用到。但細緻手勢有實際困難：咪牌需要手指精細動作、手貼近桌面時追蹤品質下降、手互相遮擋、敲桌需要偵測真實平面。另外要「在開發者沒測過的房間也撐得住」，桌面偵測失敗時必須有備案。做穩了可以拿高分，做不穩會被扣很重。 |
| Polish & Presentation | **中** | 牌桌場景的美術範圍小，是優點。但一個人開發，在 37 天內同時要做美術、音效、AI 個性（語音或動畫）和 3 分鐘影片，負擔很重。 |

### 4.1 最可能的獎（每件作品只能拿一個獎）
1. **Best New Gaming Experience（冠軍或亞軍，共 2 名）**：唯一不用跟 Adapted 組比的主獎。但只有 2 個名額，要在所有 New 組遊戲中排前二。
2. **Best Agentic Interaction**：如果 AI 對手是體驗的核心（會根據手勢、遲疑、力道做出反應，有語音互嗆，能 context-aware 地讀牌桌），這是**最貼合的特別獎**。
3. **Best First Five Minutes**：如果能做到「不用文字，5 分鐘內打完第一手牌並贏到第一個 pot」。
- 不太可能：Boldest Original Concept（撲克在其他平台也能玩，不符合「would fall apart on any other platform」）、Social & Multiplayer（只有 AI 對手）、Accessibility（除非特別去做）。

### 4.2 最不利的地方
1. **新穎度**：熟悉的類型，加上 Quest 已有同類產品，加上有幾乎同概念的作品去年沒得獎。
2. **真實桌面不會改變遊戲本身**：過去 MR 冠軍的房間或桌面都會改變玩法（球會反彈、敵人破牆），撲克只是把桌面當成放牌的地方。
3. **規則門檻與 session 長度**：跟「10 分鐘內有滿足感」和「First Five Minutes」的要求有衝突。
4. **一人開發的得獎率低**（2025 年一人帳號 2.9%，而且這個數字還偏高），時間只剩約 5 週。
5. **模擬博弈的觀感**：Meta 的 Content Guidelines 只禁止「Real money gambling… or giving users the ability to redeem or cash out in-game currencies」（https://developers.meta.com/horizon/policy/content-guidelines/ ），不賣籌碼符合這條。所以**沒有證據顯示會被 Stage One 刷掉**。但上架時可能會被標為「Simulated Gambling」（Vegas Infinite 是這樣標的），評審會怎麼看這點：**不確定**。

### 4.3 不換題目時，最有效的 3 個調整
1. **讓「AI 讀你的手」成為主角，並在影片前 30 秒就呈現**：AI 對手要對遲疑時間、丟籌碼的力道、摔牌、手抖做出看得見、聽得見的反應（語音嗆聲、表情），並記住你的習慣（tell）。這一項同時拉高 Innovation，並直接對準 Agentic Interaction 獎；還要跟 Liar's Roulette「只是用手拿牌」的做法做出區隔。
2. **把真實桌子變成玩法的一部分，並且做到穩**：真實桌子提供觸感（敲桌、推籌碼撞到桌面），這是「別的平台做不到」最有力的論點。要做到：MRUK 偵測桌面平面＋空間錨點；偵測失敗時退回虛擬桌；手勢辨識看「意圖」（照 Table Troopers 的教訓）；手勢少而準（建議 4 到 5 個，每個都穩定），寧可砍掉咪牌的細節，也不要有誤判。這一項對應 Technical 與 Experience Design 的原文。
3. **重新設計前 5 分鐘和 session 長度**：不顯示規則文字，由 AI 荷官或對手用語音加示範帶你打完第一手；用「單挑、約 8 分鐘一局」的短賽制；每局結束給一個可見的進度（對手記住你、解鎖新對手），兼顧 First Five Minutes、bus stop test 與 Reason to Come Back。最後依 Meta 的建議做「tight vertical slice」。

### 4.4 要不要考慮換題目？（只提供判斷依據，由使用者決定）
- **支持換題目的理由**：卡牌類在 Quest 原生的 Meta 比賽中得獎紀錄是 0；主獎名額只有 2 個；過去冠軍的機制都依賴真實空間，撲克天生不依賴空間；New 組也不能沿用既有程式碼。
- **支持不換的理由**：評審頁原文就把「桌子變成遊戲桌面」當範例，撲克在「坐著玩、純手勢、短 session」三項上天生就符合；沒有任何規則或文字排斥這個題材；去年完全沒有撲克作品，評審沒有看膩的問題。現在離截止只剩約 37 天，換題目的時間成本很高。
- **關鍵的判斷點**：如果「AI 讀手勢」只能做到 Liar's Roulette 那種程度（用手拿牌、AI 用規則判斷），從過去的得獎模式來看勝算偏低。如果做得到「AI 會因為你的手而改變它的打法，而且影片一看就懂」，就有機會拿到 Gaming New 組的前二名或 Agentic Interaction 獎。這個判斷建議用 1 到 2 週的原型實際驗證，再決定要不要換。

---

## 5. 沒做到的事與不確定處
- 2026 年提交數、Gaming × New 組的作品數：**查不到**（作品集還沒公開）。
- 2025 年得獎作品的「新作或更新」是從描述文字判斷的，**不是官方標記**。
- 一人開發的比例用 Devpost 成員帳號數計算，工作室用單一帳號的情況會讓結果偏高。
- 特徵比例是用關鍵字比對全文，有誤判可能，只能看大方向。
- 「Simulated Gambling」對評分是否有影響：**沒有證據**，只能標為不確定。
- 評審名單只查到 Dilmer V。其他評審**不確定**。
- 2023 年 The Digital Wardrobe 的團隊名單，不同來源有出入（WebSearch 結果提到舊的 Oculus 文章列了另一組名字）。這裡採用 Meta 目前版本的頁面。
- Oculus Launch Pad／Start 展示、2021 XR Hackathon：**沒查**。
