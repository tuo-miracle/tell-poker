using System;
using System.Collections.Generic;
using System.Globalization;
using System.Numerics;

namespace Poker.Gestures
{
    /// <summary>第一次有目的地伸手是伸向哪裡（Action＝沒看到伸手，用做出決定的那一刻）。</summary>
    public enum ReachTarget { None, Cards, Chips, BetZone, Action }

    /// <summary>某一刻的三個訊號（給 AI 讀；純數值）。ReachDelay &lt; 0＝還沒伸手。</summary>
    public readonly struct HabitSnapshot
    {
        public readonly float ReachDelay;
        public readonly ReachTarget Target;
        public readonly int Peeks;
        public readonly float ChipHover;
        public HabitSnapshot(float reach, ReachTarget target, int peeks, float hover) { ReachDelay = reach; Target = target; Peeks = peeks; ChipHover = hover; }
        public bool ReachedForChips => Target == ReachTarget.Chips || Target == ReachTarget.BetZone;
    }

    /// <summary>PlayerHabits 的門檻（公尺、秒）。</summary>
    public sealed class PlayerHabitsTuning
    {
        /// <summary>手掌速度 ≥ 這個值、持續 ReachSustain 秒、停在牌／籌碼／下注區，才算「有目的地伸手」（追蹤抖動約幾 mm/s）。</summary>
        public float ReachSpeed = 0.15f;
        public double ReachSustain = 0.12;
        /// <summary>手掌高過桌面這麼多就不算伸向桌上的東西（在空中比手畫腳）。</summary>
        public float ReachMaxHeight = 0.30f;
        /// <summary>移動停下時離牌／籌碼／下注區這麼近也算伸到了。</summary>
        public float NearRadius = 0.04f;
        /// <summary>很慢地移到目標上方、停留這麼久也算伸手（快速經過不算）。</summary>
        public double DwellSeconds = 0.3;
        /// <summary>伸向牌之後這麼久內開始咪牌／拿起來看：這次伸手是看牌，不算伸手延遲。</summary>
        public double PeekGrace = 1.5;
        /// <summary>咪牌程度 ≥ 這個值算開始看、≤ PeekEnd 算看完。</summary>
        public float PeekStart = 0.15f, PeekEnd = 0.05f;
        /// <summary>看完之後這麼快又開始：同一次（追蹤閃爍、咪完接著拿起來看）。</summary>
        public double PeekMerge = 0.6;
        /// <summary>捏合點離籌碼堆（或任一籌碼柱）水平這麼近、高度不超過 HoverMaxHeight：在籌碼上方。</summary>
        public float HoverRadius = 0.09f, HoverMaxHeight = 0.15f;
        /// <summary>辨識器說「差一點」（失敗的嘗試）前後這麼久不算逗留。</summary>
        public double FailedAttemptPad = 0.6;
        /// <summary>真的下注那一次：捏起前這麼久到放開都是有目的的動作（移過去、捏、抬）。</summary>
        public double CommitGrace = 1.0;

        // ───── 即時訊號（live talk，2026-10-14）─────
        /// <summary>回合開始後想了這麼多秒各吐一次 Thinking。</summary>
        public int[] ThinkMilestones = { 3, 7, 12 };
        /// <summary>籌碼區：手掌離最近的籌碼柱水平這麼近算伸到（捏籌碼時手掌在捏合點後方約 7.5 cm）；離開要超過 ChipZoneExit（遲滯，追蹤抖動不會進進出出）。</summary>
        public float ChipZoneEnter = 0.12f, ChipZoneExit = 0.14f;
        /// <summary>要先離籌碼這麼遠（手掌），下一次進來才算「伸過去」、離開到這麼遠才算「縮回」（真實 idle 錄製：手放在籌碼旁邊晃來晃去不算）。</summary>
        public float ChipZoneFar = 0.17f;
        /// <summary>牌區：手掌在牌的範圍外擴 CardsMargin 內算在牌上；要先離開到 CardsFarMargin 外，下一次進來才算。</summary>
        public float CardsMargin = 0.03f, CardsFarMargin = 0.08f;
        /// <summary>籌碼區的手掌高度上限（在空中比手畫腳不算）。</summary>
        public float ChipZoneMaxHeight = 0.22f;
        /// <summary>在籌碼區停這麼久、而且慢下來（手掌速度 ≤ ArriveSpeed）才算「伸過去」；快速經過不算。</summary>
        public double ChipArriveSeconds = 0.15;
        public float ArriveSpeed = 0.3f;
        /// <summary>捏著籌碼這麼久還沒放開：舉在空中（ChipsHeld）。一般下注從捏起到放開約 1–1.5 秒。</summary>
        public double HeldSeconds = 1.6;
        /// <summary>手停在牌上這麼久、沒開始看牌：可能要棄牌（TowardCards）。咪牌通常 1 秒內就抬手腕。</summary>
        public double CardsDwellSeconds = 1.0;
        /// <summary>盯著同一個 AI 這麼久（秒）算「盯著看」（GazeDwell）。</summary>
        public float GazeDwellSeconds = 2.0f;
    }

