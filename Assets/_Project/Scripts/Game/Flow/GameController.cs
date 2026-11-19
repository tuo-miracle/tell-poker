using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Poker.AI;
using Poker.Characters;
using Poker.Core;
using Poker.Gestures;
using Poker.Runtime;
using UnityEngine;
using Hand = Poker.Gestures.Hand;

namespace Poker.Game
{
    public enum GameFlow { NotStarted, Presenting, PlayerTurn, AiThinking, ShowdownWait, HandOver, GameOver }

    public enum InvalidReason { CannotCheck, InvalidBet, NotYourTurn }

    /// <summary>
    /// MR 德州撲克的流程（玩家對 Pip／Rocco／Vex，荷官 Dot；也支援一對一）：開一手 → 荷官發牌 → 依 Core 的 ToAct 輪流
    /// （玩家用手勢、AI 在背景執行緒決策）→ 攤牌／棄牌後亮出所有 AI 的牌 → 底池（含邊池）推給贏家 → 下一手；
    /// 一局打到賽程結束就顯示結算。第一次玩時荷官帶第一手（見 GameController.Meta.cs）。
    /// 所有時間都由 <see cref="Step"/> 推進（Update 呼叫它；測試直接呼叫，用合成手勢）。
    /// </summary>
    public sealed partial class GameController : MonoBehaviour
    {
        public GameConfig Config = new GameConfig();
        public TableFrame Table;
        /// <summary>實作 IHandsFrameSource 的元件（Main 場景是 HandsSourceSelector）。</summary>
        public MonoBehaviour HandsSource;
        /// <summary>玩家的頭（注視射線、角色看向誰）。沒指定就用 Camera.main。</summary>
        public Transform Head;
        public bool AutoStep = true;
        /// <summary>AI 思考時間倍率（測試、展示用）。</summary>
        public float AiThinkScale = 1f;
        /// <summary>放開籌碼的水平速度 ≥ 這個值算「丟」（Thrown），否則算「放」。</summary>
        public float ThrowSpeedThreshold = 0.5f;
        /// <summary>攤牌時等玩家翻牌的秒數，逾時自動翻。</summary>
        public float ShowdownFlipTimeout = 5f;
        public GestureTuning Tuning = new GestureTuning();

        /// <summary>有設定就由它替玩家出手（不用手勢）：展示與測試用。</summary>
        public Func<HandState, PlayerCommand> AutoPlayer;
        /// <summary>每批 Core 事件（含開局）都會傳給它，讓自動玩家追蹤牌局。</summary>
        public Action<IReadOnlyList<GameEvent>> AutoPlayerObserver;
        /// <summary>取代 AI 決策（測試用）。</summary>
        public Func<HandState, PlayerCommand> AiOverride;

        public event Action<int, PlayerCommand> ActionApplied;
        public event Action<InvalidReason> InvalidAction;
        public event Action<PlayerSignals, PerformanceCue> SignalsSent;
        public event Action<GestureEvent> GestureSeen;
        public event Action<int> HandFinished;
        /// <summary>某個座位的角色收到一條表演指令（座位、指令）。</summary>
        public event Action<int, PerformanceCue> CuePlayed;
        /// <summary>每批 Core 事件（開局、每次行動）。含只給特定座位看的事件，訂閱者要自己用 PlayerView 過濾（語音對話的行動紀錄用）。</summary>
        public event Action<IReadOnlyList<GameEvent>> CoreEvents;

        public GameSession Session { get; private set; }
        public GameFlow Flow { get; private set; } = GameFlow.NotStarted;
        public double Now { get; private set; }
        /// <summary>Pip（新手）；一對一時就是唯一的 AI。</summary>
        public CharacterPerformer Pip { get; private set; }
        public CharacterPerformer Dealer { get; private set; }
        public GestureRecognizer Recognizer { get; private set; }
        public TableProps Props { get; private set; }
        public TableSpots Spots { get; private set; }
        public GameAudio Audio { get; private set; }
        public DealerVoice Voice { get; } = new DealerVoice();
        /// <summary>整桌的發言權：同時只有一個角色在講（見 SpeechFloor）。</summary>
        public SpeechFloor Floor { get; private set; }
        public int GazeSignalsSent { get; private set; }
        /// <summary>玩家的底牌正拿在手上（跟著手、牌面朝向頭）。</summary>
        public bool CardsInHand => _held != null && _held.Active;
        /// <summary>還沒輪到時做了棄牌手勢：牌已經進棄牌堆，輪到時自動送 Fold（這一手先結束就作廢）。</summary>
        public bool PreFoldQueued { get; private set; }
        /// <summary>「Wait for your turn.」兩次之間至少隔這麼久（秒）；冷卻中只有籌碼飛回／提示音。</summary>
        public const float WaitForTurnCooldown = 10f;
        public bool IsInitialized => Session != null;
        /// <summary>所有亮牌標題（一手結束後）。</summary>
        public string RevealHeadline
        {
            get
            {
                if (Props == null) return "";
                var parts = new List<string>();
                foreach (var l in Props.SeatHeadline) if (!string.IsNullOrEmpty(l.Text)) parts.Add(l.Text);
                return string.Join(" / ", parts);
            }
        }
        public AiDecision LastDecision { get; private set; }

        public CardView PlayerCard(int i) => Props.PlayerCards[i];
        public CardView AiCard(int i) => Props.AiCards[i];
        public CardView BoardCard(int i) => Props.Board[i];
        public CardView[] CardsOf(int seat) => Props.SeatCards[seat];
        /// <summary>座位上的角色（0 號是玩家 → null）。</summary>
        public CharacterPerformer Character(int seat) => seat > 0 && seat < _chars.Length ? _chars[seat] : null;
        public int SeatCount => Session.SeatCount;

        int Human => Config.HumanSeat;
        CharacterPerformer[] _chars = Array.Empty<CharacterPerformer>();

        readonly Sequencer _seq = new Sequencer();
        readonly GazeTracker _gaze = new GazeTracker();
        readonly List<GestureEvent> _events = new List<GestureEvent>();
        IHandsFrameSource _source;

        // 顯示用的籌碼（金額，依座位）：動畫過程中和 Core 不同步，安靜時對齊 Core。
        int[] _stack = Array.Empty<int>(), _bet = Array.Empty<int>();
        int _pot, _inFlight;
        // 玩家手上／下注區待確認／正在飛回籌碼堆的枚數
        readonly int[] _heldBy = new int[2]; // 依 Hand 索引：兩隻手各自拿的枚數
        int _pending, _returning;
        Vector3 _pendingPos;
        bool _pendingFlying, _playerFolded;
        // 拿在手上看的底牌
        HeldCards _held;
        Quaternion _dealerHeadRest;
        Task<AiDecision> _aiTask;
        int _aiSeat = -1; // 正在想的 AI
        readonly List<PlayerSignals> _queuedSignals = new List<PlayerSignals>();
        int HeldTotal => _heldBy[0] + _heldBy[1];
        readonly List<FreeFlight> _free = new List<FreeFlight>();

        // 每一手、每一回合的讀人資料
        double _turnStart, _firstPick = -1;
        float _peekAmount, _peekSeconds, _lastReleaseSpeed;
        ChipDelivery _lastDelivery;
        float _think, _autoT, _handOverT, _shakeT = -1f;
        AiDecision _pendingAi;
        PlayerCommand _pendingAiCommand;
        bool _flipRequested, _aiRevealed, _headlinesShown;
        /// <summary>攤牌時推、拖、往前放開而來的亮牌：兩張牌中心要翻開在哪（牌桌本地座標）；原位翻牌時為 null。</summary>
        Vector3? _revealAt;
        // 提前棄牌：牌飛進棄牌堆的動畫（不排進 _seq，不擋 AI 的流程）；上次說「Wait for your turn.」的時間
        float _preMuckT = -1f;
        readonly Vector3[] _preMuckFrom = new Vector3[2];
        double _waitSaidAt = double.NegativeInfinity;
        int _boardCount;
        readonly Dictionary<Hand, PinchHistory> _pinch = new Dictionary<Hand, PinchHistory>
        {
            { Hand.Left, new PinchHistory() }, { Hand.Right, new PinchHistory() },
        };

        sealed class PinchHistory
        {
            public readonly Queue<(double t, Vector3 p)> Samples = new Queue<(double, Vector3)>();
            public Vector3 Last;
            public bool Valid;
        }

        sealed class FreeFlight
        {
            public ChipPile Pile;
            public Vector3 From, To;
            public float T, Duration, Arc;
            public Action Done;
            public bool Pending;
        }

        // ================= 生命週期 =================

        void Start()
        {
            if (IsInitialized) return;
            if (Table == null) Table = FindAnyObjectByType<TableFrame>();
            if (Table == null) { Debug.LogError("[Game] 找不到 TableFrame"); enabled = false; return; }
            if (Table.IsPlaced) Init();
            else Table.Placed += InitOnce;
        }

        void InitOnce(TableFrame f)
        {
            f.Placed -= InitOnce;
            Init();
        }

