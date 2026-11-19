# MR 德州撲克（Quest 3/3S）引擎與 SDK 研究筆記

查詢日期：2026-10-12。標記方式：
- 【文件】= 官方文件、release notes 或 registry 原始資料上寫的
- 【論壇】= 論壇或第三方文章，屬於軼聞
- 【推測】= 我的推論，沒有直接出處

原始資料存在 `raw/` 裡：registry JSON、Devpost 頁面的 HTML 轉文字、OpenXR xr.xml、Unity release API 回應。

---

## 0. 結論先講

**推薦：Unity 6.3 LTS（6000.3.26f1）＋ Meta XR SDK v207.0.0 全套 ＋ Unity OpenXR Plugin（Meta XR feature group），使用 URP、Vulkan、Multiview；ASW 預設關閉，FFR 開中等。**
信心：中高。比賽規則本身就點名「Unity/Unreal ISDK v207+」，而 Unity 是 ISDK、MRUK、FMM/WMM、Microgestures、範例、AI 工具（Meta agentic-tools）覆蓋最完整的路線。

第二選擇：Unreal 5.8 ＋ Meta XR plugin v207。沒選它的原因有三：plugin 和 UE 5.8 之間有版本錯配的 open issue、Blueprint 是二進位 `.uasset` 對 AI 不友善、5 週內的建置與迭代成本高。

---

## 1. 版本號（以 registry、官方下載頁的實際資料為準）

### 1.1 Unity 套件（`curl https://packages.unity.com/<pkg>`，2026-10-12 抓取）
| 套件 | dist-tags.latest | 發佈日 | 最低 Unity |
|---|---|---|---|
| com.meta.xr.sdk.all | **207.0.0** | 2026-09-24 | 6000.0.66f2 |
| com.meta.xr.sdk.core | 207.0.0 | 2026-09-24 | 6000.0.66f2（依賴 com.unity.xr.hands 1.7.2） |
| com.meta.xr.sdk.interaction | 207.0.0 | 2026-09-24 | 2022.3.15f1 |
| com.meta.xr.sdk.interaction.ovr | 207.0.0 | 2026-09-24 | 2022.3.15f1 |
| com.meta.xr.mrutilitykit | 207.0.0 | 2026-09-24 | 6000.0.66f2 |
| com.meta.xr.sdk.platform / haptics | 207.0.0 | 2026-09-24 | 6000.0 |
| （sdk.all 釘住的）audio / voice | 85.0.0 / 85.0.1 | — | — |
| com.unity.xr.openxr | 穩定版 **1.18.0**（2026-08-11）；dist-tag latest 是 1.19.0-pre.2（2026-10-03，預覽版） | | 6000.0 |
| com.unity.xr.meta-openxr | **2.6.1**（2026-08-24），依賴 openxr 1.18.0、arfoundation 6.6.0 | | 6000.0 |
| com.unity.xr.hands | 穩定版 1.9.0（2026-08-12）；latest 是 1.10.0-pre.2 | | 6000.0 |
| com.unity.xr.interaction.toolkit | 3.6.1 | 2026-09-17 | 6000.0 |
| com.unity.netcode.gameobjects | 3.1.0 需要 **6000.7**；6.3 可用的最新版是 **2.13.3**（2026-09-21） | | |
| com.unity.services.multiplayer | 2.4.0 | 2026-10-08 | 6000.0 |
| com.meta.xr.simulator（UPM） | registry 上停在 81.0.0；v207 的模擬器改成獨立下載 | | |

- **v207 之後還沒有新版**：截至 2026-10-12，registry 上的 latest 仍是 207.0.0。Meta 版號的演進是 85.0.0（2026-02）→ 201（04-17）→ 203（06-10，另有 203.0.1/.2）→ 205（07-23）→ 207（09-24），大約每 2 個月一版。【推測】下一版 209 可能在 11 月中、也就是截止日附近釋出，建議比賽期間鎖定 207。
- com.unity.render-pipelines.universal 在 registry 上的 latest 是 10.10.1，這是舊資料。Unity 6 的 URP 是隨 editor 綁定的核心套件，版本跟著 editor 走。【推測】6.3 對應的是 URP 17.3.x，這點我沒有直接從官方確認。