    /// <summary>
    /// Fool Rocco（2026-10-14 原型）：從手部資料與手勢事件抽出 AI 可以「讀」的三個習慣訊號（純邏輯、牌桌座標、時間用手部資料的時鐘）。
    /// - 伸手延遲：輪到玩家（BeginTurn）到第一次有目的地伸向牌／籌碼／下注區。伸向牌之後 PeekGrace 秒內開始看牌的不算（那是看牌）；
    ///   之後的失敗嘗試不會讓它變長（看的是第一次伸手，不是第一次成功）；整個回合都沒看到伸手就用做出決定的時間（EndTurn）。
    /// - 看牌次數：這一手（BeginHand 起）咪牌或拿起來看的「開始」次數；PeekMerge 秒內的閃爍、咪完接著拿起來看算同一次；差一點不算。
    /// - 籌碼逗留：回合中手在自己籌碼上方（含捏起又放回去）的秒數；扣掉辨識器「差一點」前後的時段（在努力捏，不是在玩籌碼）
    ///   和真的下注那一次（捏起前 CommitGrace 秒到放開）。
    /// 回合結束（EndTurn）後數值凍結，直到下一次 BeginTurn。
    /// </summary>
    public sealed class PlayerHabits
    {
        public readonly TableLayout Layout;
        public readonly PlayerHabitsTuning Tuning;

        public PlayerHabits(TableLayout layout = null, PlayerHabitsTuning tuning = null)
        {
            Layout = layout ?? new TableLayout();
            Tuning = tuning ?? new PlayerHabitsTuning();
        }

        // 回合
        bool _inTurn;
        double _turnStart = double.NaN;
        // 伸手
        double _reachAt = double.NaN, _pendingCards = double.NaN;
        ReachTarget _reachTarget;
        readonly double[] _moveStart = { double.NaN, double.NaN }, _insideSince = { double.NaN, double.NaN };
        readonly ReachTarget[] _insideKind = new ReachTarget[2];
        readonly bool[] _startedInside = new bool[2];
        readonly Queue<(double t, Vector3 p)>[] _hist = { new Queue<(double, Vector3)>(), new Queue<(double, Vector3)>() };
        // 看牌
        int _peeks;
        bool _peeking, _lifted;
        double _lastLookEnd = double.NegativeInfinity;
        // 逗留
        readonly List<(double a, double b)> _hover = new List<(double, double)>();
        readonly List<(double a, double b)> _excluded = new List<(double, double)>();
        readonly double[] _pickAt = { double.NaN, double.NaN };
        double _lastFrame = double.NaN;
        // 即時訊號
        readonly List<HabitSignal> _signals = new List<HabitSignal>();
        int _nextMilestone;
        readonly bool[] _chipSeenOut = new bool[2], _chipIn = new bool[2], _chipApproach = new bool[2];
        readonly double[] _chipEnter = { double.NaN, double.NaN };
        readonly double[] _heldSince = { double.NaN, double.NaN };
        readonly int[] _heldCount = new int[2];
        readonly bool[] _heldSent = new bool[2];
        readonly bool[] _cardsSeenOut = new bool[2], _cardsIn = new bool[2], _cardsDone = new bool[2];
        readonly double[] _cardsEnter = { double.NaN, double.NaN };
        readonly HashSet<int> _gazeSent = new HashSet<int>();
        IReadOnlyList<Vector3> _chipSpots;
        float _chipSpotRadius;

        public bool InTurn => _inTurn;
        /// <summary>伸手延遲（秒）；還沒伸手是 -1。</summary>
        public float ReachDelay => double.IsNaN(_reachAt) || double.IsNaN(_turnStart) ? -1f : (float)Math.Max(0.0, _reachAt - _turnStart);
        public ReachTarget ReachTarget => double.IsNaN(_reachAt) ? ReachTarget.None : _reachTarget;
        public int Peeks => _peeks;
        public float ChipHover => (float)Measure();

        public HabitSnapshot Snapshot() => new HabitSnapshot(ReachDelay, ReachTarget, Peeks, ChipHover);

        /// <summary>這一回合到目前的訊號統計（EndTurn 後凍結）。</summary>
        public TurnHabits Turn { get; private set; } = new TurnHabits();
        /// <summary>玩家正在講話（語音對話收音中）。</summary>
        public bool PlayerSpeaking { get; private set; }

        /// <summary>取出上次之後新吐出的即時訊號（依時間順序）。</summary>
        public HabitSignal[] DrainSignals()
        {
            var r = _signals.ToArray();
            _signals.Clear();
            return r;
        }