        void Update()
        {
            if (!AutoStep || !IsInitialized) return;
            Step(Time.deltaTime, _source?.Current);
        }

        void OnDestroy()
        {
            if (Table != null) { Table.Placed -= OnTablePlaced; Table.Placed -= InitOnce; Table.RoomChanged -= OnRoomChanged; }
            if (IsInitialized) Playtest.SessionEnd();
        }

        void OnApplicationQuit()
        {
            if (IsInitialized) Playtest.SessionEnd();
        }

        /// <summary>試玩分析用的 log（[Playtest] …，見 PlaytestLog）。</summary>
        public PlaytestLog Playtest { get; } = new PlaytestLog();
        /// <summary>試玩 log 的時鐘（預設開機後的真實秒數；測試換成遊戲時間）。</summary>
        public Func<double> PlaytestClock { get => Playtest.Clock; set => Playtest.Clock = value; }

        /// <summary>建立角色、籌碼、牌與規則層。可重複呼叫（只做一次）。</summary>
        public void Init()
        {
            if (IsInitialized) return;
            if (Table == null) Table = FindAnyObjectByType<TableFrame>();
            if (Head == null && Camera.main != null) Head = Camera.main.transform;
            _source = HandsSource as IHandsFrameSource;

            Config.Seed = SeedSource.ForSession(Config.Seed);
            Debug.Log("[Table] session seed " + Config.Seed);
            Session = new GameSession(Config);
            int n = Session.SeatCount;
            _stack = new int[n];
            _bet = new int[n];
            var kinds = new CharacterKind[n];
            foreach (var seat in Session.AiSeats) kinds[seat] = KindOf(Session.AiAt(seat).Personality.Archetype);
            Spots = new TableSpots(TableDimensions.Layout, kinds);
            Recognizer = new GestureRecognizer(TableDimensions.Layout, Tuning);
            // 實機調參用：手勢「為什麼沒觸發」的診斷寫進 logcat（adb logcat -s Unity | grep "\[Gesture\]"）
            // 教學也拿來判斷「差一點」的原因，給具體的提示（見 Tutorial.ReasonFromTrace）
            Recognizer.Trace = msg => { Debug.Log("[Gesture] " + msg); OnGestureTrace(msg); };
            Props = new TableProps(Table.transform, Spots, Config.Denomination);
            Audio = new GameAudio(Props.Root);
            InitMix();
            _held = new HeldCards(Props.PlayerCards, i => Spots.PlayerCard(i) + Vector3.up * TableProps.CardY);

            var root = Table.transform;
            _chars = new CharacterPerformer[n];
            foreach (var seat in Session.AiSeats)
            {
                var ps = Spots.SeatPose(seat);
                var c = CharacterFactory.Create(kinds[seat], root, ps.position, ps.rotation, (int)((Config.Seed + (ulong)seat * 977) % 100000));
                _chars[seat] = c;
                int mine = seat;
                c.LookPointResolver = (t, other) => ResolveLook(mine, t, other);
                if (Pip == null || kinds[seat] == CharacterKind.Rookie && Pip.Kind != CharacterKind.Rookie) Pip = c;
            }
            var ds = Spots.DealerSeat;
            Dealer = CharacterFactory.Create(CharacterKind.Dealer, root, ds.position, ds.rotation, 4242);
            Dealer.LookPointResolver = (t, other) => ResolveLook(-1, t, other);
            foreach (var c in AllCharacters())
            {
                c.AutoUpdate = false;
                c.SetPropsVisible(false);
                if (Head != null) c.SetViewer(Head);
            }
            Voice.Bind(Dealer);
            // 整桌一個發言權（2026-10-13 實機：四個角色同時開口）；大家都有反應時只挑一個 AI 講（TableTalk）
            Floor = new SpeechFloor();
            foreach (var c in AllCharacters()) Floor.Join(c.Speech);
            Session.Talk.Trace = m => Debug.Log("[Speech] " + m);
            _dealerHeadRest = Dealer.Rig.Head.localRotation;
            InitMeta();
            InitBetLabel();
            InitHints();
            InitVolume();
            InitFoolRocco();
            InitLiveTalk();
            InitSeating();
            InitHandHint();

            _gaze.GazeStarted += OnGazeStarted;
            _gaze.GazeEnded += OnGazeEnded;
            Table.Placed += OnTablePlaced;
            OnTablePlaced(Table);
        }

        void OnTablePlaced(TableFrame f)
        {
            Props?.SetOccluderActive(f.Source != TablePlacementSource.Virtual && f.Source != TablePlacementSource.None);
        }

        /// <summary>AI 原型坐哪種角色的座位（TableSeats）。</summary>
        public static CharacterKind KindOf(Archetype a) =>
            a == Archetype.Rookie ? CharacterKind.Rookie : a == Archetype.Loudmouth ? CharacterKind.Loudmouth : a == Archetype.Veteran ? CharacterKind.Veteran : CharacterKind.Cold;

        /// <summary>牌桌上的所有角色（AI 與荷官）。</summary>
        public IEnumerable<CharacterPerformer> AllCharacters()
        {
            foreach (var c in _chars) if (c != null) yield return c;
            if (Dealer != null) yield return Dealer;
        }

        /// <summary>角色要看的點（mine = 角色自己的座位，荷官 = -1）。</summary>
        Vector3? ResolveLook(int mine, LookTarget t, int other)
        {
            var root = Table.transform;
            switch (t)
            {
                case LookTarget.Pot: return root.TransformPoint(Spots.PotPile);
                case LookTarget.Board: return root.TransformPoint(Spots.Board(2));
                case LookTarget.OwnCards: return mine > 0 ? root.TransformPoint(Spots.CardOf(mine, 0)) : (Vector3?)null;
                case LookTarget.OwnChips: return mine > 0 ? root.TransformPoint(Spots.StackOf(mine)) : (Vector3?)null;
                case LookTarget.OtherSeat:
                    var c = Character(other);
                    if (c != null && other != mine) return c.Rig.Head.position;
                    return Dealer != null && mine >= 0 ? Dealer.Rig.Head.position : (Vector3?)null;
                default: return null;
            }
        }

        // ================= 每幀 =================

        public void Step(float dt, HandsFrame frame)
        {
            if (!IsInitialized) return;
            Now += dt;
            TickPresence(frame);
            if (Flow == GameFlow.NotStarted) StartNextHand();

            if (frame != null) ProcessHands(dt, frame);
            TickLiveTalk(dt, frame);

            switch (Flow)
            {
                case GameFlow.PlayerTurn when AutoPlayer != null:
                    _autoT += dt;
                    if (_autoT >= 0.5f * AiThinkScale) PlayAuto();
                    break;
                case GameFlow.AiThinking:
                    if (_aiTask != null)
                    {
                        if (!_aiTask.IsCompleted) break;
                        OnAiDecided();
                    }
                    if (TutorialHoldsAi || FoolRoccoHoldsAi) break; // 教學：玩家還在學咪牌、看牌；Fool Rocco：Dot／Rocco 還在講，劇本先停住
                    _think -= dt;
                    if (_think <= 0f) ExecuteAi();
                    break;
                case GameFlow.HandOver:
                    if (!TutorialHoldsHandOver && !FoolRoccoHoldsHandOver) _handOverT += dt; // 教學的講話那一步、Fool Rocco 的收尾還沒完：先不開下一手
                    if (_handOverT >= Config.NextHandSeconds) StartNextHand();
                    break;
                case GameFlow.GameOver:
                    TickGameOver(dt);
                    break;
            }
            TickTutorial(dt);
            TickFoolRocco(dt);

            _seq.Tick(dt);
            _held.Tick(dt);
            TickPreFoldMuck(dt);
            if (Flow == GameFlow.Presenting && _seq.IsIdle) NextTurn();

            TickSeating();
            TickFreeFlights(dt);
            LayOutChips(frame);
            TickGaze(dt);
            TickCharacters(dt);
            Props.Tick(dt);
            TickHints(dt);
            TickVolume(dt);
            TickMix(dt);
        }

        // ================= 一手的流程 =================

