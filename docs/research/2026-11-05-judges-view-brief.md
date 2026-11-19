# Tell 比賽影片研究簡報（評審視角）

調查日期：2026-11-05。範圍：`vr-poker-hank`（工作分支 `feat/realistic-hank-preview`，346 個未 commit 變更，HEAD 1ca0edc）與 `vr-poker`（main，HEAD 同為 1ca0edc）。
標記：【規則原文】官方文字；【文件】repo 文件所述；【程式】我讀了程式碼確認；【推論】我的判斷。
路徑縮寫：`H/` = `/Users/laichanean/WebstormProject/vr-poker-hank/`；`S/` = 本檔同目錄的 `devpost-snapshot-2026-11-05/`（Devpost 頁面轉文字，行號指該檔行號）。
程式路徑縮寫：`H/A/` = `H/Assets/_Project/Scripts/`。

---

## 0. 查證方式與限制

- Devpost 規則頁用 WebFetch 回 403（`https://start-developer-competition-26.devpost.com/` 與 `/rules`、`/details/*`），改用 curl 加瀏覽器 User-Agent 取得全文（repo 的 `H/docs/research/engine-sdk.md:62` 也記了這個做法），轉成文字存在 `S/`。
- 官方網址是 `start-developer-competition-26.devpost.com`（有 `-26`）。不帶 `-26` 的 `start-developer-competition.devpost.com` 是去年 2025 的比賽頁，不要混用。
- Meta 部落格 `https://developers.meta.com/horizon/blog/meta-connect-2026-vr-start-developer-competition/` 用 WebFetch 讀到：賽道定義、截止日、手要能全程操作；沒有評分標準與影片規定（部落格寫細節在 Devpost）。
- UploadVR（`https://www.uploadvr.com/meta-launches-1-million-competition-for-hands-first-apps-ahead-of-vr-glasses/`）與 MIXED（`https://mixed-news.com/en/?p=34473`）：只有獎項類別與「hands-first」描述，沒有評分標準、影片規定。
- 沒查到：Devpost 沒有任何「影片格式」「配樂版權」的專門條文（見 §2.4）。另外 Devpost 只公開列出 1 位評審（Dilmer V，`S/overview.txt:247`），評審名單不全。
- Devpost 參加人數：5,178（`S/judging.txt:78`）；repo 10/12 的數字是 2,937（`H/docs/research/past-winners.md:58`），競爭比當時預估的更大。

---

## 1. 作品摘要

### 1.1 一句話主張
- 【文件】`H/docs/specs/2026-10-12-mr-poker-design.md:7`：「用你真的手、在你真的桌子上，跟會說謊的 AI 打牌。你盯著他看能讀出破綻，他也在讀你。」
- 【文件】`H/docs/submission/video.md:5`：「They read your real hands, so you can lie with them.」
- 【文件】Devpost 標語草稿（`H/docs/submission/devpost.md:11`）：「Bare-hand poker on your real desk. Read the AI's tells — they read your real hands. Fake one and fool them.」

### 1.2 差異化特色（依重要度；重要度是【推論】，內容有出處）
1. **雙向讀心，而且你可以拿它說謊。** AI 即時看你的真手（想很久、再看牌、伸手又縮回、放回籌碼、手放牌上、盯著某人）並當場說出來，你可以故意演假破綻騙它。
   - 【程式】訊號：`H/A/Poker.Gestures/PlayerHabits.cs`；導演與台詞：`H/A/Poker.AI/LiveTalkDirector.cs`、`LiveLines.cs:36-88`；騙人情境 `H/A/Poker.AI/FoolRocco.cs:76-97`。
   - 效果有上限 ±0.15 勝率（`H/A/Poker.AI/PlayerRead.cs:111`），AI 不偷看隱藏資訊（`H/Assets/_Project/Tests/Voice/PublicStateTests.cs`）。
2. **真手＋真桌子。** 全部動作都是赤手手勢（咪牌、拿起來看、捏籌碼、丟／滑下注、敲桌過牌、推／滑棄牌、雙手推 All-in），桌面用 MRUK 對齊你的真實桌子，敲桌有真實觸感；找不到桌子退回虛擬桌。
   - 【文件】`H/docs/specs/2026-10-12-mr-poker-design.md:23-48`；【程式】`H/A/Runtime/Table/TableAnchorFlow.cs`、`OvrTableAnchorStore.cs`（OVRSpatialAnchor）、`TablePlacement.cs`。
