# 知名牌手風格研究：8 位 AI 對手原型（2026-10-28）

> 用途：把 8 位知名牌手的打法與桌上行為，換算成 MR 撲克（4 人桌、玩家對 3 個 AI）的個性參數與台詞／演出方向。
> 研究方式：用網路搜尋與頁面擷取。**沒有看影片、沒有查 HUD 資料庫**（HighStakesDB、PokerTracker 類站台都沒進去），詳見最後「沒做到的事」。

## 0. 怎麼讀這份筆記（標記規則）

- **[來源說]**：頁面內容直接支持的事實，附 URL。
- **[推估]**：我從描述反推的結論，沒有直接數據。所有 0–1 參數預設都是推估，除非標 **[數據]**。
- **[查不到]**：查過沒有，並列出查過的來源。
- 工具限制：頁面擷取工具會先把頁面摘要再回給我，所以引用的數字與句子是「摘要後的內容」，不是我逐字比對原文。要把任何數字放進對外文件前，請回原頁面核對。
- 數據品質整體很差：**全部 8 位只有 Hellmuth 與 Ivey 有「單場 VPIP」數據點**，而且都是 8–10 人桌、單場、帶 straddle 的高額現金桌。4 人桌的 VPIP 本來就會比這些數字高，換算時我有往上調（見 §3 說明）。

## 1. 人選與取捨

**入選 8 位**：Phil Ivey、Daniel Negreanu、Phil Hellmuth、Tom Dwan、Gus Hansen、Vanessa Selbst、Doyle Brunson、Antonio Esfandiari。

**沒選**
- **Fedor Holz**：搜到的資料多是單手解析（PokerNews 的手牌分析、Triton 的 solver 對照），風格是「極度攻擊、solver 派」，跟 Selbst 的「數學派＋翻前高壓」重疊太多，區隔不夠。他自己說過一句大意是「大家覺得我很瘋，但大牌時我都有牌」，這種形象有趣，但留作備選。來源：https://www.pokernews.com/strategy/fedor-holz-discusses-his-big-day-5-bluff-in-the-wsop-main-ev-22265.htm （僅由搜尋摘要得知，未整頁讀過）
- **Stu Ungar**：本輪沒有深入查（見「沒做到」）。另外他已過世、影像資料少，也不利於後面的肖像權議題。**這是時間取捨，不是查過後排除**。

**沒替換候選名單以外的人**。需要說明的缺口：8 位裡沒有真正的「被動跟注型」。Hellmuth 是最接近的（被評論員批評打得太被動），但他不是 calling station。若遊戲需要這個原型，建議用虛構角色補，不要硬套真人。

**區隔度（推估）**

| 軸 | 兩端 |
|---|---|
| 緊 ↔ 鬆 | Hellmuth（緊）… Hansen / Dwan（鬆） |
| 被動 ↔ 兇 | Hellmuth（偏被動）… Dwan / Selbst / Hansen（兇） |
| 話多 ↔ 冷面 | Negreanu / Esfandiari / Hellmuth（話多）… Ivey（冷面） |
| 讀人 ↔ 數學 | Negreanu / Ivey（讀人）… Selbst（數學／範圍推理） |

---

## 2. 逐人筆記

### 2.1 Phil Ivey — 冷面全能型（TAG 偏 LAG）

**1. 風格與代表打法**
- [來源說] 公認的「全能最強」之一，11 條 WSOP 手環全在非 Hold'em 項目。https://en.wikipedia.org/wiki/Phil_Ivey
- [來源說] 2022 年 Hustler Casino Live（HCL）$100/$200/$400 現金桌，他整晚偏緊（拿不到牌），晚點才變兇；河牌用 A♠Q♠ 加注到 $110,000 被葡萄牌型（flush）跟注，輸掉超過 $125,000。https://www.pokernews.com/news/2022/09/phil-ivey-hustler-casino-live-adelstein-42188.htm
- [來源說] PokerNews 整理的 Ivey 神讀牌：對 Negreanu 在多聽牌面用 Q7 直接 all-in 逼棄牌；對 Polk（2015 Aussie Millions）在 Polk 加注後長考、把籌碼拉回再跟注。https://www.pokernews.com/news/2022/01/phil-ivey-top-poker-reads-40477.htm
- [來源說] 他自己的觀點：把每個對手當個體看，「一般化的 tell 會讓你破產」。https://ca.sports.yahoo.com/news/phil-ivey-knows-makes-good-215400845.html

**2. 可量化傾向**
- [數據] 單場 VPIP 26%（HCL 2022，$100/$200/$400，多重 straddle，他整晚拿不到牌；PFR 與手數未報）。https://www.pokernews.com/news/2022/09/phil-ivey-hustler-casino-live-adelstein-42188.htm
- 其餘欄位見 §3，全為 [推估]。

**3. 下注與籌碼習慣**
- [來源說] 在 PokerNews 的五手樣本裡，他常用 min-raise 開弱／中等牌（K-Q、9-9、9-6），之後逐街加大下注，最大的動作是 400,000 河牌下注、870,000 all-in、以及一次 200,000 的轉牌超池下注。文章明說「沒有單一固定的尺寸模式」。https://www.pokernews.com/news/2022/01/phil-ivey-top-poker-reads-40477.htm
- [來源說] 思考節奏：在 Polk 那手長考、差點下注又把籌碼拉回；在 Negreanu 的 check-raise 面前則是立刻跟注。同上來源。
- [查不到] 出籌碼的物理方式（單手／雙手、堆疊整理習慣）。查過：上述 PokerNews、PokerListings、Wikipedia。