### 1.2 Unity Editor（Unity release API：`services.api.unity.com/unity/editor/release/v1/releases`）
- **6.3 LTS 最新是 6000.3.26f1（2026-10-08）**
- 6.0 LTS 最新是 6000.0.84f1（2026-09-16）。依官方說法，6.0 LTS 的支援到 **2026-10** 結束，6.3 LTS 支援到 2027-12。來源：https://unity.com/releases/unity-6/support
- Supported（非 LTS）的最新是 6000.6.5f1，API 標為 `recommended: true`。6000.7 是 beta，7000.0 是 alpha。
- 6.3.26f1 的 release notes 在 https://storage.googleapis.com/live-platform-resources-prd/templates/assets/6000_3_26f1_76f36edab7/6000_3_26f1_76f36edab7.md 。我只 grep 了 XR、Android、URP 相關段落，裡面沒有 Quest 或 OpenXR 的阻斷性已知問題。有一條是 URP 在 tile-based GPU 上的效能改善。

### 1.3 其他引擎與框架
- Meta Spatial SDK：Maven Central `com.meta.spatial:meta-spatial-sdk`，以及 `-physics`、`-isdk`、`-mruk`、`-toolkit` 都是 **0.14.0**（lastUpdated 2026-09-11），還沒到 1.0。
- Unreal：Meta XR plugin（UE5 integration）**v207.0**，2026-09-23 更新，要求 UE 5.8.0。來源：https://developers.meta.com/vr/downloads/package/unreal-engine-5-integration/ （這頁我沒能直接打開，內容取自搜尋摘要）
- Godot：引擎 4.7.2-stable（2026-08-18），godot_openxr_vendors 5.1.0-stable（2026-05-19）。資料來自 GitHub API。
- IWSDK（WebXR）：npm `@iwsdk/core` 1.0.1（2026-10-01）

---

## 2. 比賽規則對技術的要求

來源：
- Devpost 首頁 https://start-developer-competition-26.devpost.com/
- 官方規則 https://start-developer-competition-26.devpost.com/rules （WebFetch 回 403，改用 curl 加瀏覽器 UA 取得，全文在 `raw/devpost_*.txt`）
- Meta 部落格 https://developers.meta.com/blog/meta-connect-2026-vr-start-developer-competition/
- 論壇公告 https://communityforums.atmeta.com/discussions/News_and_Announcements/meta-vr-start-developer-competition-2026/1384076

【文件】重點如下：
- **引擎不限**。首頁寫「Built with Unity, Unreal, Godot, Android/Native or similar custom engine：upload an APK to a new release channel named "Competition"」；IWSDK 則交一個網址。
- **建議的 SDK（Suggested SDKs）**：
  - Unity：「Meta XR SDKs v81+ (v207) All-in-One XR」
  - Unreal：「Unreal Engine 5.8 and Oculus Integration SDK v207+」
  - Android：React Native/Expo 或 Jetpack Compose
  - IWSDK：Node 20.19+
- **只能用公開的 SDK**（規則原文）：「The Project must be built using development tools, SDKs, and resources that are available to the general public and/or to Start Program members. Use of Meta tools, SDKs, or features that have not been released ... is not permitted.」
- **硬性要求是 Hands-first**：「Fully usable with hands end-to-end… Can someone complete the entire experience without ever pairing a controller?」
- **設計指引**：坐姿操作、2 呎半徑內完成（airplane seat test）、冷啟動快、10 分鐘內要有滿足感。
- **評審分兩階段**：
  - Stage 1 是 pass/fail，看作品是否「reasonably applies the required tools and features」
  - Stage 2 四項各佔 **25%**
