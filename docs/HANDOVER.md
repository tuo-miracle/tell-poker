# Tell 交接文件

> 撰寫日期：2026-11-10。原作者：Max Lai（單人開發）。讀者：接手的團隊成員。
> 內容整理自 `tasks/*.md`、`docs/`、`tools/` 腳本與原作者的工作筆記。標「未查證」的段落只有文件依據，沒有實際跑過。
> 本機絕對路徑只出現在標明「範例」的地方，其他路徑一律相對 repo 根目錄。

## 目錄
1. 現況
2. 比賽：時程、規則、送件剩下的步驟
3. 架構概覽
4. 建置
5. 實機測試流程與常見坑
6. 語音中繼：本機開發、部署、費用
7. 測試
8. 秘密清單
9. 已知問題與未完成事項
10. 交接後建議的優先順序
11. 聯絡人

---

## 1. 現況

- **分支**：`main`，私人 GitHub repo `elebymax/tell-poker`。舊功能分支都已併入並刪除；沒併的卡通渲染試作留在 tag `archive/look1-toon-spike`。交接整理紀錄見 `tasks/handover-todo.md`。
- **玩法完成度**：M0–M2 完成，M3 打磨大多完成（`tasks/todo.md`）。完整一局約 8 分鐘：教學兩手（第一手被 Rocco 讀，第二手騙倒 Rocco）、5 人桌（4 位 AI＋玩家）、攤牌與讀人揭曉卡、結算、存檔與回訪問候。
- **寫實角色預覽**（`tasks/realistic-todo.md`）：用 `TELL_REALISTIC_PREVIEW=1` 建置時，座位上換成寫實角色，預設陣容 Marcus、Hank、Reginald、Jace（坐在 Pip、Rocco、Silas、Vex 的座位），荷官換成寫實版。家用標準長圓桌（外框 2.13×1.07 m）。每個角色有自己的 ElevenLabs 聲音。實機全寫實一桌約 72 fps（`tasks/realistic-todo.md:28`）。
  - 不帶旗標建置則是舊的卡通版：3 位對手、小桌。
- **最近的修正**：教學第 2 手的「等了 Rocco 就跟」改成看「輪到你」到「下注落定」的總時間，門檻 `TutorialScript.SlowBetSeconds = 4.5f`（`Assets/_Project/Scripts/Game/Flow/TutorialScript.cs:30`，判斷在 `GameController.Meta.cs:359`）。依交接說明，這已在實機驗證過（交接文件撰寫者沒有重跑）。
- **比賽影片**：做到 v15（1:45），放在原作者 Google Drive 的「Tell Demo」資料夾，要請原作者另外分享。影片製作流程在 `docs/submission/video-v9/`（由另一位同事整理）。**還沒上傳成公開影片。**
- **送件**：**還沒送**。還沒建 release channel、還沒簽正式 keystore（見 §2）。
- **語音中繼**：已部署在原作者的伺服器，對外網址 `https://vr-poker-api.elebymax.com`（隱私頁在 `/privacy`）。

## 2. 比賽：時程、規則、送件剩下的步驟

**Meta VR Start Developer Competition 2026**，Gaming 軌道、New Experience 組。
- **截止：2026-11-18 12:00 PT**（台灣時間 11/19 04:00）。截止後到得獎公布（約 12/11）是評審凍結期：**App 和語音中繼都不能改**，中繼也必須一直開著（`docs/submission/devpost.md:95`、`release-checklist.md:114`）。
- 評分：Innovation／Experience Design／Technical／Polish 各 25%。評審大約只玩 3 分鐘，多半不懂撲克，而且是在陌生房間玩（`tasks/todo.md:65`）。
- 規則重點（原文引用在 `docs/submission/devpost.md:87-106`）：
  - 要交 release channel **「Competition」**的邀請連結，不收 APK。
  - 影片 < 3 分鐘，必須是 Quest 實機畫面，並在 YouTube／Vimeo **公開**。
  - 徒手全程可玩；全英文或有英文字幕。
  - 不能出現品牌 logo 或真實人物；performant ≥ 60 fps；第三方素材要有授權。
- 獎項目標：Best New Gaming Experience 為主，兼顧 Best Agentic Interaction 和 Best First Five Minutes（`devpost.md:77-85`）。
- 原作者 **11/11–11/15 不在**。需要本人帳號的步驟要在他回來後（11/16–18）處理，或在他離開前完成。`devpost.md:91`、`release-checklist.md:115` 寫的「自訂截止 11/10」已作廢，以 11/18 為準。

### 送件剩下的步驟（對照 `docs/submission/release-checklist.md` 與 `devpost.md`）