3. **你讀 AI（注視放大破綻）。** 盯著某個 AI 約 1 秒，他的表演變大、會回應（「Why are you looking at me?」）。
   - 【程式】`H/A/Poker.AI/PlayerRead.cs:140`（GazeSeconds = 1）、`AiPlayer.cs:165`、`H/A/UX/IsdkGazeRig.cs`、`LiveLines.cs:45,58,71,84`。
   - 注意：Quest 3/3S 沒有眼動追蹤，實際是「頭部朝向」（`H/docs/specs/2026-10-12-mr-poker-design.md:89`）。影片不要寫 eye tracking。
4. **會說話、每人有自己的聲音。** 每句台詞有聲音＋頭上字幕；可選的語音對話（Gemini Live 加 ElevenLabs，經自家中繼）讓你直接問「How much do you have?」，數字會對照真實牌桌狀態檢查。
   - 【文件】`H/tasks/agent-brief.md:24-35`；【程式】`H/A/Characters/Speech/SpeechVoices.cs:20-27`。
   - 語音是選用，遊戲離線也能玩（`H/docs/submission/devpost.md:101`、`H/tasks/agent-brief.md:34`）。
5. **前 5 分鐘零文字教學。** 荷官 Dot 用「示範手」教，第 1 手被讀、第 2 手騙倒 Rocco，教學版本 v4（`H/A/Game/Flow/Tutorial.cs:27`；`TutorialScript.cs:10-14`）。
6. 輔助：寫實角色與牌桌美術（黃銅細框、墨綠絨布、襯線字，`H/tasks/realistic-todo.md:60-61,71`）、可存取性設定（字體大小、高對比、左手、Hints，`H/A/UX/MenuModel.cs:53`）。

### 1.3 真實可展示的功能清單（有程式或任務紀錄佐證）

| 功能 | 狀態 | 證據 |
|---|---|---|
| 赤手手勢全套（7 種動作） | 有 | 【程式】`H/A/Poker.Gestures/GestureRecognizer.cs`；【文件】回放正確率 90%：`H/tasks/todo.md:18`。實機手感：敲桌曾不穩，之後「待實機確認」（`todo.md:19`），我沒找到之後的確認紀錄 → 錄影前要親測 |
| 真桌面對齊＋虛擬桌備案＋雙手校準 | 有 | 【文件】`H/tasks/todo.md:14,35`；【程式】`TableAnchorFlow.cs` |
| 空間錨點（記住桌位）、陌生房間座位避牆 | 程式與測試完成，**未上機驗證** | 【文件】`H/tasks/todo.md:70`（「待實機驗證」） |
| 雙向讀心即時對話（77 句導演台詞＋ 4 個性） | 有 | 【文件】`H/tasks/todo.md:49-62`；【程式】`LiveLines.cs` |
| 讀人揭曉卡（「Rocco read you: weak — wrong!」） | 有 | 【程式】`H/A/Game/Flow/GameController.LiveTalk.cs:317-321`；`FoolRocco.cs:95-97` |
| 攤牌標題（Strong hand／Bluffing／On a draw／Unsure／Gave up） | 有 | 【程式】`H/A/Poker.AI/HandReveal.cs:44-54` |
| AI 學習（被同一種線索騙過後不太信） | 有，**只在同一局內**，換局／重開就歸零 | 【程式】`PlayerRead.cs:124-125,223-260,304-308`（`Reset()` 清掉信任值） |
| 「Not this time.」（第三手同招失靈） | **只在 Fool Rocco 情境** | 【程式】`FoolRocco.cs:41,84`；Fool Rocco 不在玩家選單，只有 `TELL_DEV_TOOLS=1` 開發建置（`H/docs/specs/2026-10-12-mr-poker-design.md:47`） |
| 荷官教學 v4（兩手固定牌） | 有 | 【程式】`Tutorial.cs:14-17,27`；`TutorialScript.cs` |
| 語音對話（Gemini Live＋ElevenLabs） | 有，但**實機對頭盔講話的驗證缺** | 【文件】`H/tasks/realistic-todo.md:52`：「即時對話未實際對頭盔講話驗證」；延遲 0.7 秒是桌上測試台數字（`devpost.md:47`「verify on Quest」） |
| 手腕／掌心選單（Resume、How to play、Move table here、Settings、New game、Quit） | 有 | 【程式】`H/A/UX/MenuModel.cs:50-54`（Settings 現為 Voice chat、Hints、Text size、High contrast、Left-handed、Back） |
| 結算看板 | 有，**只剩 Hands won、Biggest pot** | 【文件】`H/tasks/realistic-todo.md:99-103` |
| 5 人寫實桌（4 位對手＋荷官），72 fps | 有（預覽建置） | 【文件】`H/tasks/realistic-todo.md:51`（2026-11-03 實機 72 fps、聲音依角色） |
| 回訪問候（「Welcome back! N wins in M so far.」） | 有 | 【程式】`H/A/Game/Flow/DealerLines.cs:77`；`realistic-todo.md:102` |

