using System;
using Poker.Core;

namespace Poker.AI
{
    /// <summary>AI 這次行動的真實意圖。</summary>
    public enum Intent
    {
        /// <summary>有牌，想被跟（價值下注／加注，或慢打）。</summary>
        Value,
        /// <summary>沒牌硬打（純虛張）。</summary>
        Bluff,
        /// <summary>聽牌或中等牌施壓（半虛張）。</summary>
        SemiBluff,
        /// <summary>中間牌，猶豫（跟注、過牌）。</summary>
        Marginal,
        /// <summary>放棄（棄牌，或沒牌過牌）。</summary>
        GiveUp,
    }

    /// <summary>內心狀態（真實的，或演出來的）。</summary>
    public enum InnerState
    {
        /// <summary>有牌、自信。</summary>
        Strong,
        /// <summary>拿不定。</summary>
        Unsure,
        /// <summary>沒牌、想放棄。</summary>
        Weak,
        /// <summary>在虛張。</summary>
        Bluffing,
        /// <summary>面無表情（只會出現在「演出」側）。</summary>
        Masked,
    }

    /// <summary>一次破綻演出的結果：指令＋真實與演出的狀態（後兩者給 HandReveal 與統計用，角色層看不到）。</summary>
    public sealed class TellResult
    {
        /// <summary>給角色層的表演指令。</summary>
        public PerformanceCue Cue;
        /// <summary>真實狀態。</summary>
        public InnerState True;
        /// <summary>演出的狀態。</summary>
        public InnerState Shown;
        /// <summary>是否刻意演相反。</summary>
        public bool Reversed;
    }

    /// <summary>破綻統計：演出與真實一致的比例。</summary>
    public sealed class TellStats
    {
        /// <summary>演出次數。</summary>
        public int Total;
        /// <summary>演出＝真實的次數。</summary>
        public int Consistent;
        /// <summary>反向演出次數。</summary>
        public int Reversed;
        /// <summary>一致率。</summary>
        public double ConsistencyRate => Total == 0 ? 0 : Consistent / (double)Total;
    }

    /// <summary>
    /// 把 AI 的真實牌力與意圖轉成表演指令。依個性的破綻可信度決定：照實演、反向演、或面無表情。
    /// 一致的機率＝<see cref="Personality.TellCredibility"/>；不一致時反向的機率＝<see cref="Personality.ReverseActing"/>。
    /// </summary>
    public sealed class TellDirector
    {
        /// <summary>被注視時的強度倍率。</summary>
        public const float GazeBoost = 1.35f;

        readonly int _seat;
        readonly Personality _p;
        readonly Pcg32 _rng;
        readonly LineSpeaker _speaker;
        readonly Pcg32 _liveRng;
        PerformanceCue _last;
        InnerState _lastShown = InnerState.Masked;

        /// <summary>建立。</summary>
        /// <param name="liveRng">即時對話的「講不講」用的亂數（獨立串流，不影響決策時的演出）；null＝依座位固定的一條。</param>
        public TellDirector(int seat, Personality personality, Pcg32 rng, Pcg32 liveRng = null)
        {
            _seat = seat; _p = personality; _rng = rng;
            _speaker = new LineSpeaker(personality, rng);
            _liveRng = liveRng ?? new Pcg32(7919UL * (ulong)(seat + 1), 15);   // 不從 rng 抽（不改變原本的演出序列）
        }

        /// <summary>
        /// 即時對話（live talk）：玩家這一回合，這個 AI 要「演」成什麼狀態——和決策時的演出同一套可信度規則
        /// （照實＝TellCredibility；不照實時反向＝ReverseActing，否則撲克臉）。導演依這個決定開不開口，所以「講不講」本身就是破綻。
        /// 只拿真實狀態（強／弱／不確定），不拿牌；用獨立的亂數串流，不影響決策與決策時的演出。
        /// </summary>
        public InnerState LiveStance(InnerState truth)
        {
            if (Equity.Next01(_liveRng) < _p.TellCredibility) return truth;
            if (Equity.Next01(_liveRng) < _p.ReverseActing)
            {
                switch (truth)
                {
                    case InnerState.Strong: return InnerState.Weak;
                    case InnerState.Weak:
                    case InnerState.Bluffing: return InnerState.Strong;
                    default: return _liveRng.NextBounded(2) == 0 ? InnerState.Strong : InnerState.Weak;
                }
            }
            return InnerState.Masked;
        }

        /// <summary>累計統計。</summary>
        public TellStats Stats { get; } = new TellStats();
        /// <summary>整桌的台詞協調（GameSession 設定）：設了之後，反應台詞（長考、輸贏、翻牌、被盯）只提議、由 Table.Resolve 挑一個人講。</summary>
        public TableTalk Table { get; set; }
        /// <summary>台詞控制（可調冷卻）。</summary>
        public LineSpeaker Speaker => _speaker;

