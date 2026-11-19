using System;
using System.Collections.Generic;
using System.Linq;
using Poker.AI;
using Poker.Core;

namespace Poker.Game
{
    /// <summary>牌局設定（可在 Inspector 或測試裡改）。</summary>
    [Serializable]
    public sealed class GameConfig
    {
        /// <summary>每枚籌碼的面額（只有一種）。</summary>
        public int Denomination = 10;
        /// <summary>起始籌碼枚數。</summary>
        public int StartChipCount = 100;
        /// <summary>第 0 級的盲注；之後依 Schedule 升級。</summary>
        public int SmallBlind = 5;
        public int BigBlind = 10;
        /// <summary>牌局種子；0 = 每次開遊戲由 GameController 抽新種子（正式場景用 0），非 0 = 固定可重播。</summary>
        public ulong Seed = 20261015;
        /// <summary>每局第一手的按鈕位（null = 交給 Core 決定，也就是 0 號座位）。之後每手自動輪轉。</summary>
        public int? FirstButton;
        public int HumanSeat = 0;
        public int MonteCarloTrials = 1000;
        /// <summary>一手結束後自動開下一手的秒數（玩家也可以敲桌提早開始）。</summary>
        public float NextHandSeconds = 3f;
        /// <summary>AI 對手，依序坐 1、2、3（、4）號座位（玩家左手邊起順時針）。只放一個就是一對一；FiveHanded＝加上荷官右側的老手。</summary>
        public Archetype[] Opponents = { Archetype.Rookie, Archetype.Loudmouth, Archetype.Cold };
        /// <summary>各座位的起始籌碼枚數（null = 全部 StartChipCount）。測試邊池用。</summary>
        public int[] StartChipCounts;
        /// <summary>一局的節奏（盲注升級、手數與時間上限）。</summary>
        public MatchSchedule Schedule = new MatchSchedule();
        /// <summary>一局結束後結算看板停留的秒數（玩家敲桌可以提早開新局）。</summary>
        public float SummarySeconds = 20f;

        public int StartStack => Denomination * StartChipCount;
        public int SeatCount => 1 + (Opponents == null || Opponents.Length == 0 ? 1 : Opponents.Length);

        public static readonly Archetype[] HeadsUp = { Archetype.Rookie };
        /// <summary>真實尺寸桌的 5 人局（2026-11-02）：Pip、Rocco、老手 Silas（荷官右側）、Vex，依座位順序。目前只有寫實預覽用（MainSceneBuilder）。</summary>
        public static readonly Archetype[] FiveHanded = { Archetype.Rookie, Archetype.Loudmouth, Archetype.Veteran, Archetype.Cold };
    }

    /// <summary>
    /// 牌局的規則層（不含畫面）：包住 Core 的狀態與每個 AI 座位的 AiPlayer，處理開新手（盲注依賽程）、套用指令、
    /// 把事件依座位過濾後餵給各 AI、記戰績、判斷一局結束與重開。GameController 與測試都透過它操作牌局。
    /// </summary>
    public sealed class GameSession
    {
        public GameConfig Config { get; }
        public HandState State { get; private set; }
        public int HumanSeat => Config.HumanSeat;
        public int SeatCount { get; }
        public IReadOnlyList<int> AiSeats { get; }
        /// <summary>第一個 AI（一對一時的 Pip）。</summary>
        public AiPlayer Ai => _ais[AiSeats[0]];
        public GameStats Stats { get; }
        /// <summary>第幾局（1 起算）。</summary>
        public int GameNumber { get; private set; } = 1;
        public int MatchNumber => GameNumber;
        public int HandsStarted { get; private set; }
        public int HandsPlayed { get; private set; }
        public int HandsThisGame { get; private set; }
        /// <summary>這一局開始時的牌局時鐘（秒）。</summary>
        public double GameStartedAt { get; private set; }
        public IReadOnlyList<Card> PlayerHole => HoleOf(HumanSeat);
        /// <summary>第一個 AI 的底牌（一對一相容）。</summary>
        public IReadOnlyList<Card> AiHole => HoleOf(AiSeats[0]);
        /// <summary>最近一次 Apply／StartHand／SignalPlayer 時各 AI 的反應（PerformanceCue.Seat 標明是誰）。</summary>
        public IReadOnlyList<PerformanceCue> LastAiCues { get; private set; } = Array.Empty<PerformanceCue>();
        /// <summary>下一手用的固定牌（教學用），用完自動清掉。</summary>
        public CardPreset NextHandPreset { get; set; }
        /// <summary>下一手指定的按鈕位（教學用），用完自動清掉。</summary>
        public int? NextHandButton { get; set; }
        /// <summary>最近一次 DecideAi 在哪個執行緒跑（測試用：確認不在主執行緒）。</summary>
        public int LastDecideThread { get; private set; }
        /// <summary>整桌的台詞協調：大家都有反應（長考、輸贏、翻牌）時只挑一個 AI 開口。</summary>
        public TableTalk Talk { get; }