**已移除／未完成（影片不能出現或宣稱）**
- 「Tells read」統計（結算看板與 Dot 開場白）：2026-11-04 移除（`H/tasks/realistic-todo.md:99-103`；`H/A/Game/Flow/GameStats.cs:45`）。`H/docs/submission/video.md:47` 的「end-of-game stats screen」要改成只剩 Hands won／Biggest pot 的看板。
- 手勢備援按鈕（Fold／Call／Check 的 Assist buttons）：2026-11-03 整個刪除（`realistic-todo.md:54-59`；`GameController.Assist.cs`、`AssistButtonsView.cs`、`FallbackAssist.cs` 在 git status 為 D）。`H/tasks/todo.md:69` 與 `H/tasks/agent-brief.md:59-62,79` 仍寫著 Assist，**已過時**。
- Fool Rocco 不在玩家選單（`MenuModel.cs:50-54` 沒有）。
- 其餘 3 位角色（Danny、Nora、Leo）沒有模型，「選對手 UI」未做（`realistic-todo.md:30-31`）。
- 多人連線、聯機同桌：未做（`H/docs/specs/2026-10-12-mr-poker-design.md:121`）。
- 沒有 Reason-to-come-back 型的進度系統（只有戰績與回訪問候）。沒有「用語音取代手勢」的操作（語音只是聊天）。

### 1.4 角色名字與個性

**關鍵發現（容易混淆）**：遊戲內顯示與說出口的名字是「座位名」Pip／Rocco／Vex／Silas／Dot（`H/A/Characters/CharacterSpec.cs:48,59,70,83,94`；`realistic-todo.md:52` 也寫「遊戲內名字仍用座位名而非寫實角色名」）。寫實 3D 模型有另一套名字（Marcus、Hank、Reginald、Jace，是 GLB 檔名）。預設陣容 `Marcus,Hank,Reginald,Jace` 依座位 Pip、Rocco、Silas、Vex 套用（`H/A/Characters/Realistic/RealisticPreview.cs:24,27`）。所以**畫面上 Hank 的臉在字幕與揭曉卡裡叫 Rocco**。

| 座位名（遊戲內說的名字） | 寫實模型（預設陣容） | 聲音 | 個性（AI 參數） | 台詞風格 |
|---|---|---|---|---|
| **Pip** 緊張新手 | Marcus | Eric | 緊、被動，破綻 92% 可信、表演強度 0.85（`Personality.cs:55-62`） | 話多、緊張、說溜嘴：「Why are you looking at me?」「I didn't do anything!」（`LiveLines.cs:49-60`） |
| **Rocco** 大嘴巴老手 | Hank | Callum | 鬆、兇，可信度 65%，30% 反向演（`Personality.cs:65-72`） | 嗆、挑釁、叫你 champ：「Took you a while to reach for those chips. Weak.」「Changed your mind? I saw that.」「You played me!」（`FoolRocco.cs:76,83`、`LiveLines.cs:41`） |
| **Silas** 老手（2026-11-02 新增，荷官右側） | Reginald | Bill | 緊凶、話少，可信度 55%（`Personality.cs:88-96`） | 乾冷幽默：「My coffee's getting cold.」「Seen enough, kid?」（`LiveLines.cs:75-86`） |
| **Vex** 冷面賭徒 | Jace | Liam | 平衡，可信度 35%，70% 反向演，話最少（`Personality.cs:75-82`） | 少、準、冷：「Doubt is expensive.」「Clever. Once.」（`LiveLines.cs:62-73`） |
| **Dot** 荷官（教學者） | 寫實女荷官 | Sarah | 非對手；教學、報牌、中性提醒 | 「Rocco reads your hands. Just play this one.」「He learns. Mix it up.」（`FoolRocco.cs:87-92`） |