        void Emit(HabitSignalKind k, double t, Hand h = Hand.Right, int count = 0, int target = -1) =>
            _signals.Add(new HabitSignal(k, t, h, count, target));

        /// <summary>盯著誰（GameController 的注視：target＝AI 座位，-1＝沒有；dwell＝連續秒數）。同一回合每個 AI 只吐一次。</summary>
        public void ObserveGaze(double t, int target, float dwell)
        {
            TickTurn(t);
            if (!_inTurn || target < 0 || dwell < Tuning.GazeDwellSeconds || t < _turnStart) return;
            if (_gazeSent.Add(target)) Emit(HabitSignalKind.GazeDwell, t, target: target);
        }

        /// <summary>玩家有沒有在講話（語音對話收音中）；開口的那一刻吐 PlayerSpoke。</summary>
        public void ObserveVoice(double t, bool speaking)
        {
            TickTurn(t);
            bool rising = speaking && !PlayerSpeaking;
            PlayerSpeaking = speaking;
            if (!rising || !_inTurn || t < _turnStart) return;
            Turn.Spoke = true;
            Emit(HabitSignalKind.PlayerSpoke, t);
        }

        /// <summary>回合時間（想了幾秒）的里程碑；手部資料、注視、語音都會推進。</summary>
        public void TickTurn(double t)
        {
            if (!_inTurn || double.IsNaN(_turnStart) || t < _turnStart) return;
            Turn.ThinkSeconds = (float)(t - _turnStart);
            var m = Tuning.ThinkMilestones;
            while (m != null && _nextMilestone < m.Length && t - _turnStart >= m[_nextMilestone])
            {
                Emit(HabitSignalKind.Thinking, _turnStart + m[_nextMilestone], count: m[_nextMilestone]);
                Turn.ThinkMilestones++;
                _nextMilestone++;
            }
        }

        /// <summary>籌碼實際畫在哪（贏很多時籌碼堆會往後排好幾柱）；沒給就用 Layout.ChipStackCenter。</summary>
        public void SetChipFootprint(IReadOnlyList<Vector3> spots, float radius)
        {
            _chipSpots = spots;
            _chipSpotRadius = radius;
        }

        /// <summary>新的一手：看牌次數歸零、回合狀態清掉。</summary>
        public void BeginHand()
        {
            _peeks = 0;
            _peeking = _lifted = false;
            _lastLookEnd = double.NegativeInfinity;
            ResetTurn();
            _turnStart = double.NaN;
            _inTurn = false;
        }

        /// <summary>輪到玩家（t＝手部資料的時鐘）：伸手與逗留重新量。</summary>
        public void BeginTurn(double t)
        {
            ResetTurn();
            _turnStart = t;
            _inTurn = true;
            Emit(HabitSignalKind.TurnStarted, t);
        }

        /// <summary>玩家做出決定：沒看到伸手就用這一刻；之後數值凍結。</summary>
        public void EndTurn(double t)
        {
            if (!_inTurn) return;
            if (double.IsNaN(_reachAt))
            {
                if (!double.IsNaN(_pendingCards)) Confirm(_pendingCards, ReachTarget.Cards);
                else Confirm(t, ReachTarget.Action);
            }
            TickTurn(t);
            for (int i = 0; i < 2; i++) if (!double.IsNaN(_heldSince[i])) Turn.HeldSeconds += (float)Math.Max(0, t - Math.Max(_heldSince[i], _turnStart));
            _inTurn = false;
        }

        void ResetTurn()
        {
            _reachAt = _pendingCards = double.NaN;
            _reachTarget = ReachTarget.None;
            for (int i = 0; i < 2; i++) { _moveStart[i] = _insideSince[i] = double.NaN; _startedInside[i] = true; _pickAt[i] = double.NaN; }
            _hover.Clear();
            _excluded.Clear();
            Turn = new TurnHabits();
            _nextMilestone = 0;
            _gazeSent.Clear();
            for (int i = 0; i < 2; i++)
            {
                _chipSeenOut[i] = _chipIn[i] = _chipApproach[i] = false;
                _chipEnter[i] = double.NaN;
                _cardsSeenOut[i] = _cardsIn[i] = _cardsDone[i] = false;
                _cardsEnter[i] = double.NaN;
                _heldSent[i] = !double.IsNaN(_heldSince[i]);   // 回合開始前就捏著的不算「舉著」
            }
        }

        // ───────────── 每幀 ─────────────

