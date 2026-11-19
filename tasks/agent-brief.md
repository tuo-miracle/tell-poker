# Agent 共用簡報（MR 德州撲克，暫名 Tell）

- 設計 spec：`docs/specs/2026-10-12-mr-poker-design.md`（先讀）。研究在 `docs/research/`。計畫在 `tasks/todo.md`。
- 引擎與套件：Unity 6000.3.26f1、URP 17.3、OpenXR 1.18.0、Meta XR Core／Interaction（含 .ovr）／MRUK 207.0.0、Test Framework 1.6。
  Meta SDK 原始碼在 `Library/PackageCache/`（主目錄已經匯入；你的 worktree 第一次跑 Unity 才會匯入），**對 SDK 行為的斷言必須引用原始碼行號**。
- 組件：
  - `Poker.Core`、`Poker.AI`、`Poker.Gestures`：純 C# 9，`noEngineReferences`；Gestures 用 `System.Numerics`。
  - Unity 端程式另開 asmdef。
- 共用型別：`Assets/_Project/Scripts/Poker.Gestures/HandPose.cs`（HandPose／HandsFrame，**牌桌座標**：原點在玩家側桌緣中央，+Y 上，+Z 指向牌桌中央，公尺）、`HandRecording.cs`（.hands 錄製檔）。
- 測試：
  - 純 C#：`tools/run-core-tests.sh`（Mono＋NUnitLite，數秒完成）。
  - Unity：`tools/run-unity-tests.sh EditMode|PlayMode [篩選]`。第一次跑要匯入套件，約 5–10 分鐘，Bash timeout 設 600000；指令已帶 `--burst-disable-compilation`，因為本機 macOS 會用簽章錯誤殺掉 Unity。
  - Quest 建置：`tools/build-quest.sh`。
- 硬性規則：
  - **TDD**：先寫失敗測試、跑給自己看紅，再寫實作。回報要附紅燈與綠燈的摘錄。
  - **Unity 假 null**：不要寫 `GetComponent ?? AddComponent`，改用 `TryGetComponent`。
  - 只動你被分派擁有的檔案；需要改共用檔案時，停下來回報。
  - **自己開 worktree**（主目錄有別人在用）：`git -C /Users/laichanean/WebstormProject/vr-poker worktree add .claude/worktrees/<名稱> -b <名稱>`，之後只在那個目錄工作。
  - commit 用 `git -c user.name="Max Lai" commit`，訊息不得提到 Claude，也不得加 Co-Authored-By。不要 push，不要動 main。
  - 註解用臺灣中文、簡短；識別字用英文。
  - 暫存檔（截圖、分析輸出）放你 worktree 裡的 `Logs/scratch/`（已被 .gitignore 忽略）。
- 回報格式：結論（5 行內）、公開 API、紅綠燈與測試結果、worktree／分支／commit、沒做到的事／不確定處（必填）。

## 語音對話（本機開發；2026-10-13 起預設串流）
- 設計：`docs/specs/2026-10-13-voice-chat-design.md`。Unity 端在 `Assets/_Project/Scripts/Voice/`（`Poker.Voice`），中繼服務在 `tools/voice-proxy/`（TypeScript、零依賴，fetch handler 形式，之後可搬 Cloudflare Workers：`src/worker.ts`）。
- 金鑰只在中繼服務：環境變數 `GEMINI_API_KEY`／`ELEVENLABS_API_KEY`，或主 checkout 的 `.secrets/gemini.key`、`.secrets/elevenlabs.key`（已忽略）。**不要印出、不要 commit、不要放進 Assets／APK**。Unity 只有本機共用 token（預設 `tell-dev`，`VOICE_TOKENS` 可改）。
- 實機跑：
  1. `tools/voice-dev.sh`：用 Node ≥ 22.18（自動找 nvm 裡的）開中繼 `127.0.0.1:8787`，並執行 `adb reverse tcp:8787 tcp:8787`（只在本機測就加 `--no-adb`）。
  2. 建置安裝後，打開選單（非慣用手掌心朝上 → 用另一手食指碰浮出的「Menu」；或碰手背上的按鈕）→ Settings 按 `Voice chat` → 同意畫面按 `I agree` → 系統詢問麥克風權限時允許。
  3. 盯著角色約 0.8 秒（角色轉頭看你）後用英文講話；講完停 0.4 秒就算講完（開頭只叫名字時放寬到 0.7 秒）。`adb logcat -s Unity | grep "\[Voice\]"` 看各階段毫秒（`firstAudio`＝講完到第一塊回話音訊）、intent、speaker、path。
