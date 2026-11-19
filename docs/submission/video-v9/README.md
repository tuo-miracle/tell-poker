# Tell 比賽宣傳影片（v15 成片）製作流程

這個資料夾放的是比賽宣傳影片的完整製作來源，包含 ffmpeg/Python 前處理、Remotion 合成和 numpy 混音。照下面的步驟可以重建成片 **v15**（1920×1080、30 fps、3155 格 = 105.17 秒）。
資料夾名稱沿用 `video-v9`（第一次同步進 repo 時是 v9），內容已經是 v15 的最終版。

成片位置：使用者 Google Drive 的「Tell Demo」資料夾，裡面有 `Tell_demo_v15_1080p.mp4` 和 `Tell_demo_v15_720p.mp4`。影片和音檔都不進 repo。

## 目錄結構

```
video-v9/
├── edl.py            # 鏡頭表：每個鏡頭的來源、入出點、字幕、標籤、音量、變速
├── prep.py           # 依 edl.py 切片 → vidstab 防抖 → 輸出 remotion/public/s<id>.mp4、shots/s<id>.wav、remotion/src/edl.json
├── delight.py        # 壓窗簾和燈的高光（v15 只套用在 s18）
├── test_delight.py   # delight.py 的測試
├── track.py          # OpenCV 模板追蹤揭曉牌位置（spot ping 框線）→ remotion/src/tracks.json
├── mix.py            # 降噪、閘門、配樂（說話時壓低）、字卡音效、-16 LUFS → out/mix.wav
├── .gitignore        # 排除 src/ shots/ out/ music/ remotion/public/ node_modules 和產生的 json
└── remotion/
    ├── package.json  # remotion / @remotion/cli / @remotion/google-fonts 4.0.540、react 19
    ├── tsconfig.json
    └── src/
        ├── index.ts  # registerRoot
        ├── Root.tsx  # Composition "Film"，總長度由 edl.json 加總
        ├── Film.tsx  # 全片畫面：字卡、字幕、手部訊號標籤、追蹤框、片尾卡
        └── theme.ts  # 顏色、字型、easing
```

執行時會產生以下檔案（都已列在 `.gitignore`，不要 commit）：
- `src/`：頭盔原始錄影
- `music/`：配樂
- `shots/`：每個鏡頭的中間檔和音訊
- `out/`：成片
- `remotion/public/`：處理後的鏡頭
- `remotion/src/edl.json`
- `remotion/src/tracks.json`

## 需要的工具與版本

| 工具 | 版本（原始製作環境） | 備註 |
|---|---|---|
| ffmpeg / ffprobe | 7.1 | **要有 libvidstab**，也就是 `vidstabdetect`/`vidstabtransform` 濾鏡（Homebrew 版有）。可以用 `ffmpeg -filters \| grep vidstab` 確認 |
| Python | 3.12 | `prep.py` 只用標準庫。`mix.py` 要 numpy，`track.py` 和 `delight.py` 要 numpy 和 opencv（原本裝在 venv：`pip install numpy opencv-python-headless`） |
| Node.js | v20.19.3 | Remotion 4 需要 Node 18 以上 |
| Remotion | 4.0.540 | `remotion`、`@remotion/cli`、`@remotion/google-fonts` 三個要同版本，已鎖死在 package.json |
| React | 19 | 原始確切的小版本沒有紀錄。2026-11-10 重建驗證時裝到 19.3.1 |
| adb | Android SDK platform-tools | `~/Library/Android/sdk/platform-tools/adb` |

注意：
- 第一次 render 時，Remotion 會自動下載 Chrome Headless Shell（約 140 MB）。
- `Film.tsx` 用 `@remotion/google-fonts` 載入 Libre Baskerville，所以 render 時要能連網。
- 用 shell 迴圈批次呼叫 ffmpeg 時，每個 ffmpeg 都要加 `-nostdin`，否則它會吃掉迴圈的 stdin。

## 素材清單

素材是 Quest 3 實機的 MetaCam 錄影，存在頭盔的 `/sdcard/Oculus/VideoShots/` 裡。`edl.py` 的 `src` 鍵就是錄影開始的時間（HHMMSS）。