標 **[DEV]** 的只有原作者本人能做（帳號、法務、身分）。

| # | 步驟 | 誰 | 來源 |
|---|---|---|---|
| 1 | Meta 開發者帳號＋組織、VR Start 會員（論壇 Start Member 標記）、同意開發者協議 | [DEV] | `release-checklist.md:25-27` |
| 2 | Devpost 帳號、加入比賽 | [DEV] | `release-checklist.md:28` |
| 3 | 決定聯絡信箱，替換 `docs/privacy.md:101` 的 `<CONTACT_EMAIL>`，再跑 `cd tools/voice-proxy && node scripts/sync-privacy.ts`，然後重新部署中繼 | [DEV] 決定信箱，其他人可代做 | `release-checklist.md:29,105` |
| 4 | 確認 Gemini 付費方案條款、ElevenLabs 方案是否允許商用預錄台詞 | [DEV] | `release-checklist.md:30` |
| 5 | 建 release keystore（存放在 repo 外），讓建置腳本從環境變數讀 keystore（目前 `androidUseCustomKeystore: 0`，用的是 debug keystore） | [DEV] 建 keystore／可代做程式 | `release-checklist.md:31,86-87` |
| 6 | 版本號：`bundleVersion` 1.0、`AndroidBundleVersionCode` 1。每次上傳 versionCode 都要遞增 | 可代做 | `release-checklist.md:88,99` |
| 7 | 建置前的修改：`companyName` 目前是 `DefaultCompany`；檢查 supportedDevices（含 `quest`、`stanley`）；確認 debuggable=false；如果不用持久錨點就拿掉 `USE_ANCHOR_API` | 可代做 | `release-checklist.md:98-103` |
| 8 | 正式建置：`TELL_RELEASE=1`、不設 `TELL_DEV_TOOLS`、不設 `TELL_ALLOW_HTTP`，並用 `apksigner verify` 檢查 v2 簽章 | 可代做 | `release-checklist.md:96-98,87` |
| 9 | Dashboard 建 App「Tell」，建 **Competition** channel，上傳 APK，開啟邀請網址並設自動核准 | [DEV] | `release-checklist.md:35-43,89` |
| 10 | IARC 分級、年齡層（13+）、權限說明、隱私權政策網址 | [DEV] | `release-checklist.md:69-82` |
| 11 | 用**另一個 Meta 帳號**從邀請網址安裝並驗證：教學＋一局、≥ 60 fps、語音、斷網照樣能玩、`/privacy` 和 `/health` | 都可以 | `release-checklist.md:110-113` |
| 12 | 影片 v15 上傳 YouTube（公開） | 需要原作者分享檔案 | `devpost.md:15,96` |
| 13 | Devpost 表單：邀請網址、影片連結、文案、Gaming／New Experience、預計上架日 | [DEV] 送出 | `devpost.md:14-17`、`release-checklist.md:115` |

注意：
- **`devpost.md` 文案還是卡通版的描述**，例如第 22 行寫「three cartoon AI opponents」，第 34 行只提 Pip、Rocco、Vex。送件是寫實版，要改成 4 位對手和寫實角色。標 *(verify)* 的數字也要在最終版上重新確認。
- **送件版用寫實版**：美術方向已定為寫實角色，比賽影片與 11/05 之後的上機測試都是寫實版，送件 APK 要和影片一致，一律帶 `TELL_REALISTIC_PREVIEW=1` 建置（`tools/build-quest.sh` 舊註解寫的「可行性驗證、交件前用預設值重建」已在交接時更正）。
- release-checklist 的權限表沒有列 `BOUNDARY_VISIBILITY`（ProjectConfigurator 會開 boundary visibility 支援）。最終 APK 實際有哪些權限，請用 `aapt dump permissions` 確認（未查證）。

## 3. 架構概覽

### 組件（asmdef）

