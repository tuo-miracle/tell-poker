# 程式／AI agent 製作 motion graphics 與實拍剪輯：工具全景（2026-11-05 調查）

調查日：2026-11-05。環境：Mac（Apple Silicon 為主）。目標：Tell（Quest 3 MR 撲克）比賽 demo 影片 2:30–2:50，素材為頭盔錄影 1920×1080 HEVC＋AAC，需疊字幕、標題卡、轉場、機制說明動畫。硬性限制：不得出現品牌 logo、不得有 AI 生成畫面。

標記說明：【文件】＝官方文件／原始碼／指令輸出所述；【推測】＝我的推論，未經驗證；信心等級：高／中／低。
原始抓取檔都在 `/private/tmp/claude-501/-Users-laichanean-WebstormProject-claw-hand/dba936d2-a2e1-4020-9344-fc7606a601ab/scratchpad/motion-research/`（`raw/`、`hf/`、`hf-repo/src`（HyperFrames 淺層 clone，僅唯讀）、`other/`）。未安裝任何套件、未改任何專案檔。

---

## 0. 一頁結論

1. **主力建議：Remotion（React）做疊字幕／標題卡／轉場／說明動畫的合成與最終輸出，ffmpeg 負責粗剪前處理與驗片。** 理由：成熟度最高、官方 agent skills 完整、HEVC 實拍有官方支援路徑、個人使用免費且比賽作品沒有被排除（信心：中高，見 §1.5 的授權前提）。
2. **HyperFrames 是可行的第二選擇，但目前不建議當唯一主力。** 專案很新（repo 建立於 2026-03-10，版本 0.9.x，一天多版）、Mac 上有未結的確定性／XProtect 問題、skills 內建遙測與 HeyGen／AI 生成導向，與本案限制（無 AI 影像、無 logo）需要主動避開。優點是 Apache-2.0、純 HTML、`check`／`snapshot` 驗證指令對 agent 很友善。（信心：中）
3. Revideo（維護中但小）、Motion Canvas（2025-02 後無發版）、MoviePy（慢、無逐幀 seek 動畫模型）都不如上面兩者適合本案；**ffmpeg＋libass 是「純字幕＋簡單轉場」的零依賴底線方案**，也是任何方案最後都會用到的驗片工具。
4. 沒有為這個案子「必須」的 Claude Code 剪輯 skill：`browser-use/video-use` 以逐字稿剪片，需 ElevenLabs API key（付費雲端轉錄），而本案是遊戲實拍而非口述素材，價值有限。

---

## 1. Remotion

### 1.1 版本（指令輸出）
```
$ npm view remotion version time.modified
version = '4.0.540'
time.modified = '2026-11-02T08:42:03.197Z'
$ npm view remotion@latest dist-tags
{ canary: '4.0.0-alpha.217', alpha: '4.1.0-alpha12', latest: '4.0.540' }
$ npm view @remotion/captions version license    → 4.0.540 / MIT
$ npm view remotion license → 'SEE LICENSE IN LICENSE.md'
```
最新穩定版：**4.0.540**（2026-11-02）。節奏約每週一版（4.0.536 於 10/16、539 於 10/26、540 於 11/02）。GitHub：remotion-dev/remotion，約 70.8k stars，pushed 2026-11-04（`gh api repos/remotion-dev/remotion`）。信心：高。

### 1.2 官方 agent skills
來源：https://www.remotion.dev/docs/ai/skills 、https://github.com/remotion-dev/remotion/tree/main/packages/skills 、鏡像 repo https://github.com/remotion-dev/skills（6,376 stars，pushed 2026-11-02，未封存）。

【文件】安裝：
```
npx skills add remotion-dev/skills
gh skill install remotion-dev/skills
bun create video   # 建新專案時可選擇加入 skills
```
【文件】內容（斜線指令）：`/remotion-best-practices`（總括）、`/remotion-create`、`/remotion-markup`（動畫、排版、字型、媒體、音訊、時間軸）、`/remotion-studio`、`/remotion-render`、`/remotion-maps`、`/remotion-captions`、`/remotion-saas`、`/remotion-interactivity`、`/remotion-docs`、`/remotion-license`（4.0.540 起）、`/remotion-upgrade`、`/remotion-multimedia`。
我讀過的 repo 內檔案：`remotion-markup/` 含 `video-editing.md`、`embedding-videos.md`、`transitions.md`、`sequencing.md`、`ffmpeg.md`、`silence-detection.md`、`text-highlights.md`、`color-correction.md`、`light-leaks.md`、`motion-blur.md` 等；`remotion-captions/` 含 `transcribe-captions.md`、`display-captions.md`、`import-srt-captions.md`。
注意：另有一個本機已載入的 `remotion-motion-graphics` skill（出自 session 的 skill 清單，來源與版本我沒查），與官方 skills 是不同東西。信心：高（官方部分）。