- 串流（主路徑）：Unity 一開口就連中繼的 WebSocket `/live`、邊講邊送 PCM16 → 中繼轉給 Gemini Live（`gemini-3.8-live`，thinkingBudget 0，回話用 `reply()` function call，不讓模型開口）→ facts.ts 填數字 → ElevenLabs `/stream` → PCM 一塊塊回 Unity，用串流 AudioClip（`PcmStream` 環形緩衝）邊收邊播。協定見 `tools/voice-proxy/src/session.ts` 開頭。
  備案：中繼端 Live 失敗／2 s 沒回 → 同一條連線改走整段 generateContent；Unity 端連不上或回話前斷線 → 整段 WAV 走 `/talk`；逾時 5 s → 離線備案（只顯示字）。
- 說話一律走角色的 `SpeechChannel`（`Assets/_Project/Scripts/Characters/Speech/`，2026-10-13 起）：每一句（AI 台詞、荷官所有提示、語音回話、「Hm?」）都有聲音，頭上的字幕就是那句、聲音開始時出現、播完再留 0.5 s；同一角色同時只講一句（依 `SpeechPriority` 排隊或打斷，字和聲音一起換）。**不要直接呼叫 `SubtitleBubble.Show`**。
  - 固定台詞預錄在 `Assets/_Project/Resources/Speech/`（ElevenLabs，`tools/voice-proxy/bake/`）。改台詞：`SPEECH_EXPORT=1 tools/run-unity-tests.sh PlayMode SpeechInventoryTests` 更新 `bake/lines.json` → `cd tools/voice-proxy && node bake/bake.ts`（只合成改過的句子；`--count` 只算字數）→ 再跑一次測試。有數字的句子用預錄片段＋數字字拼（`SpeechText.Pieces`）。
  - **聽檢（2026-10-14 起，bake 預設開）**：flash 模型會多念含糊的字（Quest 上 "Can't check here. Call or fold." 念成 "…here, uh, try or need it. Call or fold."），358 句裡抓到 19 句壞的。所以固定台詞改用 `eleven_multilingual_v2`（即時回話仍用 flash，為了延遲），bake 每合成一批就交給 `bake/qa.ts` 聽檢：音檔前後補 0.8 s 靜音（單字片段不補會被 Gemini 編出整句話）→ `gemini-3.8-flash` 盲聽逐字轉錄（temperature 0，不給原文）→ 和原文做正規化逐字比對（大小寫／標點、縮寫、數字＝英文字、um/uh 拼法、同音字），多念／漏念／換字／比同聲音秒／字中位數慢很多都算壞；對不上時再轉錄一次，兩次都只差換字才給原文做一次引導檢查（過了算 warn）。沒過就重合成，最多 3 次；還是沒過就**不寫入 manifest**（同一句的舊音檔留著）、exit 1，要嘛改寫台詞、要嘛 `--force`。
  - 全部重新聽檢：`node bake/verify.ts [--only id,id]` → `bake/verify-report.json`（壞句與 warn 的原文／聽到的字）。轉錄快取在 `bake/verify-cache.json`（鍵＝音檔內容＋原文＋prompt 版本，commit 進去，重跑免費）。改了 `qa.ts` 的 prompt 或送出的音訊處理要把 `PROMPT_VERSION` 加一。
  - ElevenLabs 金鑰沒有 `models_read`／`user_read` 權限（查不到模型清單與剩餘額度）；方案額度 10,000 credits（multilingual_v2 每字 1 credit、flash 0.5），同時最多 2 個請求（`--concurrency` 預設 2）。回 `quota_exceeded` 時 bake 會立刻停，已過檢的句子照常寫入。
  - **目前狀態（2026-10-14）**：額度在重做途中用完，manifest 是混的：80 句 multilingual_v2（全部過檢）＋278 句 flash（數字字與片段全是 flash，拼句時音色一致）。`verify.ts` 349/358 過；還沒修好的 9 句（flash）：`pip.nervous.01`、`rocco.bigbet.02`、`dot.tutorial.call`、`dot.gameover.busted`、`dot.newgame`、`pip.canned.1`、`dot.seg.and`、`dot.num.two`、`dot.num.thousand`。額度恢復後：先 `node bake/bake.ts --only <這 9 句>`（約 230 字），想全部統一再跑不帶 `--only` 的 bake（剩下 278 句約 3,800 字）。注意 `dot.seg.and`／`dot.num.*` 是拼句用的片段，只換其中幾個會和其他 flash 片段音色不同，最好整個聲音的片段一起重做。
  - 實機 log：`[Speech] <who> clip=<id|stream|tts|none> dur=<s> text="…"`（串流在播完時記）；`[Tutorial] version=<n> step=<name> start|done|hint1|hint2 (<原因>)`、`[Tutorial] finished, saved`、`[Tutorial] skipped by player, saved`、`[Tutorial] skipped: already completed v<n>`。