- Erik（Brian 的聲音）在角色池裡但不在預設陣容（`realistic-todo.md:9,51`；`SpeechVoices.cs:20-27`）。
- 版本分歧：【程式】`GameSession.cs:28` 預設對手是 3 位卡通（Pip、Rocco、Vex）；5 人寫實桌需要 `TELL_REALISTIC_PREVIEW=1` 建置（`H/A/../Editor/Scene/MainSceneBuilder.cs:52-59,202`）。**交件版是哪一種，repo 沒有定論**（`realistic-todo.md:77` 說預覽 APK 建完會「重建卡通預設場景」）。使用者 10/29 決定「角色全部換寫實」（`realistic-todo.md:3`）。影片只能拍交件版實際長相。

---

## 2. 比賽規則（原文）

來源：官方頁 `https://start-developer-competition-26.devpost.com/`（及 `/rules`、`/details/judging`、`/details/special-awards`、`/details/faqs`），本機快照在 `S/`。

### 2.1 評分標準與權重
- 【規則原文】`S/judging.txt:89`：「All submissions will be evaluated based on the equally weighted criteria below」，四項，FAQ 明講各 25%：「Each criterion accounts for 25% of the total score」（`S/faqs.txt:184`）。
- **Innovation & Creativity**（`S/judging.txt:90-92`）：「Measures originality, creative ambition, and relevance to the chosen track… High-scoring projects differentiate themselves, show developer ingenuity, and uniquely leverage Meta device capabilities (e.g., spatial depth, hands-first design, gaze, passthrough mode, scene understanding, persistent anchors) to drive repeat usage.」
- **Experience Design**（`S/judging.txt:93-96`）：「Evaluates the quality of design, mechanics, and user journey tailored to the intended audience, emphasizing seated, hands-first, or specific context use. High-scoring entries ensure intuitive navigation from the moment of entry, clear onboarding, and a rewarding, habit-forming purpose.」「For entries that use passthrough, passthrough should be purposeful. The player's real room should meaningfully change the experience, whether a table becomes the play surface, walls host content, or UI adapts to the space, rather than a scene that plays identically with passthrough off.」
- **Technical Implementation**（`S/judging.txt:97-100`）：「Assesses technical expertise, reliability, and strategic use of platform tools, particularly the features that define hands-first experiences: gaze interactions (Unity/Unreal ISDK v207+), hand tracking, passthrough, spatial anchoring, and FoV-aware design. High-scoring entries must be performant on Meta Quest hardware (min 60 fps), solid and bug-free… correct use of scene understanding (MRUK), anchoring content to detected planes, surfaces, and room geometry, and holding up in rooms the developer never tested.」
- **Polish & Presentation**（`S/judging.txt:101-104`）：「Assesses the overall quality of the project submission, including UI/UX, art direction, and sound design. High-scoring entries are performant, visually cohesive… This includes high-quality submission materials (text, images, video) that effectively communicate the project's value. On your video/trailer: show real gameplay that honestly represents the experience. Lead with the story and what makes your project distinct. Don't lean on AI-generated video to carry the pitch.」
- 流程（`S/rules.txt:206-209`）：兩階段。Stage One 及格／不及格（「reasonably fits the theme and reasonably applies the required tools and features」）；Stage Two 用四項評分。「AI tools may also be used to assist in the judging, but final decisions will be made by human judges.」平手由評審投票（`S/rules.txt:233`）。
- 評審怎麼玩：「Judges will review apps on a Meta Quest 3 or Quest 3S.」（`S/faqs.txt:172`）。總覽頁：「judges can't score what they can't launch」（`S/overview.txt:165`）。【推論】影片是第一印象與 Polish & Presentation 的材料，但評審會實際戴頭盔玩；影片宣稱的東西玩家要玩得到。