### 1.3 對實拍影片的處理
- 【文件】`<OffthreadVideo>`：「extracts the exact frame from the video ... outside the browser using FFmpeg」；支援 codecs 清單含 **H.265 ("HEVC")**、H.264、VP8/VP9、AV1、ProRes（https://www.remotion.dev/docs/offthreadvideo ，原始 mdx 的 "Supported codecs by `<OffthreadVideo>`" 段）。
- 【文件】新影片用法官方建議改用 `@remotion/media` 的 `<Video>`（Mediabunny＋WebCodecs），但它在 **H.265 渲染時會退回 OffthreadVideo**：fallback 文件列出「The codec cannot be decoded by WebCodecs (e.g. a H.265 stream during rendering)」，並示範 log：`Cannot decode /public/video-h265.mp4, falling back to <OffthreadVideo>`（https://www.remotion.dev/docs/media/fallback）。→ HEVC 素材會走 FFmpeg 路徑，**【推測】可行但慢於 H.264；最穩的作法是先轉 H.264 mezzanine 再進 Remotion**（見 §5 流程）。
- 【文件】裁切：`<Video trimBefore={2*fps} durationInFrames={8*fps}/>` 非破壞式；`ffmpeg.md` 也說明用 `npx remotion ffmpeg` 可不另裝 ffmpeg。
- 【文件】字幕：`@remotion/captions`（MIT）定義 `Caption` 型別 `{text,startMs,endMs,timestampMs,confidence}`，來源可由 `@remotion/install-whisper-cpp`、`@remotion/whisper-webgpu`、`@remotion/openai-whisper`、`@remotion/elevenlabs` 轉入，也可匯入 .srt（`remotion-captions/import-srt-captions.md`）。→ 若字幕是自己寫的台詞（本案多半如此），直接寫成 JSON 或 SRT 即可，不需要轉錄。
- 【文件】轉場：`@remotion/transitions` 內建 presentation：fade、slide、wipe、flip、clock-wipe、iris、cube、cross-zoom、zoom-blur、film-burn、push-cut… 共約 20 種（repo `packages/docs/docs/transitions/presentations/`）。
- 【文件】渲染：`npx remotion render`／`renderMedia()`；編碼 h264（預設）、h265、vp8/9、av1、prores；macOS 可用 VideoToolbox 硬體編碼（`hardwareAcceleration: 'if-possible'`，4.0.228 起，h264/h265 自 4.0.236；https://www.remotion.dev/docs/hardware-acceleration）。

**反面證據（對使用者不利）**：
- `OffthreadVideo` 近期在 Mac／HEVC 有過實際 bug，皆已 CLOSED：#11566（HEVC 影格凍結，mac 重現，closed 2026-09-23）、#10274（平行渲染下 OffthreadVideo 取錯影格、非確定性，macOS arm64，closed 2026-08-13；`--concurrency=1` 可避開）、#10161（B-frame pyramid 快取驅逐後取錯影格，closed 2026-08-09）。（https://github.com/remotion-dev/remotion/issues/11566 、/10274 、/10161）。我**沒有**確認各 bug 的修復版號是否包含於 4.0.540（只知道 issue 關閉日期早於 540 發版日）。【推測】4.0.540 應已含，仍須以實跑驗證。
- `@remotion/media` `<Video>` 目前仍有開放 issue（#11393，Windows，與本案 Mac 無直接關係）。
- 官方文件指出 `<Video>` 不支援 `object-fit` CSS、不支援倒放（media/video 文件）。
信心：高（文件事實）／中（HEVC 實際穩定度，未實測）。

### 1.4 渲染方式與速度
- 【文件】無頭 Chrome（Chrome Headless Shell）逐幀擷取＋ FFmpeg 編碼；本機 CLI、Lambda、Cloud Run 皆可。
- 速度：**查不到官方的 1080p/2:50 實測數字**（我只查了 docs 的 encoding/hardware-acceleration 頁），不給數字。
- 確定性：Remotion 模型是「元件為 frame 的純函式」，天然可重現（HyperFrames 官方比較文也承認：「its model is genuinely simpler to hold in your head. One pure function of the frame number」）。唯一風險在 OffthreadVideo 的取影格（見上）。信心：中高。