- 教學有版本（`Tutorial.CurrentVersion`＝3，存檔 `TutorialVersion`）：只有 `TutorialDone` 的舊存檔算 v1；版本小於目前的都會再教一次新版。
  v3（2026-10-13）＝兩手固定牌情境（`TutorialScript`，需要四人桌）：玩家每個動作都親手做一次，不自動跳過、不替玩家做；卡住約 10 s 重播示範手＋重講、約 25 s 依辨識器 trace 的「差一點」原因給具體建議；只能從手腕選單主頁的「Skip tutorial」跳過。教學的兩手不算戰績與手勢次數，教完（或跳過）重設牌桌。
- 量測腳本（手動、花一點額度）：`smoke/bench-models.ts`（各模型）、`smoke/bench-live.ts`（Live）、`smoke/bench-tts.ts`（整段／串流配音）、`smoke/bench-e2e.ts`（對跑著的中繼量講完到第一塊音訊）。問題音檔用 ElevenLabs 合成一次存在 `smoke/out/q/`。
- **限流（2026-10-14 起每台裝置）**：Unity 第一次載入 `VoiceConfig` 時在 persistentDataPath 存隨機 GUID（`VoiceDeviceId`，log 只印前 4 碼），`/talk` 帶 `x-voice-device`、`/live` 帶 `?device=`。中繼（`ratelimit.ts`）以裝置計：`VOICE_DEVICE_CAPACITY` 6、`VOICE_DEVICE_PER_SEC` 0.2、`VOICE_DEVICE_DAILY_CAP` 80/UTC 日；全體 `VOICE_DAILY_CAP` 3000 只當預算保險；最多記 10k 台（LRU）。超過回 429／error `code`＝`rate_limited`｜`device_daily_cap`｜`daily_cap`（id 格式錯＝400 `bad_device`）；Unity 收到就不再用 /talk 重送，直接離線備案（rate_limited 停 10 s、每日上限停到 UTC 換日）。log 的 `dev=` 是 sha256 前 6 碼。用量：`curl -H "x-voice-token: …" https://vr-poker-api.elebymax.com/stats`。
- **TTS 額度保險（2026-10-14）**：中繼記本月（UTC）即時回話實際送去 ElevenLabs 的字數（`src/ttsbudget.ts`，存 `/data/tts-usage.json`＝伺服器 `~/vr-poker-api/data/tts-usage.json`，重開不歸零），credits＝字數 × `TTS_CREDIT_PER_CHAR`（預設 0.5＝flash）。到 `TTS_MONTHLY_CREDIT_BUDGET`（預設 15000；方案 20,000/月，留 5,000 給 bake；0＝不限）就不配音：`/talk` 回 `audio: null`＋`code: "tts_budget"`、`/live` 的 reply `audioRate: 0`＋`code`、done 帶 `code` 與 `error: "tts budget reached"` → Unity 走既有的「沒有音訊」路徑（說預錄的罐頭句，有聲音＋字幕）。到 80% 每天記一次 `[voice] WARNING tts budget 80%`，到上限每天記一次 `tts budget reached`。用量看 `/stats` 的 `tts`（`creditsThisMonth`／`budget`／`percent`）。升級方案後：部署時 `TTS_MONTHLY_CREDIT_BUDGET=<新數字> tools/deploy-voice-api.sh`（寫進伺服器 `.env`）。bake 不經過中繼，不在這個計數裡。
- **開發版遙測（2026-10-14，只有測試版）**：`tools/build-quest.sh` 預設在 `VoiceClient.json` 寫 `"telemetry": true`（`TELL_TELEMETRY=0` 關；`TELL_RELEASE=1` 一律關，正式版用這個建）。App（`Poker.Voice.DevTelemetry`，Editor 不開）收 log 開頭是白名單 tag 的行（`[Playtest] [Gesture] [GestureEvent] [LiveTalk] [Read] [Tutorial] [Table] [Speech] [Assist] [Audio] [Voice] [FoolRocco] [UX]`）＋所有 Error／Exception（含堆疊），去掉 GUID／MAC／任何像玩家逐字稿的東西（`transcript=`、`heard "…"`），記憶體最多 3000 行（丟最舊的），每 10 s（落後時 5 s）一批 ≤ 64 KB gzip NDJSON 送 `POST /telemetry`，暫停／失焦／離開時多送一次；離線指數退避到 5 分鐘。開機印一次 `[Telemetry] on (session <8 hex>)`。
  - 每行：`{"t":"<UTC ISO>","s":<序號>,"up":<開 App 後秒數>,"l":"log|warn|error|exception|assert","m":"<log>","st":"<堆疊，只有錯誤>"}`；檔案第一行是中繼寫的 `{"type":"session","session","build","dev","day","received"}`，滿了會有一行 `{"type":"capped"}`。
  - 存在伺服器 `~/vr-poker-api/data/telemetry/<UTC 日>/<裝置雜湊6>_<session>.ndjson`（每個 session 2 MB、全部 200 MB，超過刪最舊的日子；`TELEMETRY=0`／`TELEMETRY_SESSION_MAX_BYTES`／`TELEMETRY_TOTAL_MAX_BYTES` 可改）。檔案是容器的 root 建的，讀沒問題、要刪用 `sudo` 或 `docker compose exec api rm …`。
  - 拿 log：`ssh max@192.168.1.248 'ls -la ~/vr-poker-api/data/telemetry/'`（各日）→ `ssh max@192.168.1.248 'ls -la ~/vr-poker-api/data/telemetry/2026-10-14'` → `scp max@192.168.1.248:'~/vr-poker-api/data/telemetry/2026-10-14/*.ndjson' Logs/scratch/`（或 `rsync -a max@192.168.1.248:vr-poker-api/data/telemetry/ Logs/scratch/telemetry/`）。只看訊息：`jq -r 'select(.m) | "\(.t) \(.m)"' <檔案>`。不在區網時：`curl -H "x-voice-token: …" "https://vr-poker-api.elebymax.com/telemetry/sessions?day=2026-10-14"`（只有清單與大小，不能下載內容）。session id 對得上 headset log 的 `[Telemetry] on (session …)`。
