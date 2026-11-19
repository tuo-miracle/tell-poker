using System;
using System.Collections.Generic;
using System.Numerics;

namespace Poker.Gestures
{
    /// <summary>
    /// 撲克手勢辨識：每幀餵一個 HandsFrame（牌桌座標）與牌桌狀態，回傳這一幀產生的事件。
    /// 架構：特徵層（HandTrack）→ 各手勢的小狀態機 → 閘門（輪到誰、追蹤品質、同時只允許一個行動）。
    /// 純計算、不配置記憶體（事件串列重用），每幀成本是幾十個向量運算。
    /// </summary>
    public sealed class GestureRecognizer
    {
        public readonly TableLayout Layout;
        public readonly GestureTuning Tuning;

        readonly List<GestureEvent> _events = new List<GestureEvent>();
        readonly HandTrack[] _h = { new HandTrack(), new HandTrack() };
        double _lastAction = double.NegativeInfinity;
        double _time;
        bool _started;
        float _dt;
        TableContext _ctx;

        // 咪牌
        readonly float[] _peekOver = new float[2], _peek = new float[2], _peekSent = new float[2];
        readonly bool[] _peekArmed = new bool[2];
        // 診斷：這次咪牌是否已開始（量 ≥ PeekTraceOn）與最大量
        readonly bool[] _peekOn = new bool[2];
        readonly float[] _peekMax = new float[2];
        const float PeekTraceOn = 0.05f;
        // 診斷：「差一點」輸出的節流（每隻手、每個原因上次輸出的時間；原因都是常數字串）
        readonly Dictionary<string, double>[] _missAt = { new Dictionary<string, double>(), new Dictionary<string, double>() };
        // 籌碼與下注
        readonly bool[] _holding = new bool[2], _locked = new bool[2];
        readonly int[] _held = new int[2];
        readonly float[] _pickBaseY = new float[2];
        // 這次捏合開始時捏合點是否就在籌碼旁（允許稍晚移進範圍時拿起）
        readonly bool[] _pinchStartNear = new bool[2];
        // 捏起時的基準點（固定籌碼堆中心，或捏到的那柱籌碼）：離開它太遠就鎖定數量
        readonly Vector3[] _pickAnchor = new Vector3[2];
        // 遊戲層餵進來的玩家籌碼柱位置
        readonly List<Vector3> _chipSpots = new List<Vector3>();
        float _chipSpotRadius;
        int _pending;
        float _pendingTimer;
        // 這一注各次落點與籌碼畫出來的位置（撈回要在其中之一附近捏起）
        const int MaxDrops = 8;
        readonly Vector3[] _drops = new Vector3[MaxDrops];
        readonly Vector3[] _dropsShown = new Vector3[MaxDrops];
        int _dropCount;
        // 敲桌：手掌頻道與手指頻道
        readonly KnockDetector[] _knockPalm = { new KnockDetector(), new KnockDetector() };
        readonly KnockDetector[] _knockFinger = { new KnockDetector(), new KnockDetector() };
        // 推牌
        readonly bool[] _pushActive = new bool[2], _pushClaimed = new bool[2], _pushLifted = new bool[2];
        readonly Vector3[] _pushStart = new Vector3[2];
        // 推牌起點之後，另一隻手手掌的最小 z（判斷是不是雙手一起往前推＝All-in）
        readonly float[] _pushOtherMinZ = new float[2];
        readonly double[] _pushStartTime = new double[2], _pushLiftTime = new double[2];
        // All-in
        enum AllInPhase { Idle, Armed, Pushing, Countdown }
        AllInPhase _ai;
        float _aiArm, _aiStartZL, _aiStartZR, _aiCountdown, _aiPeakMean, _aiLost, _aiPaused;
        bool _aiWithdrawn;
        // 推的途中是否已發出 AllInPushStarted（還沒發＝默默追蹤，不擋其他手勢，取消也不發事件）
        bool _aiAnnounced;
        // 還沒輪到（CanActEarly）時照樣追蹤雙手推：不發 All-in 事件，到了該發 AllInPushStarted 時改發 ActionTooEarly，
        // 之後照樣擋推牌（從牌上出發的那隻手不會因此變成提前棄牌）
        bool _aiEarly;
        double _aiPushStart;
        // 這次符合準備條件的起始時間（量「最近一段時間內推了多遠」時，時間窗不往前超過它）
        double _aiEligibleSince;
        // 拿起底牌看：捏住（還沒抬起）、已拿起（牌跟著手）、已進入看牌（拿牌棄牌路徑關閉）
        readonly bool[] _cardGrab = new bool[2], _cardLifted = new bool[2], _cardLook = new bool[2], _cardPinchNear = new bool[2];
        readonly double[] _cardGrabAt = new double[2], _cardLiftAt = new double[2];
        readonly Vector3[] _cardGrip = new Vector3[2], _cardGrabPos = new Vector3[2];
        readonly Quaternion[] _cardRot = { Quaternion.Identity, Quaternion.Identity };
        // 翻牌
        readonly double[] _lastDownOverCards = { double.NegativeInfinity, double.NegativeInfinity };

        enum Gesture { Bet, Knock, FoldPush, AllIn, Flip }

        /// <summary>診斷輸出（預設 null＝關閉、不配置記憶體）：每次下敲的數值與判定、翻牌與推牌的關鍵狀態。調參與分析錄製用，不要在遊戲每幀開著。</summary>
        public Action<string> Trace;

        public GestureRecognizer(TableLayout layout = null, GestureTuning tuning = null)
        {
            Layout = layout ?? new TableLayout();
            Tuning = tuning ?? new GestureTuning();
        }

        // ── 給呈現層的即時狀態 ──
        public float PeekAmount(Hand h) => _peek[(int)h];
        public int HeldChips(Hand h) => _holding[(int)h] ? _held[(int)h] : 0;
        public int PendingBetChips => _pending;
        /// <summary>玩家正拿著自己的底牌（CardsLifted 之後、CardsReturned 之前）：哪隻手、捏合點與手掌旋轉（牌桌座標）。</summary>
        public bool HoldingCards(out Hand hand, out Vector3 grip, out Quaternion rotation)
        {
            for (int i = 0; i < 2; i++)
                if (_cardLifted[i])
                {
                    hand = (Hand)i; grip = _cardGrip[i]; rotation = _cardRot[i];
                    return true;
                }
            hand = Hand.Right; grip = default; rotation = Quaternion.Identity;
            return false;
        }

        /// <summary>All-in 倒數剩餘秒數；不在倒數中回傳 -1。</summary>
        public float AllInCountdown => _ai == AllInPhase.Countdown ? _aiCountdown : -1f;

        /// <summary>
        /// 玩家自己桌上籌碼的位置（每柱的底部中心，牌桌座標）與籌碼半徑。遊戲層在籌碼堆變動時（或每幀）呼叫；
        /// 捏合點在任何一柱 chipRadius＋ChipFootprintMargin 內也能拿籌碼（另外固定的籌碼堆範圍照舊有效）。
        /// 沒餵過（錄製回放）就只有固定範圍。只給玩家自己的籌碼堆：AI 籌碼、底池、已下注的籌碼都不要放進來。
        /// </summary>
        public void SetPlayerChipFootprint(IReadOnlyList<Vector3> chipCenters, float chipRadius)
        {
            _chipSpots.Clear();
            if (chipCenters != null)
                for (int c = 0; c < chipCenters.Count; c++) _chipSpots.Add(chipCenters[c]);
            _chipSpotRadius = Math.Max(0f, chipRadius);
        }

        /// <summary>清掉籌碼位置，回到只有固定籌碼堆範圍。</summary>
        public void ClearPlayerChipFootprint() => _chipSpots.Clear();

        /// <summary>清掉所有手勢狀態（遊戲層在一手牌結束、玩家超時時呼叫；待確認的下注直接作廢，不發事件）。</summary>
        public void Reset()
        {
            for (int i = 0; i < 2; i++)
            {
                _h[i] = new HandTrack();
                _peekOver[i] = _peek[i] = _peekSent[i] = 0;
                _peekArmed[i] = false;
                _peekOn[i] = false;
                _peekMax[i] = 0;
                _holding[i] = _locked[i] = false;
                _held[i] = 0;
                ResetKnock(i);
                ResetPush(i);
                _lastDownOverCards[i] = double.NegativeInfinity;
                ClearCards(i);
            }
            _pending = 0;
            _pendingTimer = 0;
            _dropCount = 0;
            _ai = AllInPhase.Idle;
            _aiArm = 0;
            _aiEarly = false;
            _lastAction = double.NegativeInfinity;
        }

        /// <summary>回傳的串列會在下一次 Update 重用，呼叫端要在那之前處理完。</summary>
        public IReadOnlyList<GestureEvent> Update(HandsFrame frame, in TableContext ctx)
        {
            _events.Clear();
            _dt = _started ? (float)Math.Max(0, Math.Min(0.25, frame.Time - _time)) : 0f;
            _time = frame.Time;
            _started = true;
            _ctx = ctx;

            _h[0].Update(frame.Left, _time, Tuning);
            _h[1].Update(frame.Right, _time, Tuning);

            UpdateBetTimer();
            for (int i = 0; i < 2; i++)
            {
                UpdatePeek(i);
                UpdateChips(i);
            }
            UpdateAllIn();
            for (int i = 0; i < 2; i++)
            {
                UpdateCards(i);
                UpdateKnock(i);
                UpdateFoldPush(i);
                UpdateFlip(i);
            }
            return _events;
        }