**4. 桌上行為與破綻**
- [來源說] 話極少，可以幾個小時只講幾個字；肢體語言幾乎不洩漏資訊；有一次用「像被車燈照到」的亂瞄眼神誤導對手。https://www.pokerlistings.com/poker-strategy/poker-psychology/ivey-on-the-couch （PokerListings 這篇自稱「非官方」心理分析，作者主觀成分高。文中「拒絕握手」是讀者留言，我沒採用。）
- [來源說] 「招牌凝視」在 PokerNews 那篇歸因於 McDonald 的描述，不是 Ivey 自述。
- [來源說] PokerListings 分析 Negreanu 對 Ivey 的超池詐唬時，注意到 Ivey 棄牌前表情「帶著笑意、不買單」。https://www.pokerlistings.com/daniel-negreanus-overbet-bluff-representing-nothing-explained
- [查不到] 被公開討論過的具體 tell。查過：上列所有頁面。搜尋結果也沒有。**這本身就是個性資料：他沒有已知的破綻。**
- 換算（[推估]）：Talkativeness 低、Expressiveness 低、TellCredibility 低（表演不可信）、ReverseActing 中、ReadSensitivity 極高。

**5. 台詞方向（原創描述）**
- 幾乎不講話；偶爾一句極短的、不帶情緒的陳述。
- 棄牌時只輕輕點頭或微笑，不解釋。
- 被詐唬成功時不炫耀，只是安靜地把牌蓋掉、眼神停在你身上。

---

### 2.2 Daniel Negreanu — 話多讀人的「小球」流（鬆被動偏均衡，靠讀人）

**1. 風格與代表打法**
- [來源說] 「small ball」：多玩牌、小下注、風險小，翻後才做艱難決定；自己說翻前很少 3-bet，常常只跟注。https://www.fcnp.com/2009/02/19/daniel-negreanu-on-poker-betting-strategies-in-small-ball-poker/
- [來源說] 加注大小依「對手強弱」變，不依自己牌力變：面對強對手開到 4–5 倍大盲，讓對手看不出你的牌；面對弱對手開小一點。同上。
- [來源說] 2019 WSOP Main Event，他用聊天加範圍推理，棄掉第三大的順子（10♠8♠），對手實際是 A-A。PokerNews 稱他的 table talk「world class」。https://www.pokernews.com/news/2019/07/daniel-negreanu-big-fold-main-event-34760.htm
- [來源說] 2026 年 8 月 HCL 首秀 $100/$200，前兩小時輸約 $160,000，有一手 J♥J♣ 小盲 3-bet 到 $11,500 輸給 Q♠Q♥ 的陷阱。https://www.pokernews.com/news/2026/08/daniel-negreanu-hustler-casino-live-52257.htm

**2. 可量化傾向**
- [查不到] 任何 VPIP／PFR 數據。查過：PokerNews HCL 首秀兩篇、Table 1 Vegas（該篇列表沒有他）、Pokerfuse。HCL 網站連結因為網域無法解析沒讀成。
- [來源說] 加注尺寸：翻前 4–5 倍（對強者）；PokerListings 分析的一手：翻前開 $1,600（約 4 倍）、翻牌約 1/4 池、轉牌約 1/2 池、河牌超池 $36,000 打 $21,900（約 1.6 倍池）。https://www.pokerlistings.com/daniel-negreanus-overbet-bluff-representing-nothing-explained
- 其餘 [推估]。

**3. 下注與籌碼習慣**
- 見上：翻前偏大（對強手）、翻後小、河牌偶爾超池。
- [查不到] 出籌碼方式與思考時間（PokerNews 那篇明說沒有報他的思考時間）。

**4. 桌上行為與破綻**
- [來源說] 他把「談話」當蒐集資訊的工具：問無關緊要的輕鬆問題看反應；自嘲獨白看對手是放鬆還是僵住；直接問「我棄牌你會秀牌嗎？」觀察對手是否急著影響他的決定。https://www.fcnp.com/2008/05/08/negreanu-on-poker-interrogating-an-opponent/
- [來源說] 他的啟發式：放鬆、答得順＝牌強；不安＝詐唬；詐唬者在乎你怎麼做，強牌常無所謂；靜止不動可能是詐唬。同上。
- [來源說] 他自己寫的重點是：翻前或最晚轉牌就要抓 tell。https://www.fcnp.com/2007/10/31/daniel-negreanu-on-poker-reading-tells-early-when-it-counts/ （由搜尋摘要得知）
- [重要更正] 你的題目提到「Negreanu 會講出你的牌」。**我沒有找到來源支持這句**：搜尋顯示他是用談話「套反應、縮範圍」，不是把對手的牌講出來。若遊戲要做「報牌」演出，要標明是設計，不是考據。
- 換算（[推估]）：Talkativeness 極高、Expressiveness 中高、TellCredibility 中、ReverseActing 中高（自嘲獨白、偽裝不安是工具）、ReadSensitivity 極高。

**5. 台詞方向**
- 親切、愛聊的語氣，常用輕鬆問題套你的反應（問你吃飯沒、問你是不是學過誰的打法）。
- 自嘲式碎念：「我是不是又玩太鬆了？」然後觀察你。
- 棄牌或跟注前會跟你「對話」，像在做訪談，而不是在對抗。

---

### 2.3 Phil Hellmuth — 自我形象與實際打法落差最大的緊型（Nit，會爆）