- 測試：`cd tools/voice-proxy && node --test "test/*.test.ts"`（Node ≥ 22.18；Gemini／ElevenLabs 全用假的）；真 API 冒煙測試 `node smoke/smoke.ts`（手動，不進 CI，會花一點額度）。Unity 端 `tools/run-unity-tests.sh PlayMode Poker.Tests.Voice`；真的 ClientWebSocket 連本機中繼：`VOICE_LIVE_URL=http://127.0.0.1:8788 tools/run-unity-tests.sh PlayMode Poker.Tests.Voice.VoiceLiveIntegrationTests.RealWebSocketGetsReplyAndStreamedAudio`（Explicit）。
- **即時牌桌對話（live talk，2026-10-14）**：玩家回合中 AI／荷官就會開口、讀人有感（設計見主規格 §5.1）。
  - 訊號：`PlayerHabits`（`Poker.Gestures`，每一手都量；`DrainSignals()`、`Turn`）；導演：`LiveTalkDirector`＋台詞 `LiveLines`（`Poker.AI`）；接線：`GameController.LiveTalk.cs`。
    讀人：`PlayerRead` 的 `Belief`／`Trust`／`Learn`（`PlayerSignals.Live` 的欄位），總上限 `PlayerReadSettings.LiveMaxShift`＝0.15；AI 的演出狀態 `AiPlayer.LiveStance()`（只回傳狀態，牌不出 AiPlayer）。
  - 開關：`GameController.LiveTalkEnabled`、`LiveTalkBoost`（多話倍率，測試用）；玩家講話由 `VoiceChatController` 設 `GameController.PlayerSpeaking`。
  - 實機 log：`[LiveTalk] t=<回合秒數> signal=<訊號> -> <誰> line=<id>`、`[LiveTalk] skipped <訊號> (<原因>) t=<秒>`、`[LiveTalk] closing <誰> line=<id> (Right|Fooled)`、
    `[Read] <誰> belief=<weak|strong> from <線索+線索> effect=<±0.000> equity <原>-><後> action=<動作>`、`[Read] reveal <誰>: <揭曉卡的字>`。
  - 自我對弈證據：`tools/run-core-tests.sh --where "test =~ Tool_Evidence"`（約 7 分鐘）；真實錄製的訊號分布：`--where "test =~ Tool_RealRecordingSignals"`。
  - 收尾（2026-10-14）：這一回合講過即時台詞的角色，玩家出手後不再補舊的長考台詞（`TableTalk.Veto`）；即時台詞排隊超過 `LiveTalkDirector.MaxQueueSeconds`（1.5 s）或玩家已經出手就丟掉（`Utterance.MaxWait`／`SpeechChannel.DropQueued`）。
    log：`[LiveTalk] suppressed tank <誰> line=<id> (spoke live this turn)`、`[LiveTalk] dropped <誰> line=<id> (queued 1.5 s|player acted)`。