| 組件 | 位置 | 職責 | 入口 |
|---|---|---|---|
| `Poker.Core` | `Assets/_Project/Scripts/Poker.Core` | 規則引擎：不可變 `HandState`、`Command`→`Engine.Apply`→`GameEvent`、`LegalActions`、邊池、TDA 規則、7 張牌評估器。PokerKit 雙向差異測試 10,000 手 | `Engine.cs` |
| `Poker.Gestures` | `Assets/_Project/Scripts/Poker.Gestures` | 牌桌座標下的手勢辨識（`GestureRecognizer`、`KnockDetector`）、玩家習慣訊號 `PlayerHabits`、掌上選單 `MenuSummon`、桌面校準與換位置 | `GestureRecognizer.cs`、`HandPose.cs` |
| `Poker.AI` | `Assets/_Project/Scripts/Poker.AI` | `AiPlayer`（翻牌前查表＋蒙地卡羅勝率＋個性）、破綻演出 `TellDirector`/`LineBook`、讀玩家 `PlayerRead`（勝率偏移上限 ±0.15，會學）、即時對話導演 `LiveTalkDirector` | `AiPlayer.cs` |
| `Poker.Game` | `Assets/_Project/Scripts/Game` | `GameController`（partial，依功能分檔：`LiveTalk`、`Meta`、`Hints`…）、`GameSession`、教學 `Tutorial`/`TutorialScript`、荷官 `DealerVoice` | `Flow/GameController.cs` |
| `Poker.Characters` | `Assets/_Project/Scripts/Characters` | 卡通角色、說話 `Speech/SpeechChannel`、寫實角色 `Realistic/RealisticPreview` | `Speech/SpeechChannel.cs` |
| `Poker.Voice` | `Assets/_Project/Scripts/Voice` | 語音對話用戶端、VAD、PCM 串流播放、公開牌桌狀態、開發版遙測 | `VoiceChatController.cs` |
| `Poker.Runtime` | `Assets/_Project/Scripts/Runtime` | ISDK 手部資料→`HandsFrame`、牌桌放置（MRUK／虛擬桌）、錨點、錄製工具 | `Table/TablePlacer.cs` |
| `Poker.UX` | `Assets/_Project/Scripts/UX` | 手腕選單、設定、無障礙 | `UxController.cs` |
| Editor | `Assets/_Project/Editor/` | `ProjectConfigurator`（專案設定＋重建場景）、`BuildScripts`、`MainSceneBuilder`、`RealisticBake` | — |

`Poker.Core`、`Poker.AI`、`Poker.Gestures` 是純 C# 9（`noEngineReferences`），所以可以在 Unity 外用 Mono 快速測試。

### 資料流

```
Quest 手部追蹤（ISDK/OVRHands）
  → Runtime/Hands：轉成牌桌座標的 HandsFrame（原點在玩家側桌緣中央，+Z 指向桌心）
  → Poker.Gestures：GestureRecognizer 產生 GestureEvent（敲桌、捏／丟籌碼、推牌、All-in…）
                    PlayerHabits 產生即時訊號（長考、再看牌、伸手又縮回…）
  → Game/GameController：手勢 → Command（籌碼量依 TDA 50% 規則換算）
  → Poker.Core Engine.Apply → GameEvent → 畫面、動畫、荷官台詞
  → 輪到 AI：Poker.AI AiPlayer 只看 BotView（公開資訊＋自己的底牌）
             PlayerRead 把玩家訊號轉成「信念」→ 有上限的勝率偏移 → 決策＋破綻演出
             LiveTalkDirector 在玩家思考時讓角色開口（例：「Changed your mind? I saw that.」）
  → 說話：一律走角色的 SpeechChannel（聲音＋頭上字幕，見 §5）
語音對話（選用）：
  玩家看著角色講英文 → Poker.Voice 用 WebSocket /live 送出 PCM16
  → tools/voice-proxy → Gemini Live（理解＋reply function call）→ facts.ts 用真實牌桌數字填值
  → ElevenLabs 串流 TTS → PCM 回傳，邊收邊播
  備案：Live 失敗改整段 generateContent → /talk → 離線罐頭句（一樣有聲音＋字幕）
```
隱藏資訊防線：`PublicTableState` 從不讀底牌，中繼也會再用白名單過濾一次。改這兩處要跑 `PublicStateTests`。
詳細參數與 log 格式在 `tasks/agent-brief.md`，設計在 `docs/specs/`。

## 4. 建置

```bash
TELL_REALISTIC_PREVIEW=1 SECRETS_DIR="$PWD/.secrets" tools/build-quest.sh
```
（範例，原作者本機：`SECRETS_DIR=/Users/laichanean/WebstormProject/vr-poker/.secrets`。在 worktree 建置時，讓 `SECRETS_DIR` 指向主 checkout 的 `.secrets`，不要把金鑰複製進 worktree。）

腳本的實際行為（`tools/build-quest.sh`）：
1. 如果有 `$SECRETS_DIR/voice-client.token`，就產生 `Assets/_Project/Resources/VoiceClient.json`（中繼網址＋通行碼＋遙測開關，已 gitignore）。沒有的話語音對話走離線備案。
2. `ProjectConfigurator.ApplyAndExit`：套用專案設定，並**依環境變數重建 `Main.unity`**。
3. `BuildScripts.BuildQuest` 產出 `Builds/Quest/TellPoker.apk`。**腳本不會安裝**，要自己 `adb install -r`。