        void Emit(GestureKind kind, int hand = 1, int count = 0, float amount = 0f, Vector3 pos = default, bool inZone = false, Quaternion rotation = default)
        {
            var e = new GestureEvent(kind, _time, (Hand)hand, count, amount, pos, inZone, rotation);
            if (e.IsAction) _lastAction = _time;
            _events.Add(e);
        }

        float Height(Vector3 p) => p.Y - Layout.TableY;

        static float HorizDist(Vector3 a, Vector3 b)
        {
            float dx = a.X - b.X, dz = a.Z - b.Z;
            return (float)Math.Sqrt(dx * dx + dz * dz);
        }

        bool BetBusy => _holding[0] || _holding[1] || _pending > 0;
        bool AllInBusy => (_ai == AllInPhase.Pushing && _aiAnnounced) || _ai == AllInPhase.Countdown;

        /// <summary>行動閘門：輪到玩家、手上沒有遊戲層認定的籌碼、而且沒有其他行動手勢正在進行。
        /// 還沒輪到但還在這一手（CanActEarly）：棄牌照樣辨識（遊戲層記下），籌碼可以拿（丟進下注區不算下注，見 Release）。</summary>
        bool CanAct(Gesture g, int hand)
        {
            if (!_ctx.IsPlayerTurn && !(_ctx.CanActEarly && (g == Gesture.FoldPush || g == Gesture.Bet || g == Gesture.AllIn))) return false;
            if (g != Gesture.Bet && _ctx.ChipsInHand > 0) return false;
            // All-in：準備時不小心捏起的少量籌碼不擋（推夠遠時放回籌碼堆，見 DropIncidentalChips）
            if (g == Gesture.AllIn ? HoldingRealChips(0) || HoldingRealChips(1) || _pending > 0 : g != Gesture.Bet && BetBusy) return false;
            // 剛發過行動事件（含同一幀）：短暫冷卻，避免兩個行動疊在一起
            if (_time - _lastAction < Tuning.ActionCooldown) return false;
            if (g != Gesture.AllIn && AllInBusy) return false;
            // All-in 真的在推（AllInBusy，上面已處理）才擋推牌。2026-10-12 起 All-in 的準備條件放寬成「雙手在玩家前方貼桌」，
            // 一隻手放在桌上休息、另一隻手推牌時也符合，若再以「符合準備條件」擋推牌，推牌就幾乎永遠不會觸發。
            // 推牌已佔住：擋另一隻手的手勢；同一隻手完成的其他手勢（敲兩下、翻牌）優先，推牌本身不含這些動作。
            // 真實錄製：手落到牌旁時往前漂 3 cm、翻牌時手掌往前帶，都會先佔住推牌而擋掉真正的手勢。
            // All-in 不受推牌佔住影響：雙手一起往前推時，從牌上出發的那隻手會先佔住推牌（實機 log 48.8、92.9 s），
            // 另一隻手跟上就是 All-in，All-in 開始時會清掉推牌。
            for (int i = 0; i < 2; i++)
                if (_pushClaimed[i] && i != hand && g != Gesture.AllIn) return false;
            return true;
        }

        // ───────────── 咪牌 ─────────────
        void UpdatePeek(int i)
        {
            var h = _h[i];
            var k = Tuning;
            if (_cardLifted[i])
            {
                // 牌拿在手上：不是咪牌
                _peekOver[i] = 0;
                _peekArmed[i] = false;
                SetPeek(i, 0f);
                return;
            }
            if (!h.Ok)
            {
                // 短暫遺失時維持牌角，太久才放平
                if (_time - h.LostAt > k.PeekLostTimeout) SetPeek(i, 0f);
                _peekOver[i] = 0;
                return;
            }
            // 手掌或指尖在牌上、指尖壓低；進入要掌心朝下。進入後抬手（只抬手腕，或整隻手抬起）時掌心角度會掉、指尖會離開牌一點，
            // 只要不低於 PeekHoldMinAngle、指尖不高於 PeekHoldMaxTipHeight 就維持
            var cards = Layout.CardsRect;
            bool armed = _peekArmed[i];
            float tipH = h.LowTipY - Layout.TableY;
            bool inRect = cards.Contains(h.Palm, k.PeekHoverMargin) || cards.Contains(h.MiddleTip, k.PeekHoverMargin);
            bool palmLow = Height(h.Palm) <= k.PeekMaxPalmHeight;
            bool tipLow = tipH <= (armed ? Math.Max(k.PeekMaxTipHeight, k.PeekHoldMaxTipHeight) : k.PeekMaxTipHeight);
            // 進入後翻腕把牌邊掀起來看（掌心轉到朝上）也算咪牌，見 PeekRollZeroAngle；沒先掌心朝下在牌上就位的手不算
            bool angleOk = h.PalmAngle >= k.PalmDownMinAngle || (armed && (h.PalmAngle >= k.PeekHoldMinAngle || (k.PeekRollFullAngle < k.PeekRollZeroAngle && h.PalmAngle >= k.PeekRollMinAngle)));
            // 咪牌量看手腕高出指尖多少（指尖壓牌、手腕抬起；整隻手抬起時指尖留在牌上也一樣）；手只是懸在牌上方時兩者一樣高
            float lift = h.Wrist.Y - h.LowTipY;
            if (!(inRect && palmLow && tipLow && angleOk))
            {
                if (Trace != null)
                    TracePeekMiss(i, !inRect ? null : !palmLow ? "palm too high" : !tipLow ? "fingertips too high" : armed ? "palm turned too far" : "palm not down", lift, tipH);
                _peekOver[i] = 0;
                _peekArmed[i] = false;
                SetPeek(i, 0f);
                return;
            }
            _peekOver[i] += _dt;
            if (_peekOver[i] < k.PeekArmTime) return;
            _peekArmed[i] = true;
            float target = Clamp01((lift - k.PeekLiftMin) / (k.PeekLiftMax - k.PeekLiftMin));
            // 翻腕掀牌時手是抬起來的（實機手掌 4.6–7.4 cm）；手掌朝上平放在桌上休息（idle 錄製 1–2 cm）不算
            if (k.PeekRollFullAngle < k.PeekRollZeroAngle && Height(h.Palm) >= k.PeekRollMinPalmHeight)
                target = Math.Max(target, Clamp01((k.PeekRollZeroAngle - h.PalmAngle) / (k.PeekRollZeroAngle - k.PeekRollFullAngle)));
            float a = k.PeekSmoothTime > 0 ? 1f - (float)Math.Exp(-_dt / k.PeekSmoothTime) : 1f;
            float v = _peek[i] + (target - _peek[i]) * a;
            if (Math.Abs(target - v) < 0.005f) v = target;
            SetPeek(i, v);
            // 整隻手抬離桌面、手腕卻沒比指尖高：掀不起來（只在手真的抬起時說，手平放在牌上休息不輸出）
            if (Trace != null && v < PeekTraceOn && Height(h.Palm) > 0.05f)
                TracePeekMiss(i, "wrist not above fingertips", lift, tipH);
        }

        /// <summary>手在自己的牌附近（範圍再放寬 4 cm）但沒咪牌時，說出卡在哪個條件；每隻手每個原因最多每 NearMissTraceInterval 一次。</summary>
        void TracePeekMiss(int i, string reason, float lift, float tipH)
        {
            if (_peek[i] >= PeekTraceOn) return;
            var h = _h[i];
            var k = Tuning;
            var cards = Layout.CardsRect;
            float margin = k.PeekHoverMargin + 0.04f;
            if (!(cards.Contains(h.Palm, margin) || cards.Contains(h.MiddleTip, margin)) || Height(h.Palm) > 0.20f) return;
            // 手在牌旁邊、或平平地懸在牌上方（太高）：只在手腕有抬起（像在咪牌）或已在咪牌時才說，手經過、休息、推牌時不輸出
            bool quiet = reason == null || reason == "palm too high" || reason == "fingertips too high";
            if (quiet && lift < k.PeekLiftMin && !_peekArmed[i]) return;
            // 牌旁邊：還要像咪牌的姿勢（手腕明顯抬起 1.5 cm 以上、掌心大致朝下、手不高）；休息時手腕常比指尖高 3 cm 左右
            if (reason == null && (lift < k.PeekLiftMin + 0.015f || h.PalmAngle < k.PeekHoldMinAngle || Height(h.Palm) > k.PeekMaxPalmHeight)) return;
            if (reason == null) reason = "hand beside the cards";
            if (!MissDue(i, reason)) return;
            float ny = (float)Math.Cos(h.PalmAngle * Math.PI / 180.0);
            Trace($"{_time:0.000} peek near-miss {(Hand)i}: palm h {Height(h.Palm):0.000} (max {k.PeekMaxPalmHeight:0.000}), tip h {tipH:0.000} " +
                  $"(max {(_peekArmed[i] ? Math.Max(k.PeekMaxTipHeight, k.PeekHoldMaxTipHeight) : k.PeekMaxTipHeight):0.000}), wrist-tip {lift:0.000} " +
                  $"(0 at {k.PeekLiftMin:0.000}, 1 at {k.PeekLiftMax:0.000}), angle {h.PalmAngle:0} (down ≥{k.PalmDownMinAngle:0}, hold ≥{k.PeekHoldMinAngle:0}), " +
                  $"normal.y {ny:0.00}, to cards center {HorizDist(h.Palm, Layout.CardsCenter):0.000} (tip {HorizDist(h.MiddleTip, Layout.CardsCenter):0.000}), " +
                  $"armed {_peekArmed[i]}, reason {reason}");
        }