| edl key | 頭盔檔名 | 用途（鏡頭 id） |
|---|---|---|
| `234540` | `com.maxlai.tellpoker-20261104-234540-0.mp4` | 主力素材，共 11 鏡頭：01 開場手部訊號、03 標題、07 莊家教學、02/10/08 他讀你、11b 快下注、17 盯人、21/22/23 All-in 與贏池 |
| `190120` | `com.maxlai.tellpoker-20261105-190120-0.mp4` | 共 7 鏡頭：06 徒手無控制器、12/12b 慢下注被抓、15 騙過 Rocco、19a/19/19b 開口問籌碼 |
| `003317` | `com.maxlai.tellpoker-20261104-003317-0.mp4` | 18 攤牌揭曉（追蹤框） |
| `231222` | `com.maxlai.tellpoker-20261103-231222-0.mp4` | 24 Vex「Clever. Once.」 |

`prep.py` 的 `SRC` 另外列了 `230425`、`224236`（20261103）和 `093206`（20261105）。這三個是之前版本用過的鏡頭，**v15 的 edl.py 沒有引用**，不必拉。

素材都是直接 `adb pull` 的原檔，沒有改名、裁切或轉檔，edl 的入出點以原檔時間軸為準。拉回來之後，可以先用下面的指令核對時長是否合理：

```
ffprobe -v error -show_entries format=duration -of csv=p=0 <檔案>
```

## 從頭盔拉素材

頭盔用 USB 接上並允許偵錯後執行：

```bash
cd docs/submission/video-v9
mkdir -p src
A=~/Library/Android/sdk/platform-tools/adb
for f in 20261104-234540 20261105-190120 20261104-003317 20261103-231222; do
  $A pull /sdcard/Oculus/VideoShots/com.maxlai.tellpoker-$f-0.mp4 src/ </dev/null
done
```

- 只用 pull，不要刪除頭盔上的檔案。
- 如果有多台裝置，先設定 `ANDROID_SERIAL`。
- repo 的 `tools/pull-recordings.sh` 示範了同樣的 adb 流程（檢查裝置、比對大小），不過那支腳本拉的是 `.hands` 手勢錄製檔，不是影片。
- 素材也可以放在別的地方，用環境變數指定資料夾：`TELL_SRC=/path/to/videos python3 prep.py`。預設是本資料夾下的 `src/`。

## 配樂與授權

配樂是〈Hep Cats〉，作者 Kevin MacLeod（incompetech.com），授權為 **Creative Commons: By Attribution 4.0**（CC BY 4.0）。

```bash
mkdir -p music
curl -L -o music/Hep_Cats.mp3 "https://incompetech.com/music/royalty-free/mp3-royaltyfree/Hep%20Cats.mp3"   # 原檔 8,025,406 bytes
```

- 片尾卡和影片說明欄都要標示授權，格式如下：`"Hep Cats" Kevin MacLeod (incompetech.com) Licensed under Creative Commons: By Attribution 4.0 License http://creativecommons.org/licenses/by/4.0/`
- 備選曲是〈Funkorama〉，作者和授權都相同，下載：`curl -L -o music/Funkorama.mp3 "https://incompetech.com/music/royalty-free/mp3-royaltyfree/Funkorama.mp3"`（網址依 Hep Cats 的格式推得，未實測）。改用時執行 `TRACK=Funkorama OFFSET=15 python3 mix.py`，片尾卡的標示也要一起改。

## 重建步驟（v15）

以下指令都在 `docs/submission/video-v9/` 底下執行。

```bash
# 1. 切片、防抖，輸出 remotion/public/s*.mp4、shots/s*.wav、remotion/src/edl.json（要 src/ 素材）
python3 prep.py                # 只重做某幾個鏡頭：python3 prep.py 18 24
                               # 只重建 edl.json、不碰影片：python3 prep.py NONE

# 2. 壓高光：v15 只對 s18 套用（noprotect = 不保護實心白塊）
python3 delight.py remotion/public/s18.mp4:noprotect
#    注意：之後如果重跑 prep.py 18，s18 會被覆蓋，要再跑一次這一步

# 3. 追蹤揭曉牌，輸出 remotion/src/tracks.json（讀 s08、s18；mix.py 的 ping 音效也會用到）
python3 track.py

# 4. Remotion 合成（無聲畫面）
cd remotion && npm install
npx remotion render src/index.ts Film ../out/video15.mp4 --codec h264 --crf 15 --concurrency=4 --overwrite
cd ..

# 5. 混音：降噪 + 閘門、Hep Cats -33 dB（對白時再壓 6 dB）、字卡和轉場音效、整體 -16 LUFS → out/mix.wav
python3 mix.py
#    可調的環境變數：TRACK（預設 Hep_Cats）、OFFSET、MUSICDB（-33）、LUFS（-16）、NOMUSIC=1、OUT

# 6. 合併成片，再輸出 720p 版
ffmpeg -nostdin -v error -y -i out/video15.mp4 -i out/mix.wav -map 0:v -map 1:a -c:v copy -c:a aac -b:a 256k -shortest -movflags +faststart out/v15.mp4
ffmpeg -nostdin -v error -y -i out/v15.mp4 -vf scale=1280:720 -c:v libx264 -crf 25 -preset medium -c:a aac -b:a 128k -movflags +faststart out/v15_720p.mp4
```