### 2.2 獎項定義（`S/rules.txt:131-135,237-300`）
- 賽道 **Gaming**：「Games designed for hands-first (required) or eyes and hands. Puzzle, strategy, casual, social, narrative—genres that thrive seated, without controllers.」
- 分組 **New Experience**：「Project is conceived of and built within the competition window (starting September 24, 2026). No pre-existing codebases, no shipped titles, no early access builds repurposed」。
- **Best New Gaming Experience**：冠軍 $100,000、亞軍 $50,000（`S/rules.txt:254-264`）。每件作品只能拿一個獎，取最高金額（`S/faqs.txt:188`；`S/rules.txt:237`）。
- 特別獎（`S/special-awards.txt`，均 $25,000，Judge's Choice $20,000×2）：
  - **Best Agentic Interaction**：「AI as a core part of the experience: spatial agents, voice-first interaction, procedural content, context-aware assistance.」
  - **Best First Five Minutes**：「The craft of the opening: how fast it explains itself, teaches controller-free interaction without a wall of text, and delivers a payoff before the headset comes off.」
  - Boldest Original Concept：「Rewards the idea that couldn't exist anywhere else… Judged on originality and on whether the experience would fall apart on any other platform.」（撲克在別的平台也能玩，這個獎對 Tell 不利，【推論】）
  - Best Reason to Come Back、Best Accessibility Forward（「voice alternatives for hand interactions, eyes-only navigation…」）、Best Social & Multiplayer：Tell 現況不符（見 §1.3）。
- 手要能全程操作：「Fully usable with hands end-to-end. Controller support optional.」測試：「Can someone complete the entire experience without ever pairing a controller?」（`S/rules.txt:152`）。
- 準則（非硬性）：seated「airplane seat test」、快速啟動與「Satisfying in ≤10 minutes」、「Original, not a wrapper」（`S/rules.txt:156-165`）。

### 2.3 影片規定
- 【規則原文】`S/rules.txt:170`：「Demonstration video of your Project that should be less than three (3) minutes and showcase footage of your Project as viewed on a Meta Quest device or via XR Simulator or another equivalent emulator. The video must be uploaded to and made publicly visible on YouTube or Vimeo and a link to the video must be provided on the submission form on the Competition website. Judges are not required to watch beyond three minutes.」
- FAQ（`S/faqs.txt:214-218`）：影片是必要的；「We recommend keeping your video concise (under 3 minutes) and clearly demonstrating your project's key features and innovations.」
- 內容誠實：見 §2.1 Polish 原文（真實遊玩、先講故事與差異、不要靠 AI 生成影片撐場）。
- 英文：「Projects must be entirely in the English language or include English subtitles. All Entry materials must be in English」（`S/rules.txt:184`）→ 影片字幕用英文。

### 2.4 配樂、版權、肖像、商標（沒有專門的「配樂」條文）
我在規則、FAQ、總覽、評審頁全文搜尋 music／audio／license，**沒有配樂專用規定**。適用的是一般條文：
- `S/rules.txt:187`：「Projects must not infringe, misappropriate, plagiarize, or violate any rights of any third-party including, without limitation, copyright (including moral rights), trademark…」
- `S/rules.txt:178`：「If a Project uses or relies upon any third-party tools, code, or assets, the participant must be authorized to use them in accordance with any terms and conditions or licensing requirements」
- `S/rules.txt:186`：「Projects may not mention or depict any identifiable person other than Entrants.」→ 影片只能出現開發者本人。
- `S/rules.txt:190`（商標那條）：「must not contain any commercial or corporate advertising (including, without limitation, corporate logos, brand names, and slogans), recognizable branded products, or commercial artwork.」→ 桌上杯子、筆電 logo、海報要遮；Gemini／ElevenLabs logo 不要出現。
- `S/rules.txt:189`（關聯那條）：「must not create or imply any association between Sponsor and any individual, entity…」【推論】結尾卡寫「Meta Quest 3 / 3S」是平台名稱的事實陳述，風險低，但不要用 Meta 標誌或暗示背書。
- 【推論】YouTube 的 Content ID 會對有版權的音樂下手；配樂用有書面授權的曲子或自製，並保留授權文件（`H/docs/submission/video.md:113` 已這樣規劃）。
- 提交的作品不得在 11/18 12:00 PT 後修改（`S/judging.txt:107`；`S/rules.txt` §4）。影片連結是提交表單的一部分，同樣應視為鎖定【推論】。
- 日期：提交截止 2026-11-18 12:00 PT；得獎公告約 2026-12-11（`S/rules.txt:103-104`）。