        /// <summary>節流：這隻手這個原因距上次輸出已超過 NearMissTraceInterval。</summary>
        bool MissDue(int i, string reason)
        {
            var d = _missAt[i];
            if (d.TryGetValue(reason, out var last) && _time - last < Tuning.NearMissTraceInterval) return false;
            d[reason] = _time;
            return true;
        }

        void SetPeek(int i, float v)
        {
            _peek[i] = v;
            if (Trace != null) TracePeekStartEnd(i, v);
            float sent = _peekSent[i];
            if (Math.Abs(v - sent) >= Tuning.PeekEmitStep || (v != sent && (v == 0f || v == 1f)))
            {
                _peekSent[i] = v;
                Emit(GestureKind.PeekChanged, i, amount: v);
            }
        }

        void TracePeekStartEnd(int i, float v)
        {
            var h = _h[i];
            if (!_peekOn[i] && v >= PeekTraceOn)
            {
                _peekOn[i] = true;
                _peekMax[i] = v;
                Trace($"{_time:0.000} peek start {(Hand)i}: palm h {Height(h.Palm):0.000}, wrist-tip {h.Wrist.Y - h.LowTipY:0.000}, angle {h.PalmAngle:0}");
            }
            else if (_peekOn[i])
            {
                _peekMax[i] = Math.Max(_peekMax[i], v);
                if (v == 0f)
                {
                    _peekOn[i] = false;
                    Trace($"{_time:0.000} peek end {(Hand)i}: max {_peekMax[i]:0.00}");
                }
            }
        }

        static float Clamp01(float v) => v < 0 ? 0 : v > 1 ? 1 : v;

        // ───────────── 籌碼與下注 ─────────────
        int ChipCap(int i) => _ctx.StackChipCount - _pending - (_holding[1 - i] ? _held[1 - i] : 0);

        void UpdateChips(int i)
        {
            var h = _h[i];
            if (!h.Ok) return; // 追蹤遺失不算放開：捏合狀態凍結
            // 開始捏的那一幀，或開始捏時就在籌碼旁（範圍外 ChipLatePickMargin 內）、之後 ChipLatePickWindow 內捏合點收攏移進範圍。
            // 在別處就捏著的手（推牌時手指彎曲、拿著別的東西）經過籌碼堆不算
            if (h.PinchStarted) _pinchStartNear[i] = NearChipsForLatePick(h.PinchPoint);
            if (!_holding[i] && h.Pinching && (h.PinchStarted || (_pinchStartNear[i] && _time - h.PinchStartedAt <= Tuning.ChipLatePickWindow)))
            {
                string why = TryPick(i);
                if (why != null && Trace != null) TraceChipMiss(i, why);
            }
            else if (_holding[i] && h.PinchEnded) Release(i);
            else if (_holding[i] && !_locked[i]) UpdateCount(i);
            else if (!_holding[i] && Trace != null)
                TraceChipMiss(i, h.Pinching ? "pinch began away from the chips" : "pinch below start");
        }

        bool NearChipsForLatePick(Vector3 pp)
        {
            var k = Tuning;
            if (Height(pp) > k.ChipPickMaxHeight) return false;
            if (HorizDist(pp, Layout.ChipStackCenter) <= k.ChipPickRadius + k.ChipLatePickMargin) return true;
            if (Layout.BetZone.Contains(pp)) return false;
            float limit = _chipSpotRadius + k.ChipFootprintMargin + k.ChipLatePickMargin;
            for (int c = 0; c < _chipSpots.Count; c++)
                if (!Layout.BetZone.Contains(_chipSpots[c]) && HorizDist(pp, _chipSpots[c]) <= limit) return true;
            return false;
        }

        /// <summary>試著捏起籌碼；成功（或撈回）回傳 null，沒拿到回傳原因（常數字串，診斷用）。</summary>
        string TryPick(int i)
        {
            var h = _h[i];
            var k = Tuning;
            var pp = h.PinchPoint;
            // 撈回：在下注區重新捏起（不是輪到玩家也允許）
            if (_pending > 0 && NearDrop(pp) && Height(pp) <= k.RetractMaxHeight && !AllInBusy)
            {
                Emit(GestureKind.BetRetracted, i, _pending);
                _holding[i] = true;
                _locked[i] = true;
                _held[i] = _pending;
                _pending = 0;
                _pendingTimer = 0;
                _dropCount = 0;
                return null;
            }
            if (Height(pp) > k.ChipPickMaxHeight) return "pinch too high";
            Vector3 anchor;
            if (HorizDist(pp, Layout.ChipStackCenter) <= k.ChipPickRadius) anchor = Layout.ChipStackCenter;
            else if (!NearPlayerChip(pp, out anchor)) return "outside chip footprint";
            string blocked = BetBlockedReason(i);
            if (blocked != null) return blocked;
            _pickAnchor[i] = anchor;
            _holding[i] = true;
            _locked[i] = false;
            _held[i] = 1;
            _pickBaseY[i] = pp.Y;
            Emit(GestureKind.ChipsPicked, i, 1);
            return null;
        }

        /// <summary>下注（拿籌碼）被閘門擋住的原因；可以拿回傳 null。與 CanAct(Gesture.Bet) 同一套規則，另加「沒有籌碼」。</summary>
        string BetBlockedReason(int i)
        {
            if (!CanAct(Gesture.Bet, i))
            {
                if (!_ctx.IsPlayerTurn) return "not player's turn";
                if (_time - _lastAction < Tuning.ActionCooldown) return "action cooldown";
                if (AllInBusy) return "all-in in progress";
                return "fold push in progress";
            }
            return ChipCap(i) < 1 ? "no chips left" : null;
        }

        /// <summary>
        /// 手在籌碼附近（固定籌碼堆範圍或任何一柱外再 3 cm）、手指有捏的樣子（強度 ≥ 0.25、指尖距 ≤ 4 cm 或布林值）卻沒拿起時，
        /// 說出數值與原因；每隻手每個原因最多每 NearMissTraceInterval 一次。
        /// </summary>
        void TraceChipMiss(int i, string reason)
        {
            var h = _h[i];
            var k = Tuning;
            if (!(h.PinchFlag || h.PinchStrength >= 0.25f || h.ThumbIndexGap <= 0.04f)) return;
            var pp = h.PinchPoint;
            const float near = 0.03f;
            float dZone = HorizDist(pp, Layout.ChipStackCenter);
            float dCol = float.MaxValue;
            for (int c = 0; c < _chipSpots.Count; c++)
                if (!Layout.BetZone.Contains(_chipSpots[c])) dCol = Math.Min(dCol, HorizDist(pp, _chipSpots[c]));
            float colLimit = _chipSpotRadius + k.ChipFootprintMargin;
            if (dZone > k.ChipPickRadius + near && dCol > colLimit + near) return;
            if (Height(pp) > k.ChipPickMaxHeight + 0.05f) return;
            if (!MissDue(i, reason)) return;
            string col = _chipSpots.Count == 0 ? "none" : $"{dCol:0.000} (limit {colLimit:0.000})";
            Trace($"{_time:0.000} chip near-miss {(Hand)i}: strength {h.PinchStrength:0.00} (start ≥{k.PinchStart:0.00} or ≥{k.PinchAssistStrength:0.00} with gap ≤{k.PinchAssistGap:0.000}), " +
                  $"tip gap {h.ThumbIndexGap:0.000} (touch ≤{k.PinchTouchGap:0.000}), flag {h.PinchFlag}, pinching {h.Pinching}, " +
                  $"nearest column {col}, zone {dZone:0.000} (limit {k.ChipPickRadius:0.000}), height {Height(pp):0.000} (max {k.ChipPickMaxHeight:0.000}), " +
                  $"reason {reason}" + (h.Pinching && !h.PinchStarted ? $" (pinch started {_time - h.PinchStartedAt:0.00} s ago)" : ""));
        }