- **評分項目中直接點名的技術**：
  - Technical Implementation：「gaze interactions (**Unity/Unreal ISDK v207+**), hand tracking, passthrough, spatial anchoring, and FoV-aware design」，另外要求「performant on Meta Quest hardware (**min 60 fps**)」。對用到 passthrough 的作品，還有「correct use of scene understanding (**MRUK**), anchoring content to detected planes, surfaces... holding up in rooms the developer never tested」。
  - Experience Design：「Passthrough should be purposeful: the player's real room meaningfully changes the experience」，以及「We'll also evaluate **FoV-aware design**... keep essential UI and interactions within a comfortable, narrower FoV」。這是針對 Meta VR Glasses 的 FoV 比 Quest 窄。
  - Innovation：spatial depth、hands-first、gaze、passthrough、scene understanding、persistent anchors。
  - Polish：影片要呈現真實遊玩畫面，「Don't lean on AI-generated video」。
- **特別獎**（各 $25k）：Best Social & Multiplayer、Best Agentic Interaction、Best Reason to Come Back、Best First Five Minutes、Best Accessibility Forward、Boldest Original Concept。
- **Division**：「New Experience」必須在 2026-09-24 之後才開始做，「No pre-existing codebases」。【推測】把 claw-hand 專案的程式碼拿來重用可能違規，建議全新開一個 repo。
- **內容分級**：作品要符合 10+、13+ 或 18+ 的內容指引。【論壇／推測】撲克屬於模擬賭博，Quest 商店上的 PokerStars VR 是 ESRB M17+（https://www.meta.com/experiences/pokerstars-vegas-infinite/2370815932930055/ ）。只要不涉及真錢，應該可以走 18+ 分級，但沒有查到 Meta 的明文規則，屬於非技術風險。

**對技術選型的影響**：
1. 評分把「ISDK v207+」和 MRUK 當成加分的明確信號，等於指向 Unity 或 Unreal ＋ Meta SDK v207。
2. 評審會用 v207 的 FoV 模擬工具檢查 FoV-aware，Unity Core SDK v207 內建這套工具。
3. Quest 3/3S **沒有眼動追蹤**，gaze 只能用頭部注視或射線退回（fallback），細節見第 3 節。

---

## 3. Meta 手部追蹤的最新能力

| 能力 | 文件寫的 | 出處 |
|---|---|---|
| **Fast Motion Mode（FMM）**，就是以前的 High Frequency Hand Tracking | 「previously known as High Frequency Hand Tracking」。Unity 端在 OVRCameraRig 把 Hand Tracking Frequency 設為 High（High 和 Max 沒有差別）。執行期可用 `OVRPlugin.RequestFastMotionMode()` 切換，但這只是給系統的 hint，系統可能忽略，也沒有 getter。log 範例顯示約 58.5 Hz，相機約 60 Hz（電網 50 Hz 的地區約 50 Hz）。支援 Quest 2、Pro、3 及之後的機種。**缺點**：jitter 會增加，影響精準的直接觸碰；暗處表現變差；不能和 Multimodal 同時用；passthrough 下虛擬手會比 passthrough 看到的真手「更快」；會停用 Dynamic Object Tracker。最後更新 2026-08-11 | https://developers.meta.com/horizon/documentation/unity/fast-motion-mode/ |
| 預設追蹤頻率 | Overview 頁只在 FMM 旁寫「(60Hz)」，沒寫預設是多少 | https://developers.meta.com/horizon/documentation/unity/unity-handtracking-overview/ 。【推測】預設約 30 Hz 並做外插，未查到官方數字 |
| **Wide Motion Mode（WMM）** | 手離開視野時，用 Inside-Out Body Tracking 推估出合理的手勢。只支援 Quest 3 及之後的機種。需要 Unity 6000.0.66f2+、Core v62+，OVRManager 的 Body Tracking Support 要設為 Required。「IOBT/WMM will not run when passthrough and full body (FBS) are running along with FMM or Multimodal」。手追蹤丟失時不準確，平滑處理會讓位置精度略降。最後更新 2026-08-11 | https://developers.meta.com/horizon/documentation/unity/unity-wide-motion-mode/ |
| **Microgestures** | 拇指在食指側面做 tap 和四向 swipe。支援 Quest 2、Pro、3 系列與 Meta VR Glasses。需要 Core v74+，使用 `OVRMicrogestureEventSource`。最後更新 2026-10-07 | https://developers.meta.com/horizon/documentation/unity/unity-microgestures/ |
| Hands 2.4 | 改善快速動作、移動和投擲時的可靠度 | Road to VR https://roadtovr.com/?p=223679 【論壇級，日期不明】 |
| Hands「3.x」 | **查不到**有正式發表，也查不到延遲或精度的官方數字 | — |
| 回歸問題 | 2026-05 有開發者回報 Horizon OS 某次更新後，Meta Aim Hand OpenXR 的 aim pose 卡住，Unity OpenXR app 的遠距互動壞掉 | https://communityforums.atmeta.com/discussions/Questions_Discussions/horizon-os-v2-4-breaks-hand-far-interaction-in-all-openxr-unity-apps/1375172 【論壇】 |
| v207 新功能 | ISDK 加入 Gaze interaction（標為 experimental）、GazeMultiGrab、MicrogestureScroll、**MR Tabletop sample**（把遊戲區域定位到真實空間）。Core 加入 Meta VR Glasses 的 FoV 模擬、Device Readiness Check。XR Simulator 207 加入 Look and Pinch、camera-driven hand tracking | https://developers.meta.com/vr/downloads/package/meta-xr-interaction-sdk/ 、https://developers.meta.com/vr/downloads/package/meta-xr-core-sdk/ 、https://developers.meta.com/vr/release-notes/ |
| Gaze 在 Quest 3/3S 上 | Unity 的 gaze building block 預設開啟 ray fallback：「When eye tracking stops… ray interactors take over」。官方說在沒有眼動的頭顯上可以「mock gaze input」。Unreal 有 `HmdFallback`（頭部注視）。Quest 3 沒有眼動硬體 | https://developers.meta.com/horizon/documentation/unity/unity-isdk-gaze-building-blocks/ 、https://developers.meta.com/horizon/documentation/unreal/unreal-isdk-gaze-getting-started/ 、無眼動來源 https://uploadvr.com/meta-shoots-down-quest-3-eye-tracking 【新聞】 |