        void StartNextHand()
        {
            // 等下一手的時候時間到了：先結算，不要讓 Session 悄悄重開一局（Fool Rocco 的情境用自己的牌桌，不檢查）
            if (Session.HandsStarted > 0 && Flow != GameFlow.GameOver && !FoolRoccoOwnsTable)
            {
                var over = Session.CheckGameOver(Now);
                if (over != GameEndReason.None) { EnterGameOver(over); return; }
            }
            _seq.Clear();
            ClearFreeFlights();
            Props.HideAllCards();
            Props.HideHeadlines();
            Props.Hint(false);
            Recognizer.Reset();
            _held.Clear();
            _heldBy[0] = _heldBy[1] = 0;
            _pending = _returning = 0;
            _playerFolded = false;
            PreFoldQueued = false;
            _preMuckT = -1f;
            _peekAmount = 0f; _peekSeconds = 0f;
            _flipRequested = false; _revealAt = null; _aiRevealed = false; _headlinesShown = false; _boardCount = 0; _liveRevealDone = false;

            bool first = Session.HandsStarted == 0;
            bool newGame = !first && !FoolRoccoOwnsTable && Session.CheckGameOver(Now) != GameEndReason.None;
            BeforeHandStarts();
            BeforeLiveHand();
            BeforeHandHintHand();
            Playtest.HandPlayed();
            var events = Session.StartHand(Now);
            AutoPlayerObserver?.Invoke(events);
            CoreEvents?.Invoke(events);
            for (int s = 0; s < Session.SeatCount; s++)
            {
                var seat = Session.State.Seat(s);
                _stack[s] = seat.Stack + seat.Committed;
                _bet[s] = 0;
            }
            _pot = 0; _inFlight = 0;
            Flow = GameFlow.Presenting;
            if (first) Voice.Say(DealerLines.Greeting(ReplayFirstRun ? null : Progress, Tutorial != null && Tutorial.IsActive), Now, "greeting", force: true);
            else if (newGame) Voice.Say(DealerLines.NewGame, Now, "newgame", force: true);
            PlayAiCues(Session.LastAiCues);
            PresentEvents(events, humanShown: false);
            AfterHandDealt();
        }

        void NextTurn()
        {
            if (Session.IsHandOver) { EnterHandOver(); return; }
            SyncToCore();
            if (Session.IsPlayerTurn) BeginPlayerTurn();
            else if (Session.IsAiTurn) BeginAiTurn();
        }

        void BeginPlayerTurn()
        {
            Flow = GameFlow.PlayerTurn;
            _turnStart = Now;
            _firstPick = -1;
            _autoT = 0f;
            BeginLiveTurn();
            if (PreFoldQueued) { ApplyPreFold(); return; }
            if (InTutorialHand) { OnTutorialPlayerTurn(); return; }
            if (InFoolRoccoHand && OnFoolRoccoPlayerTurn()) return;
            // 第一次用得到某個手勢（可以過牌、拿到好牌）：Dot 教一次，取代「輪到你」
            if (!TipAtPlayerTurn()) Voice.Say(LineTrigger.DealerYourTurn, Now, cooldown: 12f);
            Props.Hint(true);
        }

        void BeginAiTurn()
        {
            Flow = GameFlow.AiThinking;
            _aiSeat = Session.State.ToAct;
            // 教學那一手用腳本化的 AI（放水）；測試可用 AiOverride 取代決策
            int lesson = _tutorialHandNo;
            bool roccoCalls = _tutRoccoCalls;
            var fool = _fool;
            Func<HandState, PlayerCommand> scripted = InTutorialHand ? (st => TutorialScript.Ai(lesson, st, st.ToAct, roccoCalls))
                : InFoolRoccoHand ? (st => fool.Ai(st, st.ToAct)) : AiOverride;
            if (scripted != null)
            {
                _pendingAi = null;
                _pendingAiCommand = scripted(Session.State);
                _think = 0.6f * AiThinkScale;
                return;
            }
            // Monte Carlo 在背景執行緒跑（Quest 上一次可能數十毫秒，主執行緒會掉幀）。
            // HandState 不可變；這段期間對 AI 的其他呼叫（讀人訊號）先排隊，算完再送。
            double now = Now;
            var session = Session;
            _pendingAi = null;
            _pendingAiCommand = null;
            _think = float.MaxValue;
            _aiTask = Task.Run(() => session.DecideAi(now));
        }

        /// <summary>背景執行緒上的 AI 決策還沒算完。</summary>
        public bool AiDecisionRunning => _aiTask != null && !_aiTask.IsCompleted;

        /// <summary>
        /// 等背景 AI 決策算完（最多 timeoutMs）；沒有在算就馬上回傳 true。給同步一幀接一幀跑的測試桌用：
        /// 實機一幀 14 ms、決策幾毫秒就完成，測試桌 1 秒模擬時間只花幾毫秒，不等的話「算完的時間點」隨機器忙不忙而變。
        /// </summary>
        public bool WaitForAiDecision(int timeoutMs)
        {
            var t = _aiTask;
            return t == null || ((System.IAsyncResult)t).AsyncWaitHandle.WaitOne(timeoutMs);
        }

        void OnAiDecided()
        {
            var task = _aiTask;
            _aiTask = null;
            if (task.IsFaulted || task.Result == null)
            {
                Debug.LogError("[Game] AI 決策失敗：" + task.Exception);
                _pendingAiCommand = SafeCommand(_aiSeat);
                _think = 0.5f * AiThinkScale;
            }
            else
            {
                _pendingAi = task.Result;
                LastDecision = _pendingAi;
                LogRead(_pendingAi, _aiSeat);
                _pendingAiCommand = _pendingAi.Command;
                _think = _pendingAi.ThinkSeconds * AiThinkScale;
                if (_pendingAi.Cue != null) PlayCue(_pendingAi.Cue);
            }
            foreach (var s in _queuedSignals) SendSignals(s);
            _queuedSignals.Clear();
        }

        void ExecuteAi()
        {
            int seat = _aiSeat;
            var r = Session.Apply(_pendingAiCommand);
            if (!r.IsSuccess)
            {
                // 保險：AI 的指令理論上都合法；萬一不合法就過牌或棄牌，不讓牌局卡住
                _pendingAiCommand = SafeCommand(seat);
                r = Session.Apply(_pendingAiCommand);
                Debug.LogWarning("[Game] AI 指令不合法，改用 " + _pendingAiCommand);
            }
            _aiSeat = -1;
            ActionApplied?.Invoke(seat, _pendingAiCommand);
            AutoPlayerObserver?.Invoke(r.Events);
            CoreEvents?.Invoke(r.Events);
            PlayAiCues(Session.LastAiCues);
            Flow = GameFlow.Presenting;
            PresentEvents(r.Events, humanShown: false);
        }

        PlayerCommand SafeCommand(int seat)
        {
            var la = Session.Legal(seat);
            return la.CanCheck ? (PlayerCommand)new Check(seat) : new Fold(seat);
        }

        void PlayAuto()
        {
            var cmd = AutoPlayer(Session.State);
            if (!CommitPlayer(cmd, BuildSignals(), null)) CommitPlayer(SafeCommand(Human), null, null);
        }

        void EnterHandOver()
        {
            if (Flow == GameFlow.HandOver || Flow == GameFlow.GameOver) return;
            if (PreFoldQueued)
            {
                // 輪到玩家之前這一手就結束了（別人都棄牌、或沒有再輪到就攤牌）：沒有東西要執行
                PreFoldQueued = false;
                Debug.Log("[Game] pre-action fold dropped: hand ended before the player's turn");
            }
            Flow = GameFlow.HandOver;
            _handOverT = 0f;
            SyncToCore();
            Props.Hint(false);
            HandFinished?.Invoke(Session.State.HandNumber);
            OnHandRecorded();
            OnFoolRoccoHandOver();
            OnLiveHandOver();
            OnTutorialHandOver();
            HideHandHint();
        }

        /// <summary>套用玩家的指令；失敗時不改狀態並回傳 false。presentHuman 負責玩家自己的動畫（下注籌碼、棄牌）。</summary>
        bool CommitPlayer(PlayerCommand cmd, PlayerSignals signals, Action presentHuman)
        {
            var r = Session.Apply(cmd);
            if (!r.IsSuccess) return false;
            Props.Hint(false);
            _jitGhost = null;
            DropQueuedLiveLines();   // 還在排隊的即時台詞過時了
            if (cmd is Call || cmd is Bet || cmd is Raise || cmd is AllIn) Playtest.FirstBet(InTutorialHand, cmd.GetType().Name.ToLowerInvariant());
            // 這一回合的即時訊號（想多久、再看牌、伸手又縮回…）一起給 AI 讀
            FillLiveSignals(signals, cmd);
            // 教學、Fool Rocco 的 AI 是腳本：不讀玩家（也就不會在教學中冒出長考、被嚇到之類的台詞；Fool Rocco 的讀牌由劇本說）
            if (signals != null && !InTutorialHand && !InFoolRoccoHand) SendSignals(signals);
            if (InFoolRoccoHand) OnFoolRoccoPlayerAction(cmd);
            if (InTutorialHand) OnTutorialPlayerAction(cmd, signals);
            Habits.EndTurn(_handsClock);
            ActionApplied?.Invoke(Human, cmd);
            AutoPlayerObserver?.Invoke(r.Events);
            CoreEvents?.Invoke(r.Events);
            PlayAiCues(Session.LastAiCues);
            Flow = GameFlow.Presenting;
            presentHuman?.Invoke();
            PresentEvents(r.Events, humanShown: presentHuman != null);
            return true;
        }

        void Invalid(InvalidReason reason)
        {
            string text = DealerLines.Invalid(reason);
            Audio.Play(SfxKind.Invalid, Table.transform.TransformPoint(Spots.PlayerBet), 0.7f);
            Voice.Say(text, Now, "invalid", force: true, mood: Mood.Annoyed);
            _shakeT = 0f;
            InvalidAction?.Invoke(reason);
        }

        // ================= 讀人訊號 =================