### 1.5 授權（引用原文）
來源：https://github.com/remotion-dev/remotion/blob/main/LICENSE.md （我用 curl 取得原始檔，存於 `raw/../remotion-LICENSE.md`，49 行）。
> "Individuals and small companies are allowed to use Remotion to create videos for free (even commercial), while a company license is required for for-profit organizations of a certain size."
> "You are eligible to use Remotion for free if you are: - an individual - a for-profit organization with up to 3 employees - a non-profit or not-for-profit organization - evaluating whether Remotion is a good fit, and are not yet using it in a commercial way"
> "Permission is hereby granted, free of charge, to any person eligible for the "Free License", to use the software non-commercially or commercially for the purpose of creating videos and images and to modify the software ..."

FAQ（https://www.remotion.dev/docs/license/faq ）：
> "an individual, whether for personal or commercial use; an organization or team of individuals with up to 3 people ..." / "Can I make money under the Free License? Yes." / Company License：Creators 方案 "$25/month per person who writes Remotion code themselves or using agentic coding tools"；Automators "$0.01 per render, with a minimum spend of $100/month"。
> "Remotion is not open-source software according to the Open Source Initiative (OSI) definition." （Terms 頁）

比賽作品需不需要付費：**文件沒有提到「比賽／競賽」**（LICENSE.md 與 FAQ 皆搜尋過，無相關字樣）。依文字：個人（或 ≤3 人團隊）不論商業與否都免費，所以**以個人名義參賽＝免費，這點為文件字面推論（信心：中高）**。需留意：
- 若參賽主體是 4 人以上的營利公司，需 Company License。
- 【文件】LICENSE.md 開頭與 PR #3750：Remotion 5.0 起授權小改（"Contractors also count towards team size"；條款綁 Terms and Conditions）。目前 npm 最新仍是 4.x，故適用現行 LICENSE.md；FAQ 內「3 人」與另一處提到「4-person threshold」措辭不一致，若團隊接近邊界請直接問 Remotion。
- 禁止用途：不得為了販售／再授權而複製或修改 Remotion 程式碼做衍生品（與本案無關）。

---

## 2. HyperFrames（HeyGen）

### 2.1 基本資料
- Repo：https://github.com/heygen-com/hyperframes ；文件 https://hyperframes.heygen.com ；描述 "Write HTML. Render video. Built for agents."
- 版本（指令輸出）：
```
$ npm view hyperframes version license engines
version = '0.9.15'   license = 'Apache-2.0'   engines = { node: '>=22' }
dist-tags = { alpha: '0.7.67-alpha.0', desktop: '0.8.53-desktop.0', hdr: '0.8.190-hdr.1', latest: '0.9.15' }
$ gh api repos/heygen-com/hyperframes → stars 79078, created 2026-03-10, pushed 2026-11-05, license Apache-2.0
releases: v0.9.15 (11/05 02:36), v0.9.14 (11/05 01:33), v0.9.13 (11/04 18:50), v0.9.12, v0.9.11 (11/04)
```
→ **還在 0.x，一天可發多版**；issue 編號已到 #7204（開放 76 筆、已關閉 548 筆，`gh issue list`）。Stars 增長極快（8 個月 79k），【推測】與 HeyGen 推廣有關，不等於成熟度。信心：高（數據），中（解讀）。
- 授權：Apache-2.0（LICENSE 檔我已取得，190 行，開頭 "Apache License Version 2.0, January 2004"）。FAQ：「Media you add, such as fonts, music, or footage, keeps its own license.」

### 2.2 架構
【文件】composition 是一個 HTML 檔；用 `data-start`、`data-duration`、`data-track-index`、`class="clip"` 宣告時間；動畫由 adapter 驅動（GSAP、CSS、Lottie、Three.js、Anime.js、WAAPI），GSAP timeline 必須 `paused: true` 並註冊到 `window.__timelines[<composition-id>]`；不需要 React／bundler。（README；https://hyperframes.heygen.com/guides/hyperframes-vs-remotion ；原始檔 `docs/guides/hyperframes-vs-remotion.mdx`）

### 2.3 CLI（【文件】`docs/developers/cli.mdx`）
`npx hyperframes init my-video`｜`preview`（Studio 瀏覽器編輯器）｜`lint`｜`check`（瀏覽器、版面、動態、對比檢查）｜`snapshot --at 0,2,5`（擷取審查影格）｜`render --output output.mp4`｜`doctor`｜`transcribe <file>`｜`audio <file>`｜`tts`｜`capture <url>`｜`add <block>`｜`timeline`（`--json`，含 move/trim/split/delete）｜`compare v1/ v2/`。`render` 另有 `--docker`、`--quality draft`、`--codec h264|h265`（MP4）、`--gpu`、`--workers`。