        public void Observe(HandsFrame f)
        {
            if (f == null) return;
            double t = f.Time;
            double dt = double.IsNaN(_lastFrame) ? 0 : Math.Min(0.1, Math.Max(0, t - _lastFrame));
            _lastFrame = t;
            bool overChips = false;
            for (int i = 0; i < 2; i++)
            {
                var h = f.Get((Hand)i);
                if (h == null || !h.Tracked) { _hist[i].Clear(); _moveStart[i] = double.NaN; continue; }
                var palm = h.Position(HandJoint.Palm);
                var a = h.Position(HandJoint.ThumbTip);
                var b = h.Position(HandJoint.IndexTip);
                var pinch = (a + b) * 0.5f;
                if (OverChips(pinch)) overChips = true;
                var q = _hist[i];
                q.Enqueue((t, palm));
                while (q.Count > 2 && t - q.Peek().t > 0.25) q.Dequeue();
                if (_inTurn && double.IsNaN(_reachAt) && !(t < _turnStart)) TrackReach(i, t, palm, pinch);
                if (_inTurn && !(t < _turnStart)) TrackLive(i, t, palm, pinch);
            }
            for (int i = 0; i < 2; i++)
            {
                var h = f.Get((Hand)i);
                if (h != null && h.Tracked) continue;
                // 追蹤掉了：這次接近作廢（不猜它是縮回還是沒有）
                _chipIn[i] = _chipApproach[i] = _chipSeenOut[i] = false;
                _cardsIn[i] = _cardsSeenOut[i] = false;
            }
            TickTurn(t);
            if (_inTurn && !(t < _turnStart))
                for (int i = 0; i < 2; i++)
                    if (!double.IsNaN(_heldSince[i]) && !_heldSent[i] && t - _heldSince[i] >= Tuning.HeldSeconds)
                    {
                        _heldSent[i] = true;
                        Turn.ChipsHeldCount++;
                        Emit(HabitSignalKind.ChipsHeld, t, (Hand)i, _heldCount[i]);
                    }
            if (_inTurn && overChips && dt > 0 && !(t < _turnStart)) AddSpan(_hover, t - dt, t);
            if (_inTurn && double.IsNaN(_reachAt) && !double.IsNaN(_pendingCards) && t - _pendingCards >= Tuning.PeekGrace)
                Confirm(_pendingCards, ReachTarget.Cards);
        }

        void TrackReach(int i, double t, Vector3 palm, Vector3 pinch)
        {
            var k = Tuning;
            var inside = InsideTarget(palm, pinch);
            // 慢慢移過去：在目標上停留 ≥ DwellSeconds（回合開始時手就在上面的不算，離開再進來才算；快速經過也不算）
            if (inside == ReachTarget.None) { _startedInside[i] = false; _insideSince[i] = double.NaN; }
            else if (!_startedInside[i])
            {
                if (double.IsNaN(_insideSince[i]) || inside != _insideKind[i]) { _insideSince[i] = t; _insideKind[i] = inside; }
                else if (t - _insideSince[i] >= k.DwellSeconds && double.IsNaN(_moveStart[i])) Candidate(_insideSince[i], inside);
            }

            // 一段移動：速度 ≥ ReachSpeed 開始，降到一半以下結束；結束時停在（或很靠近）牌、籌碼、下注區才算伸手——
            // 停在別的地方（例如收回桌邊、半路經過籌碼堆）只是換位置
            var q = _hist[i];
            (double t0, Vector3 p0) = (double.NaN, default);
            foreach (var s in q) if (t - s.t >= 0.08) (t0, p0) = s;
            if (double.IsNaN(t0)) return;
            float speed = (palm - p0).Length() / (float)(t - t0);
            if (speed >= k.ReachSpeed)
            {
                // 開始時間往回推半個速度視窗（速度是用過去 0.08 s 算的）
                if (double.IsNaN(_moveStart[i])) _moveStart[i] = Math.Max(_turnStart, t - (t - t0) * 0.5);
            }
            else if (speed < k.ReachSpeed * 0.5f && !double.IsNaN(_moveStart[i]))
            {
                double began = _moveStart[i];
                _moveStart[i] = double.NaN;
                var end = inside != ReachTarget.None ? inside : NearTarget(palm, pinch);
                if (end != ReachTarget.None && t - began >= k.ReachSustain) Candidate(began, end);
            }
        }