        PlayerSignals BuildSignals() => new PlayerSignals
        {
            PeekSeconds = _peekSeconds,
            HesitationSeconds = (float)((_firstPick >= 0 ? _firstPick : Now) - _turnStart),
            Time = Now,
        };

        void SendSignals(PlayerSignals s)
        {
            if (_aiTask != null) { _queuedSignals.Add(s); return; }
            var cues = Session.SignalPlayer(s);
            SignalsSent?.Invoke(s, cues.Count > 0 ? cues[0] : null);
            foreach (var cue in cues) PlayCue(cue);
        }

        /// <summary>表演指令交給它自己座位的角色。</summary>
        void PlayCue(PerformanceCue cue)
        {
            var c = Character(cue.Seat);
            if (c == null) return;
            c.Play(cue);
            CuePlayed?.Invoke(cue.Seat, cue);
        }

        void PlayAiCues(IReadOnlyList<PerformanceCue> cues)
        {
            // Fool Rocco：AI 對腳本牌的「真實」反應（破綻、台詞）會和劇本打架，不演
            if (cues == null || InFoolRoccoHand) return;
            foreach (var c in cues) if (c != null) PlayCue(c);
        }

        // ================= 手勢 =================

        TableContext BuildContext()
        {
            var ctx = new TableContext { ChipDenomination = Config.Denomination, CanCheck = true, Phase = Phase(), HoleCardsGone = !CardsLookable, CanActEarly = CanActEarly };
            switch (Flow)
            {
                case GameFlow.PlayerTurn:
                    ctx.IsPlayerTurn = AutoPlayer == null && _seq.IsIdle;
                    ctx.PlayerChips = Session.StackOf(Human);
                    break;
                case GameFlow.ShowdownWait:
                    ctx.IsPlayerTurn = true;
                    ctx.Phase = TablePhase.Showdown;
                    break;
                case GameFlow.HandOver:
                case GameFlow.GameOver:
                    ctx.IsPlayerTurn = true; // 敲桌開下一手／下一局
                    break;
                default:
                    ctx.PlayerChips = Session.State.Phase == HandPhase.Betting ? Session.StackOf(Human) : 0;
                    break;
            }
            return ctx;
        }

        TablePhase Phase()
        {
            switch (Session.State.Street)
            {
                case Street.Flop: return TablePhase.Flop;
                case Street.Turn: return TablePhase.Turn;
                case Street.River: return TablePhase.River;
                default: return TablePhase.Preflop;
            }
        }

        readonly List<System.Numerics.Vector3> _chipFootprint = new List<System.Numerics.Vector3>();

        void ProcessHands(float dt, HandsFrame frame)
        {
            ObserveFoolRocco(frame);
            TrackPinch(Hand.Left, frame.Left, frame.Time);
            TrackPinch(Hand.Right, frame.Right, frame.Time);
            NoteHandMotion(frame);
            var ctx = BuildContext();
            // 捏起範圍跟著玩家桌上真正的籌碼走（贏了很多時籌碼堆往後多排好幾排）
            Props.StackFootprint(Human, _chipFootprint);
            Recognizer.SetPlayerChipFootprint(_chipFootprint, ChipStackMesh.Radius);
            _events.Clear();
            _events.AddRange(Recognizer.Update(frame, ctx));
            foreach (var e in _events)
            {
                if (e.Kind != GestureKind.PeekChanged) Debug.Log($"[GestureEvent] {e} turn={ctx.IsPlayerTurn} phase={ctx.Phase}");
                GestureSeen?.Invoke(e);
                NoteGestureActivity(e);
                HandleGesture(e);
            }
            if (_held.Active && Recognizer.HoldingCards(out _, out var grip, out _))
                _held.Follow(new Vector3(grip.X, grip.Y, grip.Z), HeadLocal, dt);
            // 拿起來看也算看牌時間（AI 讀的是「看了多久」，不是牌）
            if ((_peekAmount > 0.15f || _held.Active) && !Props.PlayerCards[0].FaceUp) _peekSeconds += dt;
        }

        /// <summary>
        /// 還沒輪到、但這一手之後會輪到玩家（Session.PlayerCanActEarly，或 Core 已輪到玩家、畫面還在演前一個動作）：
        /// 辨識器照樣收棄牌手勢（記下，輪到時執行）、籌碼可以拿。教學那一手、自動玩家、已棄牌（含已記下的提前棄牌）時不收。
        /// </summary>
        bool CanActEarly
        {
            get
            {
                if (AutoPlayer != null || InTutorialHand || InFoolRoccoHand || _playerFolded || PreFoldQueued) return false;
                if (!(Flow == GameFlow.AiThinking || Flow == GameFlow.Presenting)) return false;
                if (!(Session.PlayerCanActEarly || Session.IsPlayerTurn) || Session.HoleOf(Human).Count < 2) return false;
                foreach (var c in Props.PlayerCards) if (!c.gameObject.activeSelf || c.FaceUp) return false;
                return true;
            }
        }

        /// <summary>玩家面前有蓋著的底牌可以拿起來看：這一手進行中、沒棄牌、沒翻開，而且牌已經發好躺在原位（不和發牌、棄牌動畫搶）。</summary>
        bool CardsLookable
        {
            get
            {
                if (_playerFolded || Session.HoleOf(Human).Count < 2) return false;
                if (!(Flow == GameFlow.PlayerTurn || Flow == GameFlow.AiThinking || Flow == GameFlow.Presenting || Flow == GameFlow.ShowdownWait)) return false;
                var cards = Props.PlayerCards;
                for (int i = 0; i < cards.Length; i++)
                {
                    if (!cards[i].gameObject.activeSelf || cards[i].FaceUp) return false;
                    if (!_held.Busy && (cards[i].transform.localPosition - (Spots.PlayerCard(i) + Vector3.up * TableProps.CardY)).sqrMagnitude > 0.01f * 0.01f) return false;
                }
                return true;
            }
        }

        /// <summary>頭的位置（牌桌本地座標）；沒有頭就假設坐在桌緣後 30 cm、高 45 cm。</summary>
        Vector3 HeadLocal => Head != null ? Table.transform.InverseTransformPoint(Head.position) : new Vector3(0f, 0.45f, -0.3f);

        /// <summary>處理一個手勢事件（每幀由辨識器呼叫；測試也可直接注入）。</summary>
        public void HandleGesture(GestureEvent e)
        {
            // 教學中只處理這一步要的動作；其他的擋下（想下注、敲桌、棄牌就重講提示）
            if (TutorialBlocks(e.Kind)) { OnTutorialBlocked(e); return; }
            switch (e.Kind)
            {
                case GestureKind.PeekChanged:
                    // 只在牌還在自己面前、這一手進行中時咪牌（棄掉、翻開、一手結束後、牌拿在手上時都不動）
                    bool live = (Flow == GameFlow.PlayerTurn || Flow == GameFlow.AiThinking || Flow == GameFlow.Presenting) && !_playerFolded && !_held.Busy;
                    _peekAmount = live ? e.Amount : 0f;
                    if (live) foreach (var c in Props.PlayerCards) if (c.gameObject.activeSelf) c.SetPeek(e.Amount);
                    if (live && e.Amount >= Tutorial.PeekThreshold)
                    {
                        NotePlayerLooked();   // 這一手第一次看到牌：牌力提示
                        TutorialDid(TutorialAction.Peeked);
                    }
                    break;
                case GestureKind.ChipsPicked:
                    _heldBy[(int)e.Hand] = e.Count;
                    if (Flow == GameFlow.PlayerTurn && _firstPick < 0) _firstPick = Now;
                    Audio.Play(SfxKind.ChipClack, PinchWorld(e.Hand), 0.5f, 1.1f);
                    break;
                case GestureKind.ChipCountChanged:
                    _heldBy[(int)e.Hand] = e.Count;
                    Audio.Play(SfxKind.ChipClack, PinchWorld(e.Hand), 0.35f, 1.2f);
                    break;
                case GestureKind.ChipsReleased:
                    OnChipsReleased(e);
                    break;
                case GestureKind.BetRetracted:
                    CancelPendingFlights();
                    _pending = 0; _pendingFlying = false;
                    _heldBy[(int)e.Hand] = e.Count;
                    break;
                case GestureKind.BetCommitted:
                    OnBetCommitted(e.Count);
                    break;
                case GestureKind.KnockCheck:
                    OnKnock(e);
                    break;
                case GestureKind.FoldPushed:
                    if (Flow == GameFlow.PlayerTurn) { if (Fold()) RecordGesture(GhostGesture.Fold); }
                    else if (CanActEarly) QueuePreFold();
                    break;
                case GestureKind.ActionTooEarly:
                    OnActionTooEarly(e);
                    break;
                case GestureKind.AllInConfirmed:
                    // 台詞由 PresentAction 說
                    if (Flow == GameFlow.PlayerTurn && CommitPlayer(new AllIn(Human), BuildSignals(), null)) RecordGesture(GhostGesture.AllIn);
                    break;
                case GestureKind.ShowdownFlip:
                    if (Flow == GameFlow.ShowdownWait)
                    {
                        _flipRequested = true;
                        // 往前放開、推出去亮的牌：在那裡翻開（牌不再跟手、也不蓋回原位）；原本的翻牌手勢在原位翻
                        if (e.Position != default)
                        {
                            _revealAt = new Vector3(e.Position.X, 0f, e.Position.Z);
                            _held.Detach();
                            Debug.Log($"[Game] showdown reveal at ({e.Position.X:0.00},{e.Position.Z:0.00})");
                        }
                        _jitGhost = null;
                    }
                    break;
                case GestureKind.CardsLifted:
                    if (!CardsLookable) break;
                    _held.Lift();
                    _peekAmount = 0f;
                    Audio.Play(SfxKind.Deal, Table.transform.TransformPoint(Spots.PlayerCard(0)), 0.35f);
                    NotePlayerLooked();
                    TutorialDid(TutorialAction.Looked);
                    break;
                case GestureKind.CardsReturned:
                    if (!_held.Active) break;
                    // 剛棄牌（同一幀先收到 FoldPushed）：牌由棄牌動畫送進棄牌堆，這裡只轉平
                    if (_playerFolded) _held.Detach();
                    else
                    {
                        _held.Return();
                        Audio.Play(SfxKind.CardSlide, Table.transform.TransformPoint(Spots.PlayerCard(0)), 0.4f);
                    }
                    break;
            }
        }