        readonly AiPlayer[] _ais;
        readonly IReadOnlyList<Card>[] _holes;

        public GameSession(GameConfig config)
        {
            Config = config ?? new GameConfig();
            SeatCount = Config.SeatCount;
            var opps = Config.Opponents == null || Config.Opponents.Length == 0 ? GameConfig.HeadsUp : Config.Opponents;
            _ais = new AiPlayer[SeatCount];
            _holes = new IReadOnlyList<Card>[SeatCount];
            var seats = new List<int>();
            var settings = new AiSettings { MonteCarloTrials = Config.MonteCarloTrials, ReactionTrials = Math.Max(30, Config.MonteCarloTrials / 5) };
            int k = 0;
            for (int seat = 0; seat < SeatCount; seat++)
            {
                _holes[seat] = Array.Empty<Card>();
                if (seat == HumanSeat) continue;
                ulong seed = Config.Seed * 31 + 7 + (ulong)k * 1009;
                _ais[seat] = new AiPlayer(Personality.For(opps[k]), seat, seed, settings) { HumanSeat = HumanSeat };
                seats.Add(seat);
                k++;
            }
            AiSeats = seats.AsReadOnly();
            Talk = new TableTalk(Config.Seed * 131 + 3);
            foreach (var seat in AiSeats) _ais[seat].Tells.Table = Talk;
            Stats = new GameStats(HumanSeat, s => AiAt(s)?.LastReveal);
            State = HandState.NewTable(StartStacks());
        }

        int[] StartStacks()
        {
            var r = new int[SeatCount];
            for (int i = 0; i < SeatCount; i++)
            {
                int chips = Config.StartChipCounts != null && i < Config.StartChipCounts.Length ? Config.StartChipCounts[i] : Config.StartChipCount;
                r[i] = Math.Max(0, chips) * Config.Denomination;
            }
            return r;
        }

        public AiPlayer AiAt(int seat) => seat >= 0 && seat < _ais.Length ? _ais[seat] : null;
        public IReadOnlyList<Card> HoleOf(int seat) => _holes[seat];
        public string NameOf(int seat) => seat == HumanSeat ? "You" : AiAt(seat)?.Personality.Name;

        public bool IsHandOver => State.Phase == HandPhase.Idle;
        public bool IsPlayerTurn => State.Phase == HandPhase.Betting && State.ToAct == HumanSeat;
        /// <summary>
        /// 還沒輪到玩家，但這一手之後一定還會輪到（可以提前棄牌、先拿籌碼）：玩家還在局、沒 All-in，而且
        /// 這條街還要行動（面對下注，或還沒行動過且有對手能行動），或不是河牌、還有對手能行動（下一條街會再輪到）。
        /// 河牌已經行動過、沒人加注就直接攤牌：不收提前棄牌（記下了也沒機會執行）。
        /// </summary>
        public bool PlayerCanActEarly
        {
            get
            {
                if (State.Phase != HandPhase.Betting || State.ToAct == HumanSeat) return false;
                var me = State.Seat(HumanSeat);
                if (me.Status != SeatStatus.Active) return false;
                if (me.StreetBet < State.CurrentBet) return true;
                bool othersCanAct = false;
                for (int i = 0; i < State.SeatCount; i++)
                    if (i != HumanSeat && State.Seat(i).Status == SeatStatus.Active) othersCanAct = true;
                if (!othersCanAct) return false;
                return !me.HasActed || State.Street != Street.River;
            }
        }
        public bool IsAiTurn => State.Phase == HandPhase.Betting && AiAt(State.ToAct) != null;
        public int TotalChips => State.TotalChips();
        public int Pot { get { int p = 0; for (int i = 0; i < State.SeatCount; i++) p += State.Seat(i).Committed; return p; } }
        public int StackOf(int seat) => State.Seat(seat).Stack;