| 環境變數 | 作用 |
|---|---|
| `TELL_REALISTIC_PREVIEW=1` | 寫實角色場景。**目前版本一定要帶** |
| `SECRETS_DIR` | 秘密資料夾，預設 `<repo>/.secrets` |
| `TELL_RELEASE=1` | 正式版：遙測一律關（交件用） |
| `TELL_TELEMETRY=0` | 測試版也關遙測（預設開） |
| `TELL_DEV_TOOLS=1` | 錄製面板、除錯看板、選單的 Fool Rocco（拍宣傳片用）。交件版不要開 |
| `VOICE_URL`、`TELL_ALLOW_HTTP=1` | 連本機中繼：`TELL_ALLOW_HTTP=1 VOICE_URL=http://127.0.0.1:8787 …`（只限開發） |
| `UNITY` | Unity 執行檔路徑，預設 `/Applications/Unity/Hub/Editor/6000.3.26f1/Unity.app/Contents/MacOS/Unity` |

坑：
- **不要用 `--skip-configure`**：場景不會重建，裝上去變回卡通角色（2026-11-05 踩過，戴上頭盔才發現）。
- **`Main.unity` 會被改寫**：repo 裡提交的是**不帶旗標的預設（卡通）場景**，`RealisticPreviewSceneTests` 也是這樣檢查；寫實版只在建置時由旗標產生。git log 裡一串「Regenerated scene」commit 就是這樣來的。用寫實旗標建置後 git 會出現差異，**不要提交那份寫實場景**，用 `git checkout -- Assets/_Project/Scenes/Main.unity` 還原（或不帶旗標重跑 ProjectConfigurator）。在 Editor 選單按 `Poker/Rebuild Main Scene` 或 `Poker/Apply Project Settings` 時，讀的是啟動 Unity 那個環境的變數；從 Hub 開的 Unity 沒有 `TELL_REALISTIC_PREVIEW`，會重建出卡通版。
- 建置後原作者都會刪掉 `Assets/_Project/Resources/VoiceClient.json` 和 `.meta`。這個檔已 gitignore，但裡面有通行碼。
- 本機 Unity 偶爾在關閉時崩潰（exit 134/137）。腳本會用 log 的 `APPLY_OK`、`RESULT=Succeeded` 判斷實際是否成功，並且已經帶 `-releaseCodeOptimization --burst-disable-compilation` 避開 macOS 簽章問題。
- 建置 log：`Logs/quest-configure.log`、`Logs/quest-build.log`。`Builds/Quest/project-setup-report.json` 會列出 Meta Project Setup 還沒完成的項目。

## 5. 實機測試流程與常見坑

### 原則（原作者踩過坑之後定下的規則）
- **安裝後自己先在實機驗證，再交給別人試玩**。只要影響「看得到／聽得到」的改動，都要自己用 logcat、截圖確認。回報時把「實機確認過的」和「只在 Editor／合成資料驗證的」分開寫。
- 首次啟動流程要跑兩種狀態：**清空存檔**和**保留舊存檔**（2026-10-13 曾因舊存檔被判定已完成，教學根本沒出現）。
- **說話一定要有聲音＋字幕**：每句台詞（AI、荷官、教學提示、語音回話）都要同時有聲音和頭上字幕，內容一致、同時出現、播完才消失。這是無障礙需求。字和聲音對不上時要修同步，不能拿掉其中一個。斷網時也要有預錄的聲音。程式上一律走 `SpeechChannel`，不要直接呼叫 `SubtitleBubble.Show`。
- 寫實角色的臉，驗收時看 1 m 外「像不像活人」，數字只是輔助。修某個角色的參數不能直接套到另一個角色。

### 步驟

adb：`pull-recordings.sh`、`watch-gestures.sh` 預設用 Unity 內附的 `/Applications/Unity/Hub/Editor/6000.3.26f1/PlaybackEngines/AndroidPlayer/SDK/platform-tools/adb`，可以用 `ADB=` 覆寫；`voice-dev.sh` 用 PATH 上的 `adb`。（範例，原作者本機也有 `~/Library/Android/sdk/platform-tools/adb`。）下面的 `adb` 指這兩者之一。