        /// <summary>捏合點是否在玩家某一柱籌碼上（水平距離 ≤ 籌碼半徑＋ChipFootprintMargin）；回傳最近的那柱。下注區裡的一律不算。</summary>
        bool NearPlayerChip(Vector3 p, out Vector3 spot)
        {
            spot = default;
            if (_chipSpots.Count == 0 || Layout.BetZone.Contains(p)) return false;
            float best = _chipSpotRadius + Tuning.ChipFootprintMargin;
            bool found = false;
            for (int c = 0; c < _chipSpots.Count; c++)
            {
                if (Layout.BetZone.Contains(_chipSpots[c])) continue;
                float d = HorizDist(p, _chipSpots[c]);
                if (d <= best)
                {
                    best = d;
                    spot = _chipSpots[c];
                    found = true;
                }
            }
            return found;
        }

        void UpdateCount(int i)
        {
            var k = Tuning;
            var pp = _h[i].PinchPoint;
            if (HorizDist(pp, _pickAnchor[i]) > k.ChipStackRadius + k.ChipLockDistance)
            {
                _locked[i] = true;
                return;
            }
            float rise = pp.Y - _pickBaseY[i];
            // 還是 1 枚時，基準跟著往下修（捏緊的瞬間指尖會再往下一點）
            if (_held[i] == 1 && rise < 0)
            {
                _pickBaseY[i] = pp.Y;
                rise = 0;
            }
            int cap = Math.Max(1, ChipCap(i));
            int c = _held[i];
            while (c < cap && rise >= c * k.ChipStepHeight) c++;
            while (c > 1 && rise < (c - 1) * k.ChipStepHeight - k.ChipStepHysteresis) c--;
            if (c > cap) c = cap;
            if (c != _held[i])
            {
                _held[i] = c;
                Emit(GestureKind.ChipCountChanged, i, c);
            }
        }

        void Release(int i)
        {
            var h = _h[i];
            var v = h.PinchVelocity(_time, Tuning.ReleaseVelocityWindow);
            var landing = Landing(h.PinchPointRaw, v);
            string why = NotBetReason(landing, v, Height(h.PinchPointRaw));
            // 還沒輪到：本來會算下注的也不算（籌碼退回）；還在這一手時另外告訴遊戲層「太早了」
            bool tooEarly = why == null && !_ctx.IsPlayerTurn && _ctx.CanActEarly;
            if (!_ctx.IsPlayerTurn) why = "not player's turn";
            bool inZone = why == null;
            int n = _held[i];
            _holding[i] = false;
            _held[i] = 0;
            Trace?.Invoke($"{_time:0.000} chip release {(Hand)i}: n {n}, at ({h.PinchPointRaw.X:0.000},{Height(h.PinchPointRaw):0.000},{h.PinchPointRaw.Z:0.000}), " +
                          $"v ({v.X:0.00},{v.Y:0.00},{v.Z:0.00}), landing ({landing.X:0.000},{landing.Z:0.000}) → " + (inZone ? "bet" : "not bet: " + why));
            Emit(GestureKind.ChipsReleased, i, n, pos: landing, inZone: inZone);
            if (tooEarly) Emit(GestureKind.ActionTooEarly, i, n, pos: landing);
            if (inZone)
            {
                _pending += n;
                _pendingTimer = 0;
                _drops[_dropCount % MaxDrops] = landing;
                _dropsShown[_dropCount % MaxDrops] = Layout.BetDisplayPoint(landing);
                _dropCount++;
            }
        }

        /// <summary>
        /// 落點算不算下注：在自己的牌與籌碼前方（z ≥ 牌前緣＋BetPastCardsMargin、左右不超出玩家區 BetSideOvershoot）就算，
        /// 丟過頭也算（遊戲把籌碼畫在 BetDisplayPoint）。放回籌碼堆不算；落在下注區前的短區要丟或從上方放下才算。
        /// 算回傳 null，不算回傳原因。棄牌只看牌，籌碼丟進棄牌堆一帶仍是下注。
        /// </summary>
        string NotBetReason(Vector3 p, Vector3 v, float releaseHeight)
        {
            var k = Tuning;
            // 玩家區（TableLayout.PlayerZone，±0.45 m＝舊的小桌寬）：真實尺寸的長圓桌左右很寬，丟到兩端的人面前不算自己的下注
            if (Math.Abs(p.X) > Layout.PlayerZone.MaxX + k.BetSideOvershoot) return "off the side";
            if (p.Z < Layout.CardsRect.MaxZ + k.BetPastCardsMargin) return "not past own cards";
            if (HorizDist(p, Layout.ChipStackCenter) <= k.BetPutBackRadius || NearPlayerChip(p, out _)) return "back on own stack";
            if (p.Z < Layout.BetZone.MinZ - k.BetZoneLandingMargin && v.Z < k.BetShortTossSpeed && releaseHeight < k.BetShortDropHeight)
                return "set down short of the bet zone";
            return null;
        }

        bool NearDrop(Vector3 p)
        {
            for (int d = 0; d < Math.Min(_dropCount, MaxDrops); d++)
                if (HorizDist(p, _drops[d]) <= Tuning.RetractRadius || HorizDist(p, _dropsShown[d]) <= Tuning.RetractRadius) return true;
            return false;
        }

        /// <summary>落點：慢慢放就是放開處正下方；丟出去（水平速度夠大）就用拋物線外推到桌面。</summary>
        Vector3 Landing(Vector3 p, Vector3 v)
        {
            float hs = (float)Math.Sqrt(v.X * v.X + v.Z * v.Z);
            if (hs < Tuning.ThrowMinSpeed) return new Vector3(p.X, Layout.TableY, p.Z);
            const float g = 9.81f;
            float y0 = Math.Max(0f, Height(p));
            float t = (v.Y + (float)Math.Sqrt(v.Y * v.Y + 2 * g * y0)) / g;
            return new Vector3(p.X + v.X * t, Layout.TableY, p.Z + v.Z * t);
        }

        void UpdateBetTimer()
        {
            if (_pending <= 0) return;
            // 手上還拿著籌碼＝這一注還在湊，暫停（多次放下合成一注，只發一次 BetCommitted）
            if (_holding[0] || _holding[1]) return;
            // 有手在下注區上方失去追蹤時暫停（可能正要撈回）
            for (int i = 0; i < 2; i++)
                if (!_h[i].Ok && (Layout.BetZone.Contains(_h[i].LastSeenPinch, 0.05f) || Layout.BetZone.Contains(_h[i].LastSeenPalm, 0.05f)))
                    return;
            _pendingTimer += _dt;
            if (_pendingTimer >= Tuning.RetractWindow)
            {
                Emit(GestureKind.BetCommitted, count: _pending);
                _pending = 0;
                _pendingTimer = 0;
                _dropCount = 0;
            }
        }

        // ───────────── 敲桌過牌 ─────────────
        void ResetKnock(int i)
        {
            _knockPalm[i].Reset();
            _knockFinger[i].Reset();
        }

        KnockParams PalmKnock => new KnockParams
        {
            MinPeak = Math.Min(Tuning.KnockMinPeakSpeed, Tuning.KnockPeakSpeed),
            StrongPeak = Tuning.KnockPeakSpeed,
            QuickGap = Tuning.KnockQuickGap,
            SecondMinPeak = Math.Min(Tuning.KnockSecondMinPeakSpeed, Math.Min(Tuning.KnockMinPeakSpeed, Tuning.KnockPeakSpeed)),
            SoftPairMaxGap = Tuning.KnockSoftPairMaxGap,
            SoftFirstMinDrop = Tuning.KnockSoftFirstMinDrop,
            SoftSecondMinDrop = Tuning.KnockSoftSecondMinDrop,
            EndSpeed = Tuning.KnockStrokeEndSpeed,
            MaxHeight = Tuning.KnockMaxHeight,
            MinDrop = Tuning.KnockMinDrop,
            MinRise = Tuning.KnockMinRise,
            MinGap = Tuning.KnockMinGap,
            Window = Tuning.KnockWindow,
            MaxSpread = Tuning.KnockMaxSpread,
        };

        KnockParams FingerKnock => new KnockParams
        {
            MinPeak = Tuning.FingerTapPeakSpeed,
            StrongPeak = Tuning.FingerTapPeakSpeed,
            QuickGap = 0f,
            SecondMinPeak = Tuning.FingerTapPeakSpeed,
            EndSpeed = Tuning.KnockStrokeEndSpeed,
            MaxHeight = Tuning.FingerTapMaxTipHeight,
            MinDrop = Tuning.FingerTapMinDrop,
            MinRise = Tuning.FingerTapMinRise,
            MinGap = Tuning.KnockMinGap,
            Window = Tuning.FingerTapWindow,
            MaxSpread = Tuning.FingerTapMaxSpread,
        };