        /// <summary>
        /// 即時訊號（每一次，不只第一次）：
        /// - 籌碼：手從外面進到籌碼區、停 ChipArriveSeconds 而且慢下來 → ReachChips；之後沒捏著籌碼就離開（遲滯）→ PullBack。
        ///   下注、放回籌碼、其他行動會結束這次接近（不算縮回）。
        /// - 牌：手從外面進到牌上、停 CardsDwellSeconds 沒開始看牌 → TowardCards（可能要棄牌）；開始看牌就不算。
        /// 回合開始時手就在區域裡的，要先離開再進來才算。
        /// </summary>
        void TrackLive(int i, double t, Vector3 palm, Vector3 pinch)
        {
            var k = Tuning;
            float d = ChipDistance(palm, pinch);
            float height = palm.Y - Layout.TableY;
            bool inChips = height <= k.ChipZoneMaxHeight && d <= k.ChipZoneEnter;
            bool outChips = height > k.ChipZoneMaxHeight + 0.04f || d > k.ChipZoneExit;
            bool farChips = height > k.ChipZoneMaxHeight + 0.04f || d > k.ChipZoneFar;
            if (farChips) _chipSeenOut[i] = true;
            if (!_chipIn[i])
            {
                if (inChips)
                {
                    _chipIn[i] = true;
                    _chipEnter[i] = t;
                }
            }
            else if (outChips)
            {
                _chipIn[i] = false;
                _chipEnter[i] = double.NaN;
            }
            // 縮回：伸過去之後沒捏著籌碼、手離開到夠遠（ChipZoneFar）
            if (_chipApproach[i] && farChips)
            {
                _chipApproach[i] = false;
                if (double.IsNaN(_heldSince[i]))
                {
                    Turn.PullBacks++;
                    Emit(HabitSignalKind.PullBack, t, (Hand)i);
                }
            }
            if (_chipIn[i] && _chipSeenOut[i] && !_chipApproach[i] && !double.IsNaN(_chipEnter[i])
                && t - _chipEnter[i] >= k.ChipArriveSeconds && PalmSpeed(i, t) <= k.ArriveSpeed)
            {
                _chipApproach[i] = true;
                _chipSeenOut[i] = false;
                Turn.ChipReaches++;
                Emit(HabitSignalKind.ReachChips, t, (Hand)i);
            }

            // 牌區只看手掌（真實 idle 錄製：手放在牌旁邊、手指朝前時，捏合點會落在牌上）
            bool inCards = height <= k.HoverMaxHeight && Layout.CardsRect.Contains(palm, k.CardsMargin);
            bool outCards = height > k.HoverMaxHeight + 0.04f || !Layout.CardsRect.Contains(palm, k.CardsMargin + 0.03f);
            bool farCards = height > k.HoverMaxHeight + 0.04f || !Layout.CardsRect.Contains(palm, k.CardsFarMargin);
            if (!_cardsIn[i])
            {
                if (inCards) { _cardsIn[i] = true; _cardsEnter[i] = t; _cardsDone[i] = !_cardsSeenOut[i]; _cardsSeenOut[i] = false; }
            }
            else if (outCards)
            {
                _cardsIn[i] = false;
                _cardsEnter[i] = double.NaN;
            }
            if (farCards) _cardsSeenOut[i] = true;
            if (_cardsIn[i] && !_cardsDone[i] && !_peeking && !_lifted && t - _cardsEnter[i] >= k.CardsDwellSeconds)
            {
                _cardsDone[i] = true;
                TowardCards(t, (Hand)i);
            }
        }

        void TowardCards(double t, Hand h)
        {
            if (Turn.TowardCards && _signals.Exists(x => x.Kind == HabitSignalKind.TowardCards && t - x.Time < 1.0)) return;
            Turn.TowardCards = true;
            Emit(HabitSignalKind.TowardCards, t, h);
        }

        /// <summary>
        /// 手掌離最近籌碼柱的水平距離。只看手掌（2026-10-14：手張開放在桌邊時，捏合點——拇指與食指尖的中點——會落在籌碼堆旁邊幾公分，
        /// 籌碼多到排好幾柱時就「一直在籌碼上」，伸手、縮回都量不到）。
        /// </summary>
        float ChipDistance(Vector3 palm, Vector3 pinch)
        {
            var b = NearestChip(palm);
            return (float)Math.Sqrt((palm.X - b.X) * (palm.X - b.X) + (palm.Z - b.Z) * (palm.Z - b.Z));
        }

        /// <summary>最近約 0.08 秒的手掌速度（m/s）；資料不夠時當作停著。</summary>
        float PalmSpeed(int i, double t)
        {
            var q = _hist[i];
            if (q.Count < 2) return 0f;
            (double t0, Vector3 p0) = (double.NaN, default);
            Vector3 last = default;
            foreach (var s in q) { if (t - s.t >= 0.08) (t0, p0) = s; last = s.p; }
            if (double.IsNaN(t0) || t - t0 <= 1e-6) return 0f;
            return (last - p0).Length() / (float)(t - t0);
        }

        /// <summary>一次行動或下注結束了正在進行的接近（之後離開籌碼區不算縮回）。</summary>
        void EndApproaches()
        {
            _chipApproach[0] = _chipApproach[1] = false;
            _cardsDone[0] = _cardsDone[1] = true;
        }