```bash
adb devices                                         # 要看到 <序號>  device；頭盔內允許 USB 偵錯
TELL_REALISTIC_PREVIEW=1 SECRETS_DIR="$PWD/.secrets" tools/build-quest.sh
adb install -r Builds/Quest/TellPoker.apk
adb shell am broadcast -a com.oculus.vrpowermanager.prox_close   # 不戴頭盔也持續執行
adb shell am start -n com.maxlai.tellpoker/com.unity3d.player.UnityPlayerGameActivity
adb logcat -s Unity | grep -E "\[RealisticPreview\]|\[Tutorial\]|\[Speech\]|\[Voice\]"
```
- 一定要看到 `[RealisticPreview] lineup (Pip, Rocco, Silas, Vex seats): …`，才算是寫實版。
- 其他 log tag（`[Speech]`、`[Tutorial]`、`[LiveTalk]`、`[Read]`、`[Table]`、`[UX]`、`[Gesture]`…）的格式見 `tasks/agent-brief.md`。即時看手勢：`tools/watch-gestures.sh`。
- 寫實預覽版的開發用開關（`adb shell setprop`，只有寫實預覽版會讀）：
  - `debug.tell.firstrun <值>`：教學重來、忽略舊錨點。每個值只生效一次，要再來一次就換值，設 0 清掉。不會動存檔和 UX 設定。
  - `debug.tell.menu 1|settings|howtoplay|0`：叫出或關掉選單。
  - `debug.tell.lineup Marcus,Hank,Jace…`：換陣容。
  - 其他還有 `say`、`cull`、`hankface`、`fitview`…，見 `Scripts/Characters/Realistic/RealisticPreview.cs`。
- 手勢錄製（要 `TELL_DEV_TOOLS=1` 建置才有錄製面板）：`tools/pull-recordings.sh [--clear]` 會把 `.hands` 檔拉到 `Assets/_Project/Tests/Gestures/Recordings/`。`docs/device-testing.md` 是 M0 時期寫的上機說明，部分已過時。
- 試玩流程：`docs/playtest/protocol.md`。

### 常見坑
- **App 沒聲音，先分辨是哪一種**：
  1. `adb shell dumpsys audio`，找 Tell 那個 uid 的 player，看 `mutedState`：
     - `streamVolume`：頭盔音量是 0。
     - `appOps`：被別的 App 靜音。**最常見的原因是頭盔上的遠端桌面**（`com.oculus.remotedesktop`）在跑。`cmd appops get` 看不出來。force-stop 之後會被系統拉回來，要 `adb shell pm disable-user com.oculus.remotedesktop` 才關得住，關完重開 Tell。這會停用頭盔主人的遠端桌面，測完要用 `adb shell pm enable com.oculus.remotedesktop` 復原（指令出自原作者筆記，交接時沒有重跑）。
     - `none`：Tell 本身沒被靜音。
  2. 如果是 `none` 卻沒聲音：**先問按音量鍵有沒有提示音**。沒有的話，是整台頭盔的音訊卡住，要請戴的人重開頭盔。不要先重裝或重開 App（2026-11-04 踩過）。
  3. 「log 說有播放」不等於「聽得到」。另外要確認 player 是 started、`STREAM_MUSIC` 音量 > 0。
- **Mac 的喇叭有遊戲聲音**：多半是 Mac 上的 Unity batchmode PlayMode 測試在播，不是頭盔。
- **MetaCam 錄影沒有收到玩家講話**：要在頭盔的相機 App 設定裡打開麥克風錄音，否則影片只有遊戲聲音（2026-11-05 實機確認：在那之前的錄影都沒有玩家原聲，開啟後 `190120` 那段就錄到了）。錄影檔在頭盔的 `/sdcard/Oculus/VideoShots/`。
- **截圖和錄影只處理自己這次拍的檔**：拍之前先記下檔名清單，拍完只拉新出現的檔，不要用 `ls -t | head -1`。截圖可能靜默失敗，最新一張不一定是你拍的，而且資料夾裡有原作者自己的檔（曾經誤刪過）。Tell 要在前景才拍得到。
- **房間太暗**：頭盔會跳「Finding position in room」，App 畫面出不來。
- **有外部依賴的功能**（中繼、`adb reverse`、麥克風權限）：回報前先實際講一句，並看到中繼 log 有回應。曾經發生 `adb reverse` 掉了、語音全走備案，卻沒人發現。
- 頭盔被帶離區網後，可以靠開發版遙測從伺服器拿 log（`tasks/agent-brief.md:45-48`；注意第 48 行的伺服器 IP 是舊的，部署腳本現在用的是另一台）。

## 6. 語音中繼：本機開發、部署、費用