### 2.4 官方 skills
【文件】安裝：`claude plugin marketplace add heygen-com/hyperframes` → `claude plugin install hyperframes@hyperframes`；或 `npx skills add heygen-com/hyperframes`（Core Skills：`/hyperframes`、`/hyperframes-cli`、`/hyperframes-registry`、`/media-use`；`--all` 裝全部 9 個）。`skills-manifest.json` 列出：figma、hyperframes、hyperframes-cli、hyperframes-registry、media-use、remotion-to-hyperframes、reverse-any-video、slideshow、style-sampling。

**與本案限制的衝突點（我讀 SKILL.md／文件所見）**：
- `skills/hyperframes/SKILL.md` 要求開頭執行 `npx hyperframes events --skill=hyperframes`（「sends the skill's name with the CLI's usual telemetry」）；FAQ：CLI 預設送匿名使用資料，可用 `npx hyperframes telemetry disable` 關閉。
- 同檔寫 "Show real things from their source (the product's screens, the brand's logo, ...)"，與「不出現品牌 logo」需要在 prompt 明確覆蓋。
- `media-use` 的 provider 包含 `heygen`、`codex`、`mflux`、`kokoro`（CLI 文件），可生成圖片／影片／頭像（AI 生成影像）；**本案必須禁用，agent brief 要寫死「不得呼叫 media-use 生成影像」**。`--local-only` 旗標可略過所有網路 provider。
- `/reverse-any-video`、`/style-sampling` 標示為 HyperFrames Pro（README 敘述，經 WebFetch 摘要，非逐字）。

### 2.5 確定性渲染
【文件】https://hyperframes.heygen.com/concepts/determinism （`docs/concepts/determinism.mdx`）：每幀 `time = floor(frame)/fps`，adapter `seekFrame(frame)`，GSAP timeline 只 seek 不 play；Chrome `HeadlessExperimental.beginFrame` 原子擷取；FFmpeg 編碼並混音。規則：禁 `Date.now()`、禁無種子 `Math.random()`、禁渲染中 fetch。原文：「Fonts and Chrome versions differ between computers, so a local render can shift by a pixel from one machine to the next. Render in Docker when you need exact reproducibility」→ 官方自己承認**本機渲染不保證逐像素一致，要 Docker**。

### 2.6 實拍與音訊
- 【文件】`<video class="clip" src=... data-start data-duration data-media-start data-has-audio="true" data-volume>`（`docs/guides/video-components.mdx`）；`<audio>` 亦可。
- 【文件】HEVC：`rendering.mdx`「The browser editor, preview, `check` ... create cached browser proxies for local video that Chrome cannot decode reliably, including common HEVC and ProRes inputs. The original file stays in the project and remains the render source.」`skills/media-use/references/operations.md`：「HEVC/H.265 sources need no conversion for **render** (FFmpeg pre-decodes all input video)」。→ **HEVC 實拍可直接用**，是它的優點。
- 【文件】字幕：`hyperframes transcribe`（本機轉錄）、`guides/captions-and-recuts`（caption rail、lower third、pull-quote）；可匯入校正後逐字稿 JSON。
- 轉場：`@hyperframes/shader-transitions`（WebGL 轉場，npm 0.9.15，另有 registry blocks）。
- 速度：【文件】FAQ 一例「a 300-frame 1920×1080 composition rendered in 9.8 seconds」（廠商自測，非獨立；內容未知）。

### 2.7 成熟度與對使用者不利的證據（開放 issue，皆我實際讀過內文）
- **#7111（macOS，2026-11-03，OPEN）**：同一 composition 用 1 個 worker 與 3 個 worker 渲染出的幀不同（RGB 最多差 15 級）；環境 Apple M2 Max、`browserGpuMode: auto`、screenshot capture。→ 直接打到「確定性」賣點，且正好是 Mac。（https://github.com/heygen-com/hyperframes/issues/7111）
- **#6530（macOS 26.6.2 / M3，2026-10-27，OPEN，needs-info）**：`npx hyperframes render` 在「Compiling composition 5%」被 XProtect "Malicious Script Blocked" 殺掉；回報者註明「Remotion renders fine on the same machine」。有 1 則留言，未見解法。（…/issues/6530）
- **#5473（OPEN）**：`render --docker` 兩個 `<video>` 同時播放時擷取卡住（frame ~200）；本機渲染可。→ 官方建議的 Docker 確定性路徑在「兩段影片同時」時會壞；本案若有畫中畫就要注意。
- **#4435（OPEN）**：平行 worker 的每個分片第一幀可能是透明／pre-seek 狀態（screenshot 模式，ProRes 4444）；`--workers 1` 無此問題。
- **#4555／#4559（OPEN）**：CSS／WAAPI 動畫在載入前建立時，位置會差到 1 幀、每次載入不同。
- **#6871（OPEN）**：含音訊的渲染會重複編碼 AAC 2–3 次。
- **#5025（OPEN）**：「Skills: 10 cross-skill contradictions and 11 wrong facts or broken paths」——官方 skills 本身有文件矛盾。
- 團隊自述：官方比較頁承認 Remotion 「older and much more established」「Remotion Lambda in particular is a mature」，HyperFrames 的規則「breaks quietly if you don't」遵守。
- 倉庫體積：完整 clone 需 Git LFS 約 240MB 黃金測試檔（README）；淺層 clone 實測 418MB。
信心：高（issue 內容為我實讀）；對「是否影響本案」：中（我沒有在此機器上重現）。