        /// <summary>手停在離目標 NearRadius 內（還沒進到上方）也算伸到了。</summary>
        ReachTarget NearTarget(Vector3 palm, Vector3 pinch)
        {
            float r = Tuning.NearRadius;
            if (palm.Y - Layout.TableY > Tuning.ReachMaxHeight) return ReachTarget.None;
            if (Layout.CardsRect.Contains(pinch, r) || Layout.CardsRect.Contains(palm, r)) return ReachTarget.Cards;
            var c = NearestChip(pinch);
            float dx = pinch.X - c.X, dz = pinch.Z - c.Z;
            float hr = Tuning.HoverRadius + r;
            if (dx * dx + dz * dz <= hr * hr) return ReachTarget.Chips;
            if (Layout.BetZone.Contains(palm, r * 0.5f)) return ReachTarget.BetZone;
            return ReachTarget.None;
        }

        ReachTarget InsideTarget(Vector3 palm, Vector3 pinch)
        {
            if (palm.Y - Layout.TableY > Tuning.HoverMaxHeight) return ReachTarget.None;
            if (Layout.CardsRect.Contains(palm, 0.02f) || Layout.CardsRect.Contains(pinch, 0.02f)) return ReachTarget.Cards;
            if (OverChips(pinch)) return ReachTarget.Chips;
            if (Layout.BetZone.Contains(palm)) return ReachTarget.BetZone;
            return ReachTarget.None;
        }

        /// <summary>一次伸手的候選：伸向牌的先擱著（可能是要看牌），其他的直接成立（前面有擱著的就用擱著的時間）。</summary>
        void Candidate(double t, ReachTarget target)
        {
            if (!double.IsNaN(_reachAt)) return;
            if (target == ReachTarget.Cards)
            {
                if (double.IsNaN(_pendingCards)) _pendingCards = t;
                return;
            }
            if (!double.IsNaN(_pendingCards)) Confirm(_pendingCards, ReachTarget.Cards);
            else Confirm(t, target);
        }

        void Confirm(double t, ReachTarget target)
        {
            if (!double.IsNaN(_reachAt)) return;
            _reachAt = Math.Max(_turnStart, t);
            _reachTarget = target;
            _pendingCards = double.NaN;
        }

        /// <summary>開始看牌：剛才伸向牌的那一下是為了看牌，不算伸手。</summary>
        void LookStarted()
        {
            _pendingCards = double.NaN;
            _moveStart[0] = _moveStart[1] = double.NaN;
            _insideSince[0] = _insideSince[1] = double.NaN;
            _startedInside[0] = _startedInside[1] = true;   // 看完還停在牌上：離開再進來才算
            _cardsDone[0] = _cardsDone[1] = true;            // 手在牌上是為了看牌，不是要棄牌（離開再進來才重新算）
        }

        // ───────────── 手勢事件 ─────────────

        public void Observe(GestureEvent e)
        {
            double t = e.Time;
            var k = Tuning;
            switch (e.Kind)
            {
                case GestureKind.PeekChanged:
                    if (e.Amount >= k.PeekStart && !_peeking)
                    {
                        _peeking = true;
                        if (!_lifted && t - _lastLookEnd > k.PeekMerge) NewLook(t, e.Hand);
                        LookStarted();
                    }
                    else if (e.Amount <= k.PeekEnd && _peeking)
                    {
                        _peeking = false;
                        if (!_lifted) _lastLookEnd = t;
                    }
                    break;
                case GestureKind.CardsLifted:
                    if (_lifted) break;
                    if (!_peeking && t - _lastLookEnd > k.PeekMerge) NewLook(t, e.Hand);
                    _lifted = true;
                    _peeking = false;
                    LookStarted();
                    break;
                case GestureKind.CardsReturned:
                    if (_lifted) _lastLookEnd = t;
                    _lifted = false;
                    break;
                case GestureKind.ChipsPicked:
                    _pickAt[(int)e.Hand] = t;
                    Action(t, ReachTarget.Chips);
                    Picked(t, e.Hand, e.Count);
                    break;
                case GestureKind.ChipCountChanged:
                    _heldCount[(int)e.Hand] = e.Count;
                    break;
                case GestureKind.BetRetracted:
                    if (_inTurn && t >= _turnStart) { Turn.PutBacks++; Emit(HabitSignalKind.ChipsPutBack, t, e.Hand, e.Count); }
                    _heldSince[(int)e.Hand] = t;   // 撈回來的籌碼在手上
                    _heldCount[(int)e.Hand] = e.Count;
                    _heldSent[(int)e.Hand] = true;
                    break;
                case GestureKind.ChipsReleased:
                {
                    Released(t, e.Hand, e.Count, e.InBetZone);
                    double p = _pickAt[(int)e.Hand];
                    // 放進下注區：這一次是真的下注（移過去、捏、抬、帶過去）
                    if (e.InBetZone) AddSpan(_excluded, (double.IsNaN(p) ? t : p) - k.CommitGrace, t);
                    _pickAt[(int)e.Hand] = double.NaN;
                    Action(t, ReachTarget.Chips);
                    break;
                }
                case GestureKind.BetCommitted:
                    Action(t, ReachTarget.BetZone);
                    EndApproaches();
                    break;
                case GestureKind.FoldPushed:
                    Action(t, ReachTarget.Cards);
                    EndApproaches();
                    break;
                case GestureKind.AllInPushStarted:
                case GestureKind.AllInConfirmed:
                    AddSpan(_excluded, t - k.CommitGrace, t + 1.5);
                    Action(t, ReachTarget.Action);
                    break;
                case GestureKind.KnockCheck:
                    Action(t, ReachTarget.Action);
                    EndApproaches();
                    break;
            }
            if (e.Kind == GestureKind.AllInPushStarted || e.Kind == GestureKind.AllInConfirmed) EndApproaches();
        }

