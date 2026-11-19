# 牌桌語音對話 — 設計草案（待使用者確認）

> 日期：2026-10-13｜依據：使用者需求（自由對話但限牌局、中英都要、看著誰就跟誰講）＋技術研究（STT／LLM／TTS／比賽規則）
> 變更：原 spec §9「不做 LLM 即時對話」改為「做，限牌局內容」。

## 1. 體驗
- 看著某個角色（頭部朝向，Quest 3 沒有眼動）開口說話；叫名字（「Rocco，…」）以名字為準；都沒有就由荷官 Dot 回答。
- 角色用英文語音回話（比賽規則：作品須為英文或附英文字幕；語音本身是英文）。玩家用英文提問。
  ~~2026-10-13 起角色頭上不顯示語音回話與 AI 台詞的字~~ → 同日改版（§10）：每一句話都有聲音也都有字幕（聽障玩家），字和聲音走同一條 SpeechChannel，保證同步。
- 牌局內容：底池、各家籌碼、要跟多少、盲注、輪到誰、剛剛誰做了什麼；閒聊與嗆聲（「你在詐唬嗎？」）由角色依個性回嘴，並帶出符合可信度的破綻。
- 牌局外內容（天氣、寫程式…）：角色依個性回嘴把話題拉回牌桌，或不理。
- 開口前不用按鈕：盯著角色約 1 秒後開始收音（角色做出「在聽」的反應）；角色講話時暫停收音（半雙工，先不支援插話）。

## 2. 規則與公平
- 語言模型只拿到**公開資訊**（盲注、底池、各家籌碼、公牌、本街與歷史行動、誰棄牌）＋該角色自己的表演指令（虛張／自信／猶豫）與個性。**任何底牌都不送**，包括角色自己的，所以不可能說溜嘴。
- 事實數字（底池、籌碼、跟注額）由遊戲程式依結構化結果填入真實數字，或在中繼服務比對後才放行；模型不能亂報數字。
- AI 讀玩家的上限（~~±0.08~~ 2026-10-14 起 ±0.15，見主規格 §5.1）；對話內容不額外影響 AI 決策（第一版），只有「玩家開口了」是一個即時訊號（AI 不接話）。

## 3. 技術組合（建議）
| 環節 | 選擇 | 備案 |
|---|---|---|
| 語音辨識 | **Gemini**（使用者決定，2026-10-13） | — |
| 回話 | **Gemini**（使用者決定）：直接收音訊，一次完成辨識＋角色回話；回話模型做成可替換 | 品質不足時再評估 |
| 配音 | **ElevenLabs** Flash v2.5（使用者決定），4 個角色各一個聲音 | — |
| 中繼服務 | Cloudflare Workers（金鑰只放這裡；每台裝置 token、限流、內容過濾） | — |

- 結構化輸出：`{speaker, transcript, text, intent: on_topic|off_topic|banter, emotion}`。
- 斷網時：本地關鍵字（pot／stack／chips／call…）用模板回答並只顯示字幕；其他話題播預錄台詞。牌局本身不依賴網路。

## 4. 數字（推算，待實機量測）
- 延遲：講完話到角色開口約 1.3–2.2 秒；角色先播「嗯？」與抬頭動作蓋過等待。
- 費用：每局（8 分鐘、約 20 次對話）約 US$0.03–0.15；Cloudflare Workers US$5／月。
- 工期：約 7–10 人天。

## 5. 隱私與上架
- 首次使用的同意畫面＋公開隱私頁：語音即時送到辨識、Claude、配音服務，不儲存。
- 遵守 Meta Data Use Policy（麥克風資料屬裝置使用者資料，分享給第三方要同意）與 VRC.Quest.Privacy。
- 角色嗆聲維持 PG 等級（中繼服務過濾）。

## 6. 需要使用者處理（我不能代為註冊帳號或輸入金鑰）
- 申請帳號與 API 金鑰：Google（Gemini）、ElevenLabs、Cloudflare（回話若 A/B 用 Claude，另需 Anthropic）；金鑰由使用者設定到中繼服務。
- 角色聲音（2026-10-13 定案，ElevenLabs 內建）：Pip＝Jessica `cgSgspJ2msm6clMCkdW9`、Rocco＝Callum `N2lVS1w4EtoT3dr4eOWO`、Vex＝Lily `pFZP5JQG7iQjIQuC4Bku`、Dot＝Sarah `EXAVITQu4vr4xnSDxMaL`；模型 `eleven_flash_v2_5`。

### 6.1 寫實角色自己的聲音（2026-11-02，使用者回報「男角講女聲、女荷官講男聲」）
- 規則：寫實角色坐進座位時，**台詞依座位個性，聲音依角色**（之後的角色池沿用）。卡通模式（lineup 給 `-`）維持 pip/rocco/vex。
- 荷官 Dot 改 Sarah（女）；`Resources/Speech/dot` 現有 130 句仍是舊的 Eric 男聲，要重錄（bake 依 hash 會自動列入）。
- 對照表（ElevenLabs 內建聲音；ID 出處見最右欄，**全部待新 key 用 `/v1/voices` 確認帳號可用**）：