### 2.8 與 Remotion 的官方比較（廠商自己的頁面，需打折扣）
`hyperframes-vs-remotion.mdx`：同一個 15 秒 Inkwell 片，Remotion 12 分 30 秒／10 檔 1,037 行／約 950 MB node_modules；HyperFrames 11 分 50 秒／4 檔 822 行／無安裝。官方註明「One run each ... a single run decides nothing about quality」。→ 單次樣本、廠商自家行銷頁，**不能當作比較證據**。

---

## 3. 其他方案

### 3.1 Revideo
- npm `@revideo/core` 0.11.0（2026-07-10）、MIT；repo 現為 midrender/revideo（原 redotvideo），stars 4,178，最後 push 2026-07-15（RELEASING.md）；README：「Revideo is a rendering engine for creating videos in code ... headless render API」「`<Video/>` and `<Audio/>` components with audio export」「It's the engine behind Midrender」。（`npm view @revideo/core`；https://github.com/redotvideo/revideo ）
- 評價【推測】：generator 風格場景（Motion Canvas 分支），可 headless；社群與 agent skills 遠小於 Remotion；無官方 agent skill（我沒查到）。實拍 HEVC 支援文件：**查不到**（只確認有 ffmpeg 套件 `packages/ffmpeg` 與 `Video.ts` 元件）。信心：中低。

### 3.2 Motion Canvas
- npm 3.17.2（2025-02-16 以後無新穩定版；GitHub 最新 tag 為 v3.18.0-alpha.0，2025-02-16）。repo 2026-07-02 仍有 commit（僅 docs 網域重構），MIT，stars 19.7k。
- 【文件】渲染：https://motioncanvas.io/docs/rendering/ 描述的是編輯器內按 RENDER 輸出圖片序列（PNG/JPEG/WebP），頁面未提 headless/CLI；repo 另有 `packages/ffmpeg` 與 `components/Video.ts`（存在但我沒讀其文件）。→ 適合講解動畫，**不適合需要自動化渲染＋實拍合成的流程**（【推測】）。信心：中。

### 3.3 MoviePy
- PyPI 2.2.1（2025-05-21）、MIT；repo pushed 2026-08-26，stars 15.1k（`gh api repos/Zulko/moviepy`、PyPI JSON）。README：「cuts, concatenations, title insertions, video compositing」，Mac 可跑。
- 【推測】以 Pillow／ImageMagick 繪字，逐幀 Python 合成，渲染慢、動畫表現力弱；做「疊字幕＋淡入淡出」可以，做「雙向讀心說明動畫」會很吃力。無逐幀 seek 的動畫模型。

### 3.4 ffmpeg＋libass（本機實測）
```
$ ffmpeg -version → ffmpeg version 7.1
$ ffmpeg -filters → subtitles / ass (libass)、drawtext、xfade 皆存在
$ ffmpeg -encoders → libx264、h264_videotoolbox、hevc_videotoolbox、prores_videotoolbox
```
- 【文件／實測】本機 ffmpeg 7.1 具 libass 字幕（.ass 樣式字幕可精準控制字型、描邊、位置）、`xfade` 轉場、VideoToolbox 硬體編碼。信心：高。
- 限制【推測】：說明動畫（圖形、箭頭、節點動畫）不適合用 filter 寫；agent 友善度中等（指令字串易錯，需 ffprobe 驗證）。是最小依賴的底線與驗片工具。