        void OnChipsReleased(GestureEvent e)
        {
            int count = e.Count;
            _heldBy[(int)e.Hand] = 0;
            _lastReleaseSpeed = _pinch[e.Hand].Valid ? HorizontalSpeed(_pinch[e.Hand]) : 0f;
            _lastDelivery = _lastReleaseSpeed >= ThrowSpeedThreshold ? ChipDelivery.Thrown : ChipDelivery.Placed;
            var from = Table.transform.InverseTransformPoint(PinchWorld(e.Hand));
            if (e.InBetZone && Flow == GameFlow.PlayerTurn)
            {
                var landing = Spots.ClampToBetZone(new Vector3(e.Position.X, 0f, e.Position.Z));
                // 分批丟進下注區會累加成同一注（辨識器也是累加）
                _pending += count;
                _pendingPos = landing;
                _pendingFlying = true;
                float arc = _lastDelivery == ChipDelivery.Thrown ? 0.05f : 0.01f;
                StartFree(count, from, landing, 0.28f, arc, () =>
                {
                    if (!HasPendingFlight()) _pendingFlying = false;
                    Audio.Play(SfxKind.ChipClack, Table.transform.TransformPoint(landing), 0.8f);
                }, pending: true);
            }
            else
            {
                if (!e.InBetZone && TutorialRunning) Tutorial.NoteNearMiss(Tutorial.ReasonOutsideZone);
                // 不在下注區（或還沒輪到你）：飛回籌碼堆
                _returning += count;
                StartFree(count, from, Spots.PlayerStack, 0.35f, 0.03f, () =>
                {
                    _returning -= count;
                    Audio.Play(SfxKind.ChipClack, Table.transform.TransformPoint(Spots.PlayerStack), 0.6f);
                });
            }
        }

        void OnBetCommitted(int count)
        {
            var from = _pendingPos;
            if (Flow != GameFlow.PlayerTurn)
            {
                ReturnPending();
                Invalid(InvalidReason.NotYourTurn);
                return;
            }
            var cc = Session.InterpretChips(count);
            if (TutorialRefusesBet(cc)) return;
            int potBefore = Session.Pot;
            var signals = BuildSignals();
            signals.Delivery = _lastDelivery;
            signals.ThrowSpeed = _lastReleaseSpeed;
            signals.BetPotFraction = cc.IsValid ? cc.Committed / (float)Mathf.Max(1, potBefore) : 0f;
            if (!cc.IsValid || !CommitPlayer(cc.Command, signals, () => PresentPlayerBet(cc, from)))
            {
                ReturnPending();
                Invalid(InvalidReason.InvalidBet);
                return;
            }
            RecordGesture(GhostGesture.Bet);
            TutorialDid(cc.Kind == ChipCommitKind.Call ? TutorialAction.Called : TutorialAction.Raised);
        }

        void ReturnPending()
        {
            if (_pending <= 0) return;
            int n = _pending;
            _pending = 0;
            _returning += n;
            StartFree(n, _pendingPos, Spots.PlayerStack, 0.4f, 0.02f, () => _returning -= n);
        }

        void OnKnock(GestureEvent e)
        {
            if (Flow == GameFlow.HandOver) { StartNextHand(); return; }
            if (Flow == GameFlow.GameOver) { StartNewGame(); return; }
            if (Flow != GameFlow.PlayerTurn) return;
            if (!Session.Legal(Human).CanCheck) { Invalid(InvalidReason.CannotCheck); return; }
            Audio.Play(SfxKind.Knock, PinchWorld(e.Hand), 0.8f);
            if (!CommitPlayer(new Check(Human), BuildSignals(), null)) return;
            RecordGesture(GhostGesture.Knock);
        }

        /// <summary>還沒輪到時的棄牌手勢：牌馬上進棄牌堆、荷官確認，輪到時（BeginPlayerTurn）才送 Fold 給 Core。</summary>
        void QueuePreFold()
        {
            PreFoldQueued = true;
            _playerFolded = true; // 牌不能再拿起來、咪牌停止
            _peekAmount = 0f;
            _held.Detach();
            var cards = Props.PlayerCards;
            for (int i = 0; i < 2; i++) { cards[i].SetPeek(0f); _preMuckFrom[i] = cards[i].transform.localPosition; }
            _preMuckT = 0f;
            HideHandHint();
            Audio.Play(SfxKind.CardSlide, Table.transform.TransformPoint(Spots.PlayerCard(0)), 0.7f);
            Voice.Say(DealerLines.FoldNoted, Now, "prefold", force: true);
            Debug.Log($"[Game] pre-action fold queued (flow={Flow}, street={Session.State.Street}, toAct={Session.State.ToAct})");
        }

        void ApplyPreFold()
        {
            PreFoldQueued = false;
            // 牌已經在棄牌堆：presentHuman 給空的，不再演一次棄牌
            bool ok = CommitPlayer(new Fold(Human), BuildSignals(), () => { });
            Debug.Log("[Game] pre-action fold applied" + (ok ? "" : " FAILED (Core refused Fold)"));
            if (ok) RecordGesture(GhostGesture.Fold);
            else Debug.LogWarning("[Game] Core 不接受提前棄牌：" + Session.State.ToAct);
        }

        void TickPreFoldMuck(float dt)
        {
            if (_preMuckT < 0f) return;
            _preMuckT = Mathf.Min(1f, _preMuckT + dt / 0.4f);
            var cards = Props.PlayerCards;
            for (int i = 0; i < 2; i++)
                cards[i].transform.localPosition = TableProps.Path(_preMuckFrom[i], Spots.Muck + new Vector3(i * 0.012f, TableProps.CardY + i * 0.0006f, i * 0.008f), _preMuckT, 0f);
            if (_preMuckT >= 1f) _preMuckT = -1f;
        }

        /// <summary>還沒輪到就做完的動作（籌碼丟進下注區、敲桌、雙手推）：籌碼已由辨識器退回；荷官說「Wait for your turn.」（有冷卻），每次都有提示音。</summary>
        void OnActionTooEarly(GestureEvent e)
        {
            if (!(Flow == GameFlow.PlayerTurn || Flow == GameFlow.AiThinking || Flow == GameFlow.Presenting)) return;
            bool say = Now - _waitSaidAt >= WaitForTurnCooldown;
            Audio.Play(SfxKind.Invalid, e.Count > 0 ? Table.transform.TransformPoint(Spots.PlayerStack) : PinchWorld(e.Hand), 0.35f);
            if (say)
            {
                _waitSaidAt = Now;
                Voice.Say(DealerLines.WaitForTurn, Now, "waitturn", force: true);
            }
            Debug.Log($"[Game] early action rejected: {(e.Count > 0 ? "chips n=" + e.Count : "knock/all-in")} → " + (say ? "\"" + DealerLines.WaitForTurn + "\"" : "line on cooldown"));
        }

        bool Fold()
        {
            if (!CommitPlayer(new Fold(Human), BuildSignals(), PresentPlayerFold)) return false;
            TutorialDid(TutorialAction.Folded);
            return true;
        }

        // ================= 演出 =================

        enum Loc { Stack, Bet, Pot }

        Vector3 PosOf(Loc l, int seat) =>
            l == Loc.Stack ? Spots.StackOf(seat) : l == Loc.Bet ? Spots.BetOf(seat) : Spots.PotPile;

        void Take(Loc l, int seat, int amount)
        {
            if (l == Loc.Stack) _stack[seat] -= amount; else if (l == Loc.Bet) _bet[seat] -= amount; else _pot -= amount;
            _inFlight += amount;
        }

        void Give(Loc l, int seat, int amount)
        {
            if (l == Loc.Stack) _stack[seat] += amount; else if (l == Loc.Bet) _bet[seat] += amount; else _pot += amount;
            _inFlight -= amount;
        }

