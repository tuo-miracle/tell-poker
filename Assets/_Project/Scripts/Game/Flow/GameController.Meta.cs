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
    /// 牌局之外的事：荷官帶的教學（v4，兩手的短教學，見 Tutorial／TutorialScript）、一局結束的結算、進度存檔與開場白。
    /// 教學的兩手不算戰績（不寫進存檔、不算這一局的手數、手勢成功次數也不算），教完（或跳過）重設牌桌再正式開打。
    /// v4（2026-10-14）：第 1 手看牌 → 牌力提示 → Rocco 加注、玩家想的時候 Rocco 用即時系統讀他（保證至少一句）→ 跟或棄 → 揭曉「Rocco read you: …」；
    /// 第 2 手 Dot 教「等幾秒再下注」→ 等了 Rocco 跟、輸、「You played me!」；沒等 Rocco 棄、「Try waiting next time.」。決定之後的街由遊戲替玩家過牌。
    /// </summary>
    public sealed partial class GameController
    {
        /// <summary>進度存檔路徑（空白 = Application.persistentDataPath/tell_progress.json）。</summary>
        public string ProgressPath;
        /// <summary>第一次玩（存檔裡沒完成目前版本的教學）時由荷官帶兩手。</summary>
        public bool TutorialEnabled = true;
        /// <summary>第 2 手的示範手（下注）要等玩家的回合過了這麼久才出現：Dot 剛教「等幾秒再下注」，馬上示範下注會叫人別等。</summary>
        public const float BetGhostDelay = 3.5f;

        public ProgressData Progress { get; private set; }
        /// <summary>這次的教學（不需要教就是 null）。</summary>
        public Tutorial Tutorial { get; private set; }
        public int PraisesGiven { get; private set; }
        /// <summary>最近一次一局結束的結算。</summary>
        public GameSummary LastSummary { get; private set; }
        public event Action<GameSummary> GameEnded;

        ProgressStore _store;
        bool _tutorialHand, _tutorialPrompted, _finishPending, _resetAfterTutorial, _newGameLineAfterReset;
        /// <summary>這一手是教學的第幾手（1、2）。</summary>
        int _tutorialHandNo;
        Tutorial _tutorialFinished;
        int _praiseN, _tutGhostIdx;
        double _promptAt = -1, _praiseUntil, _lastReminder = double.NegativeInfinity;
        TutorialStep _promptedStep;
        float _summaryT, _tutAutoT = -1f;
        // 教學這一手：Rocco 讀到的判斷、說過即時台詞沒、第 2 手跟不跟、結果
        LiveBelief _tutBelief;
        bool _tutLiveSpoken, _tutRoccoCalls, _tutPlayerWon, _tutRoccoFolded;
        readonly List<Utterance> _tutSpeech = new List<Utterance>();

        /// <summary>這一手是教學的一手（固定牌、腳本 AI）。</summary>
        bool InTutorialHand => _tutorialHand && Tutorial != null;
        /// <summary>教學的一手，而且還沒教完。</summary>
        bool TutorialRunning => InTutorialHand && Tutorial.IsActive;
        /// <summary>這一手的教學步驟還沒做（決定之後就是 false：遊戲替玩家過牌）。</summary>
        bool TutorialStepThisHand => TutorialRunning && Tutorial.HandOf(Tutorial.Step) == _tutorialHandNo;

        /// <summary>重拍用（寫實預覽版的 debug.tell.firstrun，見 FirstRunReplay）：當成第一次玩，教學從頭、Dot 自我介紹；存檔不動。Init 之前設。</summary>
        public bool ReplayFirstRun { get; set; }

        void Awake()
        {
            // 寫實預覽版的實機：debug.tell.firstrun 換了新值 → 這次當成第一次玩（牌桌也不還原錨點，重新出雙手手印）
            if (FindAnyObjectByType<RealisticPreview>() == null || !FirstRunReplay.Active(true)) return;
            ReplayFirstRun = true;
            var placer = FindAnyObjectByType<TablePlacer>();
            if (placer != null) placer.IgnoreSavedAnchors = true;
        }

        void InitMeta()
        {
            _store = new ProgressStore(string.IsNullOrEmpty(ProgressPath) ? ProgressStore.DefaultPath : ProgressPath);
            Progress = _store.Load();
            CoreEvents += NoteTutorialEvents;
            if (!TutorialEnabled) return;
            int done = Progress.CompletedTutorialVersion;
            if (ReplayFirstRun && TutorialScript.Supports(Session.SeatCount))
            {
                Debug.Log($"[Tutorial] first-run replay: version={Tutorial.CurrentVersion} begin (saved v{done} kept)");
                NewTutorial();
                return;
            }
            if (!Tutorial.IsNeeded(Progress)) { Debug.Log($"[Tutorial] skipped: already completed v{done}"); return; }
            if (!TutorialScript.Supports(Session.SeatCount))
            {
                Debug.Log($"[Tutorial] version={Tutorial.CurrentVersion} needs {TutorialScript.Seats} or {TutorialScript.Seats + 1} seats (table has {Session.SeatCount}): not shown");
                return;
            }
            // 沒教過、或教過的是舊版：教一次新版
            Debug.Log($"[Tutorial] version={Tutorial.CurrentVersion} begin (saved v{done})");
            NewTutorial();
        }

        void NewTutorial()
        {
            Tutorial = new Tutorial();
            Tutorial.StepCompleted += OnTutorialStepDone;
            Tutorial.HintDue += OnTutorialHint;
            Tutorial.HintsQuieted += s => Debug.Log($"[Tutorial] hints quiet ({s})");
            _finishPending = false;
            _praiseN = 0;
        }

        /// <summary>手腕選單的「Replay tutorial」：下一手起重新教一次（這一手照常打完；教學在重設的牌桌上，教完再重設開新局；存檔裡的完成紀錄不動）。</summary>
        public void ReplayTutorial()
        {
            if (Tutorial != null && Tutorial.IsActive) return;
            if (FoolRoccoActive) { Debug.Log("[Tutorial] replay ignored: Fool Rocco is running"); return; }
            if (!TutorialScript.Supports(Session.SeatCount)) { Debug.Log("[Tutorial] replay needs a 4- or 5-seat table"); return; }
            Debug.Log($"[Tutorial] version={Tutorial.CurrentVersion} replay requested");
            NewTutorial();
            Voice.Say(DealerLines.TutorialNextHand, Now, "tutorial", force: true);
        }

        /// <summary>手腕選單的「Skip tutorial」：整個教學結束、存檔記為完成；教學的一手打到一半就作廢，重設牌桌正式開打。</summary>
        public void SkipTutorial()
        {
            if (Tutorial == null || !Tutorial.IsActive) return;
            bool lessonHand = InTutorialHand;
            bool midHand = lessonHand && Flow != GameFlow.HandOver && Flow != GameFlow.GameOver && Flow != GameFlow.NotStarted;
            var step = Tutorial.Step;
            Tutorial.SkipAll();
            _tutorialFinished = Tutorial;
            _finishPending = false;
            ClearTutorialCues();
            SaveTutorialDone();
            Debug.Log("[Tutorial] skipped by player, saved");
            Playtest.Event("tutorial_step", "step=Done state=skipped at=" + step);
            Voice.Say(Tutorial.SkipLine, Now, "tutorial.skip", force: true);
            if (!lessonHand) return;
            _resetAfterTutorial = true;
            _newGameLineAfterReset = false;
            if (midHand)
            {
                // 進行中的教學手作廢：演出清掉，下一幀 StartNextHand 會重設牌桌、發新的一手
                _seq.Clear();
                ClearFreeFlights();
                PreFoldQueued = false;
                Flow = GameFlow.NotStarted;
            }
        }

        void SaveProgress()
        {
            if (_store != null) _store.Save(Progress);
        }

        void SaveTutorialDone()
        {
            Progress.TutorialDone = true;
            Progress.TutorialVersion = Tutorial.CurrentVersion;
            SaveProgress();
        }

        // ================= 教學 =================

        void BeforeHandStarts()
        {
            _promptAt = -1;
            _tutorialPrompted = false;
            _tutAutoT = -1f;
            _tutBelief = null;
            _tutLiveSpoken = _tutRoccoCalls = _tutPlayerWon = _tutRoccoFolded = false;
            _tutLivePending = null;
            _tutSpeech.Clear();
            _tutGhostIdx = 0;
            if (_resetAfterTutorial)
            {
                // 教完（或跳過）：教學的籌碼不帶進正式牌局
                _resetAfterTutorial = false;
                Session.ResetTable(Now);
                if (_newGameLineAfterReset) Voice.Say(DealerLines.NewGame, Now, "newgame", force: true);
                _newGameLineAfterReset = false;
            }
            BeforeFoolRoccoHand();
            _tutorialHand = !_foolHand && Tutorial != null && Tutorial.IsActive;
            if (!_tutorialHand) return;
            _tutorialHandNo = Tutorial.HandOf(Tutorial.Step);
            // 第 1 手一律從乾淨的牌桌開始（重看教學時籌碼與盲注可能已經變了；台詞裡的金額是固定的）
            if (_tutorialHandNo == 1) Session.ResetTable(Now);
            Session.NextHandPreset = TutorialScript.Preset(_tutorialHandNo);
            Session.NextHandButton = TutorialScript.ButtonFor(Session.SeatCount);
        }

        void AfterHandDealt()
        {
            // 牌發到面前之後才說第一句提示；第 2 手一發完牌就教（教完 Rocco 才動）
            if (TutorialStepThisHand && (Tutorial.Step == TutorialStep.Peek || Tutorial.Step == TutorialStep.Bet)) _seq.Do(PromptTutorial);
            AfterFoolRoccoHandDealt();
        }

        void PromptTutorial()
        {
            _promptAt = -1;
            if (!TutorialRunning) return;
            var step = Tutorial.Step;
            if (!_tutorialPrompted || _promptedStep != step)
            {
                Debug.Log($"[Tutorial] version={Tutorial.CurrentVersion} step={step} start");
                Playtest.Event("tutorial_step", "step=" + step + " state=start");
            }
            Voice.Say(Tutorial.Prompt(step), Now, "tutorial", force: true);
            Props.ShowTutorialGlow(Tutorial.GlowFor(step));
            _lastReminder = Now;
            _tutorialPrompted = true;
            _promptedStep = step;
        }

        void OnTutorialPlayerTurn()
        {
            // 這一手的決定做過了（或教完了）：之後的街由遊戲替玩家過牌，每手都很短
            if (!TutorialStepThisHand) { _tutAutoT = 0.5f; return; }
            // 這一步已經提示過（第 2 手在發牌時就教了）：輪到玩家不再重講
            if (_tutorialPrompted && _promptedStep == Tutorial.Step) return;
            // 剛讚美完（或牌力的小聲提醒）就等說完再講下一步
            _promptAt = Math.Max(Now, _praiseUntil);
        }

        /// <summary>這一步現在做得到：看牌在第 1 手進行中；決定、下注要輪到玩家（而且是那一手）。</summary>
        bool TutorialStepReady(TutorialStep step)
        {
            if (step == TutorialStep.Done || Tutorial.HandOf(step) != _tutorialHandNo) return false;
            if (step == TutorialStep.Peek)
                return !_playerFolded && (Flow == GameFlow.PlayerTurn || Flow == GameFlow.AiThinking || Flow == GameFlow.Presenting);
            return Flow == GameFlow.PlayerTurn;
        }

        bool DealerBusy => Dealer != null && Dealer.Speech != null && (Dealer.Speech.IsSpeaking || Dealer.Speech.QueuedCount > 0);

        /// <summary>還在學看牌：AI 先不行動（劇本從 Rocco 加注開始）；第 2 手 Dot 還在教：Rocco 先不動。</summary>
        bool TutorialHoldsAi => TutorialStepThisHand &&
            (Tutorial.Step == TutorialStep.Peek || Tutorial.Step == TutorialStep.Bet && (!_tutorialPrompted || DealerBusy));

        /// <summary>教學這一手結束的台詞（揭曉、You played me!、教完）還沒講完：下一手先不要開始。</summary>
        bool TutorialHoldsHandOver => InTutorialHand && (_finishPending || TutorialSpeaking);

        bool TutorialSpeaking
        {
            get
            {
                foreach (var u in _tutSpeech) if (u.State == UtteranceState.Queued || u.State == UtteranceState.Playing) return true;
                return false;
            }
        }

        bool DealerSpeaking => Dealer != null && Dealer.Speech != null && Dealer.Speech.IsSpeaking;

        void TickTutorial(float dt)
        {
            if (!InTutorialHand) return;
            TickTutorialAutoCheck(dt);
            if (_finishPending && Flow == GameFlow.HandOver && !TutorialSpeaking) FinishTutorial();
            if (!TutorialRunning) return;
            var step = Tutorial.Step;
            bool ready = TutorialStepReady(step);
            // 沒有人在（沒有手 ≥ 5 s、失焦）：不計時、不提示，回來時 ResumeTutorial 重新計時、重講一次
            if (IdlePaused) return;
            if (_promptAt >= 0 && Now >= _promptAt && ready) PromptTutorial();
            // 提示過、這一步現在做得到、選單關著、Dot 沒在講話才算「卡住」的時間（等 AI、聽提示的時間不算）
            if (_tutorialPrompted && ready && !MenuOpen && !DealerSpeaking) Tutorial.Tick(dt);
        }

        /// <summary>決定之後的街：替玩家過牌（Rocco 也只過牌）。</summary>
        void TickTutorialAutoCheck(float dt)
        {
            if (_tutAutoT < 0f || Flow != GameFlow.PlayerTurn || !_seq.IsIdle) return;
            _tutAutoT -= dt;
            if (_tutAutoT >= 0f) return;
            _tutAutoT = -1f;
            var la = Session.Legal(Human);
            CommitPlayer(la.CanCheck ? new Check(Human) : la.CanCall ? (PlayerCommand)new Call(Human) : new Fold(Human), null, null);
        }

        /// <summary>辨識器的診斷行：和目前這一步有關的「差一點」原因記下來（第二級提示用）。</summary>
        void OnGestureTrace(string msg)
        {
            NoteFoolRoccoTrace(msg);
            if (!TutorialRunning) return;
            var why = Tutorial.ReasonFromTrace(Tutorial.Step, msg);
            if (why != null) Tutorial.NoteNearMiss(why);
        }

        void TutorialDid(TutorialAction a)
        {
            if (InTutorialHand && TutorialStepThisHand) Tutorial.Report(a);
        }

        void OnTutorialStepDone(TutorialStep step)
        {
            Debug.Log($"[Tutorial] version={Tutorial.CurrentVersion} step={step} done");
            Playtest.Event("tutorial_step", "step=" + step + " state=done");
            _tutorialPrompted = false;
            Props.ShowTutorialGlow(TutorialGlow.None);
            if (step == TutorialStep.Peek)
            {
                // 看到牌了：Dot 第一次的牌力小聲提醒就是回饋；沒有（提示關掉、說過了）才讚美
                if (JustWhispered) _praiseUntil = Now + 2.2;
                else
                {
                    PraisesGiven++;
                    Voice.Say(Tutorial.Praise(_praiseN++), Now, "praise", force: true, mood: Mood.Happy);
                    _praiseUntil = Now + 1.4;
                }
                Audio.Play(SfxKind.Win, Head != null ? Head.position : Table.transform.position, 0.5f, 1.35f);
                _promptAt = _praiseUntil;
                return;
            }
            // 決定、下注：由 Rocco 回應（OnTutorialPlayerAction），不讚美
            if (!Tutorial.IsActive) TutorialReachedTheEnd();
        }

        /// <summary>卡住了：第一級重播示範手＋重講這一步；第二級依差一點的原因給具體建議。都有聲音與字幕。</summary>
        void OnTutorialHint(TutorialStep step, int level, string reason)
        {
            Playtest.StuckAt(step.ToString(), Tutorial.TotalStepTime);
            if (level == 1)
            {
                Debug.Log($"[Tutorial] version={Tutorial.CurrentVersion} step={step} hint1");
                Playtest.Event("tutorial_step", "step=" + step + " state=hint1");
                Voice.Say(Tutorial.Prompt(step), Now, "tutorial", force: true);
                Props.ShowTutorialGlow(Tutorial.GlowFor(step));
                Ghost?.Hide(); // TickHints 會從頭再播一次
                _lastReminder = Now;
                return;
            }
            Debug.Log($"[Tutorial] version={Tutorial.CurrentVersion} step={step} hint2 ({reason ?? "no attempt"})");
            Playtest.Event("tutorial_step", "step=" + step + " state=hint2");
            Voice.Say(Tutorial.Tip(step, reason), Now, "tutorial.tip", force: true);
        }

        /// <summary>
        /// 玩家的行動被 Core 接受了（CommitPlayer 呼叫，在這一回合的訊號凍結之前）：
        /// 第 1 手的決定 → 用同一套讀人規則（PlayerRead）算出 Rocco 讀到什麼，一手結束時揭曉；這一手 Rocco 還沒開口就馬上說他讀到的（保證至少一句）。
        /// 第 2 手的下注 → 輪到你到下注落定 ≥ TutorialScript.SlowBetSeconds 秒＝Rocco 讀成虛張而跟；不然棄牌。
        /// </summary>
        void OnTutorialPlayerAction(PlayerCommand cmd, PlayerSignals signals)
        {
            if (!TutorialStepThisHand) return;
            bool aggressive = cmd is Bet || cmd is Raise || cmd is AllIn;
            var step = Tutorial.Step;
            if (step == TutorialStep.Decide && (aggressive || cmd is Call || cmd is Fold))
            {
                Habits.EndTurn(_handsClock);
                var h = Habits.Snapshot();
                _tutBelief = TutorialRead(signals);
                Debug.Log(FormattableString.Invariant($"[Tutorial] hand=1 decision {cmd.GetType().Name}: reach={h.ReachDelay:0.00}s peeks={h.Peeks} belief={(_tutBelief != null ? _tutBelief.Belief + " from " + _tutBelief.Describe() : "none")} live line before={_tutLiveSpoken}"));
                if (_tutLiveSpoken) return;
                // 來不及在你想的時候開口（一輪到就出手）：出手之後馬上說
                var line = cmd is Fold ? LiveTalk.Closing(Archetype.Loudmouth, false)
                    : FoolRocco.CalloutLine(FoolRocco.PickCallout(new FoolSignals(h.ReachDelay, h.ReachedForChips, h.Peeks, h.ChipHover)), h.Peeks);
                if (line == null) return;
                _tutLiveSpoken = true;
                Debug.Log($"[LiveTalk] tutorial read after the action -> rocco line={line.Id}");
                SayRoccoTut(line, Mood.Confident);
                return;
            }
            if (step == TutorialStep.Bet && aggressive)
            {
                Habits.EndTurn(_handsClock);
                float took = Habits.Turn.ThinkSeconds;
                _tutRoccoCalls = took >= TutorialScript.SlowBetSeconds;
                Debug.Log(FormattableString.Invariant($"[Tutorial] hand=2 bet took={took:0.00}s reach={Habits.ReachDelay:0.00}s → {(_tutRoccoCalls ? "waited: Rocco calls" : "fast: Rocco folds")}"));
                SayRoccoTut(_tutRoccoCalls ? FoolRocco.RoccoCall : FoolRocco.RoccoFold, _tutRoccoCalls ? Mood.Confident : Mood.Annoyed);
            }
        }

        /// <summary>Rocco 從這一回合的訊號讀到什麼（同一套 PlayerRead 規則；棄牌也照樣描述他看到的）。沒有任何線索＝null。</summary>
        static LiveBelief TutorialRead(PlayerSignals s)
        {
            if (s == null || !s.Live) return null;
            var copy = new PlayerSignals
            {
                Live = true, ThinkSeconds = s.ThinkSeconds, Looks = s.Looks, PullBacks = s.PullBacks, PutBacks = s.PutBacks, HeldCount = s.HeldCount,
                TowardCards = s.TowardCards, Aggressive = s.Aggressive, Folded = false, Delivery = s.Delivery, ThrowSpeed = s.ThrowSpeed, Time = s.Time,
            };
            var read = new PlayerRead(TutorialScript.ReadSeat, 1f, new PlayerReadSettings());
            read.Observe(copy);
            return read.Belief;
        }

        void NoteTutorialEvents(IReadOnlyList<GameEvent> events)
        {
            if (!InTutorialHand) return;
            foreach (var e in events)
            {
                if (e is PotAwarded pa) foreach (var s in pa.Shares) if (s.Seat == Human && s.Amount > 0) _tutPlayerWon = true;
                if (e is PlayerActed a && a.Action == ActionType.Fold && a.Seat == TutorialScript.ReadSeat) _tutRoccoFolded = true;
            }
        }

        /// <summary>EnterHandOver 呼叫：第 1 手揭曉 Rocco 讀到什麼（攤牌知道對錯時加一句收尾）；第 2 手 You played me!／下次等等看。</summary>
        void OnTutorialHandOver()
        {
            if (!InTutorialHand) return;
            if (_tutorialHandNo == 1)
            {
                bool showdown = !_playerFolded && !_tutRoccoFolded;
                var outcome = ReadOutcome.None;
                string text;
                if (_tutBelief == null) text = TutorialScript.RevealNothing;
                else
                {
                    if (showdown) outcome = (_tutBelief.Belief == "weak") == _tutPlayerWon ? ReadOutcome.Fooled : ReadOutcome.Right;
                    text = RevealText("Rocco", _tutBelief, outcome);
                }
                ShowReadReveal("Rocco", text);
                if (outcome == ReadOutcome.None) return;
                if (outcome == ReadOutcome.Fooled) Playtest.Fooled("Rocco", "tutorial", 1);
                var closing = LiveTalk.Closing(Archetype.Loudmouth, outcome == ReadOutcome.Fooled);
                if (closing != null) SayRoccoTut(closing, outcome == ReadOutcome.Fooled ? Mood.Startled : Mood.Confident);
                return;
            }
            bool fooled = _tutRoccoCalls && _tutPlayerWon;
            Debug.Log($"[Tutorial] hand=2 outcome={(fooled ? "fooled" : _tutRoccoFolded ? "rocco-folded" : "other")}");
            if (fooled)
            {
                SayRoccoTut(FoolRocco.RoccoPlayed, Mood.Startled);
                SayDotTut(FoolRocco.DotFooled, Mood.Happy);
                ShowReadReveal("Rocco", FoolRocco.RevealFooled);
                Playtest.Fooled("Rocco", "tutorial", 2);
            }
            else
            {
                SayDotTut(FoolRocco.DotRetry, Mood.Neutral);
                ShowReadReveal("Rocco", FoolRocco.RevealQuick);
            }
        }

        void SayDotTut(SpeechLine l, Mood mood)
        {
            Voice.Say(l.Text, Now, "tut." + l.Id, l.Id, force: true, mood: mood);
            var u = Dealer != null ? Dealer.Speech.LastSubmitted : null;
            if (u != null && u.Text == l.Text) _tutSpeech.Add(u);
        }

        void SayRoccoTut(SpeechLine l, Mood mood)
        {
            var r = Character(TutorialScript.ReadSeat);
            if (r == null) return;
            // 表情與看玩家（不帶字：字和聲音由 SpeechChannel 一起處理）
            r.Play(new PerformanceCue { Seat = TutorialScript.ReadSeat, Mood = mood, Intensity = 0.8f, Look = LookTarget.Player, Duration = 2.5f });
            _tutSpeech.Add(r.Speech.Say(l.Text, SpeechPriority.Info, l.Id));
        }

        /// <summary>最後一步做完：一手還在演（Rocco 跟注、攤牌）就等一手結束、台詞講完再收尾。</summary>
        void TutorialReachedTheEnd() => _finishPending = true;

        /// <summary>教完：寫入存檔（TutorialVersion），最後一句，下一手重設牌桌正式開打。</summary>
        void FinishTutorial()
        {
            if (_tutorialFinished == Tutorial) return;
            _tutorialFinished = Tutorial;
            _finishPending = false;
            ClearTutorialCues();
            SaveTutorialDone();
            Debug.Log("[Tutorial] finished, saved");
            Playtest.Event("tutorial_step", "step=Done state=finished");
            Voice.Say(Tutorial.Finished, Now, "praise.finished", force: true, mood: Mood.Happy);
            var u = Dealer != null ? Dealer.Speech.LastSubmitted : null;
            if (u != null && u.Text == Tutorial.Finished) _tutSpeech.Add(u);
            _resetAfterTutorial = true;
            _newGameLineAfterReset = true;
        }

        void ClearTutorialCues()
        {
            _promptAt = -1;
            _tutorialPrompted = false;
            Props.ShowTutorialGlow(TutorialGlow.None);
            Ghost?.Hide();
        }

        /// <summary>教學中一次丟太多（全下或超過 RaiseCapBlinds 個大盲）：退回並請玩家丟少一點（教學不能一手輸太多）。其他金額都可以。</summary>
        bool TutorialRefusesBet(ChipCommit cc)
        {
            if (!TutorialStepThisHand) return false;
            if (!(cc.IsValid && (cc.Kind == ChipCommitKind.AllIn || cc.Committed > TutorialScript.RaiseCapBlinds * Session.State.BigBlind))) return false;
            ReturnPending();
            Voice.Say(DealerLines.SmallBetOnly, Now, "tutorial.amount", force: true);
            return true;
        }

        /// <summary>教學這一手擋不擋這個手勢：這一手的步驟還沒做 → 照 Tutorial.Allows；做完了（遊戲替玩家過牌）→ 只能看牌。</summary>
        bool TutorialBlocks(GestureKind k)
        {
            if (!InTutorialHand) return false;
            if (TutorialStepThisHand) return !Tutorial.Allows(k);
            return !(k == GestureKind.PeekChanged || k == GestureKind.CardsLifted || k == GestureKind.CardsReturned);
        }

        /// <summary>教學中做了不是這一步的動作：籌碼退回、重講一次這一步（不改牌局）。看牌不會被擋。</summary>
        void OnTutorialBlocked(GestureEvent e)
        {
            switch (e.Kind)
            {
                case GestureKind.BetCommitted:
                    ReturnPending();
                    break;
                case GestureKind.KnockCheck:
                case GestureKind.FoldPushed:
                case GestureKind.AllInConfirmed:
                case GestureKind.ShowdownFlip:
                    break;
                default:
                    return; // 拿籌碼之類的小動作就安靜擋掉
            }
            if (!TutorialRunning) return;
            if (Now - _lastReminder < 1.0 && Voice.LastText == Tutorial.Prompt(Tutorial.Step)) return;
            if (TutorialStepReady(Tutorial.Step)) PromptTutorial();
        }

        // ================= 每手紀錄、一局結束 =================

        void OnHandRecorded()
        {
            // 教學的兩手不算戰績、也不會讓一局結束（教完會重設牌桌）；Fool Rocco 的手也是（結束後還原牌桌）
            if (InTutorialHand || InFoolRoccoHand) return;
            var last = Session.Stats.LastHand;
            if (last != null && last.HandNumber == Session.State.HandNumber)
            {
                Progress.RecordHand(last, Session.NameOf);
                SaveProgress();
            }
            var reason = Session.CheckGameOver(Now);
            if (reason != GameEndReason.None) EnterGameOver(reason);
        }

        void EnterGameOver(GameEndReason reason)
        {
            Flow = GameFlow.GameOver;
            _summaryT = 0f;
            Props.Hint(false);
            Props.ShowTutorialGlow(TutorialGlow.None);
            var sum = Session.Summarize(reason);
            LastSummary = sum;
            var names = new List<string>();
            foreach (var seat in Session.AiSeats) names.Add(Session.NameOf(seat));
            Progress.RecordGame(sum, names);
            SaveProgress();
            Props.ShowSummary(sum.Title, sum.Lines());
            string line = sum.Won ? DealerLines.YouTookTheTable : reason == GameEndReason.PlayerBusted ? DealerLines.Busted : DealerLines.GameOver;
            Voice.Say(line, Now, "gameover", force: true, mood: sum.Won ? Mood.Happy : Mood.Neutral);
            if (sum.Won) Audio.Play(SfxKind.Win, Head != null ? Head.position : Table.transform.position, 0.8f);
            GameEnded?.Invoke(sum);
        }

        void TickGameOver(float dt)
        {
            _summaryT += dt;
            if (_summaryT >= Config.SummarySeconds) StartNewGame();
        }

        /// <summary>結算之後（敲桌或時間到）：籌碼重設、盲注回到起點，開下一局的第一手。</summary>
        void StartNewGame()
        {
            if (Flow != GameFlow.GameOver) return;
            Props.HideSummary();
            Session.NewGame(Now);
            Voice.Say(DealerLines.NewGame, Now, "newgame", force: true);
            StartNextHand();
        }
    }
}