| 角色 | 外型／個性 | 聲音鍵 | 聲音 | voice_id | 理由 | ID 出處 |
|---|---|---|---|---|---|---|
| Marcus | 亞裔、約 40、高領毛衣、冷靜精明 | `marcus` | Eric | `cjVigY5qzO86Huf0OWal` | 中年美式、平順沉穩，不搶話的精明感 | repo 紀錄：Dot 2026-10-13～10-30 用過，130 句已成功合成 |
| Hank | （維持） | `rocco` | Callum | `N2lVS1w4EtoT3dr4eOWO` | 使用者指定沿用；坐 Rocco 座直接用現有音檔，不重錄 | §6 |
| Jace | 黑人、20 多歲、自信隨性 | `jace` | Liam | `TX3LPaxmHKxFdv7VOQHJ` | 年輕美式男聲、有活力 | SignalWire 的 ElevenLabs 聲音表 |
| Reginald | 約 70 歲紳士 | `silas` | Bill | `pqHfZKP75CvOlQylNhV4` | 唯一的年長美式男聲（內建清單標 old）；第 4 位對手「老手」Silas 的座位聲音也是 Bill，所以 Reginald 沿用聲音鍵 `silas`（坐老手座位不用錄兩次） | ElevenLabs 官方 Default Voices 頁（搜尋摘要）＋SignalWire 表 |
| Erik | 大鬍子北歐型、30–40、沉穩 | `erik` | Brian | `nPczCjzI2devNBz1zQrb` | 低沉、共鳴、平穩的中年男聲（內建沒有北歐口音） | ElevenLabs 官方 Default Voices 頁（搜尋摘要）＋SignalWire 表 |

  - 老手座位（`CharacterKind.Veteran`，卡通名 Silas，table-r4 新增）：座位聲音鍵 `silas`＝Bill；語音對話也能叫他（中繼 `CHARACTERS`／`PERSONAS`／`CANNED` 與 Unity `VoiceCharacter.Silas`、`VoiceLines`、`VoiceFallback` 都有 Silas；個性依 `Personality.Veteran`：緊凶、話少、乾冷幽默）。其他角色多了「It's Silas's turn.」「Silas has」兩句。
  - 都不和 Jessica／Callum（Hank 除外）／Lily／Sarah 重複，彼此也不重複（`test/elevenlabs.test.ts` 檢查）。
  - 備選：Jace＝Will（年輕、隨性，ID 沒查到可靠來源）；Reginald＝George `JBFqnCBsd6RMkjVDRZzb`（英式、溫暖但偏中年）。聲音描述是依 ElevenLabs 公開的內建聲音說明，沒有盲聽過，拿到 key 後先各合成一句試聽再 bake。
- 實作：
  - 聲音鍵＝預錄資料夾名＝中繼選聲音的鍵。Unity `SpeechVoices`（`Scripts/Characters/Speech/SpeechVoices.cs`）把角色名對到聲音鍵（Hank→`rocco`、Reginald→`silas`）；中繼 `src/elevenlabs.ts` 的 `CAST_VOICES` 對到 ElevenLabs ID（兩邊名單由 `CastVoiceTests.ProxyKnowsEveryCastVoice` 對齊）。
  - 預錄：`SpeechInventory` 只為**預設陣容**（`RealisticPreview.DefaultLineup`）裡聲音和座位不同的角色，再錄一次該座位的全部句子（id `marcus:pip.value.01`，檔案 `Speech/marcus/marcus_pip_value_01`）。查詢鍵仍是「聲音鍵＋原文」，所以 Hank 坐 Rocco 座＝`rocco`＋原文，零重錄。
  - 執行期：`SpeechChannel.Voice`（預設＝座位聲音），寫實角色坐上來時 `UseVoice`；這個聲音沒錄某句時退回座位聲音並 `LogWarning`（`[Speech] pip: voice rocco has no clip … using the seat voice pip`），不會無聲、也不會默默變成別人的聲音。
  - 即時回話：Unity 在 `/live` start 與 `/talk` body 帶 `voices`（例 `{"pip":"marcus"}`，只列和座位不同的），中繼配音用該聲音；不認得的鍵就用座位聲音。
  - `node bake/bake.ts --count` 依聲音列出這次要合成的句數與字元數。
  - 待錄：句子（或拼它的每個片段）已在匯出清單 lines.json、只是 manifest 還沒有＝待錄，守門測試標 Inconclusive 並列出聲音（不算通過）；清單裡根本沒有（程式漏列）或 manifest 有但音檔壞掉仍然 FAIL。執行期沒音檔：字幕照出、記 warning（無聲）；語音對話的備案改說罐頭句。

## 7. 風險
1. 評審網路不穩 → 離線備案（§3）。
2. 辨識錯誤（口音、牌桌噪音）→ 提示詞給牌局詞彙、答不出來就讓角色用個性帶過。
3. 角色聲音分不開、口音不像台灣 → 盲聽後再定。
4. 時程：與 M1 驗收、M3 打磨搶時間（見 §8）。