### 3.1 OpenXR 擴充（Khronos registry xr.xml，API 1.1.63，抓自 KhronosGroup/OpenXR-SDK-Source main）
與手相關、狀態為 `supported="openxr"` 的擴充：
- 跨廠商：`XR_EXT_hand_tracking`、`XR_EXT_hand_interaction`、`XR_EXT_hand_tracking_data_source`、`XR_EXT_hand_joints_motion_range`
- Meta：`XR_FB_hand_tracking_aim`、`_mesh`、`_capsules`，以及 `XR_META_hand_tracking_microgestures`、`XR_META_hand_tracking_frequency_hint`（推測就是 FMM）、`XR_META_hand_tracking_wide_motion_mode2`、`XR_META_hand_tracking_unextrapolated_poses`、`XR_META_simultaneous_hands_and_controllers`（Multimodal）
- `XR_KHR_hand_tracking`（#775）已經在 registry 裡，但標記為 `supported="disabled"`，也就是尚未啟用。
- `XR_FB_space_warp` 已被標記 `deprecatedby="XR_EXT_frame_synthesis"`。

### 3.2 OpenXR 路徑與 OVRPlugin 路徑的差異
- 【文件】Oculus XR Plugin 已 deprecated、排定移除。「features released in v74+ might not be compatible with the Oculus XR Plugin」。Unity OpenXR: Meta 從 OpenXR 1.14 起，與舊 plugin 已達到功能和效能對等。來源：https://developers.meta.com/horizon/documentation/unity/unity-project-setup/ 、https://developers.meta.com/horizon/documentation/unity/unity-and-openxr-compatibility （2026-10-05 更新）
- 【文件】ISDK Essentials 不需要 Core SDK 也能用（走 UnityXR 和 XR Hands）。**但 passthrough（OVRPassthroughLayer）和 MRUK 仍然需要場景中有 OVRCameraRig 或 OVRManager**（同上 compatibility 頁）。FMM、WMM、Microgestures 的文件只寫了 OVRPlugin、OVRManager 的 API，在 Unity XR Hands 或 com.unity.xr.meta-openxr 路徑上**沒有文件**。
- 【推測】正確的組合是：XR provider 用「Unity OpenXR Plugin」，再用 Meta XR Core 的 OVRCameraRig 與 OVRPlugin（它的底層也是 OpenXR runtime）。這樣兩邊的能力都能拿到。純 Unity 路線（XRI、XR Hands、meta-openxr、AR Foundation）拿不到 FMM、WMM、Microgestures 的完整 API，MRUK 和 ISDK 的整合也比較弱。
- 【文件】手部骨架：「All new projects are strongly encouraged to opt-in to the OpenXR skeleton」，OVR skeleton 只支援到 v74。來源：https://developers.meta.com/horizon/documentation/unity/unity-isdk-openxr-hand/ （2024-11 更新）

