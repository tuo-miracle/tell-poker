# Quest 3 MR 德州撲克：規則引擎、手牌評估、AI 對手研究筆記

研究日期：2026-10-12（所有「最後更新」皆以此日為基準，來源為 GitHub API `pushed_at`／頁面標示）。
方法：WebSearch／WebFetch 讀第一手頁面、`gh api` 查 repo 的授權／更新日期／檔案樹／issue、直接讀程式碼片段。沒有 clone 任何 repo 到專案目錄；暫存檔只放在 scratchpad 的 `poker-research/` 與 `dl/`。**沒有執行任何基準測試，也沒有寫任何程式碼**，所有效能數字都是來源自述，不是我量的。

信心標記：【已驗證】=我讀到原文／原始碼；【自述】=來源 README 自己宣稱，未獨立驗證；【推論】=我的判斷；【背景知識】=沒抓來源，憑通用知識，請當待驗證。

---

## 0. 一頁結論

| 項目 | 建議 |
|---|---|
| 評估器 | **自己寫一個無表的 7 張牌評估器（約 150 行、1 天含測試）作為遊戲內正式版**；用 HenryRLee/PokerHandEvaluator（Apache-2.0）的 C# 移植當**測試用 oracle**（只在 .NET 測試專案裡用，不進 Quest 包）。若實測效能不夠，再 vendor pheval 的 7 張表（約 144 KB，見 §1）。**不要用** HoldemHand.cs（GPL-3）、Cactus Kev 原 repo（GPL-3）、Two Plus Two（約 130 MB 表、授權不明）。 |
| 引擎 | 純 C# 無 Unity 依賴（C# 9、netstandard2.1），整數籌碼，命令→驗證→事件，種子化洗牌，狀態可重播；以 PokerKit（MIT、99% 覆蓋、重建 83 手 WSOP 真實牌譜）當差分測試 oracle。 |
| AI | 三層：觀察（只看公開資訊）→ 估值（翻牌前範圍表＋翻牌後 Monte Carlo vs 加權範圍）→ 風格策略（參數化個性＋種子亂數）。手勢反應做成有上限、會衰減的「閾值偏移」，不給 AI 任何真實資訊。 |
| 政策 | 虛擬籌碼的模擬賭博**不被禁止**，但**禁止真錢賭博與任何兌現／兌換**；年齡分級走 IARC 問卷，先例是 Teen 到 Mature 17+；歐洲 PEGI 很可能 18。比賽（Meta VR Start 2026）規則文字**完全沒提賭博**，但要求 hands-first、免費、遵守 Developer Content Guidelines。 |
| 最大風險 | 比賽的「New Experience」不得用既有 codebase（見 §4.3）；Unity 6 只支援 C# 9，多數新 C# 函式庫不能直接丟進去（§1.4）。 |

---

## 1. 手牌評估器

### 1.1 候選清單（授權／維護狀態／7 張／記憶體）