## 8. 排程建議
- 先完成 M1 驗收（敲桌修好＋使用者親玩 10 手判斷讀心與手勢）。
- 驗收後開工，約佔 M3 打磨前半；M3 的 FoV、多房間、美術打磨相應壓縮。
- 未查證、需實機驗證：Quest 回音消除、麥克風權限的已知坑、Gemini 的現行型號、串流延遲與英文辨識品質（2026-10-13 已確認金鑰可用；`gemini-3.8-flash` 非串流整段辨識約 2.5 秒）、Quest 是否有內建語音辨識。

## 9. 延遲改版（2026-10-13，實機回饋「回話很慢」）
實機 13 次：gemini 2.1–4.8 s（中位數約 2.7 s）＋TTS 0.2–0.5 s＋VAD 0.6 s＋上傳。目標：講完到第一個聲音 ≤ 約 1.2 s。
量測（`tools/voice-proxy/smoke/bench-*.ts`，同一組 6 句 ElevenLabs 合成的英文問題，開發機 → Google／ElevenLabs，毫秒，中位數／p90；不含 VAD 結尾靜音與 Quest 播放緩衝）：

| 做法 | 講完 → 第一塊回話音訊 | n |
|---|---|---|
| 原本：/talk 整段、gemini-3.8-flash（low）、整段 TTS | 2552 / 2724 | 6 |
| 只換模型（generateContent 串流 SSE，回話 text 欄位完成時間；未接 TTS） | 3.8-flash 2293／2892、3.7-flash 2143／2568、3.6-flash 1465／1937、3.5-flash 1403／1897、3.5-flash-lite 1106／2202、3.1-flash-lite 1143／1458、flash-lite-latest 1750／2168、2.5-flash-lite（budget 0）1398／3078 | 各 6 |
| 換最快模型＋串流 TTS（推算：flash-lite text 完成 ≈ 1.1 s＋TTS 第一塊 0.16 s） | ≈ 1.27 s（未實測端到端） | — |
| **Gemini Live（gemini-3.8-live）邊講邊送＋reply() function call＋ElevenLabs /stream** | 974 / 1113（預設思考） | 18 |
| **同上，thinkingBudget 0（採用）** | **708 / 969** | 18 |

- TTS：整段 269／399 ms，/stream 第一塊 162／182 ms（n=8）。
- Gemini 串流的回話只有 1–2 句，第一句完成和整段完成幾乎同時（差不到 50 ms），所以不切句送 TTS，等 text 完整後一次送 /stream（facts.ts 照舊對整句檢查，數字保證不變）。
- Live 的模型只能輸出 AUDIO（TEXT 被拒），所以讓模型「不開口、只呼叫 reply()」，參數就是原本的 JSON 欄位；18/18 句 intent／fact 正確，數字都用佔位符。
- 講太短、Live 還沒 setup 完就講完時（整段一次送）：1268／1464 ms（n=6）。
- VAD 結尾靜音 0.6 → 0.4 s；開頭很短（< 0.6 s，例如只叫了名字）時放寬到 0.7 s。合成資料測試：句中 0.3–0.35 s 停頓不切斷；講完一整句後停 0.5 s 會切成兩段（取捨）。
- 預估實機：0.4 s 靜音＋約 0.7 s ≈ 1.1 s（adb reverse 本機上傳可忽略；Quest 端串流播放先緩衝 0.08 s）。**尚未在 Quest 上量**。
- 備案鏈：Unity 連不上 /live 或回話前斷線 → 整段 /talk；中繼端 Live 失敗、沒呼叫 reply() 或 2 s 沒回 → 同一條連線改走 generateContent（GEMINI_MODEL）；Unity 5 s 沒收到回話 → 離線備案（只顯示字）。

## 10. 字幕與聲音同步（2026-10-13，實機回饋「荷官有字沒聲音」、無障礙需求）
- 規則：角色頭上出現的字，一定是那個角色正在講（或剛講完 0.5 s 內）的那一句；每一句話都有聲音。
- 做法：每個角色一條 `SpeechChannel`（排隊／依 `SpeechPriority` 打斷，打斷時字和聲音一起換）。先前字被蓋掉的原因是別的台詞直接覆寫同一個泡泡。
- 固定台詞全部用 ElevenLabs 預錄（322 句、4,231 字元；`tools/voice-proxy/bake/`，manifest 記 hash，可重跑）。
- 有數字的句子（開場戰績、離線備案的底池／籌碼／盲注）：用預錄片段＋數字字（zero…ninety、hundred、thousand、million、percent）拼接，不走即時 TTS——
  這些句子大多在斷網或中繼失敗時才出現，即時 TTS 正好不能用；拼接離線可用、零延遲，字幕照原句（數字顯示阿拉伯數字）。
- 半雙工只看對話的聲音（回話、「Hm?」）；玩家開口時，閒聊等級的台詞先排隊。
