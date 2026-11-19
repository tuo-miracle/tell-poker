using Poker.Core;
using Poker.Gestures;
using UnityEngine;
using Hand = Poker.Gestures.Hand;

namespace Poker.Game
{
    /// <summary>
    /// 示範手（ghost hands）：教學每一步示範要做的手勢；正式牌局中輪到玩家發呆 5 秒就輪流示範合法動作（GestureHints）。
    /// 選單開著、正在對角色講話時不顯示；玩家一動手就淡出。手勢成功的次數記在進度存檔，做會了就不再提示。
    /// </summary>
    public sealed partial class GameController
    {
        /// <summary>手掌移動超過這個速度（m/s）就算「開始動手」（牌局中的提示淡出、重新計時）。</summary>
        public const float HandActivitySpeed = 0.2f;

        public GhostHands Ghost { get; private set; }
        public GestureHints Hints { get; } = new GestureHints();
        /// <summary>語音狀態（UX 接上；沒有語音就是 null）。</summary>
        public ITutorialVoice TutorialVoice { get; set; }
        /// <summary>正在顯示的示範手勢（淡出完、或沒有 = null）。</summary>
        public GhostGesture? GhostShown => Ghost != null && Ghost.IsVisible ? Ghost.Current : null;
        /// <summary>設定裡的「Hints: On/Off」（只管牌局中的提示；教學照樣示範）。</summary>
        public bool HintsEnabled { get => Hints.Enabled; set => Hints.Enabled = value; }

        bool _menuOpen;
        /// <summary>手腕選單開著（UX 設定）：示範手立刻收起來，提示不計時。</summary>
        public bool MenuOpen
        {
            get => _menuOpen;
            set
            {
                _menuOpen = value;
                if (!value) return;
                // 選單開著時遊戲不 Step（暫停）：在這裡就收起來、重新計時，關掉選單後要再等滿
                Ghost?.Hide();
                Hints.NoteActivity();
            }
        }

        bool VoiceBusy => TutorialVoice != null && TutorialVoice.Busy;

        int _hintIndex;
        HandState _hintState;
        HintContext _hintLegal;
        readonly Vector3[] _lastPalm = new Vector3[2];
        readonly bool[] _lastPalmValid = new bool[2];
        double _lastPalmTime;

        void InitHints()
        {
            Ghost = new GhostHands(Props.Root);
            Hints.Successes = g => Progress != null ? Progress.Successes(g) : 0;
        }

        /// <summary>某個手勢成功了一次（過牌、下注／跟注、棄牌、All-in）：記進存檔（只記到不再提示為止，之後不再寫檔）。教學的兩手不算。</summary>
        void RecordGesture(GhostGesture g)
        {
            if (InTutorialHand || InFoolRoccoHand) return;
            if (Progress == null || Progress.Successes(g) >= GestureHints.SuppressAfter) return;
            Progress.RecordSuccess(g);
            SaveProgress();
        }

        /// <summary>手掌在動：提示淡出、重新計時（教學的提示看的是這一步卡了多久，不看手有沒有動；只有提示安靜了，手在牌桌附近動才再給一輪）。</summary>
        void NoteHandMotion(HandsFrame frame)
        {
            double dt = frame.Time - _lastPalmTime;
            _lastPalmTime = frame.Time;
            bool moving = false, nearTable = false;
            for (int h = 0; h < 2; h++)
            {
                var pose = frame.Get((Hand)h);
                if (!pose.Tracked) { _lastPalmValid[h] = false; continue; }
                var p = pose.Position(HandJoint.Palm);
                var v = new Vector3(p.X, p.Y, p.Z);
                if (_lastPalmValid[h] && dt > 1e-4 && dt < 0.25 && (v - _lastPalm[h]).magnitude / dt > HandActivitySpeed)
                {
                    moving = true;
                    if (NearTable(v)) nearTable = true;
                }
                _lastPalm[h] = v;
                _lastPalmValid[h] = true;
            }
            if (moving) NoteActivity(nearTable);
        }

        void NoteGestureActivity(GestureEvent e)
        {
            if (e.Kind == GestureKind.PeekChanged && e.Amount < 0.15f) return;
            NoteActivity();
        }

        void NoteActivity(bool nearTable = true)
        {
            Hints.NoteActivity();
            if (nearTable && TutorialRunning && !IdlePaused) Tutorial.NoteActivity();
        }

        void TickHints(float dt)
        {
            bool turn = Flow == GameFlow.PlayerTurn && AutoPlayer == null;
            if (turn && !ReferenceEquals(_hintState, Session.State))
            {
                // 合法動作只在牌局狀態換了才重算（LegalActions 每次都會配置）
                _hintState = Session.State;
                var la = Session.Legal(Human);
                _hintLegal = new HintContext { CanCheck = la.CanCheck, CanCall = la.CanCall, CanBet = la.CanBet || la.CanRaise, CanFold = la.CanFold, CanAllIn = la.CanAllIn };
            }
            var c = _hintLegal;
            c.PlayerTurn = turn;
            c.MenuOpen = MenuOpen;
            c.VoiceBusy = VoiceBusy;
            c.TutorialActive = InTutorialHand && Tutorial.IsActive;
            Hints.Tick(dt, c);

            GhostGesture? want = null;
            if (!MenuOpen)
            {
                if (c.TutorialActive && _tutorialPrompted) { if (!IdlePaused) want = TutorialGhost(); }
                else if (_jitGhost.HasValue && Now < _jitGhostUntil && !InTutorialHand) want = _jitGhost;
                else if (Hints.Visible && Hints.Gestures.Count > 0)
                {
                    var list = Hints.Gestures;
                    if (_hintIndex >= list.Count) _hintIndex = 0;
                    // 示範完一輪就換下一個合法動作
                    if (Ghost.Current == list[_hintIndex] && Ghost.LoopsCompleted >= 1) _hintIndex = (_hintIndex + 1) % list.Count;
                    want = list[_hintIndex];
                }
                else _hintIndex = 0;
            }
            if (want.HasValue) Ghost.Play(want.Value);
            else Ghost.Stop();
            Ghost.Tick(dt, Head != null ? Head.position : (Vector3?)null);
            TickHandHint();
        }

        /// <summary>
        /// 教學這一步的示範手：決定那一步輪流示範跟注、棄牌（每播完一輪換一個）；
        /// 第 2 手的下注要等回合過了 BetGhostDelay 秒才出現（Dot 剛教「等幾秒再下注」）。
        /// </summary>
        GhostGesture? TutorialGhost()
        {
            var step = Tutorial.Step;
            var list = Tutorial.GhostsFor(step);
            if (step == TutorialStep.Bet && !(Flow == GameFlow.PlayerTurn && (Tutorial.TotalStepTime >= BetGhostDelay || Tutorial.HintLevel > 0))) return null;
            if (list.Length == 1) return list[0];
            if (_tutGhostIdx >= list.Length) _tutGhostIdx = 0;
            if (Ghost.Current == list[_tutGhostIdx] && Ghost.LoopsCompleted >= 1) _tutGhostIdx = (_tutGhostIdx + 1) % list.Length;
            return list[_tutGhostIdx];
        }
    }
}