        /// <summary>新的一次看牌（這一手的第幾次）：回合中就吐 Look。</summary>
        void NewLook(double t, Hand h)
        {
            _peeks++;
            if (!_inTurn || t < _turnStart) return;
            Turn.Looks++;
            Emit(HabitSignalKind.Look, t, h, _peeks);
        }

        void Picked(double t, Hand h, int count)
        {
            int i = (int)h;
            _heldSince[i] = t;
            _heldCount[i] = count;
            _heldSent[i] = false;
            // 伸手的訊號沒看到（位置怪、追蹤掉）但真的捏到了：也是伸向籌碼
            if (_inTurn && t >= _turnStart && !_chipApproach[i])
            {
                _chipApproach[i] = true;
                _chipSeenOut[i] = false;
                Turn.ChipReaches++;
                Emit(HabitSignalKind.ReachChips, t, h);
            }
        }

        void Released(double t, Hand h, int count, bool inBetZone)
        {
            int i = (int)h;
            if (!double.IsNaN(_heldSince[i]) && _inTurn) Turn.HeldSeconds += (float)Math.Max(0, t - Math.Max(_heldSince[i], _turnStart));
            _heldSince[i] = double.NaN;
            _chipApproach[i] = false;   // 放進下注區＝下注；放在別處＝放回（不再另外算縮回）
            // 舉了很久但最後放進下注區：那是一次（慢的）下注，給 AI 讀的「玩籌碼」不算它（即時台詞已經講了就算了）
            if (inBetZone && _heldSent[i] && _inTurn && Turn.ChipsHeldCount > 0) Turn.ChipsHeldCount--;
            _heldSent[i] = false;
            if (inBetZone || !_inTurn || t < _turnStart) return;
            Turn.PutBacks++;
            Emit(HabitSignalKind.ChipsPutBack, t, h, count);
        }

        /// <summary>一個動作（或失敗的嘗試）：還沒看到伸手就以它為準（前面有擱著的伸向牌就用那個）。</summary>
        void Action(double t, ReachTarget target)
        {
            if (!_inTurn || !double.IsNaN(_reachAt) || t < _turnStart) return;
            if (!double.IsNaN(_pendingCards)) { Confirm(_pendingCards, ReachTarget.Cards); return; }
            double run = double.NaN;
            for (int i = 0; i < 2; i++) if (!double.IsNaN(_moveStart[i]) && (double.IsNaN(run) || _moveStart[i] < run)) run = _moveStart[i];
            Confirm(double.IsNaN(run) ? t : Math.Min(run, t), target);
        }

        /// <summary>辨識器的診斷行（GestureRecognizer.Trace）：失敗的嘗試記下來。</summary>
        public void NoteTrace(string trace)
        {
            if (!TryParseFailedAttempt(trace, out double t, out var hand, out bool peek)) return;
            if (peek) { LookStarted(); return; }   // 想咪牌沒成功：不算看牌，也不算伸手
            if (!_inTurn) return;
            bool chips = trace.Contains("chip near-miss");
            // 即時訊號：手勢沒成功，意圖還是看得出來（想捏籌碼＝伸向籌碼；推牌中斷＝手在牌上、可能要棄）
            if (t >= _turnStart)
            {
                int i = (int)hand;
                if (chips && !_chipApproach[i])
                {
                    _chipApproach[i] = _chipIn[i];
                    _chipSeenOut[i] = false;
                    Turn.ChipReaches++;
                    Emit(HabitSignalKind.ReachChips, t, hand);
                }
                // 推牌中斷：只在手真的在牌上時算（真實 idle 錄製：手在桌邊滑動也會出現推牌重設）
                else if (trace.Contains("fold-push reset") && !trace.Contains("holding chips") && _cardsIn[i] && !_cardsDone[i])
                {
                    _cardsDone[i] = true;
                    TowardCards(t, hand);
                }
            }
            // 捏籌碼差一點：那段時間在努力捏，不是在玩籌碼
            if (chips) AddSpan(_excluded, t - Tuning.FailedAttemptPad, t + Tuning.FailedAttemptPad);
            // 「all-in not armed」雙手放著不動時也會出現（不是嘗試），不當成伸手
            if (!trace.Contains("all-in not armed")) Action(t, chips ? ReachTarget.Chips : ReachTarget.Action);
        }