**1. 風格與代表打法**
- [來源說] 他自稱最佳範圍約 12–18%，讀得準的日子可拉到 40–50%；2012 WSOP Europe 決賽桌他玩了 48% 的手，並稱自己「像瘋子一樣詐唬」，但說別人仍當他是緊的。https://www.pokernews.com/strategy/interview-phil-hellmuth-part-1-21048.htm
- [來源說] 2025 年 No Gamble, No Future 單集 $50/$100：VPIP 11%，評論員批評他「太被動」「怕」。K♣Q♦ 用跟注（limp）$100 進池，翻牌後多街過牌，之後用 K 高下 $2,500，被頂對秒跟（原文對是哪一街下注的敘述含糊）。https://www.pokernews.com/news/2025/09/phil-hellmuth-plays-tight-49679.htm
- [來源說] 2023 HCL：VPIP 31%、PFR 約 8%（全桌最低），輸 $158,600。有一手用 K-high 在 A-Q-8-7-5 河牌跟注 $20,000，被 Q-Q 擊敗，評論員懷疑是讀錯。https://www.pokernews.com/news/2023/02/phil-hellmuth-hustler-casino-live-43045.htm
- [來源說] 「Hellmuth's Home Game」第 24 集：面對 $12,950 all-in 想了超過 8 分鐘後棄掉三條，河牌 K 若跟注會贏；他事後替自己辯護。https://www.pokernews.com/news/2026/05/hellmuths-home-game-episode-24-recap-51248.htm
- [搜尋摘要，未整頁讀] 2026 年 BetRivers 直播他在錦標賽接近 100% VPIP，是促銷用的特技。https://www.pokernews.com/news/2026/02/phil-hellmuth-betrivers-poker-stream-50718.htm

**2. 可量化傾向**
- [數據] VPIP 11%（2025，$50/$100，單集）；VPIP 31%／PFR 約 8%（2023 HCL，單場）；自述範圍 12–18%（常態）、40–50%（讀得準時）、48%（2012 WSOPE 決賽桌）。**同一個人，單場 VPIP 從 11% 到 31%（到近 100% 特技）**，波動極大。
- 翻前 VPIP−PFR 差距大（31 對 8）＝多數時候用跟注進池。這是他「被動」的硬證據。

**3. 下注與籌碼習慣**
- [來源說] Home Game 第 24 集：7-7 開 $200；河牌用葡萄牌型下 $300（小）；面對 Rob 的 $800 加注到 $3,000（約 3.75 倍）。同上 Home Game 連結。
- [來源說] 思考時間：8 分鐘以上（Home Game）；WSOP 2025 Main Event 一次 all-in 後想了超過 3 分鐘，被叫時鐘。https://www.pokernews.com/news/2025/07/phil-hellmuth-and-son-play-wsop-main-event-49076.htm （由搜尋摘要得知）
- [查不到] 出籌碼的物理方式。

**4. 桌上行為與破綻**
- [來源說] 自述：不覺得自己 tilt，但會對「不公平的壞運」大聲抱怨（「怎麼可能」），事後一定道歉握手；用 McEnroe 類比；說電視只放他發飆的片段，多數時間安靜或開玩笑。https://www.pokernews.com/strategy/interview-phil-hellmuth-part-2-21058.htm
- [來源說] 他說自己是「讀人派」，數學簡單，讀人才是關鍵，且「天生有或沒有」。Part 1 連結同上。
- [來源說] 反差：評論員多次批評他「怕」；2023 一手 K-high 河牌被 Q-Q 擊敗。**自我形象（神級讀牌）與可觀察結果（被質疑）之間有落差，這是很好的遊戲鉤子**（[推估]）。
- [查不到] 具體 tell（他的 Part 2 訪談明說沒談）。
- 換算（[推估]）：Talkativeness 高（尤其抱怨時）、Expressiveness 極高、TellCredibility 高（情緒是真的）、ReverseActing 很低、ReadSensitivity：自述極高、實測存疑，遊戲給中高。

**5. 台詞方向**
- 贏牌時帶著自信的小得意，輸牌時誇張地不可置信，強調「這手怎麼可能被跟」。
- 棄牌前先唸一段冗長的讀牌理由，堅持自己「只有我能棄這手牌」。
- 發完飆後立刻客氣地道歉、握手，完全不記仇。

---

### 2.4 Tom Dwan — 超高壓、愛大額詐唬的 LAG

**1. 風格與代表打法**
- [來源說] PokerNews 2022 稱「loose-aggressive」，擅長單挑現金桌，成名於 Full Tilt 的 "durrrr"。https://www.pokernews.com/news/2022/03/the-rise-and-rise-of-tom-durrrr-dwan-40819.htm
- [來源說] 2009 對 Sammy George：按鈕位 7-2 不同花開 $6,000，翻牌 $10,000（12,000 池）、轉牌 $48,000，河牌 all-in 約 $350,000 打 $162,000 池（約 2.2 倍池），逼 George 蓋掉兩對。PokerListings 評論：必須約三分之二的時間成功才獲利。https://www.pokerlistings.com/hand-analysis/tom-dwan-drops-a-350k-hammer-on-sammy-george
- [來源說] 2023 HCL $3.1M 底池：Fei 開 $30,000，Dwan 3-bet 到 $100,000，Fei 4-bet 到 $275,000，Dwan 約一分鐘後跟注；翻牌轉牌他過牌後跟注，河牌想了將近 4 分鐘，口頭推理後用一對 Q 跟注。https://uk.news.yahoo.com/crypto-millionaire-tried-run-bluff-030321853.html
- [來源說] 2024 HCL 一手：在深籌碼位置上用 7-7 跟「瘋子」的擠壓，翻牌加注、轉牌加注到 $48,000，對手 all-in 他立刻跟。https://www.pokernews.com/news/2024/06/tom-dwan-on-hustler-casino-live-46269.htm

**2. 可量化傾向**
- [查不到] VPIP／PFR／3-bet 的公開數據。查過：Wikipedia、PokerNews 2008 側寫與 2022 專文、PokerNews 2024 HCL、DeucesCracked 頁面（搜尋摘要稱數字不可靠）、搜尋「VPIP 30/40/50%」無結果。HighStakesDB 是最可能有的地方，我沒進去。
- [來源說] 下注尺寸（Sammy George 一手）：翻牌約 0.83 池、轉牌約 0.73 池、河牌約 2.2 池。單手樣本。

