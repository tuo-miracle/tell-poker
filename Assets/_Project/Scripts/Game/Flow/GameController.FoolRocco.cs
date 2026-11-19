using System;
using System.Collections.Generic;
using Poker.AI;
using Poker.Characters;
using Poker.Core;
using Poker.Gestures;
using Poker.Runtime;
using UnityEngine;

namespace Poker.Game
{
    /// <summary>
    /// 「Fool Rocco」原型情境（2026-10-14，劇本見 Poker.AI.FoolRocco）：手腕選單主頁的「Fool Rocco」開始，下一手起（一手結束時按就馬上）
    /// 在同一張四人桌上打最多四手短局——Pip、Vex 翻牌前都棄，玩家單挑 Rocco。Rocco 讀玩家真的習慣（PlayerHabits）並說出來
    /// （2026-10-14 起：你還在想、伸手、縮手時 Rocco 就用即時系統嗆你，見 GameController.LiveTalk.cs；下注後的讀牌與三手劇情照舊），
    /// 玩家照 Dot 教的「慢慢伸手」演假破綻騙他。關鍵決定之後的街由遊戲替玩家過牌（Rocco 也只過牌），每手都很短。
    /// 隔離：開始時存下牌桌、情境的手不算戰績（Session.RecordStats）、不寫存檔、不記手勢次數、AI 不讀玩家訊號、教學狀態不碰；
    /// 結束後下一手開始前把牌桌還原（籌碼、這一局第幾手、計時）。每一句都經過角色的 SpeechChannel（預錄聲音＋同一句字幕）。
    /// 實機 log：[FoolRocco] requested／start／hand=n dealt／hand=n player …／hand=n signals … outcome=…／end, table restored。
    /// </summary>
    public sealed partial class GameController
    {
        /// <summary>進行中的情境（沒有就是 null；最後一手結束到還原之前也還在）。</summary>
        public FoolRocco FoolRocco => _fool;
        /// <summary>情境已經要求（下一手開始）或進行中。</summary>
        public bool FoolRoccoActive => _foolPending || _fool != null;
        /// <summary>選單可以顯示「Fool Rocco」：四人桌、2 號座位是 Rocco（大嘴巴老手）、教學沒在跑、情境沒在跑、不在結算畫面。</summary>
        public bool CanStartFoolRocco =>
            IsInitialized && TutorialScript.Supports(SeatCount) && Character(FoolRocco.RoccoSeat) != null
            && Session.AiAt(FoolRocco.RoccoSeat).Personality.Archetype == Archetype.Loudmouth && !FoolRoccoActive
            && !(Tutorial != null && Tutorial.IsActive) && Flow != GameFlow.GameOver;
        /// <summary>玩家習慣與即時訊號的量測（每一手都餵資料；Fool Rocco 的劇本與即時對話、讀人共用）。</summary>
        public PlayerHabits Habits { get; private set; }
        /// <summary>底池上方的揭曉卡文字（沒顯示是 null）。</summary>
        public string FoolRoccoReveal => _foolReveal != null && _foolReveal.Go.activeSelf ? _foolReveal.Text : null;
        /// <summary>情境開始時存下的籌碼／結束還原後的籌碼（除錯與測試用）。</summary>
        public int[] FoolRoccoSavedStacks { get; private set; }
        public int[] FoolRoccoRestoredStacks { get; private set; }
        /// <summary>情境的一手結束（劇本的結果）。</summary>
        public event Action<FoolEnd> FoolRoccoHandEnded;

        /// <summary>揭曉卡停留秒數。</summary>
        public const float FoolRevealSeconds = 5f;

        FoolRocco _fool;
        bool _foolPending, _foolHand, _foolPlayerWon, _foolRoccoFolded, _foolPlayerFolded;
        TableSnapshot _foolSnap;
        WorldLabel _foolReveal;
        float _foolRevealT, _foolAutoCheckT = -1f;
        double _handsClock;
        readonly List<Utterance> _foolSpeech = new List<Utterance>();

        /// <summary>這一手是情境的一手（固定牌、腳本 AI）。</summary>
        bool InFoolRoccoHand => _foolHand && _fool != null;
        /// <summary>情境擁有牌桌（開始到還原之前）：不檢查一局結束。</summary>
        bool FoolRoccoOwnsTable => _fool != null;
        /// <summary>情境的台詞還在排隊或在講。</summary>
        bool FoolRoccoSpeaking
        {
            get
            {
                foreach (var u in _foolSpeech) if (u.State == UtteranceState.Queued || u.State == UtteranceState.Playing) return true;
                return false;
            }
        }
        /// <summary>Dot 還在教／Rocco 還在講：AI 先不行動（玩家的回合也就還沒開始）。</summary>
        bool FoolRoccoHoldsAi => InFoolRoccoHand && FoolRoccoSpeaking;
        /// <summary>一手結束的台詞與揭曉卡還沒完：先不開下一手。</summary>
        bool FoolRoccoHoldsHandOver => InFoolRoccoHand && (FoolRoccoSpeaking || _foolRevealT > 0f);