        /// <summary>
        /// 讀辨識器的診斷行：失敗的嘗試（捏籌碼／咪牌差一點、敲桌沒第二下、推牌中斷、All-in 沒成立或取消）的時間與手。
        /// 格式（GestureRecognizer）：「&lt;秒&gt; chip near-miss Right: …」「&lt;秒&gt; all-in not armed: …」。
        /// </summary>
        public static bool TryParseFailedAttempt(string trace, out double time, out Hand hand, out bool peek)
        {
            time = 0; hand = Hand.Right; peek = false;
            if (string.IsNullOrEmpty(trace)) return false;
            int sp = trace.IndexOf(' ');
            if (sp <= 0 || !double.TryParse(trace.Substring(0, sp), NumberStyles.Float, CultureInfo.InvariantCulture, out time)) return false;
            var rest = trace.Substring(sp + 1);
            bool failed;
            if (rest.StartsWith("peek near-miss", StringComparison.Ordinal)) { failed = true; peek = true; }
            else failed = rest.StartsWith("chip near-miss", StringComparison.Ordinal)
                          || (rest.StartsWith("knock-", StringComparison.Ordinal) && rest.Contains("no second stroke"))
                          || rest.StartsWith("fold-push reset", StringComparison.Ordinal)
                          || rest.StartsWith("all-in not armed", StringComparison.Ordinal)
                          || rest.StartsWith("all-in cancelled", StringComparison.Ordinal);
            if (!failed) return false;
            int colon = rest.IndexOf(':');
            var head = colon > 0 ? rest.Substring(0, colon) : rest;
            hand = head.EndsWith(" Left", StringComparison.Ordinal) ? Hand.Left : Hand.Right;
            return true;
        }

        // ───────────── 幾何與時段 ─────────────

        bool OverChips(Vector3 p)
        {
            if (p.Y - Layout.TableY > Tuning.HoverMaxHeight) return false;
            var c = NearestChip(p);
            float r = Tuning.HoverRadius;
            if (_chipSpots != null && _chipSpots.Count > 0) r = Math.Max(r, _chipSpotRadius + 0.04f);
            float dx = p.X - c.X, dz = p.Z - c.Z;
            return dx * dx + dz * dz <= r * r;
        }

        Vector3 NearestChip(Vector3 p)
        {
            var best = Layout.ChipStackCenter;
            if (_chipSpots == null || _chipSpots.Count == 0) return best;
            float bd = float.MaxValue;
            foreach (var s in _chipSpots)
            {
                if (Layout.BetZone.Contains(s)) continue;
                float dx = p.X - s.X, dz = p.Z - s.Z, d = dx * dx + dz * dz;
                if (d < bd) { bd = d; best = s; }
            }
            return best;
        }

        Vector3 NearestBet(Vector3 p)
        {
            var z = Layout.BetZone;
            return new Vector3(Math.Min(z.MaxX, Math.Max(z.MinX, p.X)), 0f, Math.Min(z.MaxZ, Math.Max(z.MinZ, p.Z)));
        }

        /// <summary>加一段時間（和最後一段相連就併起來）。</summary>
        static void AddSpan(List<(double a, double b)> list, double a, double b)
        {
            if (b <= a) return;
            if (list.Count > 0)
            {
                var last = list[list.Count - 1];
                if (a <= last.b + 1e-6 && a >= last.a) { list[list.Count - 1] = (last.a, Math.Max(last.b, b)); return; }
            }
            list.Add((a, b));
        }

        /// <summary>逗留秒數＝在籌碼上方的時段扣掉排除的時段（排除的時段先合併，避免重複扣）。</summary>
        double Measure()
        {
            if (_hover.Count == 0) return 0;
            var ex = new List<(double a, double b)>(_excluded);
            ex.Sort((x, y) => x.a.CompareTo(y.a));
            var merged = new List<(double a, double b)>();
            foreach (var e in ex)
            {
                if (merged.Count > 0 && e.a <= merged[merged.Count - 1].b) merged[merged.Count - 1] = (merged[merged.Count - 1].a, Math.Max(merged[merged.Count - 1].b, e.b));
                else merged.Add(e);
            }
            double total = 0;
            foreach (var h in _hover)
            {
                double len = h.b - h.a;
                foreach (var e in merged) len -= Math.Max(0, Math.Min(h.b, e.b) - Math.Max(h.a, e.a));
                total += Math.Max(0, len);
            }
            return total;
        }
    }
}