        void UpdateKnock(int i)
        {
            var h = _h[i];
            var k = Tuning;
            if (!h.Ok)
            {
                ResetKnock(i);
                return;
            }
            var palmP = PalmKnock;
            bool pair = _knockPalm[i].Update(_time, -h.Vel.Y, h.Palm.Y, Height(h.Palm), h.MaxPalmY(_time, k.KnockDropWindow) - h.Palm.Y,
                h.Palm, true, palmP);
            // 手指敲桌：手掌貼桌不動，看抬得較高的指尖
            bool palmResting = Height(h.Palm) <= k.FingerTapMaxPalmHeight && Math.Abs(h.Vel.Y) <= k.FingerTapMaxPalmSpeed;
            var fingerP = FingerKnock;
            pair |= _knockFinger[i].Update(_time, -h.TipVel.Y, h.TipY, h.TipY - Layout.TableY, h.MaxTipY(_time, k.KnockDropWindow) - h.TipY,
                h.Palm, palmResting, fingerP);
            if (Trace != null)
            {
                TraceKnock("palm", i, _knockPalm[i], palmP);
                TraceKnock("finger", i, _knockFinger[i], fingerP);
            }
            if (!pair) return;
            ResetKnock(i);
            if (_ctx.CanCheck && CanAct(Gesture.Knock, i))
            {
                ResetPush(i);
                Emit(GestureKind.KnockCheck, i);
            }
            else if (!_ctx.IsPlayerTurn && _ctx.CanActEarly && !BetBusy)
            {
                Trace?.Invoke($"{_time:0.000} knock too early {(Hand)i}");
                Emit(GestureKind.ActionTooEarly, i);
            }
        }

        void TraceKnock(string channel, int i, KnockDetector d, in KnockParams p)
        {
            if (d.FirstExpired)
                Trace($"{_time:0.000} knock-{channel} {(Hand)i}: no second stroke within {p.Window:0.00} s (max down between {d.GapMaxDown:0.00} m/s, " +
                      $"start ≥{p.MinPeak:0.00}; rise {d.LastRise:0.000}, min rise {p.MinRise:0.000})");
            if (!d.StrokeEnded) return;
            // 數值後附規則門檻，下次實機 log 可直接對照調參
            Trace($"{_time:0.000} knock-{channel} {(Hand)i}: peak {d.LastPeak:0.00} m/s, height {d.LastHeight:0.000}, drop {d.LastDrop:0.000}" +
                  (d.LastGap > 0 ? $", gap {d.LastGap:0.00} s, rise {d.LastRise:0.000}, spread {d.LastSpread:0.000}" : "") + $" → {d.LastResult}" +
                  $" [start ≥{p.MinPeak:0.00}, strong ≥{p.StrongPeak:0.00} (one of two if gap ≤{p.QuickGap:0.00}), max height {p.MaxHeight:0.000}, min drop {p.MinDrop:0.000}, " +
                  $"min rise {p.MinRise:0.000}, gap {p.MinGap:0.00}-{p.Window:0.00}, max spread {p.MaxSpread:0.000}]");
        }

        // ───────────── 拿起底牌看 ─────────────
        bool CardsActive(int i) => _cardGrab[i] || _cardLifted[i];

        /// <summary>捏合點在自己的牌上（兩張牌的矩形外 margin 內）、而且貼近桌面。</summary>
        bool OnCards(Vector3 p, float margin) => Layout.CardsRect.Contains(p, margin) && Height(p) <= Tuning.CardGrabMaxHeight;

        void ClearCards(int i)
        {
            _cardGrab[i] = _cardLifted[i] = _cardLook[i] = _cardPinchNear[i] = false;
        }

        /// <summary>結束拿牌：已拿起的發 CardsReturned（牌放回原位；棄牌時遊戲層會改成送進棄牌堆）。</summary>
        void EndCardHold(int i)
        {
            if (_cardLifted[i])
            {
                Trace?.Invoke($"{_time:0.000} cards returned {(Hand)i} (grip {_cardGrip[i].X:0.00},{Height(_cardGrip[i]):0.000},{_cardGrip[i].Z:0.00})");
                Emit(GestureKind.CardsReturned, i, pos: _cardGrip[i], rotation: _cardRot[i]);
            }
            ClearCards(i);
        }

        void EndAllCardHolds()
        {
            EndCardHold(0);
            EndCardHold(1);
        }

        void UpdateCards(int i)
        {
            var h = _h[i];
            var k = Tuning;
            if (_ctx.HoleCardsGone)
            {
                if (CardsActive(i)) EndCardHold(i);
                return;
            }
            if (!h.Ok)
            {
                // 短暫遺失：牌停在最後的位置；太久才放回
                if (_cardLifted[i] && _time - h.LostAt > k.CardHoldLostTimeout) EndCardHold(i);
                return;
            }
            if (h.PinchStarted) _cardPinchNear[i] = OnCards(h.PinchPoint, k.CardGrabMargin + 0.01f);
            if (!CardsActive(i))
            {
                // 捏住牌：開始捏的那一幀在牌上，或開始捏時就在牌旁邊、稍晚移進來。拿著籌碼、另一隻手已經拿著牌時不算
                if (_holding[i] || CardsActive(1 - i) || !h.Pinching) return;
                bool fresh = h.PinchStarted || (_cardPinchNear[i] && _time - h.PinchStartedAt <= k.CardGrabLateWindow);
                if (!fresh || !OnCards(h.PinchPoint, k.CardGrabMargin)) return;
                _cardGrab[i] = true;
                _cardGrabAt[i] = _time;
                _cardGrabPos[i] = h.PinchPoint;
                _cardPinchNear[i] = false;
                Trace?.Invoke($"{_time:0.000} cards grabbed {(Hand)i} (pinch {h.PinchPoint.X:0.00},{Height(h.PinchPoint):0.000},{h.PinchPoint.Z:0.00})");
                return;
            }
            if (h.PinchEnded)
            {
                ReleaseCards(i);
                return;
            }
            _cardGrip[i] = h.PinchPoint;
            _cardRot[i] = h.PalmRotation;
            if (!_cardLifted[i])
            {
                if (_time - _cardGrabAt[i] > k.CardLiftMaxDelay)
                {
                    ClearCards(i);
                    return;
                }
                if (Height(h.PinchPoint) < k.CardLiftHeight) return;
                _cardLifted[i] = true;
                _cardLiftAt[i] = _time;
                Trace?.Invoke($"{_time:0.000} cards lifted {(Hand)i} (grip {h.PinchPoint.X:0.00},{Height(h.PinchPoint):0.000},{h.PinchPoint.Z:0.00})");
                Emit(GestureKind.CardsLifted, i, pos: h.PinchPoint, rotation: h.PalmRotation);
            }
            if (!_cardLook[i])
            {
                string why = Height(h.Palm) >= k.CardLookCommitHeight ? "high"
                    : _time - _cardLiftAt[i] >= k.FoldCarryMaxTime ? "held" : null;
                if (why == null) return;
                _cardLook[i] = true;
                ResetPush(i);
                Trace?.Invoke($"{_time:0.000} cards look {(Hand)i}: {why} (palm h {Height(h.Palm):0.000}, grip z {h.PinchPoint.Z:0.00})");
            }
        }

        /// <summary>放開拿著的牌：拿起來過、在棄牌堆範圍內低處放開，或往前放開（見 CardReleaseFold）＝棄牌；其他地方放開＝放回原位。
        /// 攤牌時往前放開＝亮牌（見 Reveal）。</summary>
        void ReleaseCards(int i)
        {
            var h = _h[i];
            var k = Tuning;
            bool looked = _cardLook[i], folded = false;
            if (_cardLifted[i] && _ctx.Phase == TablePhase.Showdown)
            {
                // 攤牌：拿到自己牌位前方任何地方放開（不限高度）＝亮牌；放回牌位、在臉前放開、就近放下＝放回
                var g = _cardGrip[i];
                bool forward = g.Z >= Layout.CardsRect.MaxZ + k.ShowdownRevealPastCardsMargin && g.Z - _cardGrabPos[i].Z >= k.ShowdownRevealMinForward;
                if (forward && CanAct(Gesture.Flip, i))
                {
                    Reveal(i, g, "released forward");
                    folded = true;
                }
            }
            else if (_cardLifted[i])
            {
                var p = h.Palm;
                bool inMuck = Height(p) <= k.FoldCarryLandMaxHeight && HorizDist(p, Layout.MuckCenter) <= k.FoldMuckRadius;
                string how = inMuck ? "in muck" : CardReleaseFold(i);
                if (how != null && CanAct(Gesture.FoldPush, i))
                {
                    var g = _cardGrip[i];
                    Trace?.Invoke($"{_time:0.000} fold released {how} {(Hand)i} (palm {p.X:0.00},{Height(p):0.000},{p.Z:0.00}, grip {g.X:0.00},{Height(g):0.000},{g.Z:0.00})");
                    Emit(GestureKind.FoldPushed, i);
                    folded = true;
                }
            }
            EndCardHold(i);
            // 看過牌才放開：手接著落到棄牌堆不算拿牌棄牌（拿牌棄牌那條路徑在看牌時已關閉，這裡也不重新開始）。
            // 還沒進入看牌就放開（拿牌棄牌途中稍早放開）：拿牌棄牌照舊在手落下時判斷（foldpush_v2 有 2 段是先放開、手掌 0.2 s 後才落到 8 cm 以下）。
            // 放開時已經棄牌：推牌路徑也結束（手接著落進棄牌堆不要再發一次）
            if (looked || folded) ResetPush(i);
        }