**3. 下注與籌碼習慣**
- 見上：翻前開得很寬，後街大額、偶爾超池。強牌時快，大決定時可以想到 1–4 分鐘（HCL $3.1M 手：約 1 分鐘、1 分鐘、近 4 分鐘）。
- [查不到] 出籌碼的物理習慣。

**4. 桌上行為與破綻**
- [來源說] 他的螢幕名 "durrrr" 是刻意選來惹毛輸給他的對手；他自己說厭倦無意義的對視，坦承是自己把「盯人」風氣帶起來的。https://en.wikipedia.org/wiki/Tom_Dwan
- [來源說] Galfond 評他「每分鐘的想法最多」。同上。
- [來源說] 2023 那手他會被問到剩多少籌碼，簡短答了；事後稱讚對手「打得很好」。Yahoo 連結同上。
- [查不到] 被公開討論的具體 tell。
- 換算（[推估]）：Talkativeness 中、Expressiveness 低到中、TellCredibility 低中、ReverseActing 中高、ReadSensitivity 高。

**5. 台詞方向**
- 語氣隨意、帶點挑釁的幽默，像在玩一場「遊戲」而非比賽。
- 大注之後會安靜地盯著你，偶爾丟一句「你確定嗎？」。
- 輸牌時淡淡一句稱讚對手，不見情緒起伏。

---

### 2.5 Gus Hansen — 「大丹麥人」、極鬆極兇的瘋子形象

**1. 風格與代表打法**
- [來源說] 維基百科標「Ultra-Loose Aggressive」，但該段風格敘述被標 citation needed，我不單獨採信。https://en.wikipedia.org/wiki/Gus_Hansen
- [來源說] 2011 年 CardPlayer 專訪：早期職業生涯的攻擊性讓觀眾和職業牌手都看不懂；他後來收斂，不再玩太鬆，並針對對手調整；他把牌桌當戰場、享受心理戰。https://www.cardplayer.com/?p=1035420
- [來源說] 2007 Aussie Millions 冠軍，書《Every Hand Revealed》逐手記錄 329 手；PokerNews 書評：他常看翻牌再施壓、認為翻前 limp 很少是對的、位置上翻後跟注比大家想的更常正確、專門利用對手每個盲注級別首尾兩圈的膽怯。https://www.pokernews.com/news/2008/03/book-review-gus-hansen-every-hand-revealed-4856.htm
- [來源說] 他在 High Stakes Poker 以 5♦5♣ 擊敗 Negreanu 的 6♠6♥，贏 $575,700（該節目第五大池）。維基百科同上。

**2. 可量化傾向**
- [查不到] VPIP／PFR。查過：Wikipedia、PokerNews 書評（明說沒有進池率）、CardPlayer 2011、搜尋 Full Tilt／HighStakesDB（只有盈虧：Full Tilt 累計約 −$20.7M）。
- [來源說] 書評例子：三人底池以 66k 打 118k（約 0.56 池）讓兩人都棄牌。
- 其餘 [推估]。

**3. 下注與籌碼習慣**
- [查不到] 出籌碼方式、思考時間。
- [來源說] 他會在籌碼太深的桌子離桌換桌（CardPlayer 2011，與 Palmer 對局 $200,000 深度時改去淺籌碼桌）。

**4. 桌上行為與破綻**
- [來源說] 自稱「tilt 因子大概是世界最差之一」（搜尋摘要）；2011 起專訪承認線上大輸多是自己不專心。https://www.pokerlistings.com/news/gus-hansen-i-might-have-to-go-back-to-the-drawing-table-39286 （該篇本身沒談風格或 tilt，這句出自搜尋摘要，未能在該頁確認）
- [來源說] Wikipedia 的一個條目標題顯示他曾調侃 Negreanu 那個 $575,000 的底池（只有標題，內容沒讀）。
- 近期：2025 年重返錦標賽，Winamax 第 7 名；PokerListings 稱其風格「攻擊、孤注一擲」。https://www.pokernews.com/news/2025/09/gus-hansen-misses-out-on-first-poker-title-in-12-years-49701.htm （搜尋摘要）
- [查不到] 他的具體 tell；說話量的證據很薄。
- 換算（[推估]，信心低）：Talkativeness 中、Expressiveness 中、TellCredibility 中低、ReverseActing 中、ReadSensitivity 中。

**5. 台詞方向**
- 輕鬆、略帶丹麥式冷幽默的挑釁，像在開玩笑地問你敢不敢跟。
- 常把自己的荒謬操作講成「就是想試試看」。
- 被 bad beat 時反而笑，承認「這手我打得太瘋了」。

---

### 2.6 Vanessa Selbst — 範圍推理派的翻前高壓

**1. 風格與代表打法**
- [來源說] 以 3-bet、4-bet、5-bet 逼退對手聞名，特別是錦標賽後段；2010 Cannes 用 A-3 同花 4-bet，對手 5-bet 後讀成 A-K 全下，贏 450K 底池。早期也說過太兇在淺階段不好用（深籌碼的人會用弱牌看翻牌）。https://www.espn.com/poker/story/_/page/Selbst/how-vanessa-selbst-became-best-female-poker-player-all-espn-magazine
- [來源說] 她的方法論：收集資訊、推論範圍，例如「經常 3-bet 但從未秀牌，然後秀了一手跟注的 A-Q，就代表他的 3-bet 偏詐唬，可以多 4-bet 詐唬」；有人河牌常下注但從不薄價值下注，就判斷他範圍偏詐唬，多英雄跟注。https://www.cardplayer.com/?p=1036484
- [來源說] 形象管理：若自己一直在詐唬、玩很多底池，就會多跟注；若形象緊，就詐唬更有效。同上。