        /// <summary>意圖 → 真實狀態。</summary>
        public static InnerState TrueStateOf(Intent i)
        {
            switch (i)
            {
                case Intent.Value: return InnerState.Strong;
                case Intent.Bluff:
                case Intent.SemiBluff: return InnerState.Bluffing;
                case Intent.Marginal: return InnerState.Unsure;
                default: return InnerState.Weak;
            }
        }

        /// <summary>決策點（思考時間內）的演出。</summary>
        public TellResult ForDecision(Intent intent, double equity, double now, bool gazed)
        {
            var r = Choose(TrueStateOf(intent));
            r.Cue = Build(r.Shown, gazed);
            var t = LineFor(r.Shown);
            if (t.HasValue) AttachLine(r.Cue, t.Value, now, 0.35f, chorus: false);
            Remember(r);
            return r;
        }

        /// <summary>看到新公共牌的瞬間反應；勝率變化不大時回傳 null。</summary>
        public TellResult OnStreet(double equityBefore, double equityAfter, double now, bool gazed)
        {
            double d = equityAfter - equityBefore;
            if (Math.Abs(d) < 0.12) return null;
            var r = Choose(d > 0 ? InnerState.Strong : InnerState.Weak);
            r.Cue = Build(r.Shown, gazed);
            r.Cue.Duration = 1.5f;
            if (r.Shown == InnerState.Strong) { r.Cue.Mood = Mood.Happy; AttachLine(r.Cue, LineTrigger.StreetGood, now, 0.3f); }
            else if (r.Shown == InnerState.Weak) { r.Cue.Mood = Mood.Dejected; AttachLine(r.Cue, LineTrigger.StreetBad, now, 0.3f); }
            Remember(r);
            return r;
        }

        /// <summary>被玩家注視：沿用目前演出，強度放大，並依個性回應視線。</summary>
        public PerformanceCue OnGazed(double now)
        {
            var c = _last != null ? Copy(_last) : new PerformanceCue { Seat = _seat, Intensity = _p.Expressiveness * 0.6f };
            c.Intensity = Math.Min(1f, c.Intensity * GazeBoost + 0.05f);
            switch (_p.Archetype)
            {
                case Archetype.Rookie:
                    // 心虛（虛張或沒牌）就避開眼神；有牌就回看
                    if (_lastShown == InnerState.Bluffing || _lastShown == InnerState.Weak) { c.Tell = Tell.AvoidEyeContact; c.Look = LookTarget.Away; }
                    else c.Look = LookTarget.Player;
                    break;
                default:
                    c.Tell = Tell.StareDown; c.Look = LookTarget.Player;
                    break;
            }
            c.LineId = null; c.Subtitle = null;
            AttachLine(c, LineTrigger.StaredAt, now, 0.5f);
            return c;
        }

        /// <summary>對玩家動作的反應（大額下注、長考、盯著看）；沒有要演的回傳 null。</summary>
        public PerformanceCue OnPlayerNotice(PlayerNotice n, double now)
        {
            if ((n & PlayerNotice.BigBet) != 0)
            {
                var c = new PerformanceCue { Seat = _seat, Look = LookTarget.Player, Duration = 1.5f };
                switch (_p.Archetype)
                {
                    case Archetype.Rookie: c.Mood = Mood.Nervous; c.Tell = Tell.Swallow; c.Intensity = 0.9f; break;
                    case Archetype.Loudmouth: c.Mood = Mood.Annoyed; c.Tell = Tell.StareDown; c.Intensity = 0.85f; break;
                    default: c.Mood = Mood.Annoyed; c.Tell = Tell.Freeze; c.Intensity = 0.35f; break;
                }
                AttachLine(c, LineTrigger.PlayerBigBet, now, 0.8f);
                return c;
            }
            if ((n & PlayerNotice.LongHesitation) != 0)
            {
                var c = new PerformanceCue { Seat = _seat, Look = LookTarget.Player, Duration = 2f };
                switch (_p.Archetype)
                {
                    case Archetype.Loudmouth: c.Mood = Mood.Confident; c.Tell = Tell.LeanIn; c.Intensity = 0.9f; break;
                    case Archetype.Rookie: c.Mood = Mood.Hesitant; c.Tell = Tell.GlanceAtCards; c.Intensity = 0.6f; break;
                    default: c.Mood = Mood.Neutral; c.Tell = Tell.StareDown; c.Intensity = 0.4f; break;
                }
                AttachLine(c, LineTrigger.PlayerTanks, now, 1f);
                return c;
            }
            if ((n & PlayerNotice.StaredAtMe) != 0) return OnGazed(now);
            return null;
        }

        /// <summary>一手結束：贏 Happy、輸 Dejected（勝負是公開的，照實演）。</summary>
        public PerformanceCue OnHandEnd(bool won, double now)
        {
            var c = new PerformanceCue
            {
                Seat = _seat, Mood = won ? Mood.Happy : Mood.Dejected, Intensity = Math.Min(1f, _p.Expressiveness + 0.1f),
                Tell = won ? Tell.LeanBack : Tell.AvoidEyeContact, Look = won ? LookTarget.Player : LookTarget.Away, Duration = 2.5f,
            };
            AttachLine(c, won ? LineTrigger.Win : LineTrigger.Lose, now, 0.6f);
            _last = null; _lastShown = InnerState.Masked;
            return c;
        }