| 專案 | 語言 | 授權 | 最後更新 | 7 張 | 記憶體／表 | 速度（自述） | 備註 |
|---|---|---|---|---|---|---|---|
| [HenryRLee/PokerHandEvaluator](https://github.com/HenryRLee/PokerHandEvaluator) | C/C++/Python | Apache-2.0 【已驗證】 | push 2026-09-14（近期多為 dependabot；最後實質提交 2026-07-11 docs/ci）；522★ | 5/6/7/8/9 張、Omaha | pmap：5 張 60 KB、6 張 84 KB、**7 張 144 KB**；全部 648 KB（Omaha 另計，PLO4 約 30 MB）【自述】 | 隨機 7 張 1,778 ns（作者機器 12×2.6GHz，2023-09-02 基準）【自述，數字與其「72M/s」換算不一致，勿引用】 | 有 `test_data/seven` 等測試資料可當 oracle【已驗證有目錄】 |
| [bakala12/HoldemPoker.Evaluator](https://github.com/bakala12/HoldemPoker.Evaluator) | C# | Apache-2.0 【已驗證】 | 2025-10-06；3★ | 5–7 張＋Omaha | 表直接寫成 C# 原始碼（Hashtable7.cs 約 327 KB 原始碼） | 無數據 | pheval 的 C# 移植；.NET 8、file-scoped namespace、`params Card[]`（每次呼叫配置陣列）、依賴 NuGet `HoldemPoker.Cards`；測試有「全部 5 張牌型」與 Omaha，**檔案清單中看不到 7 張全枚舉測試**【已驗證檔案樹】 |
| [archaistvolts/PokerHandEvaluator.cs](https://github.com/archaistvolts/PokerHandEvaluator.cs) | C# | Apache-2.0 【已驗證】 | 2021-11-11；14★ | 5/6/7 張 | 純 C# 表（7462.cs 223 KB、Hashtable7.cs 等） | 無 | 老式語法（.NET Framework 4.7 csproj），Unity 相容性反而較好；`Eval.cs` 每次呼叫 `new byte[]` 配置；測試是對 `libpheval.so` 隨機 1000 組比對（`CompareRanks.cs`），不是窮舉 |
| [zekyll/OMPEval](https://github.com/zekyll/OMPEval) | C++ | ISC 【已驗證】 | 2020-06-30；231★ | 0–7 張 | **約 200 kB 表**、初始化約 10 ms【自述】 | Intel 3770k 單執行緒：隨機手 Rand1 約 272 M evals/s【自述，C++】 | 附 Monte Carlo 勝率計算器；**沒有找到 C# 移植**；適合 Unreal C++ 路線 |
| [kennethshackleton/SKPokerEval](https://github.com/kennethshackleton/SKPokerEval) | C++ | Apache-2.0（`LICENSE.md` 內文；GitHub 標 NOASSERTION）【已驗證】 | 2026-09-26；251★ | 7 張 | 95 KiB、無初始化【自述】 | 146–148 M/s 隨機（OMPEval README 對照）【自述】 | 32 位元 key 加法 → 查表，演算法簡單，可移植成 C# 做參考 |
| [tangentforks/TwoPlusTwoHandEvaluator](https://github.com/tangentforks/TwoPlusTwoHandEvaluator)（Two Plus Two 原作者 Ray Wotton，論壇串） | C/C++ | **未見授權檔**【已驗證 repo 無 LICENSE，README 只說 Pascal 範例是 fork 作者自己的】 | 2023-04-11（fork） | 5–7 張 | `HandRanks.dat` **>120 MB**（[bostik 頁面](https://bostik.iki.fi/aivoituksia/projects/twoplustwo-hand-evaluator.html)） | 順序存取最快（1,588 M/s），**隨機手只有 19 M/s**（快取失效；OMPEval README 對照）【自述】 | 對 Quest 不合適：表大、冷啟動載入、隨機存取慢；授權不清 |
| Cactus Kev（[suffecool/pokerlib](https://github.com/suffecool/pokerlib)；[作者頁](https://suffe.cool/poker/evaluator.html)） | C | repo 的 GitHub 授權欄＝**GPL-3.0**【已驗證】；作者頁面文字內沒有授權聲明 | 2025-08-18 | 只有 5 張；7 張需 C(7,5)=21 次 5 張評估【推論】 | 小 | — | 7462 個等價類（作者頁明確寫）。JBelthoff 的 LICENSE 聲稱其為 public domain（見下列），與 GPL-3 標示**衝突**，我無法裁決 → 不要 vendor 原碼，只借「7462 等級」概念 |
| [JBelthoff/poker.net](https://github.com/JBelthoff/poker.net) | C#（ASP.NET Razor 網站） | LICENSE 檔＝MIT＋聲明內含 Cactus Kev 移植且稱其 public domain；GitHub 標 NOASSERTION【已驗證】 | 2025-11-14；40★ | 主要是 5 張／河牌 | — | README 稱 C# 943.7 ns 為「引擎」數字（9 人局）；C++ 版約 188–191 M 7張/s【自述】 | 是網站專案不是函式庫；README 還寫「no lookup tables」但又說衍生自 Cactus Kev，前後矛盾，可信度低 |
| [platatat/SnapCall](https://github.com/platatat/SnapCall) | C# | **無授權**（GitHub 無 license） | 2023-11-09；36★ | 5–7 | 7 張表 **約 2 GB**、建表超過 1 小時 | 7.5 M/s | 對 Quest 完全不可行 |
| HoldemHand.cs（Keith Rule，[CodeProject 文章](https://www.codeproject.com/Articles/12279/Fast-Texas-Holdem-Hand-Evaluation-and-Analysis)） | C# | CodeProject 列表顯示 **GPL v3**（2006-04-18）【搜尋結果摘要，未讀到原始碼檔頭】 | 2006 | 7 張 | 由 poker-eval 移植 | — | GPL → 不要用；但它是 C# 圈最常被引用的老實作，注意別誤抄 |
| [DGilev91/PokerEngine](https://github.com/DGilev91/PokerEngine) 內建 `TexasHoldemEvaluator` | C# | MIT 【已驗證】 | 2026-08-15（專案 2026-07-30 才建立）；0★ | 最強 5 張 | 無表 | — | .NET 10；非常新、單人、無社群檢驗 |
| [AlexHedley/Poker-Hand-Evaluator](https://github.com/AlexHedley/Poker-Hand-Evaluator) | C# | 頁面未標授權 | **已封存（2025-07-18）**；1★ | 7 張＋邊池計算器 | — | — | fork 自 danielpaz6；沒看到測試專案 → 不採用 |
| [elliottneilclark/rs-poker](https://github.com/elliottneilclark/rs-poker) | Rust | Apache-2.0 | 2026-08-13；185★ | 7 張 | — | 自述 7 張 <25 ns | 附 agent arena、fuzz、mutation testing，是 AI 測試方法的好參考（§3） |

### 1.2 其他語言對應
- **Unreal C++**：直接用 pheval 的 C/C++ 原碼（Apache-2.0）或 OMPEval（ISC）。
- **Kotlin／Android 原生**：我用 GitHub 搜尋沒有找到成熟的 Kotlin／Java 7 張評估器（只找到 2 個 0★ 的 Java 練習專案）。選項：(a) 用 NDK 編 pheval C++，Kotlin 走 JNI；(b) 把下一節的自寫評估器改寫成 Kotlin（演算法與語言無關）。
- 其他語言的 pheval 移植（參考用）：Go [mattlangl/gophe](https://github.com/mattlangl/gophe)（Apache-2.0，2024-01-17）、Rust [0xvv/hermes](https://github.com/0xvv/hermes)、JS [chaines/jsPHE](https://github.com/chaines/jsPHE)。

### 1.3 「自己寫」的成本評估【推論，未實作】
- **演算法（無大表）**：把 7 張牌轉成「13 階計數＋4 花色 13-bit 遮罩」→ 花色 ≥5 張則找同花（含同花順，用 8192 項的順子遮罩 LUT）→ 否則依計數判四條／葫蘆／三條／兩對／一對／順子／高牌，產生一個可比較的整數（類別×常數＋kickers 編碼）。約 120–180 行，表只有一個 8192 項的順子 LUT。
- **正確性測試（TDD 可行，這是重點）**：
  1. **窮舉全部 C(52,7)=133,784,560 手**，統計各牌型數量，必須等於 [Wikipedia Poker probability](https://en.wikipedia.org/wiki/Poker_probability) 的精確值【已驗證】：皇家同花順 4,324／同花順 37,260／四條 224,848／葫蘆 3,473,184／同花 4,047,644／順子 6,180,020／三條 6,461,620／兩對 31,433,400／一對 58,627,800／高牌 23,294,460。這在桌機 .NET 約十幾秒量級【推論】。
  2. **差分測試**：隨機取數百萬組 (手A, 手B)，與 bakala12／pheval 的結果比對「大於／等於／小於」關係（不比對數值，因為編碼不同）。
  3. 手寫邊界案例：A-5 輪子順（A 當 1）、不可繞圈 Q-K-A-2-3、7 張內含兩組三條→葫蘆、三對取最大兩對＋最大 kicker、板面成牌（play the board）、同花 6–7 張取最大五張、同花順與同花並存時取同花順。
- **成本估計**：寫＋測 約 1 個工作天（含窮舉測試與差分測試腳手架）；比「改造 bakala12 的移植使其符合 Unity 6」更便宜，因為後者需要降 C# 版本、去掉 NuGet 依賴、消除 `params Card[]` 配置。
- **風險**：效能未實測。遊戲本身每手只需要少量 7 張評估（攤牌），瓶頸在 AI 的 Monte Carlo（§3.2）。若 C# IL2CPP 在 Quest 上每次評估 >2 µs 才需要換成表驅動版。→ **第 1 週要在真機做一次 100 萬次評估的微基準**，再決定。

### 1.4 Unity 相容性（重要）
- Unity 6（6000.0）使用 **C# 9.0**，Api Compatibility Level 預設 **.NET Standard 2.1**（[C# 編譯器頁](https://docs.unity3d.com/6000.0/Documentation/Manual/csharp-compiler.html)、[.NET profile 頁](https://docs.unity3d.com/6000.1/Documentation/Manual/dotnet-profile-support.html)）【已驗證】。
- 所以 DGilev91/PokerEngine（.NET 10、集合運算式 `[]` 為 C# 12、file-scoped namespace 為 C# 10）與 bakala12（.NET 8、file-scoped namespace）**都不能直接放進 Unity**；只能當參考或做成獨立 .NET 測試專案。
- 建議把引擎做成獨立的 `netstandard2.1` + `LangVersion 9` 類別庫（asmdef 或 dll），Unity 只引用它。

### 1.5 記憶體對 Quest 的意義
- Quest 3／3S 的 app 記憶體上限約 **5.75 GiB**（PSS）；72 Hz 最低、13.8 ms 幀預算（[Meta 記憶體文件](https://developers.meta.com/vr/documentation/unity/po-memory-ram/)、[裝置比較](https://developers.meta.com/vr/resources/device-optimization-comparison/)）【搜尋結果摘要】。
- 結論：130 MB 的 Two Plus Two 表在 Quest 技術上放得下，但冷啟動載入與隨機存取的快取失效（19 M/s）讓它不划算；pheval 的 144 KB 7 張表或自寫無表方案都遠低於任何門檻。

---

## 2. No-Limit Hold'em 規則引擎

### 2.1 規則依據
主要規則來源：**Poker TDA Rules 2026 v1.1（2026-09-21）**，[pokertda.com/view-poker-tda-rules](https://www.pokertda.com/view-poker-tda-rules/)【已驗證，我下載全文並 grep 相關條文】。此為錦標賽規則，單機遊戲可取捨，但它是業界最通行的明文標準。

| 主題 | 規則（條文） | 實作要點 |
|---|---|---|
| **最小加注** | Rule 45A：加注額至少等於本輪最大的「前一次完整下注或加注」。 | 維護 `lastFullRaiseSize`（翻牌前初始＝BB；翻牌後每輪初始＝BB）；`minRaiseTo = currentBet + lastFullRaiseSize`。以「加注到(raise-to)」為 API，不要用「加注多少(raise-by)」，避免歧義。 |
| **短 all-in 不重開下注** | Rule 49A：不足一個完整加注的 all-in（**或連續多個短 all-in 累計**仍不足）不會讓已行動且面對金額未達完整加注的玩家重新取得加注權；若累計達標而重開，最小加注額仍是**最後一次完整加注**。 | 已行動玩家只能 call/fold。PokerKit 用 `consecutive_short_all_in_amounts` 實作累計；見 §2.4 的 bug #45（它自己也出過錯）。 |
| **加注上限** | Rule 50：NL 不限次數。 | — |
| **攤牌順序** | Rule 18A：最後一個主動下注／加注者先亮；若最後一輪沒人下注，由該輪第一個行動者（按鈕左邊第一位）先亮。Rule 17：有人 all-in 且所有下注結束後，所有仍在局者**必須全部亮牌**（主池邊池皆是）。Rule 18B：無攤牌（其他人蓋牌）則最後一人不必亮。 | 引擎要輸出 `ShowdownOrder`；UI 才能照順序翻牌（也是 MR 表演重點）。 |
| **零頭籌碼** | Rule 21：奇數籌碼先拆成遊戲內最小面額；板面遊戲有 2 個以上贏家時，零頭給「按鈕左邊第一位」。 | **每個池各自**分配，零頭依「按鈕左側起算的座位順序」給贏家；整數籌碼，禁用浮點。PokerKit 另可自訂 `divmod`。 |
| **Heads-up** | Rule 36C：單挑時**按鈕＝小盲**，最後發牌，**翻牌前先行動，翻牌後最後行動**；開始單挑時按鈕可能需要調整，確保沒人連續兩次當大盲。 | 單挑特例必須有獨立測試；PokerKit 在 2025-10 還出過單挑 actor_index 錯誤（#31）。 |
| **死按鈕** | Rule 34/36B：錦標賽用 dead button。 | 休閒遊戲可選：（A）dead button：大盲位逐座位前進，小盲／按鈕可落在空位或已出局座位，保證沒人跳過或重複大盲；（B）按鈕前進到下一位在局玩家。**【推論】建議 (A) 的演算法**：先決定大盲＝上一手大盲的下一位有籌碼玩家，小盲＝其前一座位（可為空），按鈕＝小盲的前一座位；2 人時按鈕＝小盲。 |
| **未被跟注的下注** | TDA 條文內有退還說明（Rule 16B：未被跟注部分退還）。PokerKit／DGilev 都有獨立的 `UncalledBet` 概念。 | 在建池前先退還最高下注者超過第二高貢獻的部分，不要把它放進邊池。 |
| **實體籌碼模糊** | Rule 42–47、53–54：沉默推出籌碼、string bet、undercall、underraise 的整套判定。 | **MR 專屬重點**：若玩家用手推實體（虛擬）籌碼，這整組模糊判定都會變成你的 bug 來源。建議下注金額一律由**UI 明確確認**（滑桿／預設按鈕＋確認手勢），手勢只負責「動作」，不負責「金額的隱含推定」。 |

### 2.2 邊池演算法（分層法）【推論，標準做法，PokerKit 的 `pots` 與 DGilev 的 `PotState` 行為與此一致但我沒逐行審】
1. 取每位玩家本手總投入 `contrib[i]`（含已棄牌者，棄牌者的錢留在池裡）。
2. 取所有**未棄牌**玩家的不同投入額由小到大 `L1 < L2 < ... `。
3. 對每一層 `Lk`：`pot_k = Σ_i ( min(contrib[i], Lk) − min(contrib[i], L(k−1)) )`；`eligible_k = {未棄牌且 contrib[i] ≥ Lk}`。
4. 最高層若只有 1 個合格者＝未被跟注的部分，直接退還，不參與攤牌。
5. 相鄰且合格者集合相同的層可合併（顯示用）。
6. 每個池獨立評牌：在 `eligible_k` 中取最佳 7 張牌者；平手則均分，零頭按 §2.1 規則。
7. **不變量（做成屬性測試）**：(a) 發牌前後總籌碼守恆（Σ stacks + Σ pots + Σ uncalled 不變）；(b) 無負籌碼；(c) 棄牌者不得贏任何池；(d) 每池贏家 ⊆ 合格者；(e) 任一池金額為整數。

### 2.3 常見 bug 清單（附真實案例）
來自 **PokerKit**、**poker-ts**、**texasholdem** 的 issue（【已驗證】，我讀了標題與前文）：
1. **短 all-in 誤重開下注／已行動者可再加注**：[PokerKit #45](https://github.com/uoftcprg/pokerkit/issues/45)（2026-09-10，v0.7.5，已關閉）。「本身已是完整加注的 all-in 被加進累計池，導致整條街停用『短 all-in 不重開』」。
2. **邊池資格被忽略、零頭跑到別人身上**：[PokerKit #46](https://github.com/uoftcprg/pokerkit/issues/46)（2026-09-11，已關閉）。`can_win_now()` 沒檢查邊池資格，影響邊池合併、讓平手贏家之間移動 1 個籌碼。
3. **跟注一個「不足完整加注」的 all-in 時，應付金額回傳 None**：[PokerKit #25](https://github.com/uoftcprg/pokerkit/issues/25)（作者自述 RL 訓練中約每 1 萬手出現 1 次）。→ 這類錯誤只有大量隨機模擬才找得到。
4. **單挑、小盲為 0 時下一位行動者算錯**：[PokerKit #31](https://github.com/uoftcprg/pokerkit/issues/31)（2025-10-31）。
5. **最小加注被對手有效籌碼截斷**：[PokerKit #40](https://github.com/uoftcprg/pokerkit/issues/40)（2026-01-19）。
6. **平手判定錯誤**：[poker-ts #5](https://github.com/claudijo/poker-ts/issues/5)（2024-04-28，兩人同順/同對的平手）。
7. **盲注後籌碼耗盡導致下注輪提前結束，小盲沒機會跟注**：[poker-ts #7](https://github.com/claudijo/poker-ts/issues/7)（2024-05-01）。
8. **攤牌卡死**：[poker-ts #14](https://github.com/claudijo/poker-ts/issues/14)（2025-06-26，小數盲注 0.5/1 與 check 流程）。→ 籌碼一律用整數，不要用小數。
9. **最小加注多算籌碼／缺 min_raise 測試導致連續 hotfix**：[texasholdem #80](https://github.com/SirRender00/texasholdem/issues/80)、[#88](https://github.com/SirRender00/texasholdem/issues/88)（2022-04-09；內文為空，只有標題可佐證）。
10. 我自己整理、**尚未找到公開 issue 佐證**的高風險點【背景知識】：大盲選項（翻牌前所有人只跟注時大盲仍可加注）、翻牌前只剩 all-in 玩家時應跳過後續下注輪直接發完公共牌、所有人 all-in 後多餘的下注退還、一人籌碼少於盲注時的 all-in 盲注、死牌／重複牌（同一張牌被發兩次）、玩家出局後的座位與按鈕推進。

### 2.4 可參考的開源規則引擎（測試完整度排序）
| 專案 | 語言 | 授權 | 最後更新 | 為什麼值得看 | 限制 |
|---|---|---|---|---|---|
| [uoftcprg/pokerkit](https://github.com/uoftcprg/pokerkit)（[論文 arXiv 2308.07327](https://arxiv.org/abs/2308.07327)，IEEE Trans. Games 17(1):32–39, 2025） | Python | MIT 【已驗證】 | 2026-10-12（v0.7.7）；504★ | 號稱 mypy strict、doctest＋單元測試 **99% 覆蓋**；重建 **83 手 WSOP 5 萬美元 Players Championship 決賽桌牌譜**並核對底池與最終籌碼；極端低籌碼情境；查表經 MD5 與其他函式庫比對【論文自述，已讀 HTML 版】。架構：operation 都有 `verify_*`／`can_*`，驗證失敗**不改狀態**；盲注、發牌、棄牌、收籌碼等可自動化；可自訂 `divmod`。測試檔 `tests/test_wsop/`、`test_papers.py` 可讀。 | Python；效能約 847 局／秒、評牌約 100 萬手／秒（論文）；2026-09 仍有 #45／#46 這種邊池 bug，所以它是**很好的 oracle，但不是真理** |
| [claudijo/poker-ts](https://github.com/claudijo/poker-ts)（移植自 [JankoDedic/poker](https://github.com/JankoDedic/poker) C++，MIT，2020-08-27） | TypeScript | MIT 【已驗證】 | 2025-09-20（v1.5.0）；65★ | `Table` 狀態機 API（`startHand/actionTaken/endBettingRound/showdown/pots/winners`）簡潔，伺服器導向；整數籌碼。 | 修過兩對 kicker 錯誤（2025-09 commit）；issue 顯示平手、邊界案例曾出錯；無窮舉測試證據 |
| [DGilev91/PokerEngine](https://github.com/DGilev91/PokerEngine) | C# | MIT 【已驗證】 | 2026-08-15（v0.1.8，2026-07-30 建立）；0★ | **唯一找到的 C# NLHE 規則引擎**：確定性、事件式 `PokerHandEvent`、`UncalledBet` 概念、多次 runout、`PotState`、`Real*Tests`（真實牌譜重播：Fold/Uncalled、Runout、Showdown、ExtraBlind）。測試檔約 12 個。 | .NET 10 / C# 12，**不能進 Unity**；專案僅 2 週歷史、單一作者、API 未穩定（README 自述 1.0.0 前會變）；只能當設計參考與測試案例靈感 |
| [SirRender00/texasholdem](https://github.com/SirRender00/texasholdem) | Python | MIT | 2025-01-08；108★ | 有 min_raise／last_raise 的歷史 hotfix 紀錄 | 偏教學用 |
| [elliottneilclark/rs-poker](https://github.com/elliottneilclark/rs-poker) | Rust | Apache-2.0 | 2026-08-13；185★ | arena（2–16 人）、fuzz target（評牌、遊戲重播、CFR）、cargo-mutants mutation testing | Rust；概念可借 |
| [google-deepmind/open_spiel](https://github.com/google-deepmind/open_spiel) | C++ | Apache-2.0 | 2026-08-31；5,529★ | 研究級撲克環境（universal_poker） | 與遊戲規則細節不同步，不適合當 NLHE 規則參考 |

**沒有找到**：有完整測試、可直接用在 Unity 的 C# NLHE 引擎。結論是「自己寫，並用 PokerKit 當差分 oracle」。

### 2.5 測試策略（TDD 要求）
1. **規則表驅動測試**：每條 §2.1 規則至少 3 個案例（正常、邊界、違規輸入）。
2. **牌譜重播測試**：從 PokerKit 測試的 WSOP 牌譜（`pokerkit/tests/test_wsop/`）挑多邊池案例，翻成 C# 測試（動作序列＋指定牌序→預期底池與最終籌碼）。
3. **差分／屬性測試**：隨機動作序列（含非法動作）＋固定牌序，分別餵 C# 引擎與 PokerKit，比較每手最終籌碼；再加 §2.2 的不變量。PokerKit 自己 #25 的 bug 就是靠 1 萬手 RL 才抓到，所以量要夠（>100 萬手）。
4. 兩個 oracle 意見不同時，以 TDA 條文為準並人工裁決（PokerKit #45/#46 證明 oracle 也會錯）。

---

## 3. AI 對手

### 3.1 公開資料與可取用的概念
- **Loki／Poki（Alberta）**：
  - [Opponent Modeling in Poker（AAAI-98）](https://webdocs.cs.ualberta.ca/~games/poker/publications/AAAI98.pdf)【已下載並讀文字】。重點：每個對手維護一張**對起手牌（1,081 種翻牌後五張組合）的權重表**；對手每做一次下注動作，就用 EHS 對各手牌做「再加權」——例：推得需要中位強度 0.6，下限 0.4、上限 0.8，則 EHS>0.8 的手牌權重因子 1.0、<0.4 的因子 0.01、中間線性內插；通用建模（所有人同一模型）與專屬建模（每人一張表）。論文也明說：撲克的要素是手牌強度、潛力、底池賠率、**虛張聲勢**、對手建模與**不可預測性**；「最小化的虛張系統就是照博弈論算出的頻率盲目虛張」。
  - [Papp 論文第 46 節：Loki 的下注規則](https://webdocs.cs.ualberta.ca/~jonathan/PREVIOUS/Grad/papp/node46.html)【已讀】：以樂觀版 EHS' = HS + (1−HS)·PPOT 決策；EHS' ≥ 0.85 加注，≥ 0.50 下注／跟注；低於 0.5 才考慮半虛張、底池賠率。作者承認閾值是「ad hoc but reasonable」。
  - [The Challenge of Poker（AIJ 2002）](https://webdocs.cs.ualberta.ca/~duane/publications/abstracts/2002aij.shtml)：Poki 架構，只讀了摘要（【自述】）。
- **EHS 公式**（[Wikipedia](https://en.wikipedia.org/wiki/Effective_hand_strength_algorithm)，【已讀，頁面自己標「需要更多引用」，且偽碼的分母疑有誤，不要照抄】）：`HS = (ahead + tied/2)/(ahead+tied+behind)`；`EHS = HS×(1−NPOT) + (1−HS)×PPOT`。我們用不到完整的 PPOT／NPOT 枚舉，Monte Carlo 直接採樣河牌後勝率即可涵蓋潛力。
- **Pluribus**（[Science 2019，Brown & Sandholm](https://orc.mit.edu/?p=5643)）：6 人無限注、自我對弈、2 CPU＋128 GB。【自述】結論：**不要走 CFR／自我對弈**——5 週內做不完，也不需要（遊戲目標是「可信有個性」，不是「最強」）。
- **PokerSnowie**：商用神經網路自我對弈教練；沒有找到公開的演算法細節，所以只作為「有這類產品」的背景，不引用其內部做法。
- **Chen Formula**（[thepokerbank](https://www.thepokerbank.com/strategy/basic/starting-hand-selection/chen-formula/)；細節與 [Wikipedia](https://en.wikipedia.org/wiki/Texas_hold_%27em_starting_hands) 有出入）：高牌 A=10、K=8、Q=7、J=6、其餘一半；對子×2 且至少 5；同花 +2；間隔懲罰 0/1/2/4/5；順子加成 +1；進位。可當翻牌前評分的零成本起點，但我建議改用**預先離線算好的 169 類起手牌勝率排名**（同一個評估器跑 Monte Carlo 產生表），比 Chen 更準。
- **開源 bot／C# 參考**：[PokerAlgo（NuGet，C#，.NET 6+）](https://www.nuget.org/packages/PokerAlgo) 提供翻牌前／後 Monte Carlo＋Chen 分數【搜尋結果摘要，未審原始碼與授權，不建議採用】；[neuron_poker](https://github.com/dickreuter/neuron_poker)（MIT，Python，2025-08-04，728★）是 RL 環境；[rs-poker 的 ConfigAgentBuilder](https://github.com/elliottneilclark/rs-poker) 是「用 JSON 設定翻牌前範圍與翻牌後策略」的個性化 agent，**設計與我們要的最接近**（Rust，只能借概念）。我沒找到 Unity 上現成的撲克 AI。

### 3.2 建議架構（5 週內可做完）

```
IBotView（只含：自己的底牌、公開牌面、各人籌碼／下注、動作歷史、座位、玩家「表演訊號」）
   │  ※ 介面層禁止拿到玩家底牌與牌堆 → 防作弊，也利於未來聯網
   ▼
HandAssessor
   ├─ 翻牌前：169 類起手牌勝率排名 → 取「前 X%」範圍表（依位置調整）
   └─ 翻牌後：Monte Carlo 勝率（N 次採樣）vs 對手「加權範圍」；加上聽牌（outs）／牌面濕度
   ▼
Policy（風格參數化）
   ├─ 輸入：equity、potOdds、位置、有效籌碼 SPR、對手動作、個性參數、情緒狀態
   ├─ 輸出：Fold / Check / Call / Bet(size) / Raise(size) / AllIn
   └─ 隨機：種子化 RNG（可重播，利於測試與聯網）
   ▼
ReactionLayer（§3.4）→ 只輸出「閾值偏移」與「表演事件」
```

- **對手建模（輕量版 Loki）**：每個玩家維護 VPIP／PFR／攻擊頻率／棄牌面對下注頻率（滾動統計），而不是完整權重表；把對手動作映射成「範圍收窄」（例：玩家在翻牌圈加注 → 對該玩家假設較強範圍，用 Loki 的 0.4/0.8 內插法縮放勝率）。MVP 先做簡單版：只用「玩家這手是否積極下注」調整 equity 需求。
- **Monte Carlo 預算**：5 個 bot × 每次決策 N 次採樣。若 IL2CPP 每次 7 張評估 ~1–2 µs【推論，未測】，N=1000 約 1–4 ms／bot／決策；在背景執行緒或 Job 中跑並配合自然「思考延遲」（0.8–3 s）即可，不卡主執行緒。**第 1 週要在 Quest 3 真機量。**
- **虛張聲勢**：
  - 半虛張：equity < 下注門檻但有聽牌潛力（同花聽牌／兩頭順）→ 以 `bluffFreq × (1 − 對手預期棄牌阻力)` 機率下注或加注。
  - 純虛張：低 equity、對手顯示示弱（check）、位置佳、牌面對自己「故事合理」時，按 `pureBluffFreq` 觸發。
  - 頻率不要拍腦袋：標準博弈論結論是下注額為底池的 b 倍時，下注範圍中虛張的比例約 `b/(1+2b)`（b=1 → 1/3）【背景知識，未抓來源；Loki 論文只說「可由博弈論算最佳虛張頻率」】。可作為「虛張大師」的上限、「緊兇」的下限參考。
  - 加隨機性：同一情境不要每次同一動作（Loki 論文的 unpredictability）；用種子化 RNG＋小幅隨機擾動閾值（±0.03–0.05 equity）。
- **不要做的**：不要讓 AI 讀玩家底牌；不要用完全隨機的動作（看起來像笨）；不要在 5 週內碰 CFR／神經網路。

### 3.3 四種個性的參數（初值，需要模擬校準）
統計帶來源：[PokerSkill LAG 頁](https://www.pokerskill.com/poker-glossary/lag/)（6-max 現金局 HUD 慣用語，非正式定義）、[PokerStrategy VPIP](https://www.pokerstrategy.com/glossary/VPIP/)。各站數字不同，視為粗略指南。

| 個性 | VPIP（入池率） | PFR（翻前加注率） | 翻牌後攻擊度 | 虛張頻率 | 面對下注棄牌傾向 | 備註 |
|---|---|---|---|---|---|---|
| 緊兇 TAG | 約 18–25% | 約 14–22% | 高（持續下注、價值下注足） | 低–中，且多為半虛張 | 中 | 範例名：「冷靜的老教授」 |
| 鬆兇 LAG | 約 28–35% | 約 22–28% | 很高 | 中–高 | 中低 | 會用大尺度施壓 |
| 跟注站 Calling station | 35%+ | 5–12% | 低（很少加注） | 幾乎 0 | **很低**（不愛棄牌） | 對它純虛張無效，只有價值下注賺錢 |
| 虛張大師 Maniac／Bluffer | 40%+ | 25%+ | 極高 | 高（超過博弈論頻率，可被抓） | 中 | 可被「抓詐」；戲劇張力最大 |

核心參數（Policy 的 ScriptableObject／JSON）：`looseness`、`aggression`、`bluffFreq`、`semiBluffFreq`、`foldToPressure`、`callDownThreshold`、`betSizeProfile`（偏好 1/3、1/2、2/3、底池、超池）、`positionAwareness`、`tiltSensitivity`、`thinkTimeRange`。個性在 headless 自我對弈 10 萬手後驗證統計（VPIP／PFR／AF／WTSD）是否落在目標帶內，這本身就是可 TDD 的自動化測試。

### 3.4 AI 對玩家手勢「有反應」：做成可調參數【設計提案，無外部來源可引用】
原則：**手勢反應是「戲劇／演出」，不是資訊洩漏**。反應只能透過「閾值偏移」影響 AI，且有上限、會衰減，並提供全域強度滑桿（也方便無法做手勢的玩家：關掉也能完整遊玩）。

| 訊號（由 Interaction SDK／手追蹤即時取得） | 判定參數 | AI 內部效果（皆有上限、隨時間衰減） | 演出 |
|---|---|---|---|
| **摔牌／用力丟牌（棄牌手勢速度超過門檻）** | `slamSpeedThreshold`、`startleDurationSec`、`startleSensitivity`（個性相關） | 「受驚」：短時間內提高 `foldToPressure`、降低 `bluffFreq`（膽小型）或相反提高 `callDownThreshold`（懷疑型）；只影響當前這一手與下一手 | 後仰、眨眼、台詞音效 |
| **偷看底牌時間過長／次數過多** | `peekSecondsThreshold`、`peekCountThreshold`、`suspicionGain` | 「起疑」：累積 `suspicion`（0–1），把跟注／抓詐所需 equity 降低 `maxShift × suspicion`；只在該玩家下注時生效 | 瞇眼、盯著玩家 |
| **思考時間長 vs 秒下注** | `tankSeconds` | 讀作「強弱未定」：對該玩家的範圍假設做小幅調整（可設為 0） | 嘆氣、敲桌 |
| **推籌碼力道／丟籌碼** | `chipSlamThreshold` | 情緒 `confidence` 微增（被嚇到或被挑釁），影響加注尺度 | 反應動畫 |
| **注視（眼動／頭部朝向 bot）** | `gazeSecondsThreshold` | `intimidation` 微增（僅膽小型個性有效） | 迴避視線 |

共同控制參數：`reactionIntensity`（0–1 全域）、`maxThresholdShift`（建議 ≤ ±0.08 equity）、`decaySeconds`、`memoryHands`（記幾手）、`perPersonalitySensitivity`、`reactionEnabled`（無障礙開關）。
**不要**把反應做成 AI 能「猜到」玩家牌力的後門；偏移的方向與大小要對所有玩家一致（不能因為玩家其實是詐唬就讓 AI 真的讀出來）。
測試：同一牌局種子下，`reactionIntensity=0` 與 `1` 的差異必須落在上限內；模擬 1 萬手驗證 AI 平均盈虧不會因反應層而偏離基準超過設定容差。

### 3.5 5 週里程碑（建議）【推論】
| 週 | 內容 |
|---|---|
| 1 | 評估器（自寫＋窮舉測試）、引擎骨架（盲注／按鈕／下注輪／最小加注）、Quest 上評估與 MC 微基準 |
| 2 | 引擎補完（all-in、邊池、零頭、攤牌順序、單挑）＋差分測試 vs PokerKit；隨機 bot（跟注／隨機）可跑完整手 |
| 3 | HandAssessor（169 類排名、MC 勝率）＋四種個性參數＋headless 自我對弈統計驗證 |
| 4 | 玩家手勢整合與 ReactionLayer、bot 動畫與台詞 |
| 5 | 調校、年齡分級問卷與合規檢查、比賽提交包（APK／影片／說明） |

---

## 4. Meta 政策與比賽規則

### 4.1 Meta Horizon Store／開發者政策
| 項目 | 內容 | 來源／信心 |
|---|---|---|
| **真錢賭博＋兌現禁止** | 「Developer app content must not include … Real money gambling or promoting or giving users the ability to redeem or cash out in-game currencies or objects.」 | [Content Guidelines](https://developers.meta.com/horizon/policy/content-guidelines/)（頁面標 Updated Jun 27, 2025）【已驗證，下載頁面原文】 |
| 模擬賭博 | **Content Guidelines 與 App Policies 全文都沒有出現「simulated gambling」或「casino」字樣**。所以沒有明文禁止，也沒有明文允許條款；實務上由 IARC 問卷決定分級。 | 我 grep 兩份政策全文【已驗證】 |
| 分級機制 | 「All apps must be rated by the International Age Rating Coalition (IARC).」美國商店顯示 ESRB 風格分級與內容描述。 | [App Policies 5.2.1](https://developers.meta.com/horizon/policy/app-policies)【已驗證】 |
| **先例** | **PokerStars: Vegas Infinite**：ESRB **Mature 17+**，描述：Strong Language、Simulated Gambling、Use of Alcohol and Tobacco、Users Interact、In-Game Purchases (Includes Random Items)；**Poker VR**：ESRB **Teen**，描述：Simulated Gambling、Users Interact、Digital Purchases。 | [PokerStars 商店頁](https://www.meta.com/experiences/pokerstars-vegas-infinite/2370815932930055/)、[Poker VR 商店頁](https://www.meta.com/experiences/poker-vr/686548444781796/)【已驗證】。兩者分級差異可能來自其他內容（酒精、髒話、隨機道具），**不能推論「模擬賭博單獨就是 Teen 或 Mature」**。 |
| ESRB 對「Simulated Gambling」定義 | 「Player can gamble without betting or wagering real cash or currency」；Teen 級可含模擬賭博。 | [Xbox 分級頁](https://www.xbox.com/en-US/games/gameratings)、[ESRB 對 NBA 2K20 的回應](https://wccftech.com/esrb-responds-nba-2k20-gambling-controversy/amp/)（二手來源，定義文字來自舊版鏡像，非 esrb.org 現行頁）【搜尋摘要】 |
| **歐洲 PEGI** | 賭博模擬（教導／美化賭場式遊戲）→ **PEGI 18**。2025-02 Balatro 申訴成功降到 12 但 PEGI 承諾另訂更細準則，截至多篇報導**未見正式公布**；2026-03-12 PEGI 公布新風險類別，2026-06 起新提交遊戲適用：「社交賭場遊戲＝PEGI 18」、含付費隨機道具＝PEGI 16。 | [Gamereactor](https://www.gamereactor.eu/balatro-wins-its-pegi-rating-battle-to-have-its-18-rating-reduced-1503623/)、[Reed Smith](https://www.reedsmith.com/articles/pegi-launches-interactive-risk-categories-overhauls-age-ratings-for-loot-boxes-in-game-spending-and-communication-features/)、[Osborne Clarke](https://www.osborneclarke.com/insights/games-loot-boxes-get-16-plus-age-rating-europe-under-pegi-overhaul)【搜尋摘要，未讀 PEGI 官方文件】 → 預期撲克題材在歐洲最可能是 18。 |
| 年齡群自我認證 | 開發者必須自選：Teens and Adults (13+)、Mixed Ages（含 10–12 歲）、Children（10–12 歲為主）。Meta「不得有針對 10 歲以下兒童的 app」。 | [Age group policy](https://developers.meta.com/horizon/policy/age-groups/)（頁面標 Updated Jul 2, 2026）、Content Guidelines【已驗證】。**【推論】撲克題材應選 Teens and Adults，不要選 Mixed／Children**（政策沒明文，但模擬賭博對 10–12 歲不合適，且 Meta 保留審核你選的年齡群是否準確的權利）。 |
| Quest 用戶年齡 | Quest 最低年齡 10 歲，10–12 歲須家長管理帳號、家長決定可下載的 app。 | [UploadVR](https://www.uploadvr.com/meta-reducing-quest-2-3-minimum-age)／[Meta 家長指南](https://familycenter.meta.com/au/static-resource/meta-quest-parent-guide)【搜尋摘要，2023 年來源為主】 |
| **賣籌碼（IAP）** | 沒有明文禁止販售虛擬貨幣。但：(1) App Policies 1.1.1 必須用平台 IAP；(2) 禁止兌現／贖回（上方）；(3) 隨機物品（loot box）須揭露機率、**不得有現實世界金錢價值或可交易**、**18 歲以下不可購買**、僅限 Teen 以上 app（1.1.3）；(4) 行銷／促銷機制不得促進線上賭博（1.2.1.1）。先例：PokerStars 擴充包 Infinity Roulette Pack：$19.99 賣 **300,000 籌碼**、Mature 17+，並標示「**not available for players located in Washington State**」。 | [App Policies](https://developers.meta.com/horizon/policy/app-policies)、[擴充包頁](https://www.meta.com/experiences/infinity-roulette-pack/2319515621779016/)【已驗證】。**【推論】**：MVP 不要賣籌碼；若未來要賣，需要法務審查（華盛頓州例子顯示有地區性法律風險）。 |
| 必須標示？ | 我**沒有找到**要求遊戲內顯示「虛擬籌碼無現金價值」之類文字的政策條款；商店頁的「Simulated Gambling」描述是 IARC 問卷結果自動帶出。 | 【未找到＝不確定】建議自行加上「僅供娛樂，籌碼無現金價值、不可兌換」，成本低、合規加分。 |
| 廣告 | 廣告不得宣傳線上賭博／真錢賭博。 | [App Policies 2.1.4.3](https://developers.meta.com/horizon/policy/app-policies)【已驗證】 |
| 別混淆 | 「Horizon Worlds Content Guidelines」把模擬賭博列在 **13+ 世界**可含內容中；那是 Worlds 產品，不是 Horizon Store。 | [Worlds 指南](https://www.meta.com/help/quest/481214418021533/)【搜尋摘要】 |

### 4.2 比賽：Meta VR Start Developer Competition 2026
- 官方規則：[Devpost rules](https://start-developer-competition-26.devpost.com/rules)（我下載全文並 grep）；說明：[Meta 官方部落格](https://developers.meta.com/blog/meta-connect-2026-vr-start-developer-competition/)（HTTP 200，內容我沒逐段讀，只用 Devpost 全文）。
- **規則全文對「gambl／casino／poker／chips」的出現次數＝0**【已驗證，grep -c = 0】。也就是**比賽本身沒有針對模擬賭博的獨立限制**，但：
  - 專案必須遵守 Meta/VR Start 政策與條款，包含 Community Standards、Code of Conduct in VR、Developer App Policies、**Developer Content Guidelines**（→ 適用 §4.1 的真錢賭博／兌現禁令）。
  - 「Projects must be suitable for presentation in a public forum and must follow Meta VR Developer Content Guidelines for 10+, 13+, or 18+」；另禁止「adult-oriented」等內容。**【推論】**：撲克題材可能被評審用「適合公開展示」的標準看待，去酒精、去菸、去賭場霓虹風格會比較安全。
- 時程：參賽期 **2026-09-24 10:00 PT – 2026-11-18 12:00 PT**；評審約 11-18 起；得獎公布約 **2026-12-11**。
- 資格：須為 **Meta VR Start 計畫會員**、年滿 18 歲且達當地成年年齡、有 Meta 開發者帳號；巴西、魁北克及制裁地區居民不可參加（搜尋結果摘要，我讀到的 Devpost 全文同樣有資格段落，但地區清單細節以官方為準）。
- **賽道／規格**：
  - 賽道 Entertainment／Gaming／Productivity；遊戲賽道要求「**hands-first（必須）**或 eyes and hands」；hands-first＝「Fully usable with hands end-to-end. Controller support optional.」→ 撲克（坐著、手勢）很契合，**但整個流程（選單、下注、金額輸入、重新開局）都必須能純手操作**。
  - Seated-optimized：設計為坐姿、限制大動作；「airplane seat test：2 呎半徑內所有互動可用」。
  - 評分（各 25% 為主）：創新、體驗設計（要求**passthrough 有目的**：現實房間必須實質改變體驗，不能關掉 passthrough 玩起來一樣；FoV-aware UI）、技術（**Quest 上至少 60 fps**、無 bug）。
  - 「Original, not a wrapper」：核心體驗須為參賽者自建。
  - **必須免費提供給評審**（"made available free of charge"）；→ 比賽版本不能有付費牆（也呼應不賣籌碼）。
  - 提交：APK 上傳到開發者後台新 release channel「**Competition**」、<3 分鐘展示影片（YouTube／Vimeo）、英文（或附英文字幕）。
  - 第三方程式碼／素材必須有授權 → **避免 GPL**（HoldemHand.cs、Cactus Kev repo），Apache-2.0／MIT／ISC 都沒問題（但要保留授權聲明與 NOTICE）。
- **⚠ 重要風險**：「New Experience」要求專案**在 2026-09-24 之後才構思並建成，不得使用既有 codebase**；「Adapted」則是既有專案加上有意義的新功能並在窗口內發佈。這個專案目錄（claw-hand）的 git log 已有 2026-09-24 之前的提交（最近的是「Archive project: M1 fun gate not passed」）。若撲克遊戲重用 claw-hand 的程式碼，可能被判不符「New」。**建議**：撲克專案用全新 repo；手追蹤工具若要重用，先確認屬於「工具／SDK 範圍」還是「既有 codebase」，或改報 Adapted。此為我的判讀，最終由主辦方裁量（規則寫明 Sponsor 可自行判定）。

---

## 5. 建議整理

### 5.1 評估器
見 §0。流程：寫測試（窮舉牌型分佈＋差分比對）→ 寫最小實作 → 真機微基準 → 達標就結案，不達標再 vendor pheval 的 7 張表（Apache-2.0，需保留版權聲明與 NOTICE）。

### 5.2 規則引擎設計（易測試＋未來聯網）
1. **純函式式核心**：`Table`/`Hand` 狀態是不可變（或可複製）的資料；`Apply(state, command) → (newState, events[])`，不碰 Unity、時間、全域亂數。命令（`Fold/Check/Call/RaiseTo/AllIn`）先 `Validate`，失敗不改狀態（PokerKit 的 verify 風格）。
2. **確定性**：整數籌碼（`long`）；牌堆由「種子＋Fisher–Yates」產生（用自帶的小型 PRNG，如 xoshiro／PCG，不用 `System.Random`，確保跨平台一致）【背景知識】；整手牌 = 初始狀態＋種子＋命令序列 → 可重播、可存檔、可做回歸測試。
3. **事件驅動**：引擎輸出 `HandStarted / BlindPosted / CardsDealt(hidden) / ActionApplied / StreetAdvanced / UncalledReturned / ShowdownOrder / PotAwarded / HandEnded`；UI／動畫／音效／AI 都只訂閱事件。DGilev 與 PokerKit 的 `Events`／operation 日誌是同一思路。
4. **伺服器權威＋隱藏資訊過濾**：單機模式＝本機同行程跑「伺服器」；聯網時同一份核心搬到伺服器，客戶端只送命令、只收**依視角過濾後的事件**（別人底牌永遠不離開伺服器直到攤牌）。AI 與人類走同一個命令介面（`IPlayerAgent.Decide(IBotView)`）。
5. **可測試的邊界**：評估器介面化（`IHandEvaluator`，測試時可換假的）；時間（思考延遲、計時器）和 RNG 都由外部注入；引擎專案獨立為 `netstandard2.1`＋`LangVersion 9`，用一般 .NET 測試專案跑（秒級回饋）。

### 5.3 AI 架構
見 §3.2：IBotView → HandAssessor（範圍表＋MC）→ Policy（個性參數）→ ReactionLayer；靠 headless 自我對弈統計（VPIP／PFR／AF）驗證個性；先規則型，**不碰 CFR／神經網路**。

---

## 6. 沒做到的事／不確定處
1. **沒有任何基準測試與原型程式**。所有速度數字來自各專案 README 自述，硬體與條件不同，不可橫向比較；C# on Quest 的實際速度未知（建議第 1 週真機量）。
2. **授權衝突未解**：Cactus Kev 的 `suffecool/pokerlib` GitHub 標 GPL-3.0，而 JBelthoff 的 LICENSE 稱原作者釋出為 public domain，作者網頁文字沒有授權聲明。我沒有法律結論，建議避開。HoldemHand.cs 的 GPL-3 只來自 CodeProject 列表摘要，沒讀到原始碼檔頭。Two Plus Two 授權不明。
3. **PokerKit 的零頭／邊池實作我只讀了關鍵片段**（`_begin_chips_pushing`：把底池依 `divmod` 拆給贏家、餘數給第一位，未確認「第一位」是否為按鈕左側起算），所以「零頭給誰」以 TDA Rule 21 為準，**尚未用 PokerKit 行為交叉驗證**。
4. **TDA Rule 16B（未被跟注退還）的原文我只看到片段**；退還邏輯另由 PokerKit／DGilev 的結構佐證，未逐條核對。
5. **PEGI 新準則**只看到二手報導（Reed Smith、Osborne Clarke、Gamereactor），沒讀到 PEGI 官方文件；IARC 問卷題目本身沒取得（Meta 後台才看得到），所以**無法預測最終分級**，只能列先例。
6. **Meta 是否有「虛擬籌碼遊戲必須顯示聲明」的規定**：沒找到，不等於沒有。建議在開發者後台實際走一次 IARC 問卷與提交流程確認。
7. **Meta VR Start 競賽的資格細節**（居民地區清單、團隊規則）以搜尋摘要為準；若要參賽請直接讀 Devpost 規則全文。Meta 部落格公告頁我只確認 HTTP 200，沒有逐段讀。
8. **「New Experience 不得用既有 codebase」對本 repo 的適用性**是我的判讀，需要確認。
9. **AI 手勢反應（§3.4）與個性參數初值（§3.3）是設計提案**，沒有可引用的學術來源；統計帶來自玩家社群 HUD 慣用語，各站不一致。
10. **Kotlin 評估器**：沒找到成熟開源；建議路線是 JNI 或自行移植。
11. 公開 issue 佐證的 bug 只有 §2.3 的 #1–#9；第 10 項屬於我的風險清單，沒有找到對應的公開案例。
12. 工具使用備註：WebFetch 曾自動把一份 PDF 存到 `~/.claude/projects/.../tool-results/`（工具行為，非專案目錄）；GitHub search API 曾短暫觸及速率限制，之後改用 `repos/.../issues/N` 直接查。

---

## 7. 主要來源索引
- TDA 2026 規則：https://www.pokertda.com/view-poker-tda-rules/
- PokerKit：https://github.com/uoftcprg/pokerkit ；論文 https://arxiv.org/abs/2308.07327 ；HTML https://arxiv.org/html/2308.07327v6
- HenryRLee/PokerHandEvaluator：https://github.com/HenryRLee/PokerHandEvaluator
- bakala12/HoldemPoker.Evaluator：https://github.com/bakala12/HoldemPoker.Evaluator
- archaistvolts/PokerHandEvaluator.cs：https://github.com/archaistvolts/PokerHandEvaluator.cs
- OMPEval：https://github.com/zekyll/OMPEval ；SKPokerEval：https://github.com/kennethshackleton/SKPokerEval
- Two Plus Two（fork）：https://github.com/tangentforks/TwoPlusTwoHandEvaluator ；說明 https://bostik.iki.fi/aivoituksia/projects/twoplustwo-hand-evaluator.html
- Cactus Kev：https://suffe.cool/poker/evaluator.html ；https://github.com/suffecool/pokerlib
- DGilev91/PokerEngine：https://github.com/DGilev91/PokerEngine ；poker-ts：https://github.com/claudijo/poker-ts ；rs-poker：https://github.com/elliottneilclark/rs-poker
- Wikipedia 7 張牌型分佈：https://en.wikipedia.org/wiki/Poker_probability
- Loki：https://webdocs.cs.ualberta.ca/~games/poker/publications/AAAI98.pdf ；https://webdocs.cs.ualberta.ca/~jonathan/PREVIOUS/Grad/papp/node46.html
- Meta：https://developers.meta.com/horizon/policy/content-guidelines/ ；https://developers.meta.com/horizon/policy/app-policies ；https://developers.meta.com/horizon/policy/age-groups/
- 比賽：https://start-developer-competition-26.devpost.com/rules ；https://developers.meta.com/blog/meta-connect-2026-vr-start-developer-competition/
- Unity 6：https://docs.unity3d.com/6000.0/Documentation/Manual/csharp-compiler.html