---

## 3. 評審視角

### 3.1 前 10 秒必須證明什麼（【推論】，依評分標準與 Polish 的影片原文推出）
1. **這是真的 Quest 手部追蹤＋真房間，不是特效或預錄動畫。** 第一個畫面就要是頭盔視角的真實桌面、你的真手，沒有控制器。
2. **一個看得懂的衝突**：AI 說出你的真實動作（「Took you a while to reach for those chips. Weak.」）。這句話同時證明「AI 讀你的手」和「有聲音有角色」，是整支片最短的差異化證明。
3. **品質訊號**：角色臉、字幕可讀、聲音乾淨。評審一看到閃爍、破面、糊字就會開始扣 Polish。
4. 不要先放 logo、黑畫面、空房間（`H/docs/submission/video.md:111` 已同意：硬切、第一秒就有 Rocco 的聲音）。

### 3.2 前 60 秒必須證明什麼（【推論】）
- **雙向**：你能讀 AI（盯著看→他演更大）、他能讀你（說出你的真動作）、你能騙他（「You played me!」）。三件都要在 60 秒內各出現一次。
- **這是完整可玩的遊戲**，不是 tech demo：至少有一手完整的 下注→對手反應→攤牌→勝負 與揭曉卡。
- **真桌面有意義**（Experience Design 的 passthrough 條文）：桌子貼在你的真桌上、敲桌有觸感，並且「關掉 passthrough 就不是同一個遊戲」。
- **赤手＋多種手勢**：至少 3 種手勢清楚看到手（咪牌、捏丟、敲桌），沒有按鈕與控制器。
- **零文字上手**：Dot 的示範手，對應 First Five Minutes 的「without a wall of text」。
- 以上順序是推論；`H/docs/submission/video.md:11-19` 的結構（0–20 秒鉤子、20–35 真桌、35–60 手勢、60–85 你讀、85–105 它讀你）已大致符合。

### 3.3 每個評分標準對應的畫面

| 標準 | 影片該展示 | 我能給的素材來源 |
|---|---|---|
| Innovation & Creativity | ① 讀你的手→你反利用（Rocco 叫破→你故意慢→「You played me!」）② 盯著 Pip／Vex 他們的反應 ③ 賭桌放在你自己的桌上、敲桌 | `FoolRocco.cs:76-84`；`LiveLines.cs:45,58,71`；`video.md:27-33` |
| Experience Design | ① 荷官示範手教學（零文字）② 桌子貼上真桌、換個位置還能重放 ③ 一手完整牌局後有揭曉卡（讀對／讀錯）④ 回訪「Welcome back」 | `Tutorial.cs:14-17`；`GameController.LiveTalk.cs:317-321`；`DealerLines.cs:77` |
| Technical Implementation | ① 兩張不同桌子／房間都能貼對（MRUK、虛擬桌備案）② 72 fps 的證據（可選：OVR Metrics 數字閃過 2 秒，【推論】）③ 手勢各種姿勢都準、無誤觸 ④ 注視用 ISDK | `todo.md:14,35,66`；`realistic-todo.md:51`；`IsdkGazeRig.cs` |
| Polish & Presentation | 角色臉與表情、字幕（用同一種遊戲內字幕）、每人有自己的聲音、桌面材質與黃銅牌子、乾淨的取景、英文字幕、配樂不搶台詞 | `realistic-todo.md:60-61,71`；`SpeechVoices.cs` |

### 3.4 會被扣分的畫面與宣稱
**畫面**
- 開發工具外露：`TELL_DEV_TOOLS=1` 建置會把手勢錄製面板與手部除錯讀數放在桌上（`video.md:62`）；只要入鏡就像沒完成。若該情境只能在開發建置拍，要裁掉或遮掉。
- 寫實角色殘留瑕疵：Reginald 下巴暗缺口、Erik 臉頰接縫、Jace 太陽穴接縫、Marcus 鏡腳鋸齒（`realistic-todo.md:26,29`）；Vex 座泡泡偏低（`realistic-todo.md:52`）。鏡頭別對這些部位久留，或錄影前先確認是否已修。
- 文字太小／被角色擋住：字幕在低眼位會浮在角色身上、示範手字幕偶爾退而求其次（`todo.md:89-92`）。剪輯時確認 1080p 手機畫面看得懂。
- 房間太暗追蹤丟失：系統跳「Finding position in room」，App 畫面沒出來（`realistic-todo.md:66`）→ 開燈、均勻打光（`video.md:102`）。
- 追蹤抖動、手勢誤判、AI 在玩家沒動作時亂說話（有被 spam 的歷史，`todo.md:62`）。
- 把攝影機放太近、頭快速轉動（單眼畫面看起來暈）。