        public LegalActions Legal(int seat) => Engine.GetLegalActions(State, seat);

        /// <summary>玩家丟出 chipCount 枚籌碼的意思（跟注、加注、全下……），不改狀態。</summary>
        public ChipCommit InterpretChips(int chipCount) =>
            ChipCommit.Interpret(State, HumanSeat, chipCount, Config.Denomination);

        /// <summary>這一局是否該結束（一手打完才有意義）。now = 牌局時鐘秒數。</summary>
        public GameEndReason CheckGameOver(double now)
        {
            if (!IsHandOver || HandsThisGame == 0) return GameEndReason.None;
            return Config.Schedule.Check(HandsThisGame, now - GameStartedAt, State.Stacks(), HumanSeat);
        }

        public GameSummary Summarize(GameEndReason reason = GameEndReason.None) =>
            Stats.Summarize(State.Stacks(), reason == GameEndReason.None ? CheckGameOver(double.MaxValue) : reason);

        /// <summary>重開一局：籌碼重設、盲注回到第 0 級、戰績清空。</summary>
        public void NewGame(double now)
        {
            if (!IsHandOver) throw new InvalidOperationException("這一手還沒結束");
            State = HandState.NewTable(StartStacks());
            if (HandsThisGame > 0 || HandsStarted > 0) GameNumber++;
            HandsThisGame = 0;
            GameStartedAt = now;
            Stats.Reset();
        }

        /// <summary>
        /// 重設牌桌但不算新的一局（教學前後用，2026-10-13 教學 v3）：籌碼回到起始、盲注回到第 0 級、這一局的戰績清空、局數不變。
        /// 進行中的一手直接作廢（從選單跳過教學時）。
        /// </summary>
        public void ResetTable(double now)
        {
            State = HandState.NewTable(StartStacks());
            for (int i = 0; i < SeatCount; i++) _holes[i] = Array.Empty<Card>();
            HandsThisGame = 0;
            GameStartedAt = now;
            NextHandPreset = null;
            NextHandButton = null;
            Stats.Reset();
        }

        /// <summary>false：這一手不算戰績（不進 Stats、不算 HandsPlayed）。插在牌局中間的情境（Fool Rocco）用。</summary>
        public bool RecordStats { get; set; } = true;

        /// <summary>存下牌桌（一手打完時呼叫）：籌碼、這一局第幾手、這一局的計時。情境結束用 RestoreTable 回到這裡。</summary>
        public TableSnapshot SaveTable(double now)
        {
            if (!IsHandOver) throw new InvalidOperationException("這一手還沒結束");
            return new TableSnapshot(State, HandsThisGame, GameStartedAt, now);
        }

        /// <summary>
        /// 情境的一手用乾淨的牌桌（2026-10-14 Fool Rocco）：籌碼回到起始、盲注第 0 級、計時從現在起；
        /// 和 ResetTable 不同的是戰績不清（情境的手本來就不記，見 RecordStats），之後由 RestoreTable 還原。
        /// </summary>
        public void ScenarioTable(double now)
        {
            if (!IsHandOver) throw new InvalidOperationException("這一手還沒結束");
            State = HandState.NewTable(StartStacks());
            for (int i = 0; i < SeatCount; i++) _holes[i] = Array.Empty<Card>();
            HandsThisGame = 0;
            GameStartedAt = now;
            NextHandPreset = null;
            NextHandButton = null;
        }

        /// <summary>回到 SaveTable 存下的牌桌；這一局的計時不算情境用掉的時間。</summary>
        public void RestoreTable(TableSnapshot snap, double now)
        {
            if (snap == null) throw new ArgumentNullException(nameof(snap));
            if (!IsHandOver) throw new InvalidOperationException("這一手還沒結束");
            State = snap.State;
            for (int i = 0; i < SeatCount; i++) _holes[i] = Array.Empty<Card>();
            HandsThisGame = snap.HandsThisGame;
            GameStartedAt = snap.GameStartedAt + Math.Max(0, now - snap.SavedAt);
            NextHandPreset = null;
            NextHandButton = null;
        }