        /// <summary>拿著的牌往前放開算棄牌嗎：不高（≤ CardFoldReleaseMaxHeight），越過棄牌線（且比捏起處往前 ≥ CardFoldReleaseMinForward），
        /// 或在自己的牌前緣之前往前丟（≥ CardFoldTossSpeed）。
        /// 算回傳方式（診斷用常數字串），不算回傳 null。放回自己的牌位、在臉前（牌前緣之後）放開都不算。</summary>
        string CardReleaseFold(int i)
        {
            var k = Tuning;
            var g = _cardGrip[i];
            if (Height(g) > k.CardFoldReleaseMaxHeight) return null;
            // 越線：還要比捏起處往前夠遠（日常雜動在牌前緣捏起、往前 8 cm 放下不算）
            if (g.Z >= Layout.FoldLineZ && g.Z - _cardGrabPos[i].Z >= k.CardFoldReleaseMinForward) return "past fold line";
            if (g.Z < Layout.CardsRect.MaxZ) return null;
            var v = _h[i].PinchVelocity(_time, k.ReleaseVelocityWindow);
            return v.Z >= k.CardFoldTossSpeed ? "tossed forward" : null;
        }

        // ───────────── 推牌棄牌 ─────────────
        void ResetPush(int i)
        {
            _pushActive[i] = false;
            _pushClaimed[i] = false;
        }

        void SetPushStart(int i, Vector3 p)
        {
            _pushStart[i] = p;
            _pushStartTime[i] = _time;
            var o = _h[1 - i];
            _pushOtherMinZ[i] = o.Ok ? o.Palm.Z : float.MaxValue;
        }

        void UpdateFoldPush(int i)
        {
            var h = _h[i];
            var k = Tuning;
            if (!h.Ok)
            {
                ResetPush(i);
                return;
            }
            // 牌拿在手上看：這隻手不走拿牌棄牌（只在棄牌堆低處放開才棄牌，見 ReleaseCards）；另一隻手推的是空的牌位
            if (_cardLook[i] || _cardLifted[1 - i])
            {
                ResetPush(i);
                return;
            }
            var p = h.Palm;
            // 貼著桌面推：手掌低，或指尖貼桌而手掌抬起（foldpush_v2：指尖中位數 1–5 cm、手掌 5–9 cm）
            bool low = Height(p) <= k.FoldPushMaxHeight ||
                       (h.LowTipY - Layout.TableY <= k.FoldPushMaxTipHeight && Height(p) <= k.FoldPushMaxPalmHeightWithTips);
            // 起點：手掌或指尖在牌上（手指壓著牌、手掌在牌前或旁邊）
            bool inStart = low && (Layout.CardsRect.Contains(p, k.FoldPushStartMargin) || Layout.CardsRect.Contains(h.MiddleTip, k.FoldPushStartMargin));
            if (!_pushActive[i])
            {
                if (inStart && !_holding[i])
                {
                    _pushActive[i] = true;
                    _pushClaimed[i] = false;
                    _pushLifted[i] = false;
                    SetPushStart(i, p);
                }
                return;
            }
            // 推的時候手指自然彎曲，捏合強度常到 0.6–0.7（真實錄製）：只有真的拿著籌碼才不算推牌
            // 推或拿：從牌上出發後可以抬起來拿到棄牌堆（foldpush_v2：手掌抬到 12–22 cm、往右前方放下），但要掌心朝下、不能太高；
            // 側向用錐形限制（棄牌堆在右前方：往前 18 cm 時側移 17 cm）
            float fwd = Math.Max(0f, p.Z - _pushStart[i].Z);
            bool carrying = Height(p) <= k.FoldCarryMaxHeight && h.PalmAngle >= k.FoldCarryMinPalmAngle;
            bool sideways = Math.Abs(p.X - _pushStart[i].X) > k.FoldPushMaxSideways + k.FoldPushSidewaysPerForward * fwd;
            if (!low && !_pushLifted[i])
            {
                _pushLifted[i] = true;
                _pushLiftTime[i] = _time;
            }
            else if (_pushLifted[i] && inStart)
            {
                // 抬起後又落回牌上：從這裡重新開始（手移過來時先經過牌的邊緣，不算那一段）
                _pushLifted[i] = false;
                SetPushStart(i, p);
            }
            if (!(low || carrying) || _holding[i] || sideways)
            {
                if (_pushClaimed[i])
                    Trace?.Invoke($"{_time:0.000} fold-push reset {(Hand)i}: {(!(low || carrying) ? "lifted" : _holding[i] ? "holding chips" : "sideways")} " +
                                  $"(palm {p.X:0.00},{Height(p):0.000},{p.Z:0.00} tip {h.LowTipY - Layout.TableY:0.000}, from {_pushStart[i].X:0.00},{_pushStart[i].Z:0.00})");
                ResetPush(i);
                return;
            }
            if (!_pushClaimed[i])
            {
                // 還沒開始推：起點跟著手（往後或停太久就重設）
                if (inStart && (p.Z < _pushStart[i].Z || _time - _pushStartTime[i] > k.FoldPushMaxTime))
                    SetPushStart(i, p);
                else if (!inStart && _time - _pushStartTime[i] > k.FoldPushMaxTime)
                {
                    ResetPush(i);
                    return;
                }
            }
            else if (_time - _pushStartTime[i] > k.FoldPushMaxTime)
            {
                Trace?.Invoke($"{_time:0.000} fold-push reset {(Hand)i}: too slow (palm z {p.Z:0.00}, from {_pushStart[i].Z:0.00})");
                ResetPush(i);
                return;
            }

            // 另一隻手也貼著桌面往前推了 All-in 起推的距離：這是雙手推（All-in），不是推牌
            var o = _h[1 - i];
            if (o.Ok && Height(o.Palm) <= k.AllInMaxHeight)
            {
                _pushOtherMinZ[i] = Math.Min(_pushOtherMinZ[i], o.Palm.Z);
                if (o.Palm.Z - _pushOtherMinZ[i] >= k.FoldPushOtherHandMaxForward)
                {
                    if (_pushClaimed[i])
                        Trace?.Invoke($"{_time:0.000} fold-push reset {(Hand)i}: other hand pushing too ({o.Palm.Z - _pushOtherMinZ[i]:0.000} m)");
                    ResetPush(i);
                    return;
                }
            }

            float d = p.Z - _pushStart[i].Z;
            if (!_pushClaimed[i] && d >= k.FoldPushClaimDistance)
            {
                if (CanAct(Gesture.FoldPush, i))
                {
                    _pushClaimed[i] = true;
                    Trace?.Invoke($"{_time:0.000} fold-push claimed {(Hand)i} from ({_pushStart[i].X:0.00},{_pushStart[i].Z:0.00})");
                }
                else
                {
                    ResetPush(i);
                    return;
                }
            }
            // 完成（兩種棄牌方式）：
            // 1. 推：一路貼桌，手掌越過棄牌線
            // 2. 拖或拿到棄牌堆放下（2026-10-12 實機 log 146–171 s 的 9 次棄牌都是這種：往右前方斜拖或拿起劃弧線，途中手掌拱高 10–23 cm，
            //    使用者確認是棄牌）。放下只要求手掌回到 FoldCarryLandMaxHeight 以下，不一定貼桌。
            // 越線看手的前緣（手掌或中指指尖）：手指壓著牌推，牌在指尖下；手掌在牌後方約 10 cm（2026-10-12 第二次實機 log 348.9 s：
            // 手掌往前 14 cm 停在 z 0.19，用手掌判斷要推 22 cm 才過線，結果以 too slow 重設）
            // 只靠指尖越線時，手掌不能抬太高（bet_20261012_145328 359.9 s：手掌 11.6 cm、指尖點桌往前甩，不是推牌）
            bool tipCross = h.MiddleTip.Z >= Layout.FoldLineZ && Height(p) <= k.FoldTipCrossMaxPalmHeight;
            bool pushed = !_pushLifted[i] && low && d >= k.FoldPushMinDistance && (p.Z >= Layout.FoldLineZ || tipCross);
            bool carried = _pushLifted[i] && Height(p) <= k.FoldCarryLandMaxHeight && HorizDist(p, Layout.MuckCenter) <= k.FoldMuckRadius &&
                           _time - _pushLiftTime[i] <= k.FoldCarryMaxTime;
            if (_pushClaimed[i] && (pushed || carried))
            {
                ResetPush(i);
                if (_ctx.Phase == TablePhase.Showdown)
                    Reveal(i, _cardLifted[i] ? _cardGrip[i] : h.MiddleTip, pushed ? "pushed" : "carried");
                else
                {
                    Trace?.Invoke($"{_time:0.000} fold {(pushed ? "pushed" : "carried to muck")} {(Hand)i} (forward {d:0.00}, palm {p.X:0.00},{Height(p):0.000},{p.Z:0.00})");
                    Emit(GestureKind.FoldPushed, i);
                }
                EndAllCardHolds();
            }
        }

        // ───────────── All-in ─────────────
        bool AllInEligible(HandTrack l, HandTrack r) => AllInIneligibleReason(l, r) == null;

