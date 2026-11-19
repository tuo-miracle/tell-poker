# Tell — Quest 3 混合實境撲克

在你真實的桌子上打德州撲克。全程徒手：咪牌、捏籌碼、丟籌碼下注、敲桌過牌、雙手推出 All-in。
AI 牌手（座位名 Pip、Rocco、Silas、Vex，荷官 Dot）會即時讀你的猶豫和手部動作、當場講出來，並影響他們的決策；你也可以反過來故意演出破綻騙他們。
看著某個角色開口，就能用英文跟他對話（語音經 `tools/voice-proxy` 中繼）。沒有網路時遊戲照樣能玩（預錄台詞）。

送件與比賽影片用的是寫實角色版：建置時要帶 `TELL_REALISTIC_PREVIEW=1`。參賽目標：Meta VR Start Developer Competition 2026（截止 2026-11-18 12:00 PT）。
**完整交接（現況、送件步驟、架構、坑）：[`docs/HANDOVER.md`](docs/HANDOVER.md)**

## 環境需求

| 項目 | 版本／說明 |
|---|---|
| macOS | 腳本用 BSD `stat -f`、預設的 Unity 路徑都是 macOS 的 |
| Unity | **6000.3.26f1**（`ProjectSettings/ProjectVersion.txt`），加裝 **Android Build Support**（含 OpenJDK、Android SDK & NDK）。腳本預設路徑 `/Applications/Unity/Hub/Editor/6000.3.26f1/…`，裝在別處就設 `UNITY=<Unity 執行檔>` |
| 套件 | URP 17.3、OpenXR 1.18.0、Meta XR Core／Interaction（OVR）／MRUK 207.0.0、glTFast 6.20、Test Framework 1.6（`Packages/manifest.json`，開專案時自動匯入） |
| Node | **≥ 22.18**（`tools/voice-proxy` 直接執行 TypeScript、零依賴，不用 `npm install`） |
| Mono | `mono`、`csc`、`nuget`（純 C# 測試 `tools/run-core-tests.sh`） |
| python3 | `build-quest.sh` 用它產生設定 JSON |
| adb | Unity 內附的（`…/PlaybackEngines/AndroidPlayer/SDK/platform-tools/adb`）或 Android SDK 的皆可 |
| 影片（選用） | `ffmpeg`（要有 vidstab）、Python `numpy`＋`opencv`、Remotion（見 `docs/submission/video-v9/`） |
| 頭盔 | Quest 3／3S。開發者模式：先在 Meta 帳號建立開發者組織（developers.meta.com），再到手機 Meta Horizon App 的裝置設定開啟；接上 USB 時在頭盔內允許 USB 偵錯 |

## clone 後第一次

1. 向原作者 Max Lai 索取秘密檔，放到 repo 根目錄的 `.secrets/`（已 gitignore）。檔名與用途見 HANDOVER §8。沒有 `voice-client.token` 也能建置，只是語音對話會走離線備案。
2. 用 Unity Hub 以 6000.3.26f1 開啟專案。第一次匯入套件約 5–10 分鐘。
3. `Main.unity` 由程式產生（`Poker.Editor.MainSceneBuilder`），不要手改場景。建置腳本每次都會依環境變數重建它（見下方注意事項）。

## 建置上機

先關掉開著這個專案的 Unity Editor（同一個專案開著時 batchmode 起不來）。adb 不在 PATH 上的話先加：
`export PATH="/Applications/Unity/Hub/Editor/6000.3.26f1/PlaybackEngines/AndroidPlayer/SDK/platform-tools:$PATH"`（範例路徑）。

```bash
adb devices                                                                  # 要看到 <序號>  device
TELL_REALISTIC_PREVIEW=1 SECRETS_DIR="$PWD/.secrets" tools/build-quest.sh   # → Builds/Quest/TellPoker.apk
adb install -r Builds/Quest/TellPoker.apk
adb shell am start -n com.maxlai.tellpoker/com.unity3d.player.UnityPlayerGameActivity
adb logcat -s Unity | grep "\[RealisticPreview\]"     # 要看到這個 tag，才代表是寫實版
```