- **手勢備援按鈕（2026-10-14）**：規則 `FallbackAssist`（`Game/Tutorial/`）、接線 `GameController.Assist.cs`、外觀 `AssistButtonsView`。玩家回合中同一種手勢失敗 2 次（敲桌沒第二下、捏籌碼差一點且 1 s 內沒捏起、推牌中斷、籌碼丟到下注線前、不合法的動作；1 s 內的 trace 算同一次；拿著／剛動籌碼 1.5 s 內的「沒第二下」不算），
  或輪到 ≥ 20 s 而手有在動（用過按鈕後：8 s，不管手動不動）→ 桌緣左側（籌碼堆另一側）淡入 Check／Call N、Fold、Bet N／Raise to N（沒有 All-in；可過牌時不給 Fold；最小下注等於全下時不給 Bet）。
  按法：`FingertipButton`（懸停滿 0.25 s 上膛、往下按到 2 cm 內）＋食指朝下（手掌比指尖高 ≥ 2 cm），手平放落下不算。教學那一手不出現；手腕選單 Settings 頁「Assist buttons: On/Off」（預設開，`UxSettings.AssistButtons`）。
  實機 log：`[Assist] shown (<knock failed|chips failed|fold failed|invalid> x2|stuck 20s|stuck 8s) buttons=Check|Bet 20`、`[Assist] used <按鈕字>`、`[Assist] hidden (used|gesture worked|turn over|off|blocked)`。