        /// <summary>籌碼從一處滑到另一處（排在演出佇列裡）。</summary>
        void Move(Loc from, int fs, Loc to, int ts, int amount, float duration, float arc = 0f, float volume = 0.6f)
        {
            if (amount <= 0) return;
            ChipPile fly = null;
            Vector3 a = default, b = default;
            _seq.Then(duration,
                () => { Take(from, fs, amount); a = PosOf(from, fs); b = PosOf(to, ts); fly = Props.Flyer(amount); fly.Position = a; },
                t => fly.Position = TableProps.Path(a, b, t, arc),
                () =>
                {
                    Give(to, ts, amount);
                    TableProps.Release(fly);
                    Audio.Play(SfxKind.ChipClack, Table.transform.TransformPoint(b), volume);
                });
        }

        /// <summary>荷官把所有人本街的注收進底池（同時滑）。</summary>
        void Collect()
        {
            int n = Session.SeatCount;
            var fly = new ChipPile[n];
            var amt = new int[n];
            _seq.Then(0.45f,
                () =>
                {
                    int sum = 0;
                    for (int s = 0; s < n; s++)
                    {
                        amt[s] = _bet[s];
                        if (amt[s] <= 0) continue;
                        sum += amt[s];
                        Take(Loc.Bet, s, amt[s]);
                        fly[s] = Props.Flyer(amt[s]);
                        fly[s].Position = PosOf(Loc.Bet, s);
                    }
                    if (sum > 0) Audio.Play(SfxKind.ChipSlide, Table.transform.TransformPoint(Spots.PotPile), 0.6f);
                },
                t =>
                {
                    for (int s = 0; s < n; s++)
                        if (fly[s] != null) fly[s].Position = TableProps.Path(PosOf(Loc.Bet, s), Spots.PotPile, t, 0f);
                },
                () =>
                {
                    for (int s = 0; s < n; s++)
                    {
                        if (fly[s] == null) continue;
                        Give(Loc.Pot, -1, amt[s]);
                        TableProps.Release(fly[s]);
                        fly[s] = null;
                    }
                });
        }

        /// <summary>all-in 跑牌：每條街等荷官講完（最多這麼久）才發牌。</summary>
        public const float RunoutVoiceTimeout = 4f;
        /// <summary>all-in 跑牌：每張牌落下後停一下，讓玩家看清楚。</summary>
        public const float RunoutBeat = 0.6f;

        /// <summary>這一手結束後這一局就結束（會出結算看板）。</summary>
        bool GameEnding => !InTutorialHand && !InFoolRoccoHand && Session.CheckGameOver(Now) != GameEndReason.None;

        void PresentEvents(IReadOnlyList<GameEvent> events, bool humanShown)
        {
            // all-in 攤牌之後的一批事件（剩下的公共牌、分池、一手結束）一次到；動畫只要兩三秒，荷官的話會落在結果後面
            // （2026-11-04 實機錄影：結算看板先出來，荷官才說「Fourth card coming up.」）。跑牌時每一步等荷官講完。
            bool runout = false;
            foreach (var e in events)
            {
                switch (e)
                {
                    case BlindPosted b:
                        Move(Loc.Stack, b.Seat, Loc.Bet, b.Seat, b.Amount, 0.25f);
                        break;
                    case HoleCardsDealt h:
                        DealHole(h.Seat);
                        break;
                    case PlayerActed a:
                        if (a.Seat != Human || !humanShown) PresentAction(a);
                        else if (a.Action == ActionType.Check) _seq.Do(() => Voice.Say(DealerLines.Check, Now, "check", cooldown: 0f));
                        break;
                    case UncalledBetReturned u:
                        Move(Loc.Bet, u.Seat, Loc.Stack, u.Seat, u.Amount, 0.35f);
                        break;
                    case StreetDealt sd:
                        Collect();
                        var trig = sd.Street == Street.Flop ? LineTrigger.DealerFlop : sd.Street == Street.Turn ? LineTrigger.DealerTurn : LineTrigger.DealerRiver;
                        _seq.Do(() => Voice.Say(trig, Now, cooldown: 0f, force: !InTutorialHand));
                        if (runout) _seq.WaitUntil(() => !Voice.Busy, RunoutVoiceTimeout);
                        foreach (var card in sd.NewCards) DealBoard(_boardCount++, card);
                        if (runout) _seq.Wait(RunoutBeat);
                        _seq.Do(OnStreetForHandHint);   // 新的一條街：牌力提示更新
                        break;
                    case ShowdownRevealed sr:
                        bool playerShows = false;
                        if (sr.IsAllInTableUp) runout = true;
                        foreach (var sh in sr.Hands) if (sh.Seat == Human) playerShows = true;
                        // 教學 v4 不教翻牌（正式牌局第一次攤牌時才教）：教學的攤牌直接翻開
                        if (InTutorialHand || sr.IsAllInTableUp || !playerShows) { FlipPlayerCards(playerShows); RevealAiCards(); }
                        else WaitForShowdownFlip();
                        break;
                    case PotAwarded pa:
                        Collect();
                        RevealAiCards();
                        ShowHeadlines();
                        Award(pa);
                        break;
                    case HandEnded _:
                        // 揭曉卡跟攤牌牌子一起出；all-in 跑牌或這一局最後一手：荷官講完「Pot goes to you!」才出結算
                        _seq.Do(OnLiveHandOver);
                        _seq.WaitUntil(() => !(runout || GameEnding) || !Voice.Busy, RunoutVoiceTimeout);
                        _seq.Do(EnterHandOver);
                        break;
                }
            }
        }

        void PresentAction(PlayerActed a)
        {
            int seat = a.Seat;
            switch (a.Action)
            {
                case ActionType.Check:
                    _seq.Do(() =>
                    {
                        if (seat != Human) Audio.Play(SfxKind.Knock, Table.transform.TransformPoint(Spots.CardOf(seat, 0)), 0.7f);
                        Voice.Say(DealerLines.Check, Now, "check", cooldown: 0f);
                    });
                    _seq.Wait(0.35f);
                    break;
                case ActionType.Fold:
                    if (seat != Human) SlideCardsToMuck(seat); else PresentPlayerFold();
                    break;
                default:
                    if (a.IsAllIn) _seq.Do(() => Voice.Say(LineTrigger.DealerAllIn, Now, force: true));
                    Move(Loc.Stack, seat, Loc.Bet, seat, a.AmountAdded, 0.45f, 0f, 0.8f);
                    break;
            }
        }

        void PresentPlayerBet(ChipCommit cc, Vector3 from)
        {
            ChipPile fly = null;
            var to = Spots.PlayerBet;
            _seq.Then(0.3f,
                () =>
                {
                    _pending = 0; _pendingFlying = false;
                    Take(Loc.Stack, Human, cc.Pushed);
                    fly = Props.Flyer(cc.Pushed);
                    fly.Position = from;
                },
                t => fly.Position = TableProps.Path(from, to, t, 0f),
                () => { Give(Loc.Bet, Human, cc.Pushed); TableProps.Release(fly); });
            if (cc.Adjustment < 0) Move(Loc.Bet, Human, Loc.Stack, Human, -cc.Adjustment, 0.35f, 0.02f);
            else if (cc.Adjustment > 0) Move(Loc.Stack, Human, Loc.Bet, Human, cc.Adjustment, 0.35f, 0.02f);
        }

        void PresentPlayerFold()
        {
            _playerFolded = true;
            HideHandHint();
            _held.Detach();
            foreach (var c in Props.PlayerCards) c.SetPeek(0f);
            var cards = Props.PlayerCards;
            var start = new Vector3[2];
            _seq.Then(0.4f,
                () =>
                {
                    for (int i = 0; i < 2; i++) { start[i] = cards[i].transform.localPosition; cards[i].SetPeek(0f); }
                    Audio.Play(SfxKind.CardSlide, Table.transform.TransformPoint(Spots.PlayerCard(0)), 0.7f);
                },
                t =>
                {
                    for (int i = 0; i < 2; i++)
                        cards[i].transform.localPosition = TableProps.Path(start[i], Spots.Muck + new Vector3(i * 0.012f, TableProps.CardY + i * 0.0006f, i * 0.008f), t, 0f);
                });
        }

        void SlideCardsToMuck(int seat)
        {
            var cards = Props.SeatCards[seat];
            var start = new Vector3[2];
            _seq.Then(0.4f,
                () => { for (int i = 0; i < 2; i++) start[i] = cards[i].transform.localPosition; },
                t =>
                {
                    for (int i = 0; i < 2; i++)
                        cards[i].transform.localPosition = TableProps.Path(start[i], Spots.MuckOf(seat, i) + Vector3.up * TableProps.CardY, t, 0f);
                });
        }