- 程式在 `tools/voice-proxy/`：TypeScript、零依賴、Node ≥ 22.18 直接執行，fetch handler 形式（`src/worker.ts` 預留給 Cloudflare Workers）。協定說明在 `src/session.ts` 開頭，環境變數在 `src/node-server.ts`。設計見 `docs/specs/2026-10-13-voice-chat-design.md`。這個資料夾沒有 README，`tasks/agent-brief.md:24-49` 是目前最完整的說明。
- **本機開發**：`tools/voice-dev.sh` 會在 `127.0.0.1:8787` 開中繼，並執行 `adb reverse tcp:8787 tcp:8787`（只在 Editor 測就加 `--no-adb`）。如果預設的 node 太舊，它會去 nvm 裡找夠新的版本。App 要用 `TELL_ALLOW_HTTP=1 VOICE_URL=http://127.0.0.1:8787` 建置。
- **部署**：`tools/deploy-voice-api.sh [ssh 目標]`。預設目標是原作者區網裡的伺服器（寫在腳本第 7 行），隊友需要原作者開 ssh 權限才能部署。腳本做的事：
  1. rsync 程式碼和 `deploy/vr-poker-api/docker-compose.yml`。
  2. 經 ssh stdin 寫入金鑰和 `.env`，不會出現在指令列。
  3. `docker compose up -d` 之後再 `restart api`。程式碼是用 volume 掛載的，不 restart 不會載入新程式。
  
  需要 `.secrets/` 裡的 `gemini.key`、`elevenlabs.key`、`voice-client.token`、`vr-poker-tunnel.token`。對外靠 Cloudflare Tunnel（`cloudflared` 容器）。
- **費用與額度（重要）**：
  - ElevenLabs 是即時回話和預錄台詞**共用的同一個方案額度**。依 compose 註解，方案是 20,000 credits/月；原作者說過評審期前會升級，**升級後的額度要向他確認**。
  - 即時回話一句約 30–50 credits（flash 每字 0.5）。中繼用 `TTS_MONTHLY_CREDIT_BUDGET`（預設 15,000）擋住每月用量，到上限就不配音，改說預錄罐頭句。
  - 限流以每台裝置計：每台每天 80 句。全體每日上限 `VOICE_DAILY_CAP`：**部署腳本不設的話會寫 400**（compose 自己的預設是 3000）。部署時要依額度設定，例如 `VOICE_DAILY_CAP=… TTS_MONTHLY_CREDIT_BUDGET=… tools/deploy-voice-api.sh`。
  - 用量查詢：`GET /stats`（要帶 `x-voice-token` header）。
  - Gemini 用的是付費方案。
  - **批次呼叫付費 API 之前**（重錄台詞 `bake`、`verify`、`smoke/bench-*`）：先估用量、查剩餘額度（ElevenLabs 金鑰沒有查額度的權限，就問原作者），設好上限，超過就停。即時回話要保留至少約 30% 的餘量。2026-10-14 曾經一次 bake 把整月額度用完，連正式的語音對話都跟著停擺。升級方案、換供應商這類決定交給原作者。
- **改台詞**：`SPEECH_EXPORT=1 tools/run-unity-tests.sh PlayMode SpeechInventoryTests` 更新 `bake/lines.json`，然後 `cd tools/voice-proxy && node bake/bake.ts --count`（只算字數、不花錢），確認後再跑不帶 `--count` 的 bake（會自動聽檢），最後重跑測試。流程細節在 `tasks/agent-brief.md:34-38`（未查證）。
- **評審凍結期**（11/18 12:00 PT 到約 12/11）：中繼要一直開著、**不能改**。評審結束後要換掉 `voice-client.token`：它被打包在 APK 裡，可以被拆出來（`release-checklist.md:104`）。

## 7. 測試

| 指令 | 範圍 | 備註 |
|---|---|---|
| `tools/run-core-tests.sh [NUnitLite 參數]` | Poker.Core／AI／Gestures（含手勢錄製回放） | Mono＋NUnitLite。篩選用 `--where "class =~ X"` 或 `--where "test =~ X"`，**不是** regex 位置參數。交接時實跑全套：873 個，825 過、0 失敗、48 略過 |
| `tools/run-unity-tests.sh EditMode [filter]` | Unity EditMode | 第二個參數傳給 `-testFilter`（regex）。第一次跑要匯入套件，約 5–10 分鐘 |
| `tools/run-unity-tests.sh PlayMode [filter]` | Unity PlayMode | 結果在 `Logs/PlayMode-results.xml` |
| `cd tools/voice-proxy && node --test "test/*.test.ts"` | 中繼 | 外部 API 全用假的。交接時實跑 178/178 通過（Node 24） |
| `PY=<有 pokerkit 的 python> tools/difftest/run-diff.sh [手數] [種子]` | 規則引擎對 PokerKit 的差異測試 | 選用 |

- 最近一次全套數字：PlayMode 884／878 過／0 失敗／6 略過；EditMode 874／826／0／48；core 871／823／0／48（`tasks/realistic-todo.md:109`，後續修改之後還沒有更新）。
- **TDD**：先寫失敗的測試、看到紅燈，再寫實作。這是原作者一路以來的做法，大部分功能都有對應的紅綠燈紀錄。
- Unity 開著同一個專案時，batchmode 測試會開不起來。