- **叫出選單（2026-10-14，實機回報「用手叫選單很不靈敏」）**：邏輯在 `Poker.Gestures/MenuSummon.cs`（`PalmMenu`、`TouchButton`、`MenuSummonLog`，純 C#，`tools/run-core-tests.sh --where "class =~ PalmMenu"`），接線 `UxController.OnFrame`，畫面 `WristMenuView.UpdatePalmButton`。
  - 主要＝掌心朝上：非慣用手掌心法線離正上方 ≤ 35°（出現後放寬到 50°）、中指＋無名指尖離手掌平均 ≥ 6 cm（出現後 5 cm）、沒有捏合（`IndexPinching`、強度 ≥ 0.35、拇指食指尖 < 2.5 cm 都算捏）、手掌高於桌面 6 cm，停 0.4 s → 手掌上方 9 cm 出現按鈕（平滑跟手，時間常數 0.05 s；按鈕面朝頭）。另一手食指進到半徑 3 cm 的球停 0.15 s（開始停之後抖到球外 1 cm 內不重來）或一幀內穿過按鈕面（穿過點在 3 cm 圓內）＝按下；冷卻 0.6 s、要離開 1.5 cm 才能再按。掌心轉開 > 0.3 s 收起；掌心那手掉追蹤 0.5 s 內按鈕停在原地，食指那手掉追蹤 0.25 s 內停留計時不重來（手疊手遮擋）。
  - 擋下（不出現）：掌心正對頭 25° 內（系統選單手勢是掌心朝頭＋捏合）、捏合、手掌低於 15 cm 且離自己的牌 15 cm 內／籌碼堆 12 cm 內（翻腕咪牌、攤牌翻牌都會掌心朝上）、任一手拿著牌／咪牌中／拿著籌碼、攤牌等玩家翻牌（`GameFlow.ShowdownWait`）。132 段真實錄製（兩手各當叫選單的手跑一次，共 264 次）0 次誤叫（不給牌桌狀態也是 0 次）。
  - 次要＝手腕按鈕（`WristMenuButton`）：顯示 4 cm 圓盤，可按範圍半寬 3 cm＋邊界 1.5 cm、沿手背 −2～+3.5 cm，停 0.2 s 就按（不用上膛）；拿著牌或籌碼時不按；掌心朝上時收起。
  - 第一次用掌上按鈕前，按鈕上多一行「Touch with your other hand」（`UxSettings.PalmMenuHintSeen`，存檔）。沒有適合的預錄台詞，所以沒有語音提示。
  - 實機 log（`[UX]` 已加進遙測白名單）：`[UX] palm menu button shown`、`[UX] menu opened|closed via palm|wrist`、
    `[UX] menu near-miss: <原因> <細節>`（同一個 log 每 1.5 s 最多一行，被吃掉的行數附在下一行 `(+N suppressed)`）。原因：
    姿勢沒叫出來（大致朝上 ≤ 65° 持續 ≥ 0.4 s，或持續 0.8 s 時先記）＝`palm-angle`／`facing-head`／`pinching`／`fingers-curled`／`too-low`／`blocked-by-cards`／`blocked-by-chips`／`blocked-holding`／`blocked-showdown`／`hold-short`（夠朝上但不到 0.4 s）／`tracking-lost palm`，細節 `angle=<最小角>deg hold=<最長連續>s ext=<cm> height=<cm>`；
    按鈕出現但沒按到＝`finger-missed`／`tracking-lost finger`／`tracking-lost palm`／`palm-away`，手腕＝`wrist-missed`／`tracking-lost wrist`／`tracking-lost finger`／`blocked-holding`，細節 `via=palm|wrist closest=<離按鈕範圍 cm> dwell=<s>`。