        void DealHole(int seat)
        {
            var cards = Props.SeatCards[seat];
            var hole = Session.HoleOf(seat);
            if (hole.Count < 2) return;
            float yaw = Spots.CardYaw(seat);
            for (int i = 0; i < 2; i++)
            {
                int k = i;
                var target = Spots.CardOf(seat, k) + Vector3.up * TableProps.CardY;
                _seq.Then(seat == Human ? 0.22f : 0.16f,
                    () =>
                    {
                        TableProps.Place(cards[k], hole[k], Props.DeckPos, yaw);
                        Audio.Play(SfxKind.Deal, Table.transform.TransformPoint(Spots.Deck), 0.6f);
                    },
                    t => cards[k].transform.localPosition = TableProps.Path(Props.DeckPos, target, t, 0.04f));
            }
        }

        void DealBoard(int index, Card card)
        {
            if (index >= Props.Board.Length) return;
            var c = Props.Board[index];
            var target = Spots.Board(index) + Vector3.up * TableProps.CardY;
            _seq.Then(0.25f,
                () => { TableProps.Place(c, card, Props.DeckPos, 0f); Audio.Play(SfxKind.Deal, Table.transform.TransformPoint(Spots.Deck), 0.6f); },
                t => c.transform.localPosition = TableProps.Path(Props.DeckPos, target, t, 0.03f));
            Flip(new[] { c }, 0.22f);
        }

        /// <summary>翻牌動畫：邊翻邊抬起一點再落下。</summary>
        void Flip(CardView[] cards, float duration, Action start = null)
        {
            var basePos = new Vector3[cards.Length];
            _seq.Then(duration,
                () =>
                {
                    start?.Invoke();
                    for (int i = 0; i < cards.Length; i++) { basePos[i] = cards[i].transform.localPosition; cards[i].SetPeek(0f); }
                },
                t =>
                {
                    for (int i = 0; i < cards.Length; i++)
                    {
                        cards[i].SetFlipProgress(t);
                        cards[i].transform.localPosition = basePos[i] + Vector3.up * (0.03f * Mathf.Sin(Mathf.PI * t));
                    }
                },
                () =>
                {
                    for (int i = 0; i < cards.Length; i++) { cards[i].SetFaceUp(true); cards[i].transform.localPosition = basePos[i]; }
                    Audio.Play(SfxKind.Flip, Table.transform.TransformPoint(Spots.Board(2)), 0.6f);
                });
        }

        void WaitForShowdownFlip()
        {
            // 第一次攤牌：Dot 教怎麼亮牌（示範手），這一次多等一下
            bool tip = AutoPlayer == null && ShowdownTipDue();
            _seq.Do(() =>
            {
                Flow = GameFlow.ShowdownWait;
                _flipRequested = false;
                _revealAt = null;
                if (tip) SayTip(JitTip.Showdown);
                else Voice.Say(LineTrigger.DealerShowdown, Now, force: true);
            });
            float timeout = AutoPlayer != null ? 0.3f : tip ? FirstShowdownFlipTimeout : ShowdownFlipTimeout;
            _seq.WaitUntil(() => _flipRequested, timeout, () => Flow = GameFlow.Presenting);
            FlipPlayerCards(true);
            RevealAiCards();
        }

        void FlipPlayerCards(bool show)
        {
            if (!show || Props.PlayerCards[0].FaceUp || !Props.PlayerCards[0].gameObject.activeSelf) return;
            var cards = Props.PlayerCards;
            var from = new Vector3[cards.Length];
            var to = new Vector3[cards.Length];
            _seq.Then(0.3f,
                () =>
                {
                    // 往前放開、推出去亮的牌：從現在的位置（手上或桌上）滑到 _revealAt、邊走邊翻開。
                    // 原本的翻牌手勢、逾時：在原位翻；還拿在手上（攤牌逾時）先蓋回原位
                    var at = _revealAt;
                    _revealAt = null;
                    if (at.HasValue) _held.Clear(); else _held.SnapBack();
                    for (int i = 0; i < cards.Length; i++)
                    {
                        cards[i].SetPeek(0f);
                        from[i] = cards[i].transform.localPosition;
                        to[i] = at.HasValue ? RevealSpot(i, at.Value) : from[i];
                    }
                },
                t =>
                {
                    for (int i = 0; i < cards.Length; i++)
                    {
                        cards[i].SetFlipProgress(t);
                        cards[i].transform.localPosition = TableProps.Path(from[i], to[i], t, 0.03f);
                    }
                },
                () =>
                {
                    for (int i = 0; i < cards.Length; i++) { cards[i].SetFaceUp(true); cards[i].transform.localPosition = to[i]; }
                    Audio.Play(SfxKind.Flip, Table.transform.TransformPoint(Spots.Board(2)), 0.6f);
                });
        }

        /// <summary>亮在 center（兩張牌中心）時第 i 張牌的位置：和在自己牌位上一樣左右並排。</summary>
        Vector3 RevealSpot(int i, Vector3 center)
        {
            var mid = (Spots.PlayerCard(0) + Spots.PlayerCard(1)) * 0.5f;
            var p = center + Spots.PlayerCard(i) - mid;
            p.y = TableProps.CardY;
            return p;
        }

        /// <summary>一手結束：所有發到牌的 AI 一起亮牌（棄掉的也亮，讓玩家驗證自己的判讀），牌轉向玩家。</summary>
        void RevealAiCards()
        {
            if (_aiRevealed) return;
            _aiRevealed = true;
            var shown = new List<CardView>();
            foreach (var seat in Session.AiSeats)
                if (Session.HoleOf(seat).Count == 2)
                    foreach (var c in Props.SeatCards[seat]) if (c.gameObject.activeSelf && !c.FaceUp) shown.Add(c);
            if (shown.Count == 0) return;
            Flip(shown.ToArray(), 0.35f, () => { foreach (var c in shown) c.SetYaw(0f); });
        }

        /// <summary>每個發到牌的 AI，在自己的牌上方顯示這手的真相（"Bluffing"；沒輪到他就是 "No action"）。</summary>
        void ShowHeadlines()
        {
            if (_headlinesShown) return;
            _headlinesShown = true;
            _seq.Do(() =>
            {
                if (InFoolRoccoHand || InTutorialHand) return; // 揭曉卡由 Fool Rocco／教學自己顯示
                foreach (var seat in Session.AiSeats)
                {
                    var rev = Session.AiAt(seat).LastReveal;
                    if (rev == null || rev.HandNumber != Session.State.HandNumber) continue;
                    ShowHeadline(seat, rev);
                }
            });
        }

        /// <summary>一個 AI 的攤牌標題，放在他亮出的牌上方（ShowHeadlines 與截圖共用）。</summary>
        public void ShowHeadline(int seat, HandReveal rev)
        {
            {
                {
                    var at = (Spots.CardOf(seat, 0) + Spots.CardOf(seat, 1)) * 0.5f;
                    var toPlayer = new Vector3(-at.x, 0f, -at.z).normalized * 0.02f;
                    // 多人桌：對面座位的標題放高一點；兩側座位的標題退到牌後面（不擋住亮出的牌），彼此錯開
                    float h = 0.075f;
                    // 2026-10-14 座位調整（Pip／Vex 往後、Rocco 往左）：對面的標題再高一點才不會和側邊的疊在一起；側邊的只退 2 cm（退 5 cm 時字小於 1.2°）
                    if (Spots.IsMultiway && Mathf.Abs(at.x) < 0.3f) h = 0.15f;
                    else if (Spots.IsMultiway) { h = 0.105f; toPlayer *= -1f; }   // 高過旁邊的籌碼堆（0.08 時 Pip 的標題穿過 Rocco 的籌碼）
                    Props.SeatHeadline[seat].Show(rev.Headline, at + toPlayer + new Vector3(0f, h, 0f));   // 一行就好（2026-11-03 拿掉「Acted the opposite」那種補充）
                }
            }
        }

        void Award(PotAwarded pa)
        {
            _seq.Do(() =>
            {
                string text;
                if (pa.Shares.Count == 1)
                {
                    int w = pa.Shares[0].Seat;
                    string who = w == Human ? "you" : Session.NameOf(w);
                    text = DealerLines.WinnerLine(who, pa.PotIndex > 0);
                }
                else text = pa.PotIndex > 0 ? DealerLines.SideSplit : DealerLines.SplitPot;
                Voice.Say(text, Now, "winner", force: true);
            });
            foreach (var share in pa.Shares)
                Move(Loc.Pot, -1, Loc.Stack, share.Seat, share.Amount, 0.6f, 0f, 0.8f);
            _seq.Do(() =>
            {
                foreach (var share in pa.Shares)
                    if (share.Seat == Human) Audio.Play(SfxKind.Win, Head != null ? Head.position : Table.transform.position, 0.6f);
            });
            _seq.Wait(pa.PotIndex > 0 ? 0.6f : 0.3f);
        }

        // ================= 籌碼擺放 =================

        void SyncToCore()
        {
            int potTotal = Session.Pot;
            int bets = 0;
            for (int s = 0; s < Session.SeatCount; s++)
            {
                var seat = Session.State.Seat(s);
                _stack[s] = seat.Stack;
                _bet[s] = Session.State.Phase == HandPhase.Betting ? seat.StreetBet : 0;
                bets += _bet[s];
            }
            _pot = Session.State.Phase == HandPhase.Betting ? potTotal - bets : 0;
            _inFlight = 0;
        }