做完後要核對：`out/v15.mp4` 應該是 30 fps、3155 格。可以用下面的指令確認：

```
ffprobe -v error -select_streams v:0 -count_packets -show_entries stream=nb_read_packets out/v15.mp4
```

只想確認 Remotion 專案能不能載入、手邊沒有素材時：

1. 先跑 `python3 prep.py NONE`。
2. 在 `remotion/src/tracks.json` 放一個 `{}`。
3. 執行 `npx remotion compositions src/index.ts`，應該會列出 `Film 30 1920x1080 3155 (105.17 sec)`。

render 一定要用 track.py 產生的真的 tracks.json。

### 重新剪輯時用的逐字稿（選用）

挑入出點時用的是 faster-whisper `base.en`（`pip install faster-whisper`）。要先用 ffmpeg 把音訊解成 16 kHz mono float32 的 numpy 陣列再交給 `model.transcribe()`。直接傳影片路徑會出現 TypeError。

```python
import subprocess, numpy as np
from faster_whisper import WhisperModel
pcm = subprocess.run(["ffmpeg","-nostdin","-v","error","-i",path,"-ac","1","-ar","16000","-f","f32le","-"], capture_output=True).stdout
segs, _ = WhisperModel("base.en").transcribe(np.frombuffer(pcm, np.float32), word_timestamps=True)
```

當時挑鏡頭用的輔助腳本沒有收進來，包括逐字稿、動作量、候選片段聯絡表、review sheet、事件時間軸。這些腳本只用來決定 edl.py 的時間碼，成片管線不讀它們的輸出。

## 比賽影片規則

- 片長 **3 分鐘以內**（v15 為 1 分 45 秒）。
- 必須是 **Quest 實機畫面**（本片全部來自 MetaCam 錄影）。
- 語言為**英文，或附英文字幕**。
- **不得使用 AI 生成的影片**（本片的字卡、字幕、框線都是 Remotion 程式繪製）。
- **不得出現商標**，也**不得出現參賽者以外可辨識的人**。
- 公開上傳到 **YouTube 或 Vimeo**。
- 截止時間 **2026-11-18 12:00 PT**。

## 影片不能宣稱的功能

以下功能遊戲目前沒有，字卡、字幕和旁白都不能提：

- 眼動追蹤（eye tracking）
- 統計式 Tells
- Assist 按鈕
- 跨場次學習
- 「Not this time」這句台詞（不得出現在片中）

另外，MetaCam 預設不錄麥克風，要在頭盔相機 App 的設定裡打開，2026-11-05 之前的錄影（`234540`、`003317`、`231222`）都沒有玩家原聲。`190120` 是開啟後錄的，19a 鏡頭玩家問 Rocco 籌碼那句有原聲；Rocco 的回覆是遊戲的實際輸出。

## 版本與歷史

- `docs/submission/video-v7/` 是 2026-11-05 v7 成片當時的舊快照，只有 edl/prep/mix/Film/theme，沒有 track 和 delight。**已被本資料夾完全取代**，保留做歷史參考，不要用它來 render。
- 歷史說明：原始工作區在 Claude session 的暫存區，2026-11-10 被系統清除。`remotion/src/index.ts`、`remotion/src/Root.tsx`、`remotion/tsconfig.json` 是依 session 紀錄（2026-11-05 建立後未再修改）逐字還原的。`remotion/package.json` 依當時的安裝指令重建，三個 Remotion 套件從原本的 `^4.0.540` 改為鎖死 `4.0.540`，套件名稱也從 `remotion` 改為 `tell-video-v9`，以免和依賴同名。
- 與原工作區的差異：
  - 原本素材改名為 `v<key>.mp4`，分散在兩個資料夾，現在統一用頭盔原檔名放 `src/`。
  - `track.py` 和 `delight.py` 原本放在 `tools/` 底下，現在放在根目錄，`track.py` 不帶參數時預設使用本資料夾。
  - `prep.py` 和 `mix.py` 會自動建立 `shots/` 和 `out/`。
  - 剪輯、混音的邏輯與參數都沒有更動。
