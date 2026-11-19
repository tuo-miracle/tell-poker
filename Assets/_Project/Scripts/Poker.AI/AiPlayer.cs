using System;
using System.Collections.Generic;
using Poker.Core;

namespace Poker.AI
{
    /// <summary>
    /// 一個 AI 座位的大腦：決策、讀玩家、破綻演出、亮牌。
    /// 遊戲層用法：
    /// <list type="number">
    /// <item>每次 <see cref="Engine.Apply"/> 後，把 <c>PlayerView.Filter(events, ai.Seat)</c> 送進 <see cref="OnEvents"/>（回傳要演的反應）。</item>
    /// <item>輪到 AI：<see cref="Decide"/> → 在 <see cref="AiDecision.ThinkSeconds"/> 內播 <see cref="AiDecision.Cue"/>，時間到送出 <see cref="AiDecision.Command"/>。</item>
    /// <item>玩家的肢體訊號送進 <see cref="OnPlayerSignals"/>；一手結束後看 <see cref="LastReveal"/>。</item>
    /// </list>
    /// 種子相同、輸入序列相同 → 決策完全相同（決策、MC、演出、反應各用獨立的 PCG32 串流，演出不會影響決策重播）。
    /// </summary>
    public sealed class AiPlayer
    {
        readonly AiSettings _settings;
        readonly Pcg32 _decisionRng, _mcRng, _reactRng;
        readonly List<RevealMoment> _moments = new List<RevealMoment>();
        double _streetEquity = -1;
        bool _gazed, _sawFlop;
        IReadOnlyList<Card> _humanShown;

        /// <summary>建立。</summary>
        public AiPlayer(Personality personality, int seat, ulong seed, AiSettings settings = null)
        {
            Personality = personality ?? throw new ArgumentNullException(nameof(personality));
            Seat = seat;
            _settings = settings ?? new AiSettings();
            _decisionRng = new Pcg32(seed, 11);
            _mcRng = new Pcg32(seed, 12);
            _reactRng = new Pcg32(seed, 14);
            Tells = new TellDirector(seat, personality, new Pcg32(seed, 13));
            Read = new PlayerRead(seat, personality.ReadSensitivity, _settings.ReadSettings);
            Observations = new TableObservations(seat);
        }

        /// <summary>個性。</summary>
        public Personality Personality { get; }
        /// <summary>座位。</summary>
        public int Seat { get; }
        /// <summary>真人玩家的座位（讀玩家只在此座位仍在局時影響決策）；-1 表示不讀。</summary>
        public int HumanSeat { get; set; } = -1;
        /// <summary>此 AI 自己的牌局紀錄（只含可見事件）。</summary>
        public TableObservations Observations { get; }
        /// <summary>讀玩家。</summary>
        public PlayerRead Read { get; }
        /// <summary>破綻演出。</summary>
        public TellDirector Tells { get; }
        /// <summary>設定。</summary>
        public AiSettings Settings => _settings;
        /// <summary>上一手的亮牌資訊（一手結束後產生）。</summary>
        public HandReveal LastReveal { get; private set; }
        /// <summary>玩家目前是否盯著這個 AI。</summary>
        public bool IsGazed => _gazed;
        /// <summary>上一手結束時：這一手對玩家的判斷（沒有是 null）與攤牌後的結果。</summary>
        public LiveBelief LastReadBelief { get; private set; }
        public ReadOutcome LastReadOutcome { get; private set; }

        /// <summary>
        /// 即時對話（live talk）：玩家這一回合，這個 AI 要演成什麼狀態（遊戲層每個玩家回合開頭問一次）。
        /// 真實狀態用自己看到最新公共牌時的勝率（每位對手的等效勝率：≥ 0.6 強、&lt; 0.4 弱、其他不確定），再經過 TellDirector 的可信度。
        /// 只回傳狀態，牌不出這個類別。
        /// </summary>
        public InnerState LiveStance()
        {
            if (Observations.MyHole == null || !IAmLive()) return InnerState.Masked;
            double eq = _streetEquity >= 0 ? _streetEquity : ReactionEquity();
            int n = Math.Max(1, LiveOpponentCount());
            double e1 = Math.Pow(Math.Max(0, eq), 1.0 / n);
            var truth = e1 >= 0.6 ? InnerState.Strong : e1 < 0.4 ? InnerState.Weak : InnerState.Unsure;
            return Tells.LiveStance(truth);
        }

        /// <summary>
        /// 做決策。只透過 <see cref="BotView"/>（公開資訊＋自己的底牌）與 <paramref name="observations"/>（過濾後的事件）取得資訊。
        /// </summary>
        public AiDecision Decide(HandState state, int seat, TableObservations observations)
        {
            if (seat != Seat) throw new ArgumentException("此 AI 的座位是 " + Seat, nameof(seat));
            var v = BotView.From(state, seat);
            var obs = observations ?? Observations;
            double shift = 0;
            if (HumanSeat >= 0 && HumanSeat != seat && HumanSeat < v.Seats.Count && v.Seats[HumanSeat].IsLive)
            {
                shift = Read.CurrentShift(obs.Now);
                if (shift != 0 && Read.Belief != null) Read.MarkUsed();   // 照這個判斷打了：攤牌時拿來學
            }
            var r = Policy.Decide(v, obs, Personality, shift, _decisionRng, _mcRng, Math.Max(1, _settings.MonteCarloTrials));
            var d = new AiDecision
            {
                Command = r.Command, Intent = r.Intent, RawEquity = r.RawEquity, Equity = r.Equity, ReadShift = r.Equity - r.RawEquity,
                Difficulty = r.Difficulty, Street = v.Street,
            };
            d.ThinkSeconds = ThinkTime(d, v);
            d.Tell = Tells.ForDecision(d.Intent, d.Equity, obs.Now, _gazed);
            d.Tell.Cue.Duration = d.ThinkSeconds;
            _moments.Add(new RevealMoment
            {
                Street = v.Street, Action = Describe(d.Command), Intent = d.Intent, Equity = d.Equity,
                TrueState = d.Tell.True, ShownState = d.Tell.Shown, Reversed = d.Tell.Reversed,
            });
            return d;
        }