        /// <summary>桌上看得到的籌碼總額（籌碼堆＋下注＋底池＋飛行中），應該永遠等於 Core 的總額。</summary>
        public int DisplayedTotalChips
        {
            get
            {
                int t = _pot + _inFlight;
                for (int s = 0; s < _stack.Length; s++) t += _stack[s] + _bet[s];
                return t;
            }
        }

        void LayOutChips(HandsFrame frame)
        {
            Props.LayOut(_stack, _bet, _pot, HeldTotal + _pending + _returning, Human);
            // 待確認的下注（飛行中由 FreeFlight 負責畫）
            if (_pending > 0 && !_pendingFlying) { Props.Pending.SetChips(_pending, false); Props.Pending.Position = _pendingPos; }
            else Props.Pending.SetChips(0, false);
            // 手上的籌碼跟著捏合點（兩隻手各自一堆、各自一個金額標籤）
            for (int h = 0; h < 2; h++)
            {
                var pile = Props.InHand[h];
                var label = Props.HandCount[h];
                var hist = _pinch[(Hand)h];
                int held = _heldBy[h];
                if (held > 0 && hist.Valid)
                {
                    var p = hist.Last;
                    int shown = Mathf.Min(held, ChipStackMesh.MaxPerColumn);
                    pile.SetChips(held, false);
                    pile.Position = p - Vector3.up * (shown * ChipStackMesh.Thickness + 0.004f);
                    label.Show(HeldLabel(held), p + Vector3.up * 0.04f);
                    if (Head != null) label.Face(Head.position);
                }
                else
                {
                    pile.SetChips(0, false);
                    label.Hide();
                }
            }
            if (Head != null) Props.FaceLabels(Head.position);
        }

        readonly Dictionary<int, string> _labelCache = new Dictionary<int, string>();

        string HeldLabel(int chips)
        {
            if (!_labelCache.TryGetValue(chips, out var s)) _labelCache[chips] = s = (chips * Config.Denomination).ToString();
            return s;
        }

        void StartFree(int chips, Vector3 from, Vector3 to, float duration, float arc, Action done, bool pending = false)
        {
            var pile = Props.Flyer(chips * Config.Denomination);
            pile.Position = from;
            _free.Add(new FreeFlight { Pile = pile, From = from, To = to, Duration = duration, Arc = arc, Done = done, Pending = pending });
        }

        bool HasPendingFlight()
        {
            foreach (var f in _free) if (f.Pending) return true;
            return false;
        }

        /// <summary>撈回待確認的下注時，還在飛往下注區的籌碼直接收掉（改由手上那堆顯示）。</summary>
        void CancelPendingFlights()
        {
            for (int i = _free.Count - 1; i >= 0; i--)
            {
                if (!_free[i].Pending) continue;
                TableProps.Release(_free[i].Pile);
                _free.RemoveAt(i);
            }
        }

        void TickFreeFlights(float dt)
        {
            for (int i = _free.Count - 1; i >= 0; i--)
            {
                var f = _free[i];
                f.T += dt;
                float t = Mathf.Clamp01(f.T / f.Duration);
                f.Pile.Position = TableProps.Path(f.From, f.To, t, f.Arc);
                if (t < 1f) continue;
                _free.RemoveAt(i);
                TableProps.Release(f.Pile);
                f.Done?.Invoke();
            }
        }

        void ClearFreeFlights()
        {
            foreach (var f in _free) TableProps.Release(f.Pile);
            _free.Clear();
        }

        // ================= 手部追蹤 =================

        void TrackPinch(Hand h, HandPose pose, double time)
        {
            var hist = _pinch[h];
            if (pose == null || !pose.Tracked) { hist.Valid = false; hist.Samples.Clear(); return; }
            var a = pose.Position(HandJoint.ThumbTip);
            var b = pose.Position(HandJoint.IndexTip);
            var p = new Vector3((a.X + b.X) * 0.5f, (a.Y + b.Y) * 0.5f, (a.Z + b.Z) * 0.5f);
            hist.Last = p;
            hist.Valid = true;
            hist.Samples.Enqueue((time, p));
            while (hist.Samples.Count > 12) hist.Samples.Dequeue();
        }

        /// <summary>最近約 0.08 秒捏合點的水平速度（m/s）。</summary>
        static float HorizontalSpeed(PinchHistory h)
        {
            if (h.Samples.Count < 2) return 0f;
            var arr = h.Samples.ToArray();
            var last = arr[arr.Length - 1];
            for (int i = arr.Length - 2; i >= 0; i--)
            {
                double dt = last.t - arr[i].t;
                if (dt >= 0.08 || i == 0)
                {
                    if (dt <= 1e-4) return 0f;
                    var d = last.p - arr[i].p;
                    d.y = 0f;
                    return (float)(d.magnitude / dt);
                }
            }
            return 0f;
        }

        Vector3 PinchWorld(Hand h) =>
            _pinch[h].Valid ? Table.transform.TransformPoint(_pinch[h].Last) : Table.transform.TransformPoint(Spots.PlayerStack);

        // ================= 注視 =================

        /// <summary>注視目標編號：AI 用座位號，荷官用這個。</summary>
        public const int DealerGazeId = 100;

        /// <summary>所有可注視的角色與它的注視編號（AI＝座位號、荷官＝DealerGazeId）。外部注視來源（ISDK）要用同一套編號。</summary>
        public IEnumerable<(CharacterPerformer Who, int Id)> GazeTargets()
        {
            foreach (var seat in Session.AiSeats) yield return (_chars[seat], seat);
            if (Dealer != null) yield return (Dealer, DealerGazeId);
        }

        /// <summary>玩家正盯著的 AI 座位（沒有、或盯著荷官 = -1）。</summary>
        public int GazedSeat => Character(_gaze.Current) != null ? _gaze.Current : -1;
        /// <summary>正盯著的注視編號（AI＝座位號、荷官＝DealerGazeId；沒有＝-1），已滿 1 秒的「盯著」。</summary>
        public int GazedId => _gaze.Current;
        /// <summary>頭部射線（或 ISDK）目前命中、還在累積的注視編號與秒數（還沒滿 1 秒也算）。</summary>
        public int GazeCandidate => _gaze.Candidate;
        public float GazeDwell => _gaze.DwellTime;

        CharacterPerformer GazeTarget(int id) => id == DealerGazeId ? Dealer : Character(id);

        void TickGaze(float dt)
        {
            if (Head == null) return;
            var o = Head.position;
            var d = Head.forward;
            int hit = -1;
            float best = float.MaxValue;
            void Test(CharacterPerformer c, int id)
            {
                var rig = c.Rig;
                if (GazeTracker.RayHitsSphere(o, d, rig.Head.position, 0.16f, out float t1) && t1 < best) { best = t1; hit = id; }
                if (GazeTracker.RayHitsSphere(o, d, rig.Chest.position, 0.17f, out float t2) && t2 < best) { best = t2; hit = id; }
            }
            foreach (var seat in Session.AiSeats) Test(_chars[seat], seat);
            Test(Dealer, DealerGazeId);
            // 下注堆／底池：比角色近才算看籌碼（看籌碼不算看角色），否則算看角色
            int pile = HitBetPile(o, d, out float pileDist);
            if (pile >= 0 && pileDist < best) hit = -1; else pile = -1;
            _gaze.Update(dt, hit);
            _pileGaze.Update(dt, pile);
            TickBetLabel();
        }

        void OnGazeStarted(int id)
        {
            var c = GazeTarget(id);
            if (c == null) return;
            c.SetGazedAt(true);
            TipOnGaze(id);
            if (c == Dealer || InTutorialHand || InFoolRoccoHand) return; // 教學的破綻由劇本演（ReadTheTell）；Fool Rocco 的 AI 也是腳本
            // 開始盯的那一刻告訴所有 AI 一次（被盯的那個放大破綻，其他人知道你不在看他）
            GazeSignalsSent++;
            SendSignals(new PlayerSignals { GazeSeat = id, GazeSeconds = _gaze.DwellTime, Time = Now });
        }

        void OnGazeEnded(int id) => GazeTarget(id)?.SetGazedAt(false);

        // ================= 角色 =================

        void TickCharacters(float dt)
        {
            var center = Table.transform.TransformPoint(new Vector3(0f, 0f, Spots.Layout.FeltSize.Y * 0.5f));
            foreach (var c in AllCharacters())
            {
                c.TableCenter = center;
                c.Tick(dt);
            }
            if (_shakeT >= 0f)
            {
                // 荷官搖頭（疊在角色本身的頭部動作之後）
                _shakeT += dt;
                float k = Mathf.Clamp01(1f - _shakeT / 0.7f);
                float yaw = _shakeT > 0.7f ? 0f : 18f * Mathf.Sin(_shakeT * 24f) * k;
                Dealer.Rig.Head.localRotation = _dealerHeadRest * Quaternion.Euler(0f, yaw, 0f);
                if (_shakeT > 0.7f) _shakeT = -1f;
            }
        }
    }
}