---

## 4. 引擎與框架比較

| 項目 | Unity 6.3 ＋ Meta SDK v207 | Unreal 5.8 ＋ Meta plugin v207 | Meta Spatial SDK 0.14 | Godot 4.7 ＋ OpenXR vendors 5.1 | WebXR / IWSDK 1.0.1 |
|---|---|---|---|---|---|
| 手部追蹤與 ISDK | **最完整**：Hand Grab 與手勢錄製、Poke、Ray、Throw、Gaze（v207）、Microgestures、FMM、WMM、MR Tabletop sample | 有 ISDK 和 Gaze v207（文件齊全），但功能與範例比 Unity 少【推測】 | 有 `meta-spatial-sdk-isdk` 和 microgestures 移動（0.14 release notes）。ISDK 的範圍較窄【推測】；規則點名的「ISDK v207+ gaze」只寫了 Unity/Unreal | 只有 OpenXR hand tracking，**沒有 ISDK** | WebXR Hand Input。IWSDK 有抓取類互動，但沒有 FMM/WMM【推測】 |
| MR（passthrough、MRUK、桌面） | MRUK 207 有場景標籤、EnvironmentRaycast（基於 Depth API 的即時放置，官方範例正是「把桌遊放到桌上」）。注意 v207 停用了 Scene model V2 | 有 MRUK for Unreal | 有 `-mruk` | vendors plugin 有 scene 和 passthrough，工具較少 | WebXR plane/mesh detection |
| 物理（大量籌碼、軟牌） | PhysX 成熟。軟牌建議用 shader 或 bone 做彎曲，不要用布料物理【推測】 | Chaos 物理最強，但 Quest 上很重【推測】 | 有 physics 模組，實作細節不明（查不到） | Jolt（4.4 起內建）【推測】 | JS 物理，效能最弱 |
| Quest 3S 效能 | URP、Vulkan、Multiview 是官方主流路線；有 FFR 與 ASW | 行動版 forward 渲染可行，但調校成本高 | 原生、輕量，效能好【推測】 | 尚可 | 最弱，60fps 有風險 |
| AI 輔助開發 | 中高：C# 和 `.unity`/`.prefab` 是 YAML 文字；`-batchmode -executeMethod`、`-runTests` 可在命令列建置與測試；Meta 有 agentic-tools（35 個 skill 加上 `metavr` MCP，Unity 支援最多）https://github.com/meta-quest/agentic-tools ；v207 Core 內建 Meta XR Operator MCP | 低：Blueprint 是二進位 `.uasset`；C++ 編譯慢 | **最高**：Kotlin 加 Gradle，完全是文字。但 API 變動快、範例少 | 高（GDScript、`.tscn` 都是文字） | 高（TypeScript） |
| 社群與文件 | 最成熟 | 中 | 小，仍是 0.x | 小 | 中 |
| 5 週風險 | **低到中** | 中高：有 open issue 指出 v207 plugin 實際是為 UE 5.7.0 編譯，在 5.8 上模組載入失敗 https://developers.meta.com/horizon/feedback/vr/investigations/1053459897316880/ | 中高：0.x API，評分偏向 ISDK v207 | 高：Stage 1「reasonably applies the required tools」可能吃虧 | 高：效能、手追蹤品質 |