## 8. 秘密清單

全部放在 repo 根目錄的 `.secrets/`（已 gitignore），`build-quest.sh`、`deploy-voice-api.sh` 用 `SECRETS_DIR` 指向它；`voice-dev.sh` 與 `tools/voice-proxy/bake` 讀 `VOICE_SECRETS_DIR`（或直接設 `GEMINI_API_KEY`／`ELEVENLABS_API_KEY`），沒設時找 repo 根目錄的 `.secrets/`。**值要向原作者 Max Lai 另外索取**，不要貼進聊天、issue 或 commit。

| 檔名 | 用途 | 誰讀它 |
|---|---|---|
| `voice-client.token` | App 連中繼的通行碼。建置時寫進 APK，部署時成為伺服器 `.env` 的 `VOICE_TOKENS` | `tools/build-quest.sh`、`tools/deploy-voice-api.sh` |
| `gemini.key` | Gemini API：中繼的語音理解與回話；bake 的聽檢 | `tools/voice-dev.sh`、`deploy-voice-api.sh`、`voice-proxy/src/keys.ts`、`bake/` |
| `elevenlabs.key` | ElevenLabs TTS：即時回話與預錄台詞 | 同上 |
| `vr-poker-tunnel.token` | Cloudflare Tunnel，讓 `vr-poker-api.elebymax.com` 連到伺服器 | `deploy-voice-api.sh`（寫成 `TUNNEL_TOKEN`） |
| `cloudflare.token` | repo 裡**沒有任何腳本使用**。推測是 Cloudflare 帳號的 API token，請向原作者確認 | — |
| `meta-llama.key` | repo 裡**沒有程式使用**。只在 `docs/research/2026-11-03-meta-model-api.md` 評估 Meta 模型 API 時用過 | — |

另外，伺服器上有 `~/vr-poker-api/secrets/` 和 `.env`（由部署腳本寫入），以及以後要建的 release keystore 和 Meta app secret。這些都**不進 repo**。

## 9. 已知問題與未完成事項

「來源」欄是 `檔案:行號`。標「可能已過時」的，是後面的紀錄看起來已經處理，但原本的勾選框沒更新。

### 交接整理
- 已完成，紀錄見 `tasks/handover-todo.md`。

### 送件（見 §2 表格）
- `docs/submission/release-checklist.md:25-31, 69-72, 82, 86-90, 110-115`：勾選項全部未完成。
- `docs/submission/release-checklist.md:97-106`：F 表的建置修改，除了第 1 項（關閉明文 HTTP）已做、第 11 項只需確認以外，都還沒做。
- `docs/submission/devpost.md:91-106`：規則檢查清單未勾。`devpost.md:14-17` 的佔位欄位（邀請網址、影片、上架日、信箱）未填。`devpost.md:22,34` 是卡通版描述。
- `docs/privacy.md:101`：`<CONTACT_EMAIL>` 未填。
- `tasks/todo.md:44-46`：實機錄影、Devpost 文案、release channel 上架。

