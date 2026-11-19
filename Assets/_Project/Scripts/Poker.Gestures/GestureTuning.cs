namespace Poker.Gestures
{
    /// <summary>
    /// 手勢門檻（全部可調；遊戲層可包成 ScriptableObject 在頭盔內調）。
    /// 研究依據見 docs/research/gestures.md：追蹤誤差約 1 cm（S20）、延遲約 40 ms、手指被手掌擋住時最不準（S3、S9）。
    /// 數值除特別註明外都是【推測】，要靠實機錄製（.hands）回放再調。單位：公尺、秒、公尺/秒、度。
    /// </summary>
    public sealed class GestureTuning
    {
        // ── 追蹤品質 ──
        /// <summary>低於這個信心值的幀視為不可靠：不啟動新手勢、計時暫停。</summary>
        public float MinConfidence = 0.5f;
        /// <summary>估速度用的最小平方法時間窗（72 Hz 約 4 個樣本）。太短會被 1 cm 雜訊放大，太長會壓低敲擊的峰值。</summary>
        public float VelocityWindow = 0.05f;
        /// <summary>估高度用的平均時間窗（壓雜訊）。</summary>
        public float HeightSmoothWindow = 0.05f;

        /// <summary>發出一個行動事件後，這段時間內不接受新的行動手勢（同一幀或緊接著的另一手勢不會疊上去）。</summary>
        public float ActionCooldown = 0.3f;

        // ── 捏合（IndexPinch 遲滯；另與 runtime 布林值 IndexPinching 取「或」）──
        /// <summary>捏合強度 ≥ 這個值算開始捏（遲滯上緣）。Quest 3 實測強度封頂 0.70（92 段錄製 129 幀剛好 0.70、從未超過），
        /// 拇指食指距離 &lt; 1.5 cm 的 17 次緊捏，峰值 0.51–0.70。2026-10-12 第三次實機回報拿籌碼不靈敏（整場只有 6 次 ChipsPicked），
        /// 由 0.60 降到 0.45：bet_v2_152538 在籌碼堆上輕捏只到 0.43、指尖距 2.3 cm 就沒拿到；休息的手最高 0.34（bet_v2_152604）。</summary>
        public float PinchStart = 0.45f;
        /// <summary>捏合強度 &lt; 這個值且布林值為 false、指尖距 &gt; PinchReleaseGap 才算放開（遲滯下緣）。實測 0.30–0.50 都沒有 0.3 秒內放開又捏回的抖動。</summary>
        public float PinchRelease = 0.4f;
        /// <summary>拇指尖與食指尖距離 ≤ 這個值也算捏合（bet_v2：指尖已碰在一起 1.0 cm，強度只有 0.46）。
        /// 單看距離分不出捏籌碼與拿水杯，所以撿籌碼仍要求捏合點在籌碼堆上。92 段 v1 錄製只有 22 幀 ≤ 1.2 cm。</summary>
        public float PinchTouchGap = 0.012f;
        /// <summary>輕捏：強度 ≥ PinchAssistStrength 且指尖距 ≤ PinchAssistGap 也算開始捏。只看指尖距不行：休息的手拇指食指相距 2.9–3.2 cm、
        /// 強度 0.17–0.34（bet_v2_152604），2.5 cm 只留 0.4 cm 餘裕，所以要再加強度條件。</summary>
        public float PinchAssistStrength = 0.30f;
        public float PinchAssistGap = 0.025f;
        /// <summary>距離超過這個值（且強度、布林值都放開）才算放開（遲滯）。要大於 PinchAssistGap，輕捏才不會一捏就放開（舊值 0.02）。
        /// 真實錄製放開時指尖會張到 6–9 cm。</summary>
        public float PinchReleaseGap = 0.03f;

        // ── 咪牌 ──
        /// <summary>手掌或中指指尖在牌矩形外多少範圍內仍算「在牌上方」。真實錄製：看得到牌時指尖蓋在牌上、手掌在牌前 5–7 cm。</summary>
        public float PeekHoverMargin = 0.04f;
        /// <summary>咪牌時指尖（食指、中指較低者）離桌高度上限：指尖要壓在牌上，手只是從牌上方經過不算。</summary>
        public float PeekMaxTipHeight = 0.05f;
        /// <summary>進入咪牌後，掌心角度只要 ≥ 這個值就維持（抬手腕時實測掉到 104–120°；進入仍要 PalmDownMinAngle）。
        /// peek_v2_153106 整隻手抬起時掌心轉到 84–96°（舊值 90 在抬到一半就把牌放平）；翻牌要掌心朝上 ≤ 50°，所以停在 70。</summary>
        public float PeekHoldMinAngle = 70f;
        /// <summary>進入咪牌後指尖離桌高度上限（進入仍要 PeekMaxTipHeight）：整隻手抬起時指尖會跟著離開牌 3–5.7 cm（peek_v2_153106）。</summary>
        public float PeekHoldMaxTipHeight = 0.07f;
        /// <summary>手腕高出指尖的量：Min 對到 0、Max 對到 1。手平放 0.4–2.9 cm（真實錄製）；完整咪牌 12–16 cm。
        /// 2026-10-12 第三次實機回報咪牌不靈敏：Max 由 0.10 降到 0.07，手腕高出指尖 5 cm＝0.5（教學判定 0.45）、6 cm＝0.75。
        /// Min 維持 0.03：降到 0.025 時 idle 錄製 Peek≥0.3 由 2 段變 5 段（休息姿勢 2.5–2.9 cm 就開始掀）。
        /// 代價：合成雜動（含 4° 旋轉雜訊，手腕對指尖約 ±1.2 cm）Peek≥0.3 由 14/500 變 140/500；真實錄製休息時同一姿勢只變動 ±1–3 mm。</summary>
        public float PeekLiftMin = 0.03f;
        public float PeekLiftMax = 0.07f;
        /// <summary>翻腕咪牌：掌心朝下在牌上進入咪牌後，翻腕把牌邊掀起來看（掌心轉向朝上），掌心角度 ≤ Zero 開始掀、到 Full 掀滿；
        /// 取與手腕抬起量的較大者。2026-10-13 實機 log 3646.9–3648.5：開發者「把牌翻起來看」時掌心轉到 39–53°（normal.y +0.61～+0.78）、
        /// 手掌 4.6–7.4 cm 高、指尖 2.9–4.8 cm，舊規則判 palm not down（掌心 &lt; PeekHoldMinAngle 就放平）。
        /// 只改咪牌量（私下看牌）；攤牌亮牌仍只由 ShowdownFlip 決定。Full ≥ Zero 時停用。
        /// Zero 取 85°：idle 錄製手側放（掌心約 93–99°）時不能掀；110° 時 idle Peek≥0.3 由 2 段變 6 段。</summary>
        public float PeekRollZeroAngle = 85f;
        public float PeekRollFullAngle = 50f;
        /// <summary>翻腕咪牌時掌心角度低於這個值（整隻手翻成掌心朝上，像攤牌翻牌）就放平：實機掀牌時最低 39°。</summary>
        public float PeekRollMinAngle = 25f;
        /// <summary>翻腕咪牌時手掌離桌至少這麼高：實機掀牌 4.6–7.4 cm；idle 錄製（150237、150327）在牌旁把手翻成掌心朝上平放桌上，手掌 1–2 cm。</summary>
        public float PeekRollMinPalmHeight = 0.04f;
        /// <summary>手掌離桌超過這個高度就不算咪牌（研究：牌上方 10 cm 內，留 2 cm 給雜訊）。</summary>
        public float PeekMaxPalmHeight = 0.12f;
        /// <summary>掌心法線與 +Y 的夾角至少這麼大才算掌心朝下（180° 為正朝下；允許 45° 偏差）。</summary>
        public float PalmDownMinAngle = 135f;
        /// <summary>掌心朝下要維持這麼久才進入咪牌（研究建議 0.3 s，這裡取短一點減少延遲）。</summary>
        public float PeekArmTime = 0.12f;
        /// <summary>【已停用，保留欄位相容】舊版用手腕離桌絕對高度算咪牌量；手懸在牌上方也會被當成掀牌。改用 PeekLiftMin／PeekLiftMax。</summary>
        public float PeekWristMinHeight = 0.045f;
        public float PeekWristMaxHeight = 0.10f;
        /// <summary>咪牌量的低通時間常數（壓 1 cm 雜訊造成的牌角抖動）。</summary>
        public float PeekSmoothTime = 0.08f;
        /// <summary>咪牌量變化超過這個值才發事件（降低事件量）。</summary>
        public float PeekEmitStep = 0.02f;
        /// <summary>追蹤遺失超過這麼久，咪牌量歸 0。</summary>
        public float PeekLostTimeout = 0.25f;

        // ── 籌碼 ──
        /// <summary>計數用的籌碼堆範圍：捏合點離中心超過這個值＋ChipLockDistance，數量就鎖定。</summary>
        public float ChipStackRadius = 0.06f;
        /// <summary>開始捏的位置離籌碼堆中心的水平距離上限。遊戲把籌碼畫成多柱（柱距 4.3 cm、範圍約中心 ±6.3 cm，
        /// ChipStackMesh.ColumnOffset），bet_v2 使用者在 6.7–7.5 cm 處捏。</summary>
        public float ChipPickRadius = 0.09f;
        /// <summary>遊戲層餵了玩家籌碼柱位置（GestureRecognizer.SetPlayerChipFootprint）時，捏合點在某一柱的籌碼邊緣外這麼近也能拿。
        /// 2026-10-12 實機：贏了很多籌碼後籌碼堆往後多排好幾排，離堆中心 12 cm 以上，固定的 ChipPickRadius 捏不到。
        /// 2026-10-12 第三次實機回報拿籌碼不靈敏：由 2 cm 放寬到 3 cm（追蹤誤差約 1 cm，捏合點在拇指食指中點、常偏離籌碼邊緣）。</summary>
        public float ChipFootprintMargin = 0.03f;
        /// <summary>捏合開始後這段時間內，捏合點移進籌碼範圍也能拿起：達到捏合門檻時手指還在收攏、捏合點還在移動（合成手約 0.6 cm），
        /// 平滑又慢 0.05 s，只在「開始捏」那一幀判斷，籌碼邊緣附近的捏合就會漏掉（PeekPickKnockSensitivityTests 外 2.8 cm 的案例）。【實機未驗證】</summary>
        public float ChipLatePickWindow = 0.3f;
        /// <summary>稍晚拿起只限開始捏時捏合點就在籌碼範圍外這麼近（ChipLatePickWindow）。0.02 時合成雜動 Picked 由 120 變 139／500、
        /// allin_v2_153025 推的途中又被判成捏起；0.01 時 136／500、該段通過。</summary>
        public float ChipLatePickMargin = 0.01f;
        /// <summary>捏合點離桌高度上限（籌碼堆可能疊得高）。</summary>
        public float ChipPickMaxHeight = 0.10f;
        /// <summary>每抬高這麼多加 1 枚。指尖誤差約 1 cm，所以一階至少 2 cm 以上。</summary>
        public float ChipStepHeight = 0.025f;
        /// <summary>數量往下減時多要求的高度（遲滯），避免在階的邊界上跳動。</summary>
        public float ChipStepHysteresis = 0.008f;
        /// <summary>捏合點離開籌碼堆範圍再多這麼遠，數量就鎖定（研究：離開堆約 3 cm 才確定 N）。</summary>
        public float ChipLockDistance = 0.03f;

        // ── 放開與下注 ──
        /// <summary>估放開速度的時間窗（放開前這段時間的平均速度）。</summary>
        public float ReleaseVelocityWindow = 0.10f;
        /// <summary>水平速度超過這個值才當作「丟出去」，用拋物線外推落點；否則落點就是放開位置。</summary>
        public float ThrowMinSpeed = 0.5f;
        /// <summary>落點在下注區外這麼近也算進下注區：籌碼半徑 2 cm＋1 cm 追蹤誤差。bet_v2 使用者常一過線就放手（落點 z 0.29–0.30）。</summary>
        public float BetZoneLandingMargin = 0.03f;
        /// <summary>
        /// 落點在自己的牌與籌碼前方就算下注（不只下注區）：2026-10-13 實機 log pid 8951 的 33 次放開有 10 次 zone=False，
        /// 偏短 z 0.22–0.25、偏右 x 0.39–0.45、丟過頭 z 0.81–0.94；遊戲會把籌碼畫在夾進下注區的位置（TableLayout.BetDisplayPoint）。
        /// 最近的界線：z ≥ 牌前緣（0.194）＋這個值＝0.209（實機最短 0.22 留 1 cm）。
        /// </summary>
        public float BetPastCardsMargin = 0.015f;
        /// <summary>落點可以超出絨布左右緣這麼遠（預設 0.45＋0.10＝±0.55；實機偏右最遠 0.45）。</summary>
        public float BetSideOvershoot = 0.10f;
        /// <summary>落點離籌碼堆中心這麼近就是放回籌碼堆（不是下注）：捏起範圍 9 cm＋3 cm。實機放回 (0.21,0.10)、(0.23,0.12) 離中心 ≤ 3.6 cm；
        /// 偏短的下注 (0.14,0.22)、(0.17,0.25) 離中心 14–15 cm。遊戲餵了籌碼柱位置時，柱邊緣外 ChipFootprintMargin 內也算放回。</summary>
        public float BetPutBackRadius = 0.12f;
        /// <summary>
        /// 落點在牌前緣與下注區之間（短區，z &lt; BetZone.MinZ − BetZoneLandingMargin）時，要「丟」或「從上方放下」才算下注：
        /// 放開時往前速度 ≥ BetShortTossSpeed，或捏合點離桌 ≥ BetShortDropHeight。手貼著桌面慢慢放下不算——
        /// 合成日常雜動（拿起籌碼、就近放下）的落點正好在 (0.16–0.33, 0.22–0.28)，全算的話雜動誤觸由 4/500 變 16/500（門檻 1%）；
        /// 那些放開都在離桌 ≤ 3.2 cm、往前 ≤ 0.22 m/s。bet_v2 真實下注放開時離桌 5.6–10.9 cm、往前 0.38–0.78 m/s。
        /// </summary>
        public float BetShortTossSpeed = 0.3f;
        public float BetShortDropHeight = 0.05f;
        /// <summary>撤回時間窗：籌碼落在下注區後這麼久沒被撈回才確認下注（spec §4：1.5 秒）。</summary>
        public float RetractWindow = 1.5f;
        /// <summary>撈回要在某次落點這麼近的範圍內捏起（不是在下注區任何地方捏一下就全撈回）。</summary>
        public float RetractRadius = 0.08f;
        /// <summary>撈回時捏合點離桌高度上限。</summary>
        public float RetractMaxHeight = 0.10f;

        // ── 過牌（敲兩下）──
        /// <summary>手掌往下速度超過這個值才算一次敲擊峰值。研究建議 > 0.6 m/s，但 4 樣本速度估計會壓低 3–4 cm 小敲擊的峰值；
        /// 合成測試實測（400 次敲擊／1500 段日常雜動）：0.5 漏 3.8%、誤觸 0.33%；0.45 漏 1.3%、誤觸 0.53%；0.4 誤觸 1.5%。</summary>
        public float KnockPeakSpeed = 0.45f;
        /// <summary>開始一次下敲的速度。真實錄製：第二下常只有 0.33–0.43 m/s；快節奏（≤ KnockQuickGap）時兩下只要一下 ≥ KnockPeakSpeed。</summary>
        public float KnockMinPeakSpeed = 0.35f;
        /// <summary>第一下成立後、手已回升 KnockMinRise，第二下的起始速度（手掌頻道）。2026-10-13 實機 log 3586–3599（張開手敲兩下）：
        /// 第一下都有判到，第二下只有 0.24–0.34 m/s，全部「等不到第二下」；單敲一下後手放鬆落回桌面 0.06–0.14 m/s。
        /// 只在第一下落差 ≥ KnockSoftFirstMinDrop（實機 0.034–0.061 m）時放寬。</summary>
        public float KnockSecondMinPeakSpeed = 0.22f;
        public float KnockSoftFirstMinDrop = 0.035f;
        /// <summary>慢節奏（間隔 &gt; KnockQuickGap）時原本兩下都要 ≥ KnockPeakSpeed（否則 weak）；放寬成：第一下清楚、間隔 ≤ KnockSoftPairMaxGap、
        /// 第二下 ≥ KnockSecondMinPeakSpeed 且落差 ≥ KnockSoftSecondMinDrop 也算（實機 3599.065：0.36／0.39 m/s、間隔 0.49 s、落差 0.041／0.036）。
        /// 間隔 0.6 s 或第二下落差 0.025 時，idle 錄製（idle_20261012_150313 944.6 s）與合成雜動會多出誤觸敲桌，所以收在 0.55 s／0.03 m。</summary>
        public float KnockSoftPairMaxGap = 0.55f;
        public float KnockSoftSecondMinDrop = 0.03f;
        /// <summary>兩下峰值間隔 ≤ 這個值算快節奏（真實錄製 0.25–0.36 s）；更慢時兩下都要 ≥ KnockPeakSpeed。</summary>
        public float KnockQuickGap = 0.45f;
        /// <summary>兩下的水平位置差上限：真人敲在同一點（≤ 1.7 cm），翻牌、推牌、雜動的兩次下拍相差 4.6 cm 以上。</summary>
        public float KnockMaxSpread = 0.04f;
        /// <summary>往下速度低於這個值，該次敲擊結束。</summary>
        public float KnockStrokeEndSpeed = 0.15f;
        /// <summary>敲擊結束時手掌離桌要低於這個高度（spec：y &lt; 6 cm）。2026-10-12 實機 log pid 22510 有 4 下在 6.0–6.7 cm 被判 too-high
        /// （桌面校準誤差約 1–3 cm）；放寬到 7 cm 時合成統計與全部錄製的誤觸不變。</summary>
        public float KnockMaxHeight = 0.07f;
        /// <summary>兩次峰值的最長間隔（spec：0.6 秒內兩次）。</summary>
        public float KnockWindow = 0.6f;
        /// <summary>兩次峰值的最短間隔（同一次敲擊的回彈不算兩次）。</summary>
        public float KnockMinGap = 0.12f;
        /// <summary>每次敲擊在撞擊前 KnockDropWindow 內至少落下這麼多。第二下也因此要求兩下之間有回升。</summary>
        public float KnockMinDrop = 0.02f;
        /// <summary>第一下之後，手要從最低點回升至少這麼高，第二下才算數（連續往下放的手被雜訊切成兩段時不會回升）。
        /// 2026-10-12 實機 log pid 22510：輕敲時兩下之間手掌只回升約 1.3–2 cm（2005.687／2007.062 s 判 no-rise），但降到 0.018／0.016／0.012
        /// 時合成雜動誤觸敲桌由 2 段變 4／5／8 段（超過 1% 門檻），推牌與 All-in 腳本也開始出現敲桌，所以維持 0.02；trace 改印真正的回升量，等下一次實機資料。</summary>
        public float KnockMinRise = 0.02f;
        /// <summary>量落差的時間窗：真的敲擊在 0.1 s 左右落下 3–6 cm；慢慢放下的手在這段時間內落差不夠。</summary>
        public float KnockDropWindow = 0.15f;

        // ── 診斷 ──
        /// <summary>Trace 的「差一點」診斷（籌碼、咪牌）每隻手、每個原因最短間隔（秒），避免每幀輸出。</summary>
        public float NearMissTraceInterval = 0.25f;

        // ── 手指敲桌（手掌貼桌不動、食指或中指抬起再敲下；真實錄製抬 5.7–10 cm、敲下 1.2–1.7 m/s）──
        /// <summary>指尖往下峰值速度，兩下都要達到。</summary>
        public float FingerTapPeakSpeed = 0.8f;
        /// <summary>開始一次手指下敲時，手掌離桌高度上限與手掌上下速度上限（手掌在動就交給手掌頻道）。</summary>
        public float FingerTapMaxPalmHeight = 0.06f;
        public float FingerTapMaxPalmSpeed = 0.3f;
        /// <summary>指尖敲下結束時離桌高度上限（含手掌 1 cm 追蹤誤差）、敲下前的落差下限（實測 ≥ 5.5 cm）、兩下之間指尖回升下限。</summary>
        public float FingerTapMaxTipHeight = 0.05f;
        public float FingerTapMinDrop = 0.035f;
        public float FingerTapMinRise = 0.03f;
        /// <summary>兩下峰值最長間隔（真實錄製 0.21–0.26 s）與手掌位置差上限。</summary>
        public float FingerTapWindow = 0.45f;
        public float FingerTapMaxSpread = 0.03f;

        // ── 推牌棄牌 ──
        /// <summary>開始推的位置：手掌在牌矩形外多少範圍內。</summary>
        public float FoldPushStartMargin = 0.05f;
        /// <summary>推牌過程手掌離桌高度上限（貼近桌面）。</summary>
        public float FoldPushMaxHeight = 0.06f;
        /// <summary>指尖離桌 ≤ 這個值時，手掌可抬到 FoldPushMaxPalmHeightWithTips 仍算貼桌推（用手指推牌）。foldpush_v2 壓在牌上時指尖 −3～+2 cm；
        /// 放寬到 6 cm 時，手在牌上方 8–10 cm 往前滑過的雜動也會被當成推牌。</summary>
        public float FoldPushMaxTipHeight = 0.035f;
        public float FoldPushMaxPalmHeightWithTips = 0.12f;
        /// <summary>手掌要往前移動至少這麼遠，並越過棄牌線。</summary>
        public float FoldPushMinDistance = 0.10f;
        /// <summary>推牌的左右偏移上限（相對起點）：|側移| ≤ FoldPushMaxSideways + FoldPushSidewaysPerForward × 往前距離。
        /// 2026-10-12 實機 log：真人往右前方斜拖到棄牌堆（約 48°，棄牌堆本身在牌的 50° 方向），146.4 s 側移 21 cm／往前 18 cm
        /// 被舊錐形（0.10＋0.6×）擋掉；起步時先往右 10 cm 才往前 3 cm（166.9 s）；foldpush_v2_152932 抬手時先往右 8 cm。
        /// 上限仍要在推到棄牌線（往前 12 cm）前擋掉 60° 的側掃（RealDataShapeTests：往前 9.9 cm 時就超過）。</summary>
        public float FoldPushMaxSideways = 0.10f;
        public float FoldPushSidewaysPerForward = 0.75f;
        /// <summary>從牌上出發後，拿起來移到棄牌處途中手掌可到的高度上限與掌心角度下限（0＝不限制）。
        /// 2026-10-12 實機 log 164.8／166.5／168.6 s（使用者確認是棄牌）：拿起牌劃弧線，途中手掌拱到 20–23.5 cm、
        /// 掌心角度 &lt; 110°（舊規則判 lifted），0.2 s 後落在棄牌堆旁。角度實際多少不知道，所以不限制；高度留 6.5 cm 餘裕。
        /// 誤觸靠「要在時限內落在棄牌堆範圍」擋。</summary>
        public float FoldCarryMaxHeight = 0.30f;
        public float FoldCarryMinPalmAngle = 0f;
        /// <summary>拿到棄牌堆「放下」時手掌離桌高度上限（一路貼桌的推牌仍用 FoldPushMaxHeight）。
        /// 實機 log 落下時手掌最低 0.057／0.073／0.057 m（取樣 30–390 ms 一點），成功的 6 次 0.03–0.07 m。</summary>
        public float FoldCarryLandMaxHeight = 0.08f;
        /// <summary>拿起來（中途離開桌面）的棄牌，放下處要在棄牌堆中心這個範圍內；一路貼桌推的不受限。
        /// foldpush_v2 8 段都放在 (0.19–0.22, 0.29–0.31)，離棄牌堆 6–8 cm。手放在牌上再抬到別處放下的雜動因此不算。</summary>
        public float FoldMuckRadius = 0.12f;
        /// <summary>拿起來的棄牌，從離開桌面到放下的最長時間：foldpush_v2 約 0.45–0.5 s；慢慢移到別處再放下的雜動不算。</summary>
        public float FoldCarryMaxTime = 0.8f;
        /// <summary>從開始推到越線的最長時間（實機斜拖 0.2–0.3 s）。2026-10-12 第二次實機 log：慢慢滑牌 14 cm 花 1.46 s 還沒判定就被重設，
        /// 放寬到 2.5 s（合成慢滑 1.2–2.0 s 加上起點前的停留）。</summary>
        public float FoldPushMaxTime = 2.5f;
        /// <summary>棄牌線改看「手掌或中指指尖」越線（手指壓著牌推，牌在指尖下；2026-10-12 第二次實機 log 348.9 s 手掌推 14 cm 停在 z 0.19）。
        /// 只有指尖越線時，手掌離桌要 ≤ 這個高度：foldpush_v2 推牌時手掌 5–9 cm；v1 錄製 bet_20261012_145328 手掌 11.6 cm、
        /// 指尖點桌往前甩（使用者在當時沒畫出的「籌碼堆」位置比劃下注，剛好落在現在的牌上）會被當成棄牌。【只有 1 段反例支撐，待實機確認】</summary>
        public float FoldTipCrossMaxPalmHeight = 0.10f;
        /// <summary>往前推超過這麼遠才算「推牌進行中」，開始佔用行動手勢。</summary>
        public float FoldPushClaimDistance = 0.03f;

        // ── 拿起底牌看 ──
        /// <summary>開始捏的那一幀，捏合點在兩張牌合起來的矩形外這個範圍內、離桌不高於 CardGrabMaxHeight，才算捏住牌。
        /// 籌碼堆在牌右邊約 17 cm（ChipStackCenter 0.24 − 牌右緣 0.0675），2 cm 不會碰到籌碼的捏起範圍（ChipPickRadius 9 cm → x ≥ 0.15）。【推測，待實機錄製】</summary>
        public float CardGrabMargin = 0.02f;
        /// <summary>捏住牌時捏合點離桌上限：牌平放在桌上，捏牌時指尖在 0–3 cm（合成手 1 cm）；在空中捏的手不算。</summary>
        public float CardGrabMaxHeight = 0.05f;
        /// <summary>稍晚捏住：開始捏時捏合點在範圍外 1 cm 內，這段時間內移進範圍也算（同 ChipLatePickWindow 的道理）。</summary>
        public float CardGrabLateWindow = 0.3f;
        /// <summary>捏住後，捏合點抬到離桌這個高度以上才算「拿起來」（CardsLifted，牌開始跟著手）。
        /// 咪牌時指尖留在牌上（PeekMaxTipHeight），翻牌時手掌 ≤ FlipMaxHeight 但不捏；8 cm 以上才離開這兩者常見的範圍。【推測】</summary>
        public float CardLiftHeight = 0.08f;
        /// <summary>捏住後要在這段時間內抬到 CardLiftHeight，否則當成沒拿（手捏著放在牌上休息、咪牌途中手指收攏）。</summary>
        public float CardLiftMaxDelay = 2.0f;
        /// <summary>
        /// 拿起來之後進入「看牌」的條件（任一成立）：手掌離桌 ≥ CardLookCommitHeight，或拿起已超過 FoldCarryMaxTime（拿牌棄牌本來就要在這段時間內落下）。
        /// 進入看牌後，「拿到棄牌堆落下」的棄牌路徑關閉，只有在棄牌堆範圍內低處放開（FoldCarryLandMaxHeight、FoldMuckRadius）才棄牌。
        /// 高度 0.25：2026-10-12 實機拿牌棄牌劃弧線時手掌最高 23.5 cm（見 FoldCarryMaxHeight），留 1.5 cm。
        /// 不用「往臉的方向拉回」：foldpush_v2 拿牌棄牌時，捏合點在抬起的瞬間先往後 7–8 cm（z 0.14 → 0.06），之後才往右前方。
        /// </summary>
        public float CardLookCommitHeight = 0.25f;
        /// <summary>拿著牌時追蹤遺失超過這麼久，牌放回原位（短暫遺失時牌停在最後的位置）。</summary>
        public float CardHoldLostTimeout = 0.6f;
        /// <summary>
        /// 拿著牌往前放開＝棄牌（看過牌也算）：捏合點離桌 ≤ CardFoldReleaseMaxHeight，而且越過棄牌線（TableLayout.FoldLineZ），
        /// 或在自己的牌前緣之前、往前速度 ≥ CardFoldTossSpeed（丟出去）。放回自己牌位、在臉前高處放開仍是放回。
        /// 2026-10-13 實機 log（pid 21470）：想棄牌的 5 次放開在 z 0.27–0.33、高 6.8–10.2 cm；看完放回的 7 次 z ≤ 0.12。
        /// </summary>
        public float CardFoldReleaseMaxHeight = 0.25f;
        /// <summary>越線放開時，捏合點比捏起處往前至少這麼遠（實機 5 次 16–21 cm；合成雜動「在牌前緣捏起、就近放下」往前 6–9 cm 會越線）。</summary>
        public float CardFoldReleaseMinForward = 0.12f;
        public float CardFoldTossSpeed = 0.3f;

        // ── All-in ──
        /// <summary>【已停用，保留欄位相容】舊版要求每隻手離籌碼堆中心在這個距離內（夾住籌碼堆）。
        /// 2026-10-12 實機 log：真人推 All-in 時雙手在自己前方（例：左 x −0.17、右 +0.13；或從桌緣 x 0.12／0.33 往中間推），不會夾住籌碼堆。</summary>
        public float AllInStackRadius = 0.18f;
        /// <summary>All-in 準備時雙手要在玩家前方這個 x 範圍內（實機起點 x −0.20～+0.36）。</summary>
        public float AllInZoneMinX = -0.45f;
        public float AllInZoneMaxX = 0.50f;
        /// <summary>準備時雙手要在下注線後方至少這個距離。</summary>
        public float AllInStartBehindBetLine = 0.02f;
        /// <summary>兩手距離的範圍（太近會互相遮擋，研究建議 > 15 cm；這裡用手掌中心距離）。
        /// 實機起推時 0.19–0.32（看不清意圖的兩段到 0.46–0.47），推的途中往中間收攏到 0.11–0.25。
        /// 下限 2026-10-12 第二次實機後由 0.10 降到 0.06：log 裡 0.01–0.10 的拒絕都是雙手在桌緣後方（z −0.14～+0.02）靠在一起休息，
        /// 不是 All-in 嘗試；但降低後合成統計與全部錄製的誤觸不變，推的途中雙手收攏也比較不會被取消。</summary>
        public float AllInMinHandGap = 0.06f;
        public float AllInMaxHandGap = 0.48f;
        /// <summary>預備時的手掌高度上限。實機「往後收再往前推」起推時手掌 4.5–11 cm（舊值 0.09 在收手時就掉出預備）。</summary>
        public float AllInArmMaxHeight = 0.12f;
        /// <summary>推的途中手掌高度上限。真實錄製雙手推時手掌最高 5.0–10.7 cm（v1）、8.1–14 cm（v2，看得到籌碼堆）。
        /// 就位時仍用 AllInArmMaxHeight：雙手懸在籌碼堆兩側 9–10 cm 的雜動不該進入預備。</summary>
        public float AllInMaxHeight = 0.14f;
        /// <summary>雙手就位要維持這麼久才進入預備。</summary>
        public float AllInArmTime = 0.15f;
        /// <summary>雙手在最近 AllInPushWindow 內都往前超過這個距離（或已越線）才發 AllInPushStarted。
        /// 2026-10-12 錄製：21 段 All-in 雙手 0.5 s 內同時往前 0.198–0.361 m；其他 240 段（含 16 段 idle：雙手放在自己前方休息）最多 0.067 m。
        /// 舊版從「進入預備」累計（0.04），準備條件放寬到「雙手在前方貼桌」後，休息時的漂移就會累積成起推。</summary>
        public float AllInStartDistance = 0.08f;
        /// <summary>雙手都往前超過這個距離就開始「默默追蹤」這次推（不發事件、不擋其他手勢）；之後推到 AllInStartDistance 才發 AllInPushStarted，
        /// 越線且推夠 AllInMinPushDistance 就直接進倒數。2026-10-12 第二次實機：開發者要 All-in 更好觸發；1 cm 追蹤漂移下，
        /// 只推 8.5–9 cm 的雙手常量不到 8 cm。直接把 AllInStartDistance 降到 0.06 會讓 2 段 idle 錄製發出 AllInPushStarted，所以分成兩段。</summary>
        public float AllInTrackDistance = 0.05f;
        /// <summary>量起推距離的時間窗（實機推 0.3–0.7 s；舊值 0.5 s，放寬讓 0.8 s 的慢推也量得到。HandTrack 歷史 64 幀，72 Hz 約 0.89 s）。</summary>
        public float AllInPushWindow = 0.7f;
        /// <summary>推牌途中另一隻手也貼桌往前這麼遠，就當成雙手推（All-in），不算推牌。</summary>
        public float FoldPushOtherHandMaxForward = 0.04f;
        /// <summary>越線時雙手都要從起點往前至少這麼遠（越線看手掌或中指指尖，起點就在下注線附近時不能一碰就算）。
        /// 實機 All-in 雙手各推 21–30 cm；2026-10-12 第二次實機後放寬（舊值 0.12）：起點靠近下注線時推 8 cm、指尖過線就算。</summary>
        public float AllInMinPushDistance = 0.08f;
        /// <summary>兩手往前距離差上限（同步）。真實錄製 0.012–0.080 m（舊值 0.07 擋掉 1/14）。</summary>
        public float AllInMaxDesync = 0.10f;
        /// <summary>從起推（開始默默追蹤）到推過下注線的最長時間（實機 0.3–0.7 s；舊值 1.5，追蹤改從 5 cm 開始後放寬到 2.0）。</summary>
        public float AllInMaxPushTime = 2.0f;
        /// <summary>推過線（或推夠 AllInConfirmPushDistance）後的倒數。spec 原為 1 秒；2026-10-13 實機回報 All-in 不靈敏
        /// （推到線還要再等 1 秒），縮短到 0.5 s。倒數期間雙手貼桌拉回 AllInRetractDistance 仍可取消。</summary>
        public float AllInConfirmDelay = 0.5f;
        /// <summary>不必越線：雙手從起推點都往前推超過這個距離（而且同步，見 AllInMaxDesync）也開始倒數。
        /// 2026-10-13 實機 log 14 次 All-in：準備時手掌 z 0.01–0.19（多在 0.07 上下），要推 0.11–0.33 m 指尖才過下注線。
        /// 單手推牌、合成日常雜動都不會兩手同時推這麼遠。目標是「各推 12 cm 就算」，1 cm 追蹤雜訊下門檻取 0.11：
        /// 合成「桌緣推 12–15 cm」0.12 時 93.5%、0.11 時 98.5%；0.10 時「兩手先後滑牌與籌碼」開始有 2/200 成為 All-in。</summary>
        public float AllInConfirmPushDistance = 0.11f;
        /// <summary>推夠 AllInConfirmPushDistance、或以推的距離（AllInStartDistance）發出 AllInPushStarted，都看最近這段時間內雙手各自的位移：
        /// 休息時雙手慢慢一起往前漂（12 cm／2 s，任 0.7 s 最多約 6 cm）、兩手先後各滑各的都不算。實機推 0.3–0.7 s。
        /// 越線的路徑不受此限（只受 AllInMaxPushTime）。</summary>
        public float AllInConfirmPushTime = 0.7f;
        /// <summary>「推夠遠」還要雙手在最近 AllInSyncWindow 內都往前至少 AllInSyncDistance（兩手同時在推）：
        /// 合成「左手滑牌 11 cm 停住、0.4 s 後右手捏籌碼滑 14 cm」在 0.7 s 窗內兩手都推了 12 cm 以上，靠這條排除。</summary>
        public float AllInSyncWindow = 0.35f;
        public float AllInSyncDistance = 0.04f;
        /// <summary>雙手在 All-in 準備姿勢時，手上不小心捏起的籌碼 ≤ 這個枚數、沒有抬起（捏合點離桌 ≤ AllInDropChipsMaxHeight）、
        /// 還在籌碼堆旁（沒鎖定數量），就不擋 All-in；雙手一起起推時放回籌碼堆（ChipsReleased、不在下注區）。
        /// 2026-10-13 實機 log 2812.575：雙手放到籌碼堆旁時不小心捏起 1 枚，All-in 被「holding chips」擋住。</summary>
        public float AllInDropChipsMax = 2f;
        public float AllInDropChipsMaxHeight = 0.06f;
        /// <summary>倒數期間雙手平均往回收超過這個距離就取消。</summary>
        public float AllInRetractDistance = 0.08f;
        /// <summary>倒數期間雙手一直貼桌（手掌 ≤ 這個高度）收回才算撤銷；任一手抬高就當作推完收手，之後不再撤銷。
        /// allin_v2：推過線 0.2–0.3 s 後使用者就把手抬起收回（收回 8 cm 時手掌 8.8–12.9 cm 高），再放回自己面前的桌上。</summary>
        public float AllInRetractMaxHeight = 0.05f;
        /// <summary>推的途中追蹤遺失超過這麼久就取消（倒數期間遺失則暫停倒數）。</summary>
        public float AllInLostTimeout = 0.3f;
        /// <summary>倒數期間因追蹤不可靠累計暫停超過這麼久就取消，避免狀態卡住。</summary>
        public float AllInMaxPause = 2.0f;

        // ── 攤牌翻牌 ──
        /// <summary>手在牌矩形外多少範圍內仍算在牌上方。</summary>
        public float FlipCardMargin = 0.06f;
        /// <summary>翻牌時手掌離桌高度上限。</summary>
        public float FlipMaxHeight = 0.15f;
        /// <summary>掌心法線與 +Y 的夾角小於這個值算掌心朝上。</summary>
        public float PalmUpMaxAngle = 50f;
        /// <summary>掌心朝上那一刻，手掌或中指指尖離牌矩形的範圍：翻牌時手掌會被帶離牌 8–13 cm（真實錄製）。</summary>
        public float FlipUpMargin = 0.12f;
        /// <summary>從掌心朝下（≥135°）轉到朝上（≤50°）的最長時間。慢慢轉手不算翻牌。</summary>
        public float FlipMaxTime = 0.5f;
        /// <summary>
        /// 攤牌時拿著牌在自己牌位前方放開＝亮牌：捏合點 z ≥ 牌前緣＋這個距離（不限高度、不必越線）。
        /// 2026-10-13 s8.log：教學翻牌那一步 4 次往前丟到中間，放開在 z 0.31–0.33；看完放回原位的放開 z ≤ 0.15。
        /// </summary>
        public float ShowdownRevealPastCardsMargin = 0.02f;
        /// <summary>同上，捏合點還要比捏起處往前至少這麼遠：s8.log 4 次往前 18–21 cm；日常雜動「拿起來、就近放下」往前 ≤ 8 cm（RobustnessStatsTests.Fidget）。</summary>
        public float ShowdownRevealMinForward = 0.10f;
    }
}