        /// <summary>用自己的座位與紀錄做決策。</summary>
        public AiDecision Decide(HandState state) => Decide(state, Seat, Observations);

        /// <summary>吃可見事件，回傳要演的反應（看到公共牌、一手結束）。</summary>
        public IReadOnlyList<PerformanceCue> OnEvents(IEnumerable<GameEvent> visibleEvents)
        {
            var cues = new List<PerformanceCue>();
            foreach (var e in visibleEvents)
            {
                Observations.Observe(e);
                switch (e)
                {
                    case HandStarted _:
                        _moments.Clear(); _streetEquity = -1; _sawFlop = false;
                        Read.BeginHand();
                        _humanShown = null;
                        break;
                    case ShowdownRevealed sr:
                        foreach (var h in sr.Hands) if (h.Seat == HumanSeat) _humanShown = h.Cards;
                        break;
                    case HoleCardsDealt hc when hc.Seat == Seat:
                        _streetEquity = ReactionEquity();
                        break;
                    case StreetDealt sd when IAmLive():
                    {
                        if (sd.Street == Street.Flop) _sawFlop = true;
                        double now = ReactionEquity();
                        if (_streetEquity >= 0)
                        {
                            var t = Tells.OnStreet(_streetEquity, now, Observations.Now, _gazed);
                            if (t != null) cues.Add(t.Cue);
                        }
                        _streetEquity = now;
                        break;
                    }
                    case HandEnded _:
                        LastReadBelief = Read.Belief;
                        LastReadOutcome = ReadOutcome.None;
                        // 攤牌看到玩家的牌（而且我也攤了牌）：照判斷打過的就學（被騙就打折）
                        if (_humanShown != null && IAmLive() && Observations.Board.Count == 5)
                            LastReadOutcome = Read.Learn(PlayerWasStrong(_humanShown));
                        if (Observations.MyHole != null)
                        {
                            bool won = WonThisHand();
                            if (won || _sawFlop || Observations.Stats(Seat).VpipThisHand) cues.Add(Tells.OnHandEnd(won, Observations.Now));
                            LastReveal = HandReveal.Build(Observations.HandNumber, Seat, Personality.Name, Observations.MyHole, _moments);
                        }
                        break;
                }
            }
            return cues;
        }

        /// <summary>吃玩家的肢體訊號：更新讀心並回傳反應演出（沒有要演或反應關閉時為 null）。</summary>
        public PerformanceCue OnPlayerSignals(PlayerSignals s)
        {
            var n = Read.Observe(s);
            _gazed = s.GazeSeat == Seat && s.GazeSeconds >= Read.Settings.GazeSeconds;
            Observations.Now = Math.Max(Observations.Now, s.Time);
            if (!Read.Settings.ReactionsEnabled) return null;
            return Tells.OnPlayerNotice(n, s.Time);
        }

        // ───────── 內部 ─────────

        float ThinkTime(AiDecision d, BotView v)
        {
            var p = Personality;
            double t = p.ThinkBase + p.ThinkSpread * d.Difficulty + 0.4 * Equity.Next01(_reactRng);
            if (v.Legal.CanCall && v.Legal.CallAmount > v.Pot / 2) t += 0.3;
            if (d.Command is AllIn) t += 0.3;
            if (p.Archetype == Archetype.Rookie && d.Intent == Intent.Bluff) t += 0.4;
            return (float)Math.Max(0.8, Math.Min(3.5, t));
        }

        bool IAmLive()
        {
            if (Observations.MyHole == null) return false;
            return !Observations.Stats(Seat).Folded;
        }

        int LiveOpponentCount()
        {
            int n = 0;
            foreach (var s in Observations.SeatsInHand) if (s != Seat && !Observations.Stats(s).Folded) n++;
            return n;
        }

        double ReactionEquity()
        {
            var hole = Observations.MyHole;
            int n = LiveOpponentCount();
            if (hole == null || n == 0) return 1.0;
            return Equity.Estimate(hole[0], hole[1], Observations.Board, n, Math.Max(1, _settings.ReactionTrials), _reactRng);
        }

        /// <summary>攤牌的玩家算不算有牌：兩對以上；一對要贏了才算（一對輸給我＝沒牌）；高牌不算。</summary>
        bool PlayerWasStrong(IReadOnlyList<Card> hole)
        {
            var cards = new List<Card>(hole);
            cards.AddRange(Observations.Board);
            var cat = HandEvaluator.Evaluate(cards).Category;
            if (cat >= HandCategory.TwoPair) return true;
            if (cat < HandCategory.Pair) return false;
            foreach (var e in Observations.HandEvents)
                if (e is PotAwarded pa)
                    foreach (var sh in pa.Shares) if (sh.Seat == HumanSeat && sh.Amount > 0) return true;
            return false;
        }

        bool WonThisHand()
        {
            foreach (var e in Observations.HandEvents)
                if (e is PotAwarded pa)
                    foreach (var sh in pa.Shares) if (sh.Seat == Seat && sh.Amount > 0) return true;
            return false;
        }

        static string Describe(PlayerCommand c)
        {
            switch (c)
            {
                case Bet b: return "Bet " + b.Amount;
                case Raise r: return "Raise to " + r.ToAmount;
                case AllIn _: return "All-in";
                case Call _: return "Call";
                case Check _: return "Check";
                default: return "Fold";
            }
        }
    }
}