**2. 可量化傾向**
- [查不到] 任何 VPIP／PFR 數據。查過：ESPN、CardPlayer Q&A、Wikipedia（整篇不談風格）、DeucesCracked（搜尋摘要稱有明顯不一致）。
- [來源說] 深籌碼定義為 60 大盲以上。

**3. 下注與籌碼習慣**
- [來源說] ESPN：使用大額加注與再加注製造壓力、縮窄範圍。
- [查不到] 出籌碼方式、思考時間。

**4. 桌上行為與破綻**
- [來源說] 她享受秀出弱牌時對手的反應；承認大牌前會緊張，但能維持冷靜執行計畫；早年被視為「狂放、愛實驗」。ESPN 連結同上。
- [來源說] 同儕評價（Mercier：「看起來什麼都做得出來」；Laak：風格難以複製，因為需要數學、研究與精準）。同上。
- [查不到] 話多話少的證據；具體 tell。
- 換算（[推估]，信心低）：Talkativeness 低到中、Expressiveness 低、TellCredibility 低中、ReverseActing 中、ReadSensitivity 高（範圍／行為統計式）。

**5. 台詞方向**
- 條理清楚、帶點學者氣質的冷靜語氣，會簡短說明「你這個動作排除了什麼牌」。
- 在大詐唬成功後，微微一笑把弱牌秀給你看。
- 對傳統「讀表情」不屑一顧，更相信統計。

---

### 2.7 Doyle Brunson — 老派德州「大爸」，鬆兇但會調整

**1. 風格與代表打法**
- [來源說] 被歸類為 LAG；在最高額度「非常兇、願意投入很多錢去贏更多」。https://www.pokerstrategy.com/?p=39608
- [來源說] 《Super/System》（1978）主張的無限注德州是高度攻擊：常用弱牌或空氣贏池、用恐懼施壓；後來大家讀了書，反而開始一直加注他，他說他改回「ABC 撲克」，連贏 15 場沒詐唬過一次。https://www.cardplayer.com/?p=1031994 與 https://www.cardplayer.com/?p=1038444
- [來源說] 1976、1977 連兩年用 10-2 在決勝手完成葡萄牌型贏得 WSOP 主賽事，這手牌因此被叫「Doyle Brunson」。https://en.wikipedia.org/wiki/Doyle_Brunson
- [來源說] Negreanu 稱他是「原始的 solver」，曾手動發牌反覆比對 A-K 對小對子的勝率。https://www.pokernews.com/news/2026/10/daniel-negreanu-remembers-doyle-brunson-52516.htm
- 注意：他已於 2023-05-14 逝世（Wikipedia）。

**2. 可量化傾向**
- [查不到] VPIP／PFR。查過：PokerStrategy 傳記、CardPlayer 兩篇、Wikipedia、PokerNews 2026 追憶。
- 文字描述全部是「質性」，換算全為 [推估]。

**3. 下注與籌碼習慣**
- [查不到] 具體尺寸、籌碼習慣、思考時間。

**4. 桌上行為與破綻**
- [來源說] 描述為魅力十足的德州人、溫和且虔誠；他在 Negreanu 的追憶裡與 Ivey 常常鬥嘴但互相敬重。PokerStrategy 與 PokerNews 2026 兩連結同上。
- [查不到] 具體 tell、讀人手法、說話量。
- 換算（[推估]，信心低）：Talkativeness 中高、Expressiveness 中、TellCredibility 中、ReverseActing 中、ReadSensitivity 高。

**5. 台詞方向**
- 慢條斯理的德州腔、說教式老派口吻（「孩子，這樣的牌不能這樣打」）。
- 偶爾自嘲「我就是靠一手爛牌贏的」，把運氣說得雲淡風輕。
- 被逼急時回到「穩穩來」，用一句簡短的話說明自己改打保守。

---

### 2.8 Antonio Esfandiari — 魔術師型表演家

**1. 風格與代表打法**
- [來源說] 前職業魔術師，外號 "The Magician"，以花式籌碼技巧聞名；2012 Big One for One Drop 冠軍，$18,346,673。https://en.wikipedia.org/wiki/Antonio_Esfandiari
- [來源說] PokerNews 2008 側寫：2002 WPT 決賽桌用花式籌碼並對 Hellmuth 說攻擊性的垃圾話；2008 EPT 蒙地卡羅決賽桌時一邊聽音樂、一邊緊盯對手。https://www.pokernews.com/news/2008/12/pokernews-profile-antonio-esfandiari-5915.htm
- [來源說] High Stakes Poker 第 1 季第 12 集：Negreanu 翻牌 all-in，他做了「困難且正確」的跟注，雙方邊談話邊縮範圍，底池 $173,900。https://www.pokernews.com/strategy/high-stakes-poker-reviewed-esfandiari-makes-big-call-against-35802.htm

**2. 可量化傾向**
- [查不到] VPIP／PFR／任何統計。查過：Wikipedia、PokerNews 側寫（明說沒談攻擊度）、PokerNews HSP 分析、搜尋 HCL／HSP 無結果。
- 搜尋摘要提到論壇留言「打得像石頭但話多所以能吸引行動」，以及 Camp Hellmuth 筆記裡的「做狼不做羊」、「早期散播關於你打法的假訊息」。**這兩點我整頁讀過後沒找到對應內容（poker1 那頁其實是 Mike Caro 的文章），不採用。** https://www.lasvegasadvisor.com/gambling-with-an-edge/poker-tournament-camp/ 沒有整頁讀過。

**3. 下注與籌碼習慣**
- [來源說] 花式籌碼技巧（見上）。
- [查不到] 下注尺寸習慣、思考時間。