- **不要用 `--skip-configure`**：會跳過場景重建，裝上去變回舊的卡通角色。
- 建置會改寫 `Assets/_Project/Scenes/Main.unity`。repo 提交的是不帶旗標的預設（卡通）場景；寫實版建置後 git 會出現差異，不要提交，用 `git checkout -- Assets/_Project/Scenes/Main.unity` 還原。
- 其他旗標：`TELL_RELEASE=1`（正式版，關遙測）、`TELL_DEV_TOOLS=1`（錄製面板與除錯看板，交件版不要開）、`VOICE_URL`／`TELL_ALLOW_HTTP=1`（連本機中繼）。說明在 `tools/build-quest.sh` 開頭。
- log：`Logs/quest-configure.log`、`Logs/quest-build.log`。

## 測試

```bash
tools/run-core-tests.sh                                 # Poker.Core／AI／Gestures 純 C#（Mono＋NUnitLite），可加 --where "class =~ SidePot"
tools/run-unity-tests.sh EditMode                       # Unity batchmode；第二個參數是 testFilter（regex，可省略）
tools/run-unity-tests.sh PlayMode 'Poker.Tests.Voice'
(cd tools/voice-proxy && node --test "test/*.test.ts")  # 中繼，Node ≥ 22.18；外部 API 全用假的
```
Unity 測試結果在 `Logs/<EditMode|PlayMode>-results.xml`。Unity 開著同一個專案時，batchmode 測試會開不起來。

## 資料夾地圖

| 路徑 | 內容 |
|---|---|
| `Assets/_Project/Scripts/Poker.Core` | 規則引擎（不可變 `HandState`、`Engine.Apply`、邊池、牌力評估），純 C# |
| `Assets/_Project/Scripts/Poker.Gestures` | 手勢辨識、玩家習慣訊號、選單叫出、桌面校準，純 C# |
| `Assets/_Project/Scripts/Poker.AI` | AI 決策、破綻演出、讀玩家（`PlayerRead`）、即時對話導演，純 C# |
| `Assets/_Project/Scripts/Game` | `GameController*`（遊戲流程接線）、教學、荷官、籌碼、牌、音效 |
| `Assets/_Project/Scripts/Characters` | 角色、說話（`Speech/SpeechChannel`）、寫實角色（`Realistic/`） |
| `Assets/_Project/Scripts/Voice` | 語音對話用戶端、開發版遙測 |
| `Assets/_Project/Scripts/Runtime`、`UX` | 手部資料來源、牌桌放置與錨點、錄製工具；手腕選單與設定 |
| `Assets/_Project/Editor` | 建置（`ProjectConfigurator`、`BuildScripts`）、場景產生器、寫實貼圖 bake |
| `Assets/_Project/Tests` | EditMode／PlayMode／純 C# 測試、手勢錄製檔 |
| `Assets/_Project/Resources/Speech` | 預錄台詞（ElevenLabs） |
| `tools/` | 建置、測試、拉錄製檔、語音中繼（`voice-proxy/`）、部署、PokerKit 差異測試（`difftest/`） |
| `deploy/vr-poker-api` | 中繼的 docker compose（api＋cloudflared） |
| `docs/` | 規格 `specs/`、研究 `research/`、送件 `submission/`、隱私權政策、試玩流程 |
| `tasks/` | 計畫與進度（`todo.md`、`realistic-todo.md`…）、agent 簡報、教訓 |

## 去哪看更多

- [`docs/HANDOVER.md`](docs/HANDOVER.md)：交接全文
- [`docs/specs/2026-10-12-mr-poker-design.md`](docs/specs/2026-10-12-mr-poker-design.md)：遊戲設計
- [`tasks/agent-brief.md`](tasks/agent-brief.md)：技術細節（語音、log 格式、各系統參數）
- [`docs/submission/release-checklist.md`](docs/submission/release-checklist.md)：上架與送件清單