        /// <summary>開新的一手；這一局已經結束（或沒人能打）就先重開一局。回傳這一手開始的事件（含玩家看不到的 AI 底牌事件）。</summary>
        public IReadOnlyList<GameEvent> StartHand(double now = 0)
        {
            if (!IsHandOver) throw new InvalidOperationException("上一手還沒結束");
            if (CheckGameOver(now) != GameEndReason.None || State.Stacks().Count(s => s > 0) < 2) NewGame(now);
            ulong seed = Config.Seed * 1000003UL + (ulong)HandsStarted;
            int? button = NextHandButton ?? (HandsThisGame == 0 ? Config.FirstButton : null);
            var (sb, bb) = Config.Schedule.BlindsFor(HandsThisGame, Config.SmallBlind, Config.BigBlind);
            var r = Engine.Apply(State, new StartHand(seed, button, sb, bb, NextHandPreset));
            NextHandPreset = null;
            NextHandButton = null;
            if (!r.IsSuccess) throw new InvalidOperationException("開局失敗：" + r.Error);
            HandsStarted++;
            HandsThisGame++;
            State = r.State;
            for (int i = 0; i < SeatCount; i++)
            {
                var s = State.Seat(i);
                _holes[i] = s.Status == SeatStatus.Out ? (IReadOnlyList<Card>)Array.Empty<Card>() : new[] { s.Hole1, s.Hole2 };
            }
            Absorb(r.Events);
            return r.Events;
        }

        /// <summary>套用指令。失敗時狀態不變、AI 不收到任何事件。</summary>
        public ApplyResult Apply(Command command)
        {
            var r = Engine.Apply(State, command);
            if (!r.IsSuccess) { LastAiCues = Array.Empty<PerformanceCue>(); return r; }
            State = r.State;
            Absorb(r.Events);
            return r;
        }

        /// <summary>輪到的 AI 做決策（now = 牌局時鐘秒數，給讀人衰減與台詞冷卻用）。可在背景執行緒呼叫。</summary>
        public AiDecision DecideAi(double now)
        {
            LastDecideThread = Environment.CurrentManagedThreadId;
            var ai = AiAt(State.ToAct) ?? throw new InvalidOperationException("輪到的不是 AI：" + State.ToAct);
            ai.Observations.Now = Math.Max(ai.Observations.Now, now);
            return ai.Decide(State);
        }

        /// <summary>玩家的肢體訊號送給每個 AI（注視只影響被盯的那個），回傳各 AI 的反應。</summary>
        public IReadOnlyList<PerformanceCue> SignalPlayer(PlayerSignals s)
        {
            var cues = new List<PerformanceCue>();
            foreach (var seat in AiSeats)
            {
                var c = _ais[seat].OnPlayerSignals(s);
                if (c != null) cues.Add(c);
            }
            Talk.Resolve();
            return cues;
        }

        void Absorb(IReadOnlyList<GameEvent> events)
        {
            // 每個 AI 只收到自己座位看得到的事件（不偷看別人的底牌）
            var cues = new List<PerformanceCue>();
            foreach (var seat in AiSeats)
                foreach (var c in _ais[seat].OnEvents(PlayerView.Filter(events, seat).ToList()))
                    if (c != null) cues.Add(c);
            Talk.Resolve();
            LastAiCues = cues;
            // 戰績在 AI 之後（一手結束時要用 AI 剛產生的 HandReveal）；情境的手不記
            if (!RecordStats) return;
            foreach (var e in events)
            {
                Stats.Observe(e);
                if (e is HandEnded) HandsPlayed++;
            }
        }
    }

    /// <summary>GameSession.SaveTable 存下的牌桌（不可變的 HandState 直接留著）。</summary>
    public sealed class TableSnapshot
    {
        internal readonly HandState State;
        internal readonly int HandsThisGame;
        internal readonly double GameStartedAt, SavedAt;
        internal TableSnapshot(HandState state, int handsThisGame, double startedAt, double savedAt)
        {
            State = state; HandsThisGame = handsThisGame; GameStartedAt = startedAt; SavedAt = savedAt;
        }
        public IReadOnlyList<int> Stacks => State.Stacks();
    }
}