**4. 桌上行為與破綻**
- [來源說] 話多，對對手（尤其 Hellmuth、Negreanu）用談話施壓；自述魔術讓他懂得判斷人（搜尋摘要）。
- [查不到] 公開討論過的具體 tell；他的詐唬習慣。
- 換算（[推估]）：Talkativeness 極高、Expressiveness 高、TellCredibility 中低（表演本身就是工具）、ReverseActing 高、ReadSensitivity 中高。

**5. 台詞方向**
- 表演型、戲劇性的語氣，像在變魔術前的鋪陳（「你看好，這張牌馬上會消失」）。
- 一邊轉籌碼一邊調侃人，語速快、愛開玩笑。
- 贏下大池時做一個誇張的謝幕動作。

---

## 3. 參數總表

**欄位定義與轉換說明（[推估]）**
- Vpip／Pfr 以「4 人桌」為目標設計值。實測數據來自 8–10 人桌，所以我把 Hellmuth 的 31% 與 Ivey 的 26% 往上調整後再用；其他人全為描述推估。
- Aggression＝翻後攻擊度。BluffFrequency＝詐唬占下注比。FoldToBet＝面對下注棄牌率。
- BetSizePot：常用下注占底池比例。**超過 1.0 代表超池，遊戲內建議把範圍夾在 0.25–1.5**，個別超池另當事件處理（Dwan、Negreanu 的河牌超池）。
- ThinkBase：一般決策的基礎思考秒數。**除 Hellmuth 的長考尖峰外全為 [推估]**；Hellmuth 的長考（8 分鐘以上）用「大決策時才觸發」的事件做，不放進基礎值。
- 標記：**D**＝有數據根據（僅單場），**E**＝依描述推估。

| 牌手 | Vpip | Pfr | Aggression | BluffFreq | FoldToBet | BetSizePot | ThinkBase(秒) |
|---|---|---|---|---|---|---|---|
| Ivey | 0.30 (D 部分：HCL 26%) | 0.22 (E) | 0.75 (E) | 0.40 (E) | 0.35 (E) | 0.75 (E，min-raise 開、後街加大) | 4 (E) |
| Negreanu | 0.40 (E) | 0.20 (E，少 3-bet) | 0.55 (E) | 0.35 (E) | 0.45 (E) | 0.50 (E，翻牌 1/4 池、轉牌 1/2 池、河牌偶超池) | 5 (E) |
| Hellmuth | 0.28 (D 部分：11%／31%／自述 12–18%) | 0.12 (D 部分：約 8%) | 0.35 (E) | 0.12 (E) | 0.60 (E) | 0.60 (E) | 8 (E；尖峰 180–480 為 D) |
| Dwan | 0.55 (E) | 0.40 (E) | 0.85 (E) | 0.55 (E) | 0.30 (E) | 0.90 (D 單手：0.83／0.73／2.2 池) | 3 (E；大決策 60–240 為 D 單手) |
| Hansen | 0.65 (E) | 0.45 (E) | 0.80 (E) | 0.50 (E) | 0.35 (E) | 0.70 (D 單手：0.56 池) | 3 (E) |
| Selbst | 0.35 (E) | 0.30 (E，VPIP−PFR 小) | 0.85 (E) | 0.50 (E) | 0.40 (E) | 0.80 (E，「大額加注」) | 4 (E) |
| Brunson | 0.45 (E) | 0.30 (E) | 0.75 (E) | 0.45 (E) | 0.40 (E) | 0.80 (E) | 4 (E) |
| Esfandiari | 0.38 (E，信心低) | 0.25 (E) | 0.65 (E) | 0.40 (E) | 0.45 (E) | 0.65 (E) | 3 (E) |

**桌上行為參數**

| 牌手 | Talkativeness | Expressiveness | TellCredibility | ReverseActing | ReadSensitivity | 依據 |
|---|---|---|---|---|---|---|
| Ivey | 0.10 | 0.20 | 0.30 | 0.50 | 0.95 | 話少：PokerListings（來源說）；其餘 E |
| Negreanu | 0.90 | 0.70 | 0.50 | 0.60 | 0.95 | 談話套資訊：fcnp 兩篇（來源說）；數值 E |
| Hellmuth | 0.85 | 0.95 | 0.80 | 0.15 | 0.70 | 抱怨與發飆：PokerNews 訪談（來源說）；讀人能力自述高、實測有爭議，故給 0.70 |
| Dwan | 0.50 | 0.30 | 0.35 | 0.60 | 0.90 | Wikipedia、Yahoo HCL 手；全 E |
| Hansen | 0.50 | 0.55 | 0.40 | 0.50 | 0.60 | 證據薄；全 E，信心低 |
| Selbst | 0.35 | 0.30 | 0.40 | 0.40 | 0.85 | ESPN、CardPlayer；E |
| Brunson | 0.60 | 0.50 | 0.50 | 0.50 | 0.90 | 證據薄；E，信心低 |
| Esfandiari | 0.90 | 0.85 | 0.40 | 0.70 | 0.80 | PokerNews 側寫（垃圾話、花式籌碼）；其餘 E |

**差異檢查（[推估]）**：VPIP 從 0.28 到 0.65、Aggression 從 0.35 到 0.85、Talkativeness 從 0.10 到 0.90，區隔夠大。弱點：Dwan／Hansen／Selbst 的 Aggression 都落在 0.80–0.85，可靠 Vpip（0.55／0.65／0.35）與詐唬型態（Dwan 超池、Selbst 翻前加碼、Hansen 翻後施壓）拉開。

---

## 4. 肖像權／姓名使用：Meta Quest 與 VR Start 競賽

### 4.1 結論（先看這裡）