---

## 5. Unity 路線的細節

### 5.1 XR provider
- **XR Plug-in Management → OpenXR（Android）→ 勾選 Meta XR feature group**
  - 版本用 com.unity.xr.openxr 1.18.0 穩定版，不要用 1.19.0-pre。
  - 【文件】Meta 的 setup 頁寫「Recommended Version: 1.15.1」，但那是頁面舊資料；registry 的實際最新穩定版是 1.18.0。sdk.all 207 並沒有釘住 openxr 的版本。
- 安裝 Meta XR All-in-One 207.0.0，或只裝需要的子套件：core、interaction、interaction.ovr、mrutilitykit、haptics、platform。
- **場景使用 OVRCameraRig 搭配 ISDK OVR 整合**，不要用 XRI rig。理由見 3.2：passthrough、MRUK、FMM、WMM、Microgestures 都綁在 OVRManager 上。
- 手部骨架選 OpenXR skeleton。
- 【推測】不需要加裝 com.unity.xr.meta-openxr（AR Foundation 路線）。它會拉進 ARF 6.6，與 OVR 路線重疊。Meta 文件提到 Depth API「needs Unity OpenXR: Meta」，指的是 Meta XR feature group，不一定是這個套件。這點不確定，建議用 Meta 的 Project Setup Tool 檢查。

### 5.2 渲染
- URP（Meta 的 setup 頁推薦 Universal 3D 範本）、Vulkan、Single Pass Instanced/Multiview、IL2CPP、ARM64、Linear color space。後面這幾項是 Meta Project Setup Tool 的慣例設定【推測】，setup 頁本身沒有寫。
- **注視點渲染（FFR）**：Meta XR Foveation 和 generic Foveated Rendering 兩個都要開【論壇】。Unity 6.1、6.2 曾有 FFR 讓效能暴跌的回報，Unity 說在 6000.3.0f1 上無法重現，也有使用者確認 6.3 修好了。來源：https://discussions.unity.com/t/bug-severe-ffr-glitches-performance-loss-with-unity-6-1-6-2-with-urp-vulkan-openxr-on-quest/1677819
- **Application SpaceWarp：建議預設關閉，只當作效能保險**
  - 【文件】需求：Unity 6.1+、OpenXR 1.11+、URP 17.0.3+、render graph、Vulkan。自訂 shader 要自己加 motion vector pass。來源：https://docs.unity3d.com/Manual/xr-graphics-spacewarp.html 、https://developers.meta.com/horizon/documentation/unity/unity-asw/
  - 【論壇】2026-06 有人回報 ASW 開啟後，Quest 3 上的手部網格會落後好幾幀，透明材質的手特別明顯。來源：https://discussions.unity.com/t/application-spacewarp-xr-fb-space-warp-causes-severe-hand-tracking-mesh-lag-on-quest-3/1724221
  - 這個遊戲整個建立在手上，這種延遲是致命的。目標應該是不靠 ASW 就跑到 72 Hz 原生幀率，比賽門檻是 60fps。
- Passthrough 背景：camera clear 設為透明黑。MRUK v207 的 shader 已拆成 URP 和 BiRP 兩版，預設 URP。

### 5.3 相容性與已知 bug
- **ISDK v207 修了「Unity 6.3+ compile errors」**（GetInstanceID 相關），所以 6.3 搭配舊版 ISDK 會有問題，**必須用 207**。
- MRUK v207 修了 Unity 6.4 的相容性問題。
- Meta setup 頁寫「6000.0.66f2 or later (6.1 or later recommended)」。
- 6.0 LTS 的支援在本月結束；6.4 到 6.6 是非 LTS；Netcode 3.x 需要 6.7（beta）。所以 **6.3 LTS 是唯一合理的選擇**。

---

## 6. 推薦方案