        void InitFoolRocco()
        {
            Habits = new PlayerHabits(TableDimensions.Layout);
            // 每一手都量（即時對話與讀人也用，見 GameController.LiveTalk.cs）
            GestureSeen += e => Habits.Observe(e);
            CoreEvents += NoteFoolRoccoEvents;
            _foolReveal = WorldLabel.Plaque(Props.Root, "FoolRoccoReveal", 0.0046f);
        }

        /// <summary>手腕選單的「Fool Rocco」：下一手開始情境（一手結束、還沒開始時馬上開始）。</summary>
        public void StartFoolRocco()
        {
            if (!CanStartFoolRocco) { Debug.Log($"[FoolRocco] not available (flow={Flow}, tutorial={(Tutorial != null && Tutorial.IsActive)}, active={FoolRoccoActive})"); return; }
            _foolPending = true;
            Debug.Log($"[FoolRocco] requested (flow={Flow})");
            if (Flow == GameFlow.HandOver) StartNextHand();
            else if (Flow != GameFlow.NotStarted) SayDot(FoolRocco.DotNextHand);
        }

        // ================= 每一手 =================

        /// <summary>BeforeHandStarts 呼叫：情境結束就還原牌桌；要開始就存下牌桌；情境的每一手用乾淨的牌桌與固定牌。</summary>
        void BeforeFoolRoccoHand()
        {
            _foolHand = false;
            _foolAutoCheckT = -1f;
            _foolPlayerWon = _foolRoccoFolded = _foolPlayerFolded = false;
            _foolSpeech.Clear();
            HideFoolReveal();
            if (_fool != null && _fool.IsDone) { EndFoolRocco(); return; }
            if (_foolPending)
            {
                _foolPending = false;
                _foolSnap = Session.SaveTable(Now);
                FoolRoccoSavedStacks = Session.State.Stacks();
                Session.RecordStats = false;
                _fool = new FoolRocco();
                Debug.Log("[FoolRocco] start (table saved; scenario hands are not recorded)");
            }
            if (_fool == null) return;
            _fool.BeginHand();
            _foolHand = true;
            Session.ScenarioTable(Now);
            Session.NextHandPreset = FoolRocco.Preset(_fool.Hand);
            Session.NextHandButton = FoolRocco.ButtonFor(SeatCount);
            Habits.BeginHand();
            Debug.Log($"[FoolRocco] hand={_fool.Hand}{(_fool.Replaying ? " (replay)" : "")} dealt");
        }

        void EndFoolRocco()
        {
            Session.RestoreTable(_foolSnap, Now);
            Session.RecordStats = true;
            FoolRoccoRestoredStacks = Session.State.Stacks();
            _fool = null;
            _foolSnap = null;
            Debug.Log("[FoolRocco] end, table restored");
        }

        /// <summary>牌發好了：Dot 的開場（第 1 手）或教學（第 2 手）。講完 AI 才行動。</summary>
        void AfterFoolRoccoHandDealt()
        {
            if (!InFoolRoccoHand) return;
            var intro = _fool.HandIntro();
            if (intro != null) _seq.Do(() => SayDot(intro));
        }

        /// <summary>輪到玩家：關鍵決定之前開始量習慣；決定之後替玩家過牌（回傳 true＝不用提示輪到你）。</summary>
        bool OnFoolRoccoPlayerTurn()
        {
            // 習慣的量測已經在 BeginLiveTurn 開始了（每一手都量）
            if (_fool.Decided) { _foolAutoCheckT = 0.5f; return true; }
            return false;
        }

        /// <summary>玩家的行動被 Core 接受了：決定那一回合的訊號交給劇本，Rocco 馬上回應。</summary>
        void OnFoolRoccoPlayerAction(PlayerCommand cmd)
        {
            _foolAutoCheckT = -1f;
            if (_fool.Decided || !Habits.InTurn) return;
            Habits.EndTurn(_handsClock);
            var h = Habits.Snapshot();
            var a = cmd is Fold ? FoolAction.Fold : cmd is Check ? FoolAction.Check : cmd is Call ? FoolAction.Call : FoolAction.Raise;
            var line = _fool.OnPlayerAction(a, new FoolSignals(h.ReachDelay, h.ReachedForChips, h.Peeks, h.ChipHover));
            Debug.Log(FormattableString.Invariant($"[FoolRocco] hand={_fool.Hand} player {a}: reach={h.ReachDelay:0.00}s ({h.Target}) peeks={h.Peeks} hover={h.ChipHover:0.00}s → {(line != null ? line.Id : "-")}"));
            if (line == null) return;
            var mood = line == FoolRocco.RoccoFold ? Mood.Annoyed : Mood.Confident;
            SayRocco(line, mood);
        }