### 3.5 Claude Code 生態的剪輯類 skill（我查到的）
| 名稱 | 事實 | 與本案相關性 |
|---|---|---|
| browser-use/video-use（https://github.com/browser-use/video-use ） | MIT；stars 30,560；pushed 2026-10-08；「Edit videos with coding agents」。流程：ElevenLabs Scribe 逐字稿→LLM 讀稿決定剪點→EDL→ffmpeg 渲染→自我驗片。需 `ELEVENLABS_API_KEY`（README／SKILL.md）。可呼叫 HyperFrames／Remotion／Manim／PIL 生成動畫 overlay。Hard rules 有：字幕最後套用、逐段 extract 再 `-c copy` concat、音訊 30ms 淡入淡出、逐字稿邊界剪等。 | 設計思路值得借鑑（尤其「字幕最後套、避免二次編碼」），但**依賴付費雲端轉錄、核心情境為口述素材**；本案是遊戲實拍。若要用，先估額度並問使用者（付費 API 規則）。 |
| digitalsamba/claude-code-video-toolkit（MIT，2,443 stars） | README 明示整合 FLUX.2／Ideogram／LTX-2（AI 生圖／生影片）、Qwen3-TTS、ElevenLabs，需雲端 GPU（Modal／RunPod）。 | **與「不可 AI 生成畫面」衝突，不採用**（僅可參考 moviepy 範例）。 |
| barefootford/buttercut（617 stars） | 「Edit Video with Claude Code」，我沒讀內容。 | 查不到細節，未評估。 |
| zhuyansen/awesome-claude-video-skills（1,590 stars） | 列 180 個 repo 的 awesome list，自稱各 repo 有安全分級（未驗證）。 | 可作索引，不是工具。 |
查到才寫：其餘零星 repo（stars ≤ 個位數）不列。

---

## 4. 比較表

評分：高／中／低（括號為依據）。本案＝Mac、HEVC 實拍、需字幕＋說明動畫、不可 AI 影像／logo。

| 項目 | Remotion 4.0.540 | HyperFrames 0.9.15 | Revideo 0.11.0 | Motion Canvas 3.17.2 | MoviePy 2.2.1 | ffmpeg＋libass |
|---|---|---|---|---|---|---|
| 學習／撰寫成本 | 中（React＋frame 數學） | 中低（HTML＋GSAP＋必守規則） | 中（generator 場景） | 中 | 低（Python） | 高（filtergraph） |
| 對 agent 友善度 | 高（官方 13 個 skills、文件有 .md 版） | 高（9 skills、`check`/`snapshot`/`timeline --json`），但 skills 有 #5025 矛盾回報 | 中低（無官方 skill，查不到） | 低 | 中 | 中 |
| 實拍素材合成 | 高（`<Video>`/OffthreadVideo，HEVC 有官方路徑，近期 mac bug 已關） | 高（FFmpeg 預解碼，HEVC 免轉檔） | 中（有 Video 元件，HEVC 查不到） | 低（頁面未提） | 中（讀 HEVC 靠 ffmpeg） | 高 |
| 字幕 | 高（`@remotion/captions`、SRT 匯入、逐字動畫） | 高（caption rail、逐字轉錄） | 低中（自寫） | 低 | 中（TextClip） | 高（.ass 精準樣式，但動畫弱） |
| 轉場 | 高（約 20 種內建 presentation） | 中高（shader-transitions、registry） | 低（自寫） | 低（自寫） | 低（crossfade 等） | 中（xfade） |
| 說明動畫（圖形） | 高（React/SVG/Lottie） | 高（HTML/CSS/GSAP/Lottie） | 高 | 高（原本強項） | 低 | 低 |
| 渲染速度 | 未查到數字；macOS 可 VideoToolbox | 廠商自測 300 幀 1080p 9.8 s（未獨立驗證） | 未查 | 圖片序列＋另行編碼 | 慢（Python 逐幀）【推測】 | 最快 |
| 確定性 | 中高（純函式模型；OffthreadVideo 近期有已修的非確定 bug） | 設計上高，但 macOS 開放 issue #7111、官方說本機不保證逐像素 | 中【推測】 | 中 | 高（無瀏覽器） | 高 |
| 授權 | 專有（source-available）：個人／≤3 人／非營利免費，公司另購 | Apache-2.0 | MIT | MIT | MIT | LGPL/GPL（視 build；本機 build 未查） |
| 風險 | 授權邊界（團隊 ≥4 人）；React 重；OffthreadVideo 取幀 bug 史 | 專案太新、日更 0.x、Mac 開放 bug、遙測／AI 生成導向 skills | 小專案、文件與實拍資訊少 | 無新發版、無 headless 說明 | 效能與動畫表現力 | 複雜畫面難維護 |

---

## 5. 給本案的建議

