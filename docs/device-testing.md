# 實機測試步驟（M0：舞台與手部錄製工具）

> 對象：第一次把 APK 裝進 Quest 3/3S 的人。照順序做，每一步寫了「應該看到什麼」。
> 下面標【未實機驗證】的行為，只有原始碼依據，第一次實測時請特別留意並回報。

## 0. 準備
1. 頭盔開啟開發者模式，用 USB-C 接上 Mac，頭盔內按「允許 USB 偵錯」（勾「一律允許」）。
2. 確認連線：
   ```bash
   ADB=/Applications/Unity/Hub/Editor/6000.3.26f1/PlaybackEngines/AndroidPlayer/SDK/platform-tools/adb
   $ADB devices        # 要看到一行 <序號>  device
   ```
3. 建置並安裝：
   ```bash
   tools/build-quest.sh                       # 產出 Builds/Quest/TellPoker.apk
   $ADB install -r Builds/Quest/TellPoker.apk
   ```
4. 建議在桌子前坐好、房間開燈（暗處手部追蹤會變差）。如果還沒做過「空間設定」（Space Setup），可以先不做，見第 2 步。

## 1. 啟動
- 頭盔內：應用程式庫 →「未知來源」→ **Tell**（套件 id `com.maxlai.tellpoker`）。
- 也可以用指令啟動：`$ADB shell monkey -p com.maxlai.tellpoker 1`
- **應該看到**：
  - 背景是 passthrough（看得到真實房間），不是黑色或天空盒。
  - 放下控制器後，看到自己的手（ISDK 的手部模型）跟著真手動。
- 首次啟動會跳出「空間資料」（Spatial data / USE_SCENE）權限對話框 → 請按**允許**。
  - 拒絕也能玩，只是牌桌會一直是虛擬桌。

## 2. 牌桌
牌桌會依序嘗試三種放法，後面的會被更好的取代：

| 放法 | 什麼時候 | 看起來 |
|---|---|---|
| 虛擬桌 | 一啟動就放；沒權限、沒桌子時保持 | 前方約 45 cm、低於視線約 40 cm，有木色桌體與四隻桌腳 |
| EnvironmentRaycast | 有權限後，視線前下方找到一塊**平整、無雜物、至少 50 × 30 cm** 的水平面（低於視線 15–75 cm） | 綠色絨布貼在真實桌面高度，沒有桌腳；位置在你正前方 |
| MRUK TABLE | 空間設定裡有標記為「桌子」的家具 | 綠色絨布的近側邊緣對齊真實桌子靠你這一側的邊，朝桌子中央 |

- **應該看到**：約 90 × 60 cm 的綠色絨布，上面有淡淡的標線：一條橫向下注線、一個底池圓圈、你面前的牌位方框、左／遠／右三個座位小圓。
- 牌桌遠端立著一塊除錯看板，第一行是 `table: <Virtual|EnvironmentRaycast|MrukTable>  mruk: <LoadSceneFromDevice 結果>`，可以直接看出現在用哪種放法。
- 長按 Meta 鍵重設視角（recenter）後，虛擬桌／射線桌會在你新的前方重放；MRUK 桌不動（它跟著真實桌子）。
- 想測 MRUK 吸附：頭盔「設定 → 實體空間 → 空間設定」，把桌子框起來並標記為「桌子」，再重開 app（或等 15 秒，app 會自動重載房間資料）。
- 【未實機驗證】
  - EnvironmentRaycast 的命中條件與穩定度（PlaceBox 50 × 30 cm、連續 3 次高度差 < 2 cm）；桌面很亂時可能一直找不到。
  - 雙手放在桌上時，會不會被當成桌面（PlaceBox 應該會擋掉）。
  - MRUK 桌面的長寬軸方向、桌緣中央是否真的對齊。
  - 絨布抬高 2 mm 是否足以避免與 passthrough 下的真實桌面閃爍。