        /// <summary>不符合 All-in 準備條件的原因（診斷用）；符合時回傳 null。</summary>
        string AllInIneligibleReason(HandTrack l, HandTrack r)
        {
            var k = Tuning;
            // 雙手推籌碼時手指彎曲，捏合強度常到 0.6–0.7（真實錄製 8/14 段）：只有真的拿著籌碼才排除；
            // 不小心捏起、沒抬起的 1–2 枚不算（起推時放回籌碼堆）
            if (HoldingRealChips(0) || HoldingRealChips(1)) return "holding chips";
            // 2026-10-12 實機 log：真人推 All-in 時雙手在自己正前方（左 x≈−0.17、右 x≈+0.13），不會夾住右前方的籌碼堆；
            // 所以只要求雙手在玩家前方的區域、還沒越過下注線
            if (r.Palm.X <= l.Palm.X) return "hands crossed";
            if (l.Palm.X < k.AllInZoneMinX) return $"left hand outside player area (x {l.Palm.X:0.00})";
            if (r.Palm.X > k.AllInZoneMaxX) return $"right hand outside player area (x {r.Palm.X:0.00})";
            float limitZ = Layout.BetLineZ - k.AllInStartBehindBetLine;
            if (l.Palm.Z > limitZ) return $"left hand already past bet line (z {l.Palm.Z:0.00})";
            if (r.Palm.Z > limitZ) return $"right hand already past bet line (z {r.Palm.Z:0.00})";
            if (Height(l.Palm) > k.AllInArmMaxHeight) return $"left hand too high ({Height(l.Palm):0.000})";
            if (Height(r.Palm) > k.AllInArmMaxHeight) return $"right hand too high ({Height(r.Palm):0.000})";
            float gap = HorizDist(l.Palm, r.Palm);
            if (gap < k.AllInMinHandGap || gap > k.AllInMaxHandGap) return $"hand gap outside {k.AllInMinHandGap:0.00}-{k.AllInMaxHandGap:0.00} ({gap:0.00})";
            return null;
        }

        /// <summary>這隻手拿著籌碼，而且不是「準備 All-in 時不小心捏起」的少量籌碼（見 GestureTuning.AllInDropChipsMax）。</summary>
        bool HoldingAny => _holding[0] || _holding[1];

        bool HoldingRealChips(int i)
        {
            if (!_holding[i]) return false;
            var k = Tuning;
            return _held[i] > k.AllInDropChipsMax || _locked[i] || Height(_h[i].PinchPoint) > k.AllInDropChipsMaxHeight;
        }

        /// <summary>雙手一起起推：把不小心捏起的籌碼放回籌碼堆（ChipsReleased、不在下注區，遊戲層會讓籌碼飛回堆上）。</summary>
        void DropIncidentalChips()
        {
            for (int i = 0; i < 2; i++)
            {
                if (!_holding[i] || HoldingRealChips(i)) continue;
                int n = _held[i];
                _holding[i] = false;
                _held[i] = 0;
                _pinchStartNear[i] = false; // 手還捏著：不要在稍晚拿起的時間窗內又拿起來
                Trace?.Invoke($"{_time:0.000} all-in push: put back {n} incidental chip(s) from {(Hand)i}");
                Emit(GestureKind.ChipsReleased, i, n, pos: new Vector3(_pickAnchor[i].X, Layout.TableY, _pickAnchor[i].Z), inZone: false);
            }
        }

        string _aiLastReason;

        /// <summary>雙手都貼近桌面（像要推的樣子）卻沒進入準備時，說出原因；原因改變才輸出一次。</summary>
        void TraceAllInNotArmed(HandTrack l, HandTrack r)
        {
            if (Trace == null || !l.Ok || !r.Ok) return;
            const float lowEnough = 0.12f;
            if (Height(l.Palm) > lowEnough || Height(r.Palm) > lowEnough) { _aiLastReason = null; return; }
            string why = AllInIneligibleReason(l, r);
            if (why == null) return;
            // 只比對原因的類型（括號前），數值每幀都在變，比整句會每幀輸出一次
            int paren = why.IndexOf('(');
            string kind = paren > 0 ? why.Substring(0, paren) : why;
            if (kind == _aiLastReason) return;
            _aiLastReason = kind;
            Trace($"{_time:0.000} all-in not armed: {why} (L {l.Palm.X:0.00},{Height(l.Palm):0.000},{l.Palm.Z:0.00} R {r.Palm.X:0.00},{Height(r.Palm):0.000},{r.Palm.Z:0.00})");
        }

        /// <summary>推的途中是否仍成立；不成立時回傳原因（常數字串，診斷用），成立回傳 null。</summary>
        string AllInPushInvalid(HandTrack l, HandTrack r)
        {
            var k = Tuning;
            if (HoldingRealChips(0) || HoldingRealChips(1)) return "holding chips";
            if (Height(l.Palm) > k.AllInMaxHeight || Height(r.Palm) > k.AllInMaxHeight) return "too high";
            float gap = HorizDist(l.Palm, r.Palm);
            if (gap < k.AllInMinHandGap || gap > k.AllInMaxHandGap) return "hand gap";
            float dl = l.Palm.Z - _aiStartZL, dr = r.Palm.Z - _aiStartZR;
            return Math.Abs(dl - dr) <= k.AllInMaxDesync ? null : "desync";
        }

        void AllInEnd(GestureKind kind)
        {
            if (_aiEarly)
            {
                // 提前的雙手推：不發 All-in 事件；「確認」時照樣算一次行動的冷卻（輪到時 AllInConfirmed 也會擋住緊接著的推牌）
                if (kind == GestureKind.AllInConfirmed) _lastAction = _time;
                _aiEarly = false;
                _ai = AllInPhase.Idle;
                _aiArm = 0;
                return;
            }
            // 還在默默追蹤（沒發過 AllInPushStarted）就不發取消事件
            if (_ai != AllInPhase.Pushing || _aiAnnounced || kind != GestureKind.AllInCancelled) Emit(kind);
            _ai = AllInPhase.Idle;
            _aiArm = 0;
        }

        /// <summary>發出 AllInPushStarted：從這裡起才擋其他手勢；從牌上出發的那隻手若已佔住推牌，清掉。</summary>
        void AllInAnnounce()
        {
            _aiAnnounced = true;
            ResetPush(0);
            ResetPush(1);
            if (_aiEarly)
            {
                Trace?.Invoke($"{_time:0.000} all-in push too early");
                Emit(GestureKind.ActionTooEarly);
                _lastAction = _time; // 與 AllInPushStarted 一樣算行動冷卻
            }
            else Emit(GestureKind.AllInPushStarted);
        }

        void StartCountdown(HandTrack l, HandTrack r, string why)
        {
            var k = Tuning;
            _ai = AllInPhase.Countdown;
            Trace?.Invoke($"{_time:0.000} all-in countdown starts: {why} (reach L {Math.Max(l.Palm.Z, l.MiddleTip.Z):0.00} R {Math.Max(r.Palm.Z, r.MiddleTip.Z):0.00}, " +
                          $"pushed L {l.Palm.Z - _aiStartZL:0.00} R {r.Palm.Z - _aiStartZR:0.00} in {_time - _aiPushStart:0.00} s) " +
                          $"[line {Layout.BetLineZ:0.00} with ≥{k.AllInMinPushDistance:0.00}, or ≥{k.AllInConfirmPushDistance:0.00} within {k.AllInConfirmPushTime:0.00} s; countdown {k.AllInConfirmDelay:0.00} s]");
            _aiCountdown = k.AllInConfirmDelay;
            _aiWithdrawn = false;
            _aiPaused = 0;
            _aiPeakMean = (l.Palm.Z + r.Palm.Z) * 0.5f;
        }