### 遊戲與實機
| 項目 | 來源 |
|---|---|
| 敲桌手感（輕敲、張開手敲）不穩；咪牌、捏籌碼、All-in、棄牌放寬後待實機確認 | `tasks/todo.md:19` |
| 使用者親玩 10 手的正式驗收（讀心爽感、手勢準度） | `tasks/todo.md:20` |
| 「全程零文字」與目前的金額標籤／字幕不一致，需要決定 | `tasks/todo.md:29` |
| 美術與音效打磨（音效「待人耳聽」） | `tasks/todo.md:32`、`:73` |
| 4 角色同場 ≥ 60 fps：**可能已過時**，`todo.md:66` 量到 72 fps，`realistic-todo.md:28,51` 全寫實也是 72 fps | `tasks/todo.md:33` |
| 3 個以上房間的陌生房間測試 | `tasks/todo.md:34` |
| FoV 檢查（`todo.md:72` 已做過一輪 FoV 審查） | `tasks/todo.md:36` |
| 無障礙只做了一部分 | `tasks/todo.md:37` |
| Fast Motion Mode A/B 結論 | `tasks/todo.md:38` |
| 「確認開發者是否不在」：**已過時**，已確認 11/11–15 不在 | `tasks/todo.md:41` |
| 字幕位置、按鈕字大小、座位感覺仍待實機確認；格子版面好不好戳、Move table here 對齊 | `tasks/todo.md:92`、`:105` |
| 換臉流程整理成可重用的腳本並寫下步驟（`realistic-todo.md:24-26` 的 3a/3b 看起來已參數化，**可能已過時**） | `tasks/realistic-todo.md:20` |
| 其餘 3 位寫實角色（Danny、Nora、Leo）還在等模型 | `tasks/realistic-todo.md:30` |
| 角色池功能（選對手 UI、規則參數、Gemini 台詞）未開始 | `tasks/realistic-todo.md:31` |
| 「Tells read 永遠 0」：結算看板已拿掉這一行（`:99`），回訪開場白也不再提（`:102`）。但「讀對」的定義問題還沒決定 | `tasks/realistic-todo.md:88` |
| 寫實臉部殘留：Reginald 下巴暗缺口、Erik 右頰與 Jace 太陽穴接縫、Marcus 鏡腳鋸齒 | `tasks/realistic-todo.md:26,29` |
| Hank 看桌面時虹膜灰藍反光（原因未驗證）、截圖有黑條、看鄰座轉臉 46–50° | `tasks/realistic-todo.md:40` |
| Vex 座位的泡泡偏低；卡通正式版的扶手只在 Editor 驗過；教學在實機只驗到第一步；遊戲內名字仍用座位名（Pip/Rocco/Silas/Vex），不是寫實角色名；即時語音對話沒有實際對頭盔講話驗證過 | `tasks/realistic-todo.md:52` |
| 手腕選單的頭盔截圖沒拍到（當時房間太暗） | `tasks/realistic-todo.md:66` |
| 9 句預錄台詞聽檢沒過（10/14 的狀態；11/03 重錄過一批，**是否已修好未查證**） | `tasks/agent-brief.md:38` |

### 文件自身的過時處
- `tasks/agent-brief.md:18-19`：worktree 路徑和 commit 署名是原作者本機與個人的設定，不適用接手者。
- `tasks/agent-brief.md:48`：遙測伺服器 IP 是舊的（部署腳本與 `realistic-todo.md:51` 已改用另一台）。
- `tasks/agent-brief.md:37`：說 ElevenLabs 方案 10,000 credits，compose 註解寫 20,000/月。以原作者確認的為準。
- `docs/device-testing.md`：M0 時期的上機說明（虛擬桌尺寸、錄製面板預設出現）已不符合現況。
- `tools/run-core-tests.sh:2` 註解寫「數秒完成」。交接時實跑全套花了 7 分鐘，但當時機器負載很高（load average 超過 200），平常要多久沒有確認。用 `--where` 篩選會快很多。

## 10. 交接後建議的優先順序

截止是 11/18 12:00 PT，原作者 11/11–15 不在，所以先處理會卡住送件、而且只有原作者能做的事。

1. **（原作者，越早越好）** 帳號與資格：§2 表的 1、2、4、10。還有信箱、keystore、建 App 和 Competition channel、分享影片 v15。這幾項沒做完，其他人做再多也送不出去。
2. **用寫實版做 §2 的 5–8**：簽章、版本號、`companyName`、supportedDevices、權限檢查，用 `TELL_RELEASE=1 TELL_REALISTIC_PREVIEW=1` 建正式版。
3. **改 Devpost 文案**，讓它和送件版一致（對手數量、寫實角色、*(verify)* 數字）。順便準備商店素材（`release-checklist.md:49-63`）。
4. **影片 v15 上傳 YouTube（公開）**。依規則，影片不能出現品牌 logo。
5. **上傳 APK 到 Competition**，然後用另一個帳號從邀請網址做完整驗證（§2 第 11 項），並確認中繼的額度和上限夠撐評審期。
6. **11/18 12:00 PT 前送出 Devpost**。送出後到約 12/11，App、channel、中繼都不要動，每天檢查 `/health`。
7. 還有餘力才處理 §9 的遊戲問題。原則是「這會讓評審前 3 分鐘更好嗎？」：優先順序是陌生房間、手勢穩定、教學完整，最後才是新功能。改任何東西之後，一律重新上機驗證。

## 11. 聯絡人

- **Max Lai**：原作者、唯一開發者。帳號、秘密、伺服器、Meta／Devpost／Google Drive 的權限都在他身上。11/11–11/15 不在。
- 影片製作流程：負責整理 `docs/submission/video-v9/` 的同事。

---
交接時（2026-11-10）實跑的驗證：
- `node --test "test/*.test.ts"`（voice-proxy，Node 24）：178/178 通過。
- `tools/run-core-tests.sh`：873 個，825 過、0 失敗、48 略過（Explicit／Ignored）。

Unity 測試、建置、實機流程都沒有在撰寫本文件時重跑。