**宣稱（與實際不符會傷 Technical 與「honest」那條）**
- 「AI 會學」：實際只在同一局內，換局歸零（`PlayerRead.cs:304-308`）。可說「被騙過一次就不太信那個訊號」，不要說「跨場記住你」。
- 「Not this time.」第三手：只在 Fool Rocco 情境（開發建置，玩家選單沒有，`MenuModel.cs:50-54`）。如果影片放這一段，評審自己玩不到。可改用正式牌局裡的 ReadFooled 台詞（「You faked that? Nice.」`LiveLines.cs:47`）或教學第 2 手。
- 「眼動追蹤」：Quest 3/3S 沒有。只能說「盯著看（頭部朝向）」。
- 「Tells read N%」或任何讀人準確率：已移除，不可出現；文件舊稿（`video.md:47`）要改。
- 「Assist／Fold-Call 按鈕」：已刪。
- 語音對話延遲數字（0.7 秒）、「幾乎立刻回話」：是測試台數字，Quest 上沒驗證（`realistic-todo.md:52`；`devpost.md:47`）。語音那段要用真錄影，不加速、不剪掉等待。
- 自我對弈數字（+25.4 BB/100、−4.0）：是模擬，放在影片裡容易被當成真人實測。
- 「記住桌位（persistent anchor）」「陌生房間也穩」：程式有、實機未驗（`todo.md:70`）。沒驗證就不要在片裡打字宣稱；拍到的才算。
- 「5 人桌」「4 位對手」：預覽建置才有，正式卡通版是 3 位。以交件版為準。
- 一局約 8 分鐘（`devpost.md:38`）：節奏實機未驗（`todo.md:26`）。影片不要寫「8 minutes」。
- 角色名稱：畫面是 Hank 的臉、字幕卻叫 Rocco。字幕與旁白一律用字幕上的名字（Rocco），不要在片中另外叫 Hank。
- 版權／AI 披露：角色 3D 模型由 Tripo 生成後換上 Rocketbox 臉（MIT，`H/Assets/_Project/Art/Characters/*/CREDITS.md`），台詞由 ElevenLabs 合成，語音對話用 Gemini。規則只針對「AI 生成影片」，但 Polish 條文也提醒「Don't lean on AI-generated video」。影片全部是真實頭盔錄影即可；不要隱瞞 AI 角色與語音（Devpost 文字有寫）。Tripo 輸出的商用條款我沒查到（repo 無紀錄），列為未確認。

### 3.5 最強的「哇」時刻候選（依力道排序，【推論】）
1. **Rocco 叫破你的真實遲疑，你再反過來騙他**：「Took you a while to reach for those chips. Weak.」→ 你刻意慢伸手下注 → 「Took your time… you're bluffing. I call.」→ 攤牌 → 「You played me!」＋ 揭曉卡「Rocco read you: weak — wrong!」。一個 15–20 秒的完整弧線，一次證明全部差異化（`FoolRocco.cs:76,81,83,95`）。
   - 取得方式：開發建置的 Fool Rocco；或正式建置的教學第 2 手（Dot 會講「wait a few seconds before you bet」，有被引導的味道，但評審玩得到，`TutorialScript.cs:10-14`、`FoolRocco.cs:89`）。