        void UpdateAllIn()
        {
            var k = Tuning;
            HandTrack l = _h[0], r = _h[1];
            bool ok = l.Ok && r.Ok;
            switch (_ai)
            {
                case AllInPhase.Idle:
                case AllInPhase.Armed:
                {
                    if (!ok || !AllInEligible(l, r))
                    {
                        if (_ai == AllInPhase.Idle) TraceAllInNotArmed(l, r);
                        _ai = AllInPhase.Idle;
                        _aiArm = 0;
                        return;
                    }
                    _aiArm += _dt;
                    _aiEligibleSince = _time - _aiArm;
                    if (_aiArm < k.AllInArmTime) return;
                    _ai = AllInPhase.Armed;
                    // 起推：最近 AllInPushWindow 內雙手都往前夠遠、而且同步（休息時的慢漂移不算）。
                    // 時間窗不超過這次符合準備條件以來的時間：雙手從別處移過來就位的那一段不算推
                    float window = Math.Min(k.AllInPushWindow, _aiArm);
                    float minL = l.MinPalmZ(_time, window), minR = r.MinPalmZ(_time, window);
                    float dl = l.Palm.Z - minL, dr = r.Palm.Z - minR;
                    float track = Math.Min(k.AllInTrackDistance, k.AllInStartDistance);
                    if (dl >= track && dr >= track && Math.Abs(dl - dr) <= k.AllInMaxDesync)
                    {
                        _aiStartZL = minL;
                        _aiStartZR = minR;
                        if (CanAct(Gesture.AllIn, -1))
                        {
                            // 先默默追蹤；推到 AllInStartDistance（或越線）才發 AllInPushStarted
                            _ai = AllInPhase.Pushing;
                            _aiAnnounced = false;
                            _aiEarly = !_ctx.IsPlayerTurn;
                            _aiPushStart = _time;
                            _aiLost = 0;
                            if (dl >= k.AllInStartDistance && dr >= k.AllInStartDistance && !HoldingAny) AllInAnnounce();
                        }
                        else
                        {
                            _ai = AllInPhase.Idle;
                            _aiArm = float.NegativeInfinity; // 要等雙手離開準備條件（例：抬手）再重新就位
                        }
                    }
                    return;
                }
                case AllInPhase.Pushing:
                {
                    if (_ctx.IsPlayerTurn == _aiEarly || (_aiEarly && !_ctx.CanActEarly))
                    {
                        AllInEnd(GestureKind.AllInCancelled);
                        return;
                    }
                    if (!ok)
                    {
                        _aiLost += _dt;
                        if (_aiLost > k.AllInLostTimeout) AllInEnd(GestureKind.AllInCancelled);
                        return;
                    }
                    _aiLost = 0;
                    string why = AllInPushInvalid(l, r) ?? (_time - _aiPushStart > k.AllInMaxPushTime ? "push too slow" : null);
                    // 默默追蹤時其他行動手勢成立了（例：推牌棄牌）：這不是 All-in
                    if (why == null && !_aiAnnounced && _lastAction >= _aiPushStart) why = "another action";
                    if (why != null)
                    {
                        if (_aiAnnounced)
                            Trace?.Invoke($"{_time:0.000} all-in cancelled while pushing: {why} (L {l.Palm.X:0.00},{Height(l.Palm):0.000},{l.Palm.Z:0.00} R {r.Palm.X:0.00},{Height(r.Palm):0.000},{r.Palm.Z:0.00})");
                        AllInEnd(GestureKind.AllInCancelled);
                        return;
                    }
                    // 越線：雙手的前緣（手掌或中指指尖）都越過下注線，而且雙手都推了夠遠。
                    // 2026-10-12 實機 log：真人推 All-in 時手掌停在 z 0.25–0.28（下注線 0.30 前 2–5 cm），是手指越過下注線
                    float reachL = Math.Max(l.Palm.Z, l.MiddleTip.Z), reachR = Math.Max(r.Palm.Z, r.MiddleTip.Z);
                    float pushedL = l.Palm.Z - _aiStartZL, pushedR = r.Palm.Z - _aiStartZR;
                    bool crossed = reachL >= Layout.BetLineZ && reachR >= Layout.BetLineZ &&
                                   pushedL >= k.AllInMinPushDistance && pushedR >= k.AllInMinPushDistance;
                    // 不必越線：雙手在最近 AllInConfirmPushTime 內一起推夠遠（2026-10-13 實機：準備姿勢在桌緣，推 23 cm 才到線）。
                    // 用時間窗內的位移而不是從起推點累計：休息時雙手慢慢一起往前漂（12 cm／2 s）、兩隻手先後各滑各的都不算
                    float w = (float)Math.Min(k.AllInConfirmPushTime, _time - _aiEligibleSince);
                    float wl = l.Palm.Z - l.MinPalmZ(_time, w), wr = r.Palm.Z - r.MinPalmZ(_time, w);
                    // 同時推：最近 AllInSyncWindow 內雙手也都還在往前（一手推完停住、另一手才開始，0.7 s 窗內兩手都推夠遠也不算）
                    float sw = (float)Math.Min(k.AllInSyncWindow, _time - _aiEligibleSince);
                    bool together = l.Palm.Z - l.MinPalmZ(_time, sw) >= k.AllInSyncDistance && r.Palm.Z - r.MinPalmZ(_time, sw) >= k.AllInSyncDistance;
                    bool far = together && wl >= k.AllInConfirmPushDistance && wr >= k.AllInConfirmPushDistance;
                    // 手上還有不小心捏起的籌碼：只有「雙手同時推夠遠」算數（兩手先後各滑各的、一手拿籌碼滑時越線不算），成立時放回籌碼堆
                    if (HoldingAny)
                    {
                        if (!far) return;
                        DropIncidentalChips();
                    }
                    if (!_aiAnnounced && (crossed || far || (wl >= k.AllInStartDistance && wr >= k.AllInStartDistance)))
                    {
                        if (!CanAct(Gesture.AllIn, -1))
                        {
                            _ai = AllInPhase.Idle;
                            _aiArm = float.NegativeInfinity;
                            return;
                        }
                        AllInAnnounce();
                    }
                    if (crossed || far) StartCountdown(l, r, crossed ? "crossed bet line" : "pushed far");
                    return;
                }
                case AllInPhase.Countdown:
                {
                    if (_ctx.IsPlayerTurn == _aiEarly || (_aiEarly && !_ctx.CanActEarly))
                    {
                        AllInEnd(GestureKind.AllInCancelled);
                        return;
                    }
                    if (!ok)
                    {
                        // 追蹤不可靠：倒數暫停，暫停太久就取消（不讓狀態卡住）
                        _aiPaused += _dt;
                        if (_aiPaused > k.AllInMaxPause)
                        {
                            Trace?.Invoke($"{_time:0.000} all-in cancelled in countdown: tracking lost {_aiPaused:0.00} s");
                            AllInEnd(GestureKind.AllInCancelled);
                        }
                        return;
                    }
                    float mean = (l.Palm.Z + r.Palm.Z) * 0.5f;
                    _aiPeakMean = Math.Max(_aiPeakMean, mean);
                    // 撤銷＝雙手貼著桌面把籌碼拖回來；推完把手抬起收回（之後放回桌面）不算撤銷
                    if (Height(l.Palm) > k.AllInRetractMaxHeight || Height(r.Palm) > k.AllInRetractMaxHeight) _aiWithdrawn = true;
                    if (!_aiWithdrawn && _aiPeakMean - mean >= k.AllInRetractDistance)
                    {
                        Trace?.Invoke($"{_time:0.000} all-in cancelled in countdown: hands pulled back {_aiPeakMean - mean:0.000} m with {_aiCountdown:0.00} s left");
                        AllInEnd(GestureKind.AllInCancelled);
                        return;
                    }
                    _aiCountdown -= _dt;
                    if (_aiCountdown <= 0)
                        AllInEnd(GestureKind.AllInConfirmed);
                    return;
                }
            }
        }

        // ───────────── 攤牌翻牌 ─────────────
        /// <summary>
        /// 攤牌亮牌（取代棄牌）：攤牌時棄牌類動作（推、拖、拿到棄牌堆、拿著牌往前放開）都是「把牌亮出來」，不會棄牌。
        /// 2026-10-13 s8.log 7445–7450 s：教學翻牌那一步連續 4 次拿起牌往前丟到中間，都被當成棄牌擋下。
        /// at：牌最後在哪（捏合點或推牌的指尖）；牌畫在 RevealDisplayPoint（放開處附近、玩家前方桌面上）。
        /// </summary>
        void Reveal(int i, Vector3 at, string how)
        {
            var land = Layout.RevealDisplayPoint(at);
            Trace?.Invoke($"{_time:0.000} showdown reveal {how} {(Hand)i} (at {at.X:0.00},{Height(at):0.000},{at.Z:0.00} → cards at {land.X:0.00},{land.Z:0.00})");
            Emit(GestureKind.ShowdownFlip, i, pos: land);
        }

        void UpdateFlip(int i)
        {
            var h = _h[i];
            var k = Tuning;
            if (!h.Ok) return;
            var cards = Layout.CardsRect;
            bool low = Height(h.Palm) <= k.FlipMaxHeight;
            // 翻牌時手指在牌上、手掌常在牌前；翻的過程手掌會被帶離牌 8–13 cm（真實錄製）
            bool over = low && (cards.Contains(h.Palm, k.FlipCardMargin) || cards.Contains(h.MiddleTip, k.FlipCardMargin));
            float upMargin = Math.Max(k.FlipCardMargin, k.FlipUpMargin);
            bool near = low && (cards.Contains(h.Palm, upMargin) || cards.Contains(h.MiddleTip, upMargin));
            if (!near)
            {
                _lastDownOverCards[i] = double.NegativeInfinity;
                return;
            }
            if (h.PalmAngle >= k.PalmDownMinAngle)
            {
                if (over) _lastDownOverCards[i] = _time;
            }
            else if (h.PalmAngle <= k.PalmUpMaxAngle && _time - _lastDownOverCards[i] <= k.FlipMaxTime)
            {
                _lastDownOverCards[i] = double.NegativeInfinity;
                if (_ctx.Phase == TablePhase.Showdown && CanAct(Gesture.Flip, i))
                {
                    ResetPush(i);
                    Emit(GestureKind.ShowdownFlip, i);
                    EndAllCardHolds();
                }
            }
        }
    }
}