- **明文 HTTP（2026-10-14 起 release 關閉）**：`ProjectConfigurator` 設 `InsecureHttpOption.NotAllowed`。要連本機中繼：`TELL_ALLOW_HTTP=1 VOICE_URL=http://127.0.0.1:8787 tools/build-quest.sh`（只給開發建置；`build-quest.sh` 遇到 http 的 VOICE_URL 沒設這個就停）。
- 公開狀態序列化（`PublicTableState`）從不讀 HandState 的底牌；中繼也會白名單過濾一次。改這兩處要跑 `PublicStateTests`。
- **牌桌換位置（2026-10-15，實機回報「校準過的桌子轉向／換房間後改不了」）**：
  - 錨點放回時先檢查（`TableRestoreCheck`）：頭的水平朝向與「頭→玩家側桌緣」夾角 ≤ 60°（桌緣離頭 < 0.2 m 時改用桌面中心）、桌緣水平距離 ≤ 1.5 m、桌面低於眼睛 0.15–0.9 m。不合理就這次不用：放到面前（這個 session 只對齊**面前**的 MRUK 桌子，`PickTable(frontOnly)`）＋顯示雙手手印；舊錨點不刪（`TableAnchorFlow.DetachActive`）。
  - 換位置校準（`Poker.Gestures/TableRelocate.cs` 的 `TableRelocateDetector`，接線 `HandSurfaceCalibration`）：桌子鎖定（HandCalibrated／Restored）時不用提示，雙手平放（掌心朝下、同高 2.5 cm 內、兩掌 15–80 cm、靜止 1.2 cm 內）2.0 s，而且位置明顯不同：雙手中心離目前手牌區 > 0.30 m（但放在目前絨布上同高度 ±4 cm 不算），或兩掌連線＋手指方向＋混合 yaw 都轉了 > 25°；放在大腿／扶手（比桌面低、桌緣前 5 cm 到後 70 cm、左右 35 cm 內）不算；桌面要低於眼睛 0.15–0.9 m。算法同提示校準（高度＋yaw＋原點），之後照常存成新錨點；搬超過 1.5 m 就把舊錨點留給原本的地方。觸發後要先抬手才會再觸發。UX 播一聲確認音（`SfxKind.Win`，pitch 1.25、音量 0.35；沒有適合的預錄台詞）。虛擬桌一律顯示手印。
  - 驗證：`tools/run-core-tests.sh --where "class =~ TableRelocate"`（132 段真實錄製、1000 段雜動、1000 次絨布上休息、500 次大腿／扶手休息都 0 次誤觸；300 次換位置全偵測到）。
  - 實機 log：`[Table] restored anchor rejected (<原因>) -> placement`、`[Table] keeping anchor <uuid> for later (<原因>)`、`[Table] relocate candidate held=<s>s (still holding|too close[, no yaw|, not a desk]) dist=<m>m yaw=<度>° [lap|on current felt|…]`、`[Table] relocate candidate held=<s>s (released)`、`[Table] recalibrated at new spot (dist=<m>m yaw=<度>°)`。
- **選單瘦身（2026-10-15）**：`MenuModel`（項目與動作）＋`WristMenuView`（格子版面、字太寬先縮到 85%、再折兩行）。主頁 Resume／How to play／Move table here／Settings／New game／Quit（教學中＋Skip tutorial）；How to play＝7 個示範（Peek、Bet、Check、Fold、All-in、Show cards、Talk）＋Replay tutorial＋Back；Settings＝Voice chat、Hints、Assist buttons、Text size、High contrast、Left-handed、Back。
  - Move table here＝`TablePlacer.BringToFront("menu")`（系統 recenter 也走這裡，log `[Table] menu|recenter -> place in front (was <來源>)`）；場景沒有 TablePlacer 才退回只顯示手印。
  - Hints 一個開關＝`UxSettings.SetHints`（`Hints`＋`HandHints` 兩欄都留著）；Text size＝`UxSettings.NextTextSize`；Left-handed＝`Handedness` Left↔Auto。
  - 讀檔遷移 `UxSettings.Migrate()`（`SettingsStore.Load` 每次做）：兩種提示關了任一個就都關、`GestureSensitivity` 回到 1.0、Fast motion 照存檔。
  - Fool Rocco 只在開發工具場景（`TELL_DEV_TOOLS=1 tools/build-quest.sh`，場景有 `RecorderPanel`）的主頁出現（`GameMenuActions.FoolRoccoAvailable`）；情境程式與 `FoolRoccoFlowTests` 照舊。
  - 版面測試：`tools/run-unity-tests.sh PlayMode GesturesMenuIntegrationTests.EveryPage_FitsThePanel_WithBigButtonsAndLegibleLabels`（每頁按鈕 ≥ 4 × 3 cm、字 ≥ 1.2°，截圖在 `Logs/scratch/ux-menu-*.png`）。