1. **用真人姓名、臉、聲音、招牌台詞，需要授權；沒有授權就是違約且高風險。** 信心：**中高**。
   - 依據：Meta 的 Developer Distribution Agreement §10.1(a)（開發者擔保產品不侵害第三方的智慧財產「或其他專有、**公開權**、隱私權」）與 §10.1(c)（開發者擔保擁有產品所需權利，包括「名稱／肖像的商標或**公開權**」），§11.1 開發者須賠償 Meta 因違約引起的第三方請求。https://developers.meta.com/horizon/policy/developer-distribution-agreement/ （條文以頁面擷取摘要引用，我沒逐字核對原文）
   - 也就是說：Meta 沒有一條「不得使用真人」的獨立規則，是**把風險用契約擔保轉給開發者**。
2. **「以某人為靈感、不用名字」（綽號式角色）：風險較低但不是安全港。** 信心：**中低**。
   - 這是法律推論，不是 Meta 規範。美國法院對遊戲中的公開權案件常用「轉化性」測試：Hart v. EA（3d Cir. 2013）與 Keller v. EA（9th Cir. 2013）都判定 EA 把真人運動員「照原樣」放進熟悉場景，不受第一修正案保護。https://www.crowell.com/en/insights/client-alerts/third-circuit-finds-that-use-of-a-college-football-player-s-likeness-in-a-video-game-violates-his-right-of-publicity （二手摘要，沒讀判決原文）
   - 反面（有利於「沒有名字」的說法）：搜尋顯示 Lindsay Lohan 告 GTA V 一案被駁回，理由大意是沒用她的名字、角色也不完全像她，是「虛構與諷刺作品」。https://www.pushsquare.com/news/2023/12/florida-joker-wants-a-massive-rockstar-payday-for-allegedly-using-likeness-in-gta-6 （這頁是搜尋結果裡提到 Lohan 的清單之一，該頁主題其實是 Florida Joker，Lohan 部分我沒在頁內確認，**信心低**）
   - 反面（不利於「綽號式」）：如果角色的外型、口頭禪、綽號、招牌打法加起來「讓人一看就是某人」，仍可能被視為利用其身分。這是推論，沒有找到撲克相關判例。
3. **Doyle Brunson 已過世（2023-05-14）**：死後公開權因州而異，我**沒查**，列為待確認。https://en.wikipedia.org/wiki/Doyle_Brunson
4. **VR Start Developer Competition 2026**：官方部落格沒有談 IP／肖像／原創性規則，細則在 Devpost，我的擷取被 403 擋住，**沒讀到規則全文**。https://developers.meta.com/horizon/blog/meta-connect-2026-vr-start-developer-competition/ ，規則頁 https://start-developer-competition-26.devpost.com/rules （403）。已知：開放給 Start 計畫成員；9/24 開始收件、11/18 截止、12/11 公布。此外部落格說體驗須「全程可用手勢操作」、建議坐姿優化。
   - 另一個容易混淆的點：Meta Horizon Start 計畫條款裡有一條「參與者授權 Meta 為宣傳使用其姓名、肖像、聲音」，那是**開發者自己**授權給 Meta，與使用他人肖像無關。https://developers.meta.com/horizon/start/terms/ （搜尋摘要）

### 4.2 其他 Meta 規範（和這題相關的）

- 第三方 IP：「Instances of assets or mentions of third party intellectual property will require approval by the copyright owner」，包含外部遊戲、取自他處的音樂「或任何其他現存 IP」。https://developers.meta.com/horizon/resources/publish-common-flags/ 這頁**沒有**提到真人、姓名、肖像或名人。
- 內容準則：禁止真錢賭博與可兌現的遊戲內貨幣；沒有談模擬賭博；有一條「代表真實有組織暴力團體或個人」，與本題無關。https://developers.meta.com/horizon/policy/content-guidelines/
- 商店政策：禁止廣告宣傳線上賭博（§2.1.4(3)）、戰利品箱要揭露機率、不得賣給未滿 18 歲（§1.1.3）；沒有定義模擬賭博。https://developers.meta.com/vr/policy/store-policies/
- 商店上現有的撲克應用：Poker VR 評級 Teen、含「Simulated Gambling」；PokerStars Vegas Infinite 評級 Mature 17+。https://www.meta.com/experiences/poker-vr/686548444781796/ 、https://www.meta.com/experiences/pokerstars-vegas-infinite/2370815932930055/ （搜尋摘要；顯示撲克類能上架，但要標模擬賭博分級）

### 4.3 「綽號式／靈感式撲克角色」的先例

- [查不到] 找不到任何撲克遊戲使用「以某人為靈感、不用名字」的做法的明確案例。搜尋過：「poker game fictional characters inspired by real poker players」「Hellmuth parody character」、「poker video game licensed real players likeness」。
- 相反的產業慣例是**付費授權**：Activision 的《World Series of Poker》（2005）使用 Chris Ferguson 等 15 位職業牌手的肖像與打法（https://investor.activision.com/node/16371 ，搜尋摘要）；《Stacked with Daniel Negreanu》（2005）由本人合作（https://www.pokernews.com/news/2005/01/stacked-game-daniel-negreanu-1449.htm ，搜尋摘要）。
- PokerStars VR（Quest）有品牌大使的頭像（搜尋摘要提到 Liv Boeree 在內），但授權細節查不到。https://pokerfuse.com/news/industry/210170-pokerstars-vr-officially-launches/

### 4.4 反面證據與保留

- Meta 的公開文件裡**沒有**專門針對真人姓名／肖像的規則；我的「需要授權」結論來自契約擔保條款＋美國案例，不是 Meta 明文要求。若 Meta 審查實務比條文寬鬆，也是可能的，但我沒有證據。
- 我沒查臺灣或其他司法管轄區的規定；公開權在美國是州法，各州不同，我沒有逐州查（Nevada、California 等）。
- 頁面擷取工具摘要過的條文，引用到對外文件前請核對原頁。

### 4.5 給設計的建議（[推估]，非法律意見）