### 5.1 組合（建議）
1. **ffmpeg**：粗剪（保留 HEVC 原檔不動，另轉一份 H.264 mezzanine 供合成）、音訊正規化、最終驗片（ffprobe／抽幀／黑幀與靜音偵測）。
2. **Remotion 4.0.540**（鎖版本，不要隨手 upgrade）：一個專案裡放全部疊層——標題卡、章節卡、逐字／整句字幕、轉場、「雙向讀心」說明動畫（SVG＋`interpolate`/`spring`）。字幕從手寫 JSON（台詞表）餵入，不依賴轉錄。
3. **HyperFrames 當對照／備援**：若 Remotion 在 mezzanine 上遇到取幀問題，或說明動畫想用 GSAP 加速，再單獨用 HyperFrames 渲染「純動畫卡片」成獨立 mp4，由 ffmpeg 合併。不要兩套同時主力。
4. 不用：video-use（ElevenLabs 付費）、claude-code-video-toolkit（AI 生成）。

理由：Remotion 風險較「已知且已記載」，授權對個人明確免費，官方 skills 齊全。HyperFrames 的 Mac 開放 bug（#7111、#6530）直接落在你的環境與「確定性」賣點上。信心：中（§8 只做過 5 秒實測）。

### 5.2 建議先做 1 小時 spike 再定案（【推測】但成本很低）
用同一段 10 秒真實 HEVC 錄影＋一行字幕＋一個淡入轉場，分別在 Remotion（直接吃 HEVC vs 吃 H.264 mezzanine）與 HyperFrames 渲染，比較：是否成功、有無凍結幀／黑幀、`--concurrency=1` 與預設平行是否逐幀一致（ffmpeg `framemd5` 比對）、耗時。把結果寫回此檔。

### 5.3 流程（粗剪→動畫→合成→驗片）
1. **盤點**：`ffprobe` 每支錄影（codec、fps、VFR？、色彩空間、音軌）。Quest 錄影若為可變幀率，先轉 CFR（`-vsync cfr -r 30/60`）。【推測：頭盔錄影常為 VFR，未驗證你的檔案】
2. **粗剪**：ffmpeg 以時間碼切段，重新編碼為 H.264 mezzanine（高位元率、全關鍵幀較佳，如 `-c:v libx264 -crf 14 -g 1` 或 `h264_videotoolbox`）；保留原始 HEVC。記錄 EDL（JSON：來源、入點、出點、章節）。
3. **腳本與字幕**：台詞／字幕表寫成 JSON（startMs/endMs/text），同時產 .srt 備份；每句台詞需有聲音＋字幕（專案既有規則）。
4. **動畫**：標題卡、章節卡、「雙向讀心」說明圖，在 Remotion 以 `<Composition>`＋`Sequence`／`TransitionSeries` 實作；先 `npx remotion still` 輸出關鍵幀給人／agent 看圖修版。
5. **合成**：`<Video src=mezzanine trimBefore durationInFrames>` ＋ 字幕層 ＋ 轉場；音訊（遊戲聲、旁白、音樂）在同一個 timeline 做 volume 曲線。渲染：`npx remotion render --codec=h264 --concurrency=...`（可先 1 再提高）。
6. **驗片（宣告完成前必做）**：
   - `ffprobe`：解析度、fps、時長 2:30–2:50、音訊取樣率、色彩標籤。
   - 抽幀檢查：每個轉場前後、每張字幕卡、首尾幀；`ffmpeg -vf blackdetect,silencedetect` 找黑幀／靜音；`framemd5` 兩次渲染比對。
   - **限制檢查**：逐章節抽幀確認（a）沒有品牌 logo——包含 Remotion／HyperFrames 範本預設資產、Quest／Meta 系統 UI、撲克牌或場景內的商標；（b）沒有 AI 生成畫面——資產來源清單全數人工或程式繪製。
   - 聲音：耳機＋筆電喇叭各聽一次（HyperFrames 文件的檢查項目亦同）；字幕與語音同步。
   - 檔案體積與比賽上傳規格（尚未提供，待確認）。

---

## 6. 沒做到／不確定