## 3. 手與除錯看板
- 看板第 2、3 行是左右手：`conf`（追蹤信心）、`pinch`（食指捏合 0–1）、`wristY`（手腕離桌面高度，cm）、`palm (x, y, z)`（掌心朝外的方向，牌桌座標）。
- **請檢查並回報**：
  1. 手掌平放在桌上、掌心朝下：`palm` 應接近 `(0, -1, 0)`，`wristY` 約 2–5 cm。
  2. 手掌翻過來掌心朝上：`palm` 應接近 `(0, 1, 0)`。
  3. 兩手各做一次。左右手如果有一隻方向相反，就是關節朝向轉換有問題。
  4. 食指與拇指捏緊時 `pinch` 接近 1。
- 【未實機驗證】掌心法線的方向慣例、左右手是否一致（只依 SDK 原始碼推定，並有單元測試，但沒有真實資料）。
- 手部資料取自 ISDK rig 的**原始手**（OVRHands，未經 poke 限制），所以手指按到虛擬按鈕時，錄到的是真手的位置。
- logcat 裡第一次追蹤到手時會印 `[IsdkHandsSource] first tracked ... palm-wrist angle=...`，角度應接近 0；若接近 90 或 180，請回報。

## 4. 錄製手勢
面板在牌桌**左前角**（靠你那側的左邊），平躺在桌面上：

```
   [ status：ready / REC 3.2s 190 f / saved ... ]   ● ← 錄製中紅點閃爍
           [   REC / STOP   ]
     [ < ]    pick_bet    [ > ]
                       （你坐在這一側）
```

- 用食指**從上方往下點**按鈕（指尖要先在按鈕上方 3.5 cm 以上，再往下點到桌面）。手平貼桌面滑過去不會觸發，這是故意的。
- `<` `>`：切換標籤（錄製中不能切換）。標籤有 `peek`、`pick_bet`、`knock`、`fold_push`、`allin`、`flip`、`idle`。
- `REC / STOP`：開始或結束錄製。錄製中狀態列變紅、顯示秒數與幀數，旁邊紅點閃爍。
- 結束後狀態列顯示 `saved <標籤>_<時間>.hands  N f / X s`。
- 建議流程（每個標籤）：
  1. 選標籤。
  2. 按 REC。
  3. 手放回自然位置，停 1 秒。
  4. 做 3–5 次該手勢，每次之間停約 1 秒。
  5. 按 STOP。
- 另外錄 `idle`：正常聊天、看牌、手隨意擺放 30 秒，用來測誤觸。
- 拿下頭盔或按 Home 會自動結束並存檔。
- 錄製中如果牌桌被重新放置（MRUK 資料晚到、recenter），會自動結束這個檔，狀態列顯示 `(stopped: table moved)`；重錄即可。
- 【未實機驗證】
  - 按鈕高度門檻（按下 < 2 cm、上膛 > 3.5 cm）是否適合真實桌面；若很難按或太容易誤觸，請回報。
  - TextMesh 文字在雙眼都看得到（Single Pass Instanced）。

## 5. 把錄製檔拉回電腦
```bash
tools/pull-recordings.sh            # 拉到 Assets/_Project/Tests/Gestures/Recordings/
tools/pull-recordings.sh --clear    # 每個檔拉回且大小一致後，才刪掉頭盔上的那個檔
```
- 頭盔上的位置：`/sdcard/Android/data/com.maxlai.tellpoker/files/recordings/`
- 檔名 `<標籤>_<yyyyMMdd_HHmmss>.hands`；備註欄記錄了標籤、開始時間、機型與牌桌放置方式。
- 在 Mac 的 Unity Editor 按 Play（沒有頭盔），會自動用這個資料夾裡**檔名排序第一個**的 `.hands` 回放手部，並使用虛擬桌，方便檢查畫面。

## 6. 回報清單
請回報以下項目（有問題就附截圖或錄影）：
- passthrough 是否正常。
- 牌桌用了哪種放法、是否對齊真實桌面（高度差、邊緣差大約幾公分）。
- 手部模型是否跟手。
- 第 3 步的 `palm` 方向是否符合。
- 按鈕好不好按。
- 錄了哪些檔、`pull-recordings.sh` 的輸出。
- 若 app 閃退：
  ```bash
  $ADB logcat -d -s Unity > /tmp/tell.log
  ```
  附上 `/tmp/tell.log`。