2. **手伸出去又縮回，Rocco 立刻：「Changed your mind? I saw that.」**（`LiveLines.cs:41`）。需要鏡頭同時拍到手縮回的動作與字幕，反應要在 1 秒內。
3. **你盯著 Pip 看，他慌了：「Why are you looking at me?」**（`LiveLines.cs:58`）。笑點加說明了「注視」功能，再接 Vex 的冷反應「Find anything?」（`LiveLines.cs:71`）。
4. **整張牌桌貼上你真的桌子，手指敲下去聽到真實的敲聲＝過牌**（Experience Design 的 passthrough 條文要的畫面）。最好能配一個「不同桌子」的快切。
5. **兩手一起推 All-in**＋倒數＋荷官報牌，視覺最大、最容易看懂，但不是差異化，只當節奏高點。
6. **對 Rocco 說話，他用真實底池數字回答**。差異化強（Agentic Interaction 獎）但目前實機未驗、延遲不確定，只有在錄到穩定的一次才放。

---

## 4. 文件與程式矛盾（以程式為準）

| 文件 | 說法 | 實際 |
|---|---|---|
| `H/tasks/agent-brief.md:59-62,79`、`H/tasks/todo.md:69` | 有手勢備援按鈕、Settings 有「Assist buttons」 | 已刪（`realistic-todo.md:54-59`；`MenuModel.cs:53` 無此項） |
| `H/docs/submission/video.md:47` | 「end-of-game stats screen」 | 看板只剩 Hands won、Biggest pot；Tells read 已拿掉（`realistic-todo.md:99-103`） |
| `H/docs/specs/2026-10-12-mr-poker-design.md:46` | Settings 7 項含 Assist buttons | 現為 6 項（`MenuModel.cs:53`），spec 與 agent-brief 未同步 |
| `H/docs/submission/devpost.md:22` | 「three cartoon AI opponents and a dealer」 | 寫實預覽是 4 位對手＋荷官；交件版未定（§1.4） |
| `H/docs/submission/devpost.md:38` | 「persistent stats and a per-opponent record」「~8-minute game」 | per-opponent 紀錄我沒在程式找到對應；8 分鐘未驗 |
| `H/docs/submission/video.md:13` 等 | Rocco 讀你的鉤子用 Fool Rocco | 該情境只在開發建置；正式建置用教學 v4 第 2 手（`Tutorial.cs:14-17`） |
| `H/docs/specs/…:9` | 差異化說「查不到純手勢＋對齊真實桌面＋會讀人的 AI」 | 去年有 Liar's Roulette 同類概念，沒得獎（`past-winners.md:129`）；差異化要靠「讀你的手而且你能騙它」做出來 |
| `H/docs/specs/…:121` 與 `devpost.md:67` | 寫實角色列為「未來」 | 使用者 10/29 起已全面寫實化（`realistic-todo.md:3`） |
| `H/docs/specs/…` §5 | 角色 Pip／Rocco／Vex | 寫實角色另有名字（§1.4） |

---

## 5. 沒做到的事／不確定的點

- Devpost 規則頁 403，用 curl＋瀏覽器 UA 取得；快照日期 2026-11-05，之後若官方更新（例如影片條文）我不會知道。
- 評審人選與口味：只知道 Dilmer V（XR 教學 YouTuber，來自 repo 紀錄），其他不明；「評審多半不懂撲克」是 repo 的假設（`todo.md:65`），沒有證據。
- 交件版長相（卡通 3 位／寫實 5 人桌）repo 沒有定論；所有「角色」描述都需要使用者或 lead 確認。
- 我沒有戴頭盔、沒跑遊戲、沒看 `Builds/Quest/` 裡的 APK 或實機截圖，**所有功能狀態來自程式碼與任務紀錄，不是實際畫面**。功能存在不等於實機手感好。
- 實機狀態我沒找到的紀錄：敲桌過牌、咪牌在最新建置的穩定度；語音對頭盔講話；persistent anchor；陌生房間。
- 配樂、版權：規則沒有專門條文，上面是一般條文的推論；Tripo、ElevenLabs 的商用條款沒查。
- `vr-poker`（main）與 `vr-poker-hank` 的 HEAD 相同，main 有 10 個未 commit 檔案，我沒逐一比對差異，以 hank 分支為準。
- 附帶事故：寫檔時我把一個測試檔寫到了一個打錯的路徑 `/private/tmp/claude-501/-Users-laichanean/…/placeholder`，隨即用 `rm -r` 刪掉整個 `/private/tmp/claude-501/-Users-laichanean` 目錄；我不確定那個目錄在我建立前是否已有其他內容，若有其他 session 的暫存被刪，請告知。沒有動任何專案檔案。