- 文獻調查階段未安裝工具；之後的上機實測見 §8，§8 未涵蓋的項目（整支影片長度、轉場、字幕）仍未驗證。
- 未取得 Remotion 的獨立渲染速度數據；HyperFrames 的 9.8 秒是官方 FAQ 單例。
- 未確認 Remotion #11566／#10274／#10161 的修復落在哪個版本（只確認 issue 已關閉）。
- 未查 Remotion 與 HyperFrames 的遙測細節（HyperFrames 的遙測是讀到的；Remotion 的 Telemetry 頁存在但我沒讀）。
- Remotion 授權：文件未提到「比賽」；是否以個人名義參賽、團隊人數、是否屬公司作品，由使用者決定；有疑慮應向 Remotion 詢問（https://www.remotion.dev/docs/license/faq ）。FAQ 內「3 人」與「4-person threshold」措辭不一致。
- Revideo 的 HEVC 與實拍文件、官方 agent skill：查不到（查過 README、repo 檔案樹、npm）。Motion Canvas 的 Video 節點與 ffmpeg 套件文件：只確認檔案存在，沒讀內容。
- ffmpeg 的授權（LGPL／GPL）依本機 build，未查 `ffmpeg -version` 的 configure 旗標。
- 關於 WebFetch 結果：部分引用（Remotion LICENSE、FAQ、HyperFrames 文件）我用 curl 取得原文自行摘錄；少數（HyperFrames README 的 Pro 功能、Motion Canvas rendering 頁、PR #3750）來自 WebFetch 的小模型摘要，已在內文標註，建議必要時再讀原頁。
- HyperFrames 的 stars 暴增是否反映真實採用：不知道。
- 沒查：Remotion Studio 在比賽作品中是否需顯示任何署名（授權文件未見此要求）。
- `browser-use/video-use` 在 Mac 上的實測、ButterCut 的內容：未查。

## 7. 主要來源 URL
- Remotion LICENSE：https://github.com/remotion-dev/remotion/blob/main/LICENSE.md
- Remotion 授權 FAQ：https://www.remotion.dev/docs/license/faq ；Terms：https://www.remotion.dev/docs/license/terms
- Remotion skills：https://www.remotion.dev/docs/ai/skills ；https://github.com/remotion-dev/skills
- OffthreadVideo：https://www.remotion.dev/docs/offthreadvideo ；Fallback：https://www.remotion.dev/docs/media/fallback ；Video：https://www.remotion.dev/docs/media/video
- 硬體加速：https://www.remotion.dev/docs/hardware-acceleration ；Captions：https://www.remotion.dev/docs/captions/api
- HyperFrames：https://github.com/heygen-com/hyperframes ；https://hyperframes.heygen.com ；determinism：https://hyperframes.heygen.com/concepts/determinism ；vs Remotion：https://hyperframes.heygen.com/guides/hyperframes-vs-remotion ；FAQ：https://hyperframes.heygen.com/faq
- HyperFrames issues：https://github.com/heygen-com/hyperframes/issues/7111 、/6530 、/5473 、/4435 、/4559 、/5025
- Revideo：https://github.com/redotvideo/revideo ；Motion Canvas：https://motioncanvas.io/docs/rendering/ ；MoviePy：https://github.com/Zulko/moviepy
- video-use：https://github.com/browser-use/video-use ；toolkit：https://github.com/digitalsamba/claude-code-video-toolkit

## 8. 上機實測（2026-11-05，Apple M1 Max，機器負載偏高：load 約 38–149）

測試內容：同一個 5 秒場景（Tell 頭盔錄影＋緩慢推近＋「You ⇄ Your opponents」說明動畫＋保留原音），兩個工具各做一次，都輸出 1920×1080 H.264＋AAC。

| 項目 | Remotion 4.0.540（Node 20） | HyperFrames 0.9.15（Node 24） |
|---|---|---|
| 直接吃 Quest HEVC | 成功，5.013 s，無凍結幀，音量與來源相近 | 成功，5.000 s，無凍結幀，音量與來源相近 |
| 渲染時間（5 秒片） | 15–19 s（負載高，僅供參考） | 21–46 s（負載高，僅供參考） |
| 同設定渲染兩次（H.264 素材） | 影像 MD5 相同 | 影像 MD5 相同 |
| 不同 worker 數（HEVC 素材） | 未測 | 預設與 `-w 1` 的 MD5 不同（與 issue #7111 描述相符，肉眼差異未評估） |
| 踩到的問題 | 首次渲染連瀏覽器逾時 25 s（load 149，重跑即成功；原因為推測） | `<video>` 加 `muted` 時輸出沒有音軌，渲染仍回報成功；`check` 有擋下這個錯誤 |
| 安裝體積 | node_modules 989 MB | node_modules 108 MB（另需 Node ≥ 22） |
| 驗證工具 | 自己用 ffmpeg 抽幀 | 內建 `check`、`snapshot`、`audio`、`diff`、`timeline` |

抽幀目視：兩邊的動畫時序、排版、字型（Libre Baskerville）都正確。

未測：整支 2–3 分鐘影片的渲染、轉場、字幕層、Remotion 不同 concurrency 的一致性、HyperFrames 的 Docker 渲染。

其他：實測時已執行 `hyperframes telemetry disable`，設定存於 `~/.hyperframes/config.json`。官方 Remotion／HyperFrames skills 都還沒安裝。