        // ───────── 內部 ─────────

        TellResult Choose(InnerState truth)
        {
            var r = new TellResult { True = truth };
            if (Equity.Next01(_rng) < _p.TellCredibility) r.Shown = truth;
            else if (Equity.Next01(_rng) < _p.ReverseActing) { r.Shown = Opposite(truth); r.Reversed = true; }
            else r.Shown = InnerState.Masked;
            Stats.Total++;
            if (r.Shown == r.True) Stats.Consistent++;
            if (r.Reversed) Stats.Reversed++;
            return r;
        }

        InnerState Opposite(InnerState s)
        {
            switch (s)
            {
                case InnerState.Strong: return InnerState.Weak;
                case InnerState.Weak:
                case InnerState.Bluffing: return InnerState.Strong;
                default: return _rng.NextBounded(2) == 0 ? InnerState.Strong : InnerState.Weak;
            }
        }

        static LineTrigger? LineFor(InnerState s)
        {
            switch (s)
            {
                case InnerState.Strong: return LineTrigger.ValueBet;
                case InnerState.Bluffing: return LineTrigger.Nervous;
                case InnerState.Unsure: return LineTrigger.Unsure;
                case InnerState.Weak: return LineTrigger.Fold;
                default: return null;
            }
        }

        PerformanceCue Build(InnerState shown, bool gazed)
        {
            var c = new PerformanceCue { Seat = _seat };
            var a = _p.Archetype;
            float jitter = 0.85f + 0.15f * (float)Equity.Next01(_rng);
            c.Intensity = _p.Expressiveness * jitter;
            switch (shown)
            {
                case InnerState.Strong:
                    c.Mood = Mood.Confident;
                    if (a == Archetype.Rookie) { c.Tell = Pick(Tell.LeanIn, Tell.GlanceAtChips); c.Look = LookTarget.OwnChips; }
                    else { c.Tell = Pick(Tell.LeanBack, Tell.StareDown); c.Look = LookTarget.Player; }
                    break;
                case InnerState.Bluffing:
                    if (a == Archetype.Rookie) { c.Mood = Mood.Nervous; c.Tell = Pick(Tell.Swallow, Tell.Sweat, Tell.AvoidEyeContact, Tell.FidgetChips); c.Look = LookTarget.Away; }
                    else if (a == Archetype.Loudmouth) { c.Mood = Mood.Bluffing; c.Tell = Pick(Tell.FidgetChips, Tell.TapFingers, Tell.Swallow); c.Look = LookTarget.Pot; }
                    else { c.Mood = Mood.Bluffing; c.Tell = Pick(Tell.Swallow, Tell.AvoidEyeContact); c.Look = LookTarget.Board; }
                    break;
                case InnerState.Unsure:
                    c.Mood = Mood.Hesitant; c.Tell = Pick(Tell.TapFingers, Tell.GlanceAtCards); c.Look = LookTarget.OwnCards;
                    break;
                case InnerState.Weak:
                    c.Mood = Mood.Dejected; c.Tell = Pick(Tell.AvoidEyeContact, Tell.GlanceAtCards); c.Look = LookTarget.Away;
                    break;
                default:
                    c.Mood = Mood.Neutral; c.Tell = Tell.Freeze; c.Look = LookTarget.Board; c.Intensity = 0.15f;
                    break;
            }
            if (gazed) c.Intensity *= GazeBoost;
            c.Intensity = Math.Min(1f, c.Intensity);
            return c;
        }

        Tell Pick(params Tell[] t) => t[_rng.NextBounded((uint)t.Length)];

        /// <summary>chorus：大家同時都有反應的情境，有 Table 時只提議（見 TableTalk）。</summary>
        void AttachLine(PerformanceCue c, LineTrigger t, double now, float baseChance, bool chorus = true)
        {
            float chance = Math.Min(1f, baseChance * (0.5f + _p.Talkativeness));
            if (chorus && Table != null)
            {
                Table.Propose(_speaker, _p, c, _speaker.Propose(t, now, chance), now);
                return;
            }
            var line = _speaker.TrySpeak(t, now, chance);
            if (line == null) return;
            c.LineId = line.Id; c.Subtitle = line.Text;
        }

        void Remember(TellResult r) { _last = r.Cue; _lastShown = r.Shown; }

        static PerformanceCue Copy(PerformanceCue c) => new PerformanceCue
        {
            Seat = c.Seat, Mood = c.Mood, Intensity = c.Intensity, Tell = c.Tell, Look = c.Look, LookSeat = c.LookSeat,
            LineId = c.LineId, Subtitle = c.Subtitle, Duration = c.Duration,
        };
    }
}