        void NoteFoolRoccoEvents(IReadOnlyList<GameEvent> events)
        {
            if (!InFoolRoccoHand) return;
            foreach (var e in events)
            {
                if (e is PotAwarded pa) foreach (var s in pa.Shares) if (s.Seat == Human && s.Amount > 0) _foolPlayerWon = true;
                if (e is PlayerActed a && a.Action == ActionType.Fold)
                {
                    if (a.Seat == FoolRocco.RoccoSeat) _foolRoccoFolded = true;
                    if (a.Seat == Human) _foolPlayerFolded = true;
                }
            }
        }

        /// <summary>EnterHandOver 呼叫：劇本決定結果、說話、揭曉卡；記評估用的 log。</summary>
        void OnFoolRoccoHandOver()
        {
            if (!InFoolRoccoHand) return;
            var end = _fool.OnHandEnded(_foolPlayerWon, _foolRoccoFolded, _foolPlayerFolded);
            Debug.Log(end.Log);
            if (end.Rocco != null) SayRocco(end.Rocco, Mood.Startled);
            if (end.Dot != null) SayDot(end.Dot, end.Dot == FoolRocco.DotFooled ? Mood.Happy : Mood.Neutral);
            if (end.Reveal != null)
            {
                ShowFoolReveal(end.Reveal);
                Playtest.Event("read_reveal", "who=Rocco text=\"" + end.Reveal + "\"");
            }
            if (end.Outcome == "fooled") Playtest.Fooled("Rocco", "fool_rocco", _fool.Hand);
            FoolRoccoHandEnded?.Invoke(end);
        }

        void TickFoolRocco(float dt)
        {
            if (_foolRevealT > 0f)
            {
                _foolRevealT -= dt;
                if (_foolRevealT <= 0f) HideFoolReveal();
                else if (Head != null) _foolReveal.Face(Head.position);
            }
            if (!InFoolRoccoHand || _foolAutoCheckT < 0f || Flow != GameFlow.PlayerTurn || !_seq.IsIdle) return;
            _foolAutoCheckT -= dt;
            if (_foolAutoCheckT >= 0f) return;
            _foolAutoCheckT = -1f;
            var la = Session.Legal(Human);
            CommitPlayer(la.CanCheck ? new Check(Human) : la.CanCall ? (PlayerCommand)new Call(Human) : new Fold(Human), null, null);
        }

        /// <summary>每幀的手部資料（ProcessHands 一開始呼叫）：手部時鐘、情境的手才量習慣。</summary>
        void ObserveFoolRocco(HandsFrame frame)
        {
            _handsClock = frame.Time;
            Habits.Observe(frame);
        }

        void NoteFoolRoccoTrace(string msg)
        {
            Habits.NoteTrace(msg);
        }

        // ================= 說話與揭曉卡 =================

        void SayDot(SpeechLine l, Mood mood = Mood.Neutral)
        {
            Voice.Say(l.Text, Now, "fool." + l.Id, l.Id, force: true, mood: mood);
            var u = Dealer != null ? Dealer.Speech.LastSubmitted : null;
            if (u != null && u.Text == l.Text) _foolSpeech.Add(u);
        }

        void SayRocco(SpeechLine l, Mood mood)
        {
            var r = Character(FoolRocco.RoccoSeat);
            if (r == null) return;
            // 表情與看玩家（不帶字：字和聲音由 SpeechChannel 一起處理）
            r.Play(new PerformanceCue { Seat = FoolRocco.RoccoSeat, Mood = mood, Intensity = 0.8f, Look = LookTarget.Player, Duration = 2.5f });
            _foolSpeech.Add(r.Speech.Say(l.Text, SpeechPriority.Info, l.Id));
        }

        void ShowFoolReveal(string text)
        {
            _foolReveal.Show(text, ReadRevealSpot);   // 同一般牌局的揭曉卡位置（不蓋荷官的臉）
            if (Head != null) _foolReveal.Face(Head.position);
            _foolRevealT = FoolRevealSeconds;
        }

        void HideFoolReveal()
        {
            _foolRevealT = 0f;
            _foolReveal?.Hide();
        }
    }
}