- **參賽版（11/18 前）**：用虛構名字、虛構外型、原創台詞與聲音，只保留「風格原型」（例如「冷面讀牌高手」「話多的小球派」「緊但愛抱怨的元老」）。不要在商店文案、截圖、宣傳影片寫出任何真人姓名。
- **避免組合特徵**：不要同時複製綽號、招牌動作、口頭禪、外型與打法，避免讓人一看就是某人。
- 若要正式使用真人，需要取得授權；時程上不太可能在競賽期限前完成。
- 最終要不要冒這個風險，需要專業律師意見。

---

## 5. 沒做到的事／不確定處

**沒做到**
1. **沒查任何 HUD／追蹤站資料**（HighStakesDB、PokerTracker、Table 1 Vegas 原頁都沒進得去：Table 1 的新網址無法解析）。所以只有 Hellmuth 與 Ivey 有單場 VPIP；Negreanu、Dwan、Hansen、Selbst、Brunson、Esfandiari 的 VPIP／PFR 全是推估。
2. **沒看任何影片**（牌局、訪談、HCL、High Stakes Poker 直接觀察），所以桌上行為、思考節奏、出籌碼方式幾乎都是文字報導的二手描述。
3. **Stu Ungar 沒查**；Fedor Holz 只查了一輪搜尋，未整頁深讀。
4. **Devpost 官方規則（競賽）403 讀不到**，競賽是否有 IP／肖像限制不明。
5. **Meta 條文是經頁面擷取摘要看到的**，沒逐字核對 DDA 原文。
6. 沒有查臺灣法律、沒有查死後公開權（Brunson）、沒有讀 Hart／Keller 判決原文。
7. 沒請教 headless Claude 做交叉驗證。

**不確定／可能有誤**
- 你題目中的「Negreanu 會講出你的牌」：**查無來源**，見 §2.2。
- Esfandiari、Hansen、Brunson 的桌上行為參數信心低（證據薄）。
- 搜尋工具回傳的是摘要而非原文，少數引用（標明「搜尋摘要」者）我沒回頁面核對：Hellmuth BetRivers 近 100% VPIP、Hellmuth WSOP 2025 三分鐘長考、Hansen 2025 Winamax 第 7、Hansen「tilt 因子」自述、Ivey 之外各 Wikipedia 頁因頁面過長只讀了前 10 萬字。
- Ivey 的 26% 是「拿不到牌」的一晚；實際常態 VPIP 可能更高。

---

## 6. 主要來源索引

- PokerNews：https://www.pokernews.com/news/2022/09/phil-ivey-hustler-casino-live-adelstein-42188.htm 、https://www.pokernews.com/news/2022/01/phil-ivey-top-poker-reads-40477.htm 、https://www.pokernews.com/news/2025/09/phil-hellmuth-plays-tight-49679.htm 、https://www.pokernews.com/news/2023/02/phil-hellmuth-hustler-casino-live-43045.htm 、https://www.pokernews.com/strategy/interview-phil-hellmuth-part-1-21048.htm 、https://www.pokernews.com/strategy/interview-phil-hellmuth-part-2-21058.htm 、https://www.pokernews.com/news/2026/05/hellmuths-home-game-episode-24-recap-51248.htm 、https://www.pokernews.com/news/2019/07/daniel-negreanu-big-fold-main-event-34760.htm 、https://www.pokernews.com/news/2026/08/daniel-negreanu-hustler-casino-live-52257.htm 、https://www.pokernews.com/news/2022/03/the-rise-and-rise-of-tom-durrrr-dwan-40819.htm 、https://www.pokernews.com/news/2024/06/tom-dwan-on-hustler-casino-live-46269.htm 、https://www.pokernews.com/news/2008/03/book-review-gus-hansen-every-hand-revealed-4856.htm 、https://www.pokernews.com/news/2008/12/pokernews-profile-antonio-esfandiari-5915.htm 、https://www.pokernews.com/strategy/high-stakes-poker-reviewed-esfandiari-makes-big-call-against-35802.htm 、https://www.pokernews.com/news/2026/10/daniel-negreanu-remembers-doyle-brunson-52516.htm
- 其他媒體：https://www.fcnp.com/2009/02/19/daniel-negreanu-on-poker-betting-strategies-in-small-ball-poker/ 、https://www.fcnp.com/2008/05/08/negreanu-on-poker-interrogating-an-opponent/ 、https://www.pokerlistings.com/hand-analysis/tom-dwan-drops-a-350k-hammer-on-sammy-george 、https://www.pokerlistings.com/daniel-negreanus-overbet-bluff-representing-nothing-explained 、https://www.pokerlistings.com/poker-strategy/poker-psychology/ivey-on-the-couch 、https://www.espn.com/poker/story/_/page/Selbst/how-vanessa-selbst-became-best-female-poker-player-all-espn-magazine 、https://www.cardplayer.com/?p=1036484 、https://www.cardplayer.com/?p=1035420 、https://www.cardplayer.com/?p=1031994 、https://www.cardplayer.com/?p=1038444 、https://www.pokerstrategy.com/?p=39608 、https://uk.news.yahoo.com/crypto-millionaire-tried-run-bluff-030321853.html 、https://ca.sports.yahoo.com/news/phil-ivey-knows-makes-good-215400845.html
- 維基百科：Phil_Ivey、Daniel_Negreanu、Vanessa_Selbst、Doyle_Brunson、Antonio_Esfandiari、Gus_Hansen、Tom_Dwan（https://en.wikipedia.org/wiki/ 後接名稱）
- Meta：https://developers.meta.com/horizon/policy/developer-distribution-agreement/ 、https://developers.meta.com/horizon/resources/publish-common-flags/ 、https://developers.meta.com/horizon/policy/content-guidelines/ 、https://developers.meta.com/vr/policy/store-policies/ 、https://developers.meta.com/horizon/blog/meta-connect-2026-vr-start-developer-competition/