| 項目 | 選擇 |
|---|---|
| 引擎 | Unity **6000.3.26f1**（6.3 LTS） |
| Meta SDK | `com.meta.xr.sdk.all` **207.0.0**，整個比賽期間鎖定，不要升到 209 |
| XR provider | Unity OpenXR Plugin **1.18.0** 加 Meta XR feature group。場景用 OVRCameraRig、ISDK（interaction 與 interaction.ovr 207），手部骨架用 OpenXR skeleton |
| MR | MRUK 207：先用 EnvironmentRaycast 做即時放牌桌（不需要掃房間），有 TABLE 標籤時優先吸附到桌面；用 spatial anchor 保存位置；參考 ISDK 的 MR Tabletop sample |
| 手勢 | ISDK Hand Grab（籌碼、牌）、自訂 pose 與速度偵測（摔牌、推籌碼）；Microgestures 當輔助；Gaze 用 v207 building block（Quest 3 上走 ray 或頭部 fallback），用來在評分上打勾 |
| FMM | 預設關閉。【推測】咪牌、數籌碼都需要精準，FMM 會增加 jitter；只在摔牌、丟籌碼這類快速動作的時段，考慮用 runtime hint 開啟 |
| WMM | 可以試。注意它和「passthrough、FBS、FMM」的組合有衝突 |
| 渲染 | URP、Vulkan、Multiview、FFR 開 medium、ASW 關閉；目標 72 Hz 原生 |
| 多人預留 | 遊戲邏輯寫成純 C#、決定性的狀態機（state 加 action），和 Unity 解耦；之後再接 NGO 2.13.x、Unity Multiplayer Services 2.4，或 Meta 的 Shared Spatial Anchors／colocation |
| 工具 | `metavr` CLI 與 MCP、Meta agentic-tools skills、XR Simulator 207（Mac ARM 有版本）、Unity `-batchmode` 建置加 Test Framework 跑 EditMode 測試 |

信心：**中高**。版本號都是 registry 實測。路線本身由比賽規則的措辭和 Meta 官方推薦支持。

主要風險：
1. 手部追蹤的細緻程度（咪牌掀牌角、數籌碼）可能超出 Quest 手追蹤的精度。這是**設計風險**，比選哪個引擎更重要。建議第 1 週就在實機上做原型驗證。
2. Gaze 的 building block 是 experimental，Quest 3/3S 上沒有真正的眼動。
3. ASW 會讓手延遲；FFR 有歷史 bug。
4. 大量籌碼剛體加上 passthrough 時的 CPU 負擔（3S 和 3 的晶片相同，但 3S 的顯示解析度較低）【推測】。
5. 模擬賭博的內容分級。
6. 新 OS 更新可能讓手部 aim 回歸，參考 2026-05 的論壇回報。
7. Meta 可能在截止前釋出 209，盲目升級有風險。

第二選擇：Unreal 5.8 ＋ Meta XR plugin v207（規則有點名，ISDK gaze 有文件）。不選它的原因：plugin 與 UE 5.8 有 open 的版本錯配 issue；Blueprint 是二進位、對 AI 不友善；編譯與迭代慢；一個人 5 週做不完。Spatial SDK 是 AI 友善度最高的選項，但還在 0.x，評分又偏向 ISDK v207，所以排第三。

---

## 7. 沒做到或不確定的地方
- 沒有實機或 Unity 驗證，依規定沒有啟動 Unity。
- Hand tracking「3.x」：查不到任何官方發表或延遲數字。預設追蹤頻率也查不到官方數字。
- FMM、WMM、Microgestures 在「Unity XR Hands 與 meta-openxr」路徑上是否可用，文件沒寫。registry 有對應的 OpenXR 擴充，但沒查到 Unity 套件有封裝。
- Spatial SDK 的物理引擎和 ISDK 功能範圍：沒有查到細節。
- Unreal v207 的下載頁沒能直接打開，內容來自搜尋摘要。
- URP 在 6.3 對應的確切版本（推測是 17.3.x）沒有確認。
- 是否需要 com.unity.xr.meta-openxr 才能用 Depth API 或 EnvironmentRaycast：不確定，建議用 Project Setup Tool 檢查。
- Meta 對模擬賭博 app 分級的明文規則沒有找到。
- 6000.3.26f1 的 release notes 只用關鍵字 grep 過，沒有逐條讀。
