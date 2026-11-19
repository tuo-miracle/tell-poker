# Tell — 專案規則（給 Claude Code）

Quest 3 混合實境撲克（Unity 6000.3.26f1）。先讀 `docs/HANDOVER.md`（現況、送件步驟、坑），技術細節讀 `tasks/agent-brief.md`。
agent-brief 的第 18–19 行（worktree 路徑、commit 署名）是原作者個人的本機設定，不適用。照團隊自己的 git 規則做。

## 開發
- **TDD**：任何有邏輯的程式碼，都先寫失敗的測試並跑出紅燈，再寫最小實作。回報時附紅燈和綠燈的摘錄。純設定檔與文件可以例外。
- 測試指令（參數以腳本為準）：
  - `tools/run-core-tests.sh [--where "class =~ X"]`：純 C# 的 Core／AI／Gestures
  - `tools/run-unity-tests.sh <EditMode|PlayMode> [testFilter regex]`
  - `cd tools/voice-proxy && node --test "test/*.test.ts"`（Node ≥ 22.18）
- 長輸出導到檔案再 `tail`。Unity batchmode 第一次匯入要 5–10 分鐘，Bash timeout 設 600000。
- `Poker.Core`／`Poker.AI`／`Poker.Gestures` 是純 C# 9（`noEngineReferences`），不能引用 UnityEngine。
- Unity 假 null：不要寫 `GetComponent ?? AddComponent`，改用 `TryGetComponent`。
- 對 Meta SDK 行為的斷言，要引用 `Library/PackageCache/` 原始碼的行號。
- 隱藏資訊：AI 只看 `BotView`。`PublicTableState` 不讀底牌；改這兩處要跑 `PublicStateTests`。

## 建置與上機
- 一律用：`TELL_REALISTIC_PREVIEW=1 SECRETS_DIR=<repo>/.secrets tools/build-quest.sh`
  - **不要用 `--skip-configure`**，否則場景會變回舊的卡通角色。
  - 裝完 `adb logcat -s Unity` 要看到 `[RealisticPreview]` 才算數。
- 建置會重建 `Assets/_Project/Scenes/Main.unity`。repo 提交的是預設（卡通）場景，寫實建置後的場景不要提交，`git checkout -- Assets/_Project/Scenes/Main.unity` 還原。不要手改場景，改 `Assets/_Project/Editor/Scene/MainSceneBuilder.cs`。
- 交件版：加 `TELL_RELEASE=1`；不要帶 `TELL_DEV_TOOLS`、`TELL_ALLOW_HTTP`。
- **先在實機自己驗證，再交給人**：
  - 影響看得到／聽得到的改動，要用 logcat、截圖、`dumpsys audio` 確認。
  - 首次流程要跑「清空存檔」和「保留存檔」兩種狀態。
  - 回報時分開寫「實機確認過的」和「只在 Editor 驗證的」。
- 頭盔截圖、錄影只處理自己這次拍的檔（拍前後比對檔名），不要刪頭盔上別人的檔。
- 沒聲音的排查順序（遠端桌面造成 `mutedState:appOps`、整台頭盔音訊卡住）見 HANDOVER §5。

## 說話＝聲音＋字幕
- 每一句台詞（AI、荷官、教學、語音回話）都要同時有聲音和頭上字幕，兩者文字一致、同時出現。這是無障礙需求，不能拿掉其中一個。
- 一律走角色的 `SpeechChannel`，不要直接呼叫 `SubtitleBubble.Show`。斷網時要有預錄的聲音。
- 新台詞要能預錄（bake），流程見 `tasks/agent-brief.md` 的語音段落。

## 付費 API
- ElevenLabs、Gemini 是付費服務，即時回話和預錄台詞共用同一個額度。
- 批次工作（大約超過 500 次請求或 2k 字元 TTS，例如 `bake`、`verify`、`smoke/bench-*`）開工前：
  - 先估用量（`bake.ts --count`），再查剩餘額度。查不到就問人。
  - 寫明上限，超過就停；即時回話至少保留約 30% 的餘量。
- 升級方案、改額度上限、重新部署中繼，都要先問人。評審凍結期（2026-11-18 12:00 PT 到約 12/11）中繼和 App 都不能改。

## 秘密
- 秘密只放在 `.secrets/`（已 gitignore），用 `SECRETS_DIR` 指向它。不要讀出、印出、commit，也不要放進 `Assets/` 或文件。
- `Assets/_Project/Resources/VoiceClient.json` 是建置時產生的（含通行碼，已 gitignore），建完可以刪。
- 秘密清單（只有檔名和用途）在 HANDOVER §8，值向原作者 Max Lai 索取。

## 文件與語言
- 文件、註解、回報都用繁體中文（台灣用語），識別字用英文。遊戲內文字一律英文（比賽規則）。
- 值得留下的研究結論寫進 `docs/research/`。計畫與進度放在 `tasks/`，教訓放在 `tasks/lessons.md`。
- 文件裡的路徑寫相對 repo 根目錄的路徑。
