using System;
using System.Collections.Generic;
using System.Globalization;
using Poker.AI;
using Poker.Characters;
using Poker.Core;
using Poker.Gestures;
using Poker.Runtime;
using UnityEngine;

namespace Poker.Game
{
    /// <summary>
    /// 即時牌桌對話（live talk，2026-10-14；Quest 3 實機回饋：「AI 只在我下注之後講一句，不像真人；我還在動、還沒出籌碼時就該開口影響我、讀我」）：
    /// - 玩家的回合中，PlayerHabits 一個一個吐出看得到的訊號（回合開始、想了 3／7／12 秒、又看牌、伸向籌碼、縮回、捏著籌碼、放回、手放到牌上、
    ///   盯著某個 AI、開口講話），LiveTalkDirector 每個訊號最多挑一個人講一句（一次一人、整桌間隔、角色冷卻、洗牌袋、不蓋過玩家與別人的話、
    ///   教學不講）。講不講依 AI 這一回合演出來的狀態（AiPlayer.LiveStance，依可信度濾過）——開口本身就是破綻。牌不傳到任何新的地方。
    /// - 玩家行動時，這一回合的訊號統計跟著 PlayerSignals 送給 AI（PlayerRead 的即時判斷，影響決策、有上限、被騙會打折）。
    /// - 一手結束：用了判斷的 AI 裡影響最大的那個，在底池上方顯示「Rocco read you: weak」，攤牌知道對錯時加一句收尾台詞。
    /// 每一句都經過角色的 SpeechChannel（預錄聲音＋同一句字幕）。
    /// 實機 log：[LiveTalk] t=&lt;秒&gt; signal=&lt;名稱&gt; -&gt; &lt;誰&gt; line=&lt;id&gt;、[LiveTalk] skipped &lt;訊號&gt; (&lt;原因&gt;)、
    /// [Read] &lt;誰&gt; belief=&lt;weak|strong&gt; from &lt;線索&gt; effect=&lt;偏移&gt;、[Read] reveal &lt;誰&gt;: &lt;字&gt;。
    /// </summary>
    public sealed partial class GameController
    {
        /// <summary>即時對話開關（關掉時訊號照樣量、照樣給 AI 讀，只是不講話）。</summary>
        public bool LiveTalkEnabled = true;
        /// <summary>多話倍率（測試與展示用；1＝正常）。</summary>
        public float LiveTalkBoost = 1f;
        /// <summary>玩家正在講話（語音對話收音中；VoiceChatController 每幀設定）。</summary>
        public bool PlayerSpeaking { get; set; }
        public LiveTalkDirector LiveTalk { get; private set; }
        /// <summary>每個即時訊號的決定（講了或沒講與原因）。</summary>
        public event Action<LiveDecision> LiveTalkDecided;
        /// <summary>底池上方「Rocco read you: …」的字（沒顯示是 null）。</summary>
        public string ReadReveal => _readReveal != null && _readReveal.Go.activeSelf ? _readReveal.Text : null;
        /// <summary>揭曉卡的字（視野檢查用）。</summary>
        public WorldLabel ReadRevealLabel => _readReveal;
        /// <summary>揭曉卡的位置（牌桌座標）：底池上方。</summary>
        /// <summary>底池上方 16 cm：低於荷官的臉（2026-11-03 揭曉卡放大後，26 cm 會蓋到荷官的下巴），高過公共牌。</summary>
        public Vector3 ReadRevealSpot => Spots.PotPile + Vector3.up * 0.16f;
        /// <summary>讀人揭曉卡停留秒數。</summary>
        public const float ReadRevealSeconds = 5f;

        WorldLabel _readReveal;
        float _readRevealT;
        /// <summary>教學第 1 手：一定要讓 Rocco 開口，但發言權被占著（Dot 正在講決定那一句）時，訊號先留著，空出來再送一次。</summary>
        HabitSignal? _tutLivePending;
        readonly Dictionary<int, InnerState> _liveStance = new Dictionary<int, InnerState>();
        /// <summary>這一回合講過即時台詞的聲音（pip／rocco／vex）：玩家出手後他們不再補舊的長考台詞。</summary>
        readonly HashSet<string> _liveSpokeThisTurn = new HashSet<string>();
        const string LiveTag = "live";

        void InitLiveTalk()
        {
            LiveTalk = new LiveTalkDirector(Config.Seed * 977 + 5);
            _readReveal = WorldLabel.Plaque(Props.Root, "ReadReveal", 0.0044f);   // 約 1.7°（離玩家約 0.9 m）
            // 舊的「下注後長考」台詞（TableTalk，玩家出手時才送）：同一個人這一回合已經即時講過了就不再講（2026-10-14 收尾）
            Session.Talk.Veto = VetoTankAfterLive;
        }

        string VetoTankAfterLive(Line line)
        {
            if (line.Trigger != LineTrigger.PlayerTanks) return null;
            var voice = LineBook.VoiceOf(line);
            if (!_liveSpokeThisTurn.Contains(voice)) return null;
            Debug.Log($"[LiveTalk] suppressed tank {voice} line={line.Id} (spoke live this turn)");
            return "spoke live this turn";
        }

        /// <summary>玩家出手了（CommitPlayer）：還在排隊的即時台詞過時了，丟掉（不晚講）。</summary>
        void DropQueuedLiveLines()
        {
            foreach (var c in AllCharacters()) c.Speech.DropQueued(u => u.Tag == LiveTag, "player acted");
        }

        /// <summary>這一手：訊號重新量（每一手都量，不只 Fool Rocco）。</summary>
        void BeforeLiveHand()
        {
            Habits.BeginHand();
            HideReadReveal();
        }

        /// <summary>輪到玩家：開始量這一回合；問每個還在局裡的 AI 這一回合要演成什麼樣子。</summary>
        void BeginLiveTurn()
        {
            Props.StackFootprint(Human, _chipFootprint);
            Habits.SetChipFootprint(new List<System.Numerics.Vector3>(_chipFootprint), ChipStackMesh.Radius);
            Habits.BeginTurn(_handsClock);
            _liveStance.Clear();
            _liveSpokeThisTurn.Clear();
            foreach (var seat in Session.AiSeats)
            {
                var st = Session.State.Seat(seat);
                if (st.Status == SeatStatus.Folded || st.Status == SeatStatus.Out) continue;
                _liveStance[seat] = Session.AiAt(seat).LiveStance();
            }
        }

        /// <summary>玩家行動了：這一回合的即時訊號放進給 AI 的 PlayerSignals，然後停止量。</summary>
        void FillLiveSignals(PlayerSignals s, PlayerCommand cmd)
        {
            if (s == null || !Habits.InTurn) return;
            var t = Habits.Turn;
            s.Live = true;
            s.ThinkSeconds = (float)(Now - _turnStart);
            s.Looks = t.Looks;
            s.PullBacks = t.PullBacks;
            s.PutBacks = t.PutBacks;
            s.HeldCount = t.ChipsHeldCount;
            s.TowardCards = t.TowardCards && !(cmd is Fold);
            s.Aggressive = cmd is Bet || cmd is Raise || cmd is AllIn;
            s.Folded = cmd is Fold;
        }

        /// <summary>每幀（ProcessHands 之後）：注視、語音、時間 → 訊號 → 導演 → 講。</summary>
        void TickLiveTalk(float dt, HandsFrame frame)
        {
            if (frame == null) _handsClock += dt;   // 沒有手部資料時也讓回合時間往前走
            if (_readRevealT > 0f)
            {
                _readRevealT -= dt;
                if (_readRevealT <= 0f) HideReadReveal();
                else if (Head != null) _readReveal.Face(Head.position);
            }
            if (!Habits.InTurn) { Habits.ObserveVoice(_handsClock, PlayerSpeaking); return; }
            int gazed = GazedSeat;
            Habits.ObserveGaze(_handsClock, gazed, gazed >= 0 ? _gaze.DwellTime : 0f);
            Habits.ObserveVoice(_handsClock, PlayerSpeaking);
            Habits.TickTurn(_handsClock);
            foreach (var h in Habits.DrainSignals()) OnLiveSignal(h);
            if (_tutLivePending.HasValue && (_tutLiveSpoken || !TutorialLiveTalk)) _tutLivePending = null;
            if (_tutLivePending.HasValue && FloorFreeForLiveTalk())
            {
                var p = _tutLivePending.Value;
                _tutLivePending = null;
                OnLiveSignal(p);
            }
        }

        /// <summary>沒有人在講、沒有話在排隊、上一句講完夠久、離上一句即時台詞夠久（導演的規則；先在這裡看，免得每幀都記一行 skipped）。</summary>
        bool FloorFreeForLiveTalk()
        {
            float since = 999f;
            foreach (var c in AllCharacters())
            {
                var sp = c.Speech;
                if (sp.IsSpeaking || sp.QueuedCount > 0) return false;
                since = Mathf.Min(since, sp.SinceEnd);
            }
            return since >= LiveTalkDirector.QuietAfterSpeech && Now - LiveTalk.LastLineAt >= LiveTalkDirector.TableGap;
        }

        /// <summary>教學中 Rocco 讀你的時候：第 1 手的決定、第 2 手的下注（輪到玩家、這一手的步驟還沒做）。其他時候教學的話優先，即時對話不講。</summary>
        bool TutorialLiveTalk => TutorialStepThisHand && Flow == GameFlow.PlayerTurn && (Tutorial.Step == TutorialStep.Decide || Tutorial.Step == TutorialStep.Bet);

        static LiveCue CueOf(HabitSignal h)
        {
            switch (h.Kind)
            {
                case HabitSignalKind.Thinking: return h.Count >= 12 ? LiveCue.Think12 : h.Count >= 7 ? LiveCue.Think7 : LiveCue.Think3;
                case HabitSignalKind.Look: return LiveCue.ReLook;
                case HabitSignalKind.ReachChips: return LiveCue.Reach;
                case HabitSignalKind.PullBack: return LiveCue.PullBack;
                case HabitSignalKind.ChipsHeld: return LiveCue.ChipsHeld;
                case HabitSignalKind.ChipsPutBack: return LiveCue.PutBack;
                case HabitSignalKind.TowardCards: return LiveCue.TowardCards;
                case HabitSignalKind.GazeDwell: return LiveCue.Gaze;
                case HabitSignalKind.PlayerSpoke: return LiveCue.PlayerSpoke;
                default: return LiveCue.TurnStart;
            }
        }

        void OnLiveSignal(HabitSignal h)
        {
            var sig = new LiveSignal(CueOf(h), Now, h.Count, h.Target);
            var ctx = BuildLiveContext(out string blocked);
            // 「想了幾秒」是在讀發呆的玩家：手不在（頭盔放著、手放下）就沒有人可讀（2026-10-14）
            if (blocked == null && IsIdleCue(sig.Cue) && HandsAway) blocked = "no hands";
            var d = blocked != null ? new LiveDecision { Signal = sig, Skip = blocked } : LiveTalk.React(sig, ctx);
            string t = (Now - _turnStart).ToString("0.0", CultureInfo.InvariantCulture);
            string name = sig.ToString();
            if (d.Spoke)
            {
                Debug.Log($"[LiveTalk] t={t} signal={name} -> {d.Who} line={d.Line.Id}");
                SayLive(d);
                if (InTutorialHand) _tutLiveSpoken = true;
            }
            else
            {
                Debug.Log($"[LiveTalk] skipped {name} ({d.Skip}) t={t}");
                // 教學第 1 手一定要講：只是發言權被占著（Dot 在講）就等一下再送
                if (ctx.Force && (d.Skip == "floor busy" || d.Skip == "just spoke" || d.Skip == "table gap" || d.Skip.StartsWith("cooldown"))) _tutLivePending = h;
            }
            LiveTalkDecided?.Invoke(d);
        }

        /// <summary>導演要的牌桌狀況；blocked 不是 null＝這一手根本不講（關掉、自動玩家、Fool Rocco 決定之後）。</summary>
        LiveTalkContext BuildLiveContext(out string blocked)
        {
            blocked = null;
            if (!LiveTalkEnabled) blocked = "disabled";
            else if (AutoPlayer != null) blocked = "auto player";
            else if (InFoolRoccoHand && !_fool.LiveTalk) blocked = "scenario";
            // 教學：只有 Rocco 讀你的那一刻（決定、下注）講；第 1 手保證講（Force）
            var ctx = new LiveTalkContext
            {
                Now = Now, PlayerSpeaking = PlayerSpeaking || Habits.PlayerSpeaking, Tutorial = InTutorialHand && !TutorialLiveTalk,
                Force = InTutorialHand && _tutorialHandNo == 1 && !_tutLiveSpoken,
            };
            float since = 999f;
            foreach (var c in AllCharacters())
            {
                var sp = c.Speech;
                if (sp.IsSpeaking || sp.QueuedCount > 0) ctx.FloorBusy = true;
                since = Mathf.Min(since, sp.SinceEnd);
            }
            ctx.SinceSpeech = since;
            ctx.Chattiness = Mathf.Max(0f, LiveTalkBoost) * (InFoolRoccoHand ? _fool.LiveChattiness : InTutorialHand ? TutorialChattiness : 1f);
            foreach (var kv in _liveStance)
            {
                if ((InFoolRoccoHand || InTutorialHand) && kv.Key != FoolRocco.RoccoSeat) continue;   // 情境與教學裡只有 Rocco 在讀你
                var st = Session.State.Seat(kv.Key);
                if (st.Status == SeatStatus.Folded || st.Status == SeatStatus.Out) continue;
                ctx.Speakers.Add(new LiveSpeaker { Seat = kv.Key, Archetype = Session.AiAt(kv.Key).Personality.Archetype, Stance = kv.Value });
            }
            if (!InFoolRoccoHand && !InTutorialHand && Dealer != null) ctx.Speakers.Add(new LiveSpeaker { Seat = -1, Archetype = null, Stance = InnerState.Masked });
            return ctx;
        }

        void SayLive(LiveDecision d)
        {
            if (d.Seat < 0)
            {
                if (Voice.Say(d.Line.Text, Now, "live." + d.Line.Id, d.Line.Id, cooldown: 0f, force: false)) MarkLive(Dealer.Speech, d);
                return;
            }
            var c = Character(d.Seat);
            if (c == null) return;
            _liveSpokeThisTurn.Add(d.Who);
            var a = Session.AiAt(d.Seat).Personality.Archetype;
            // 表情與看玩家（不帶字：字和聲音由 SpeechChannel 一起處理）
            c.Play(new PerformanceCue
            {
                Seat = d.Seat, Look = LookTarget.Player, Duration = 1.8f, Intensity = 0.7f,
                Mood = a == Archetype.Loudmouth ? Mood.Confident : a == Archetype.Rookie ? Mood.Nervous : Mood.Neutral,
                Tell = a == Archetype.Loudmouth ? Tell.LeanIn : a == Archetype.Rookie ? Tell.GlanceAtChips : Tell.StareDown,
            });
            MarkLive(c.Speech, d, c.Speech.Say(d.Line.Text, SpeechPriority.Chatter, d.Line.Id));
        }

        /// <summary>
        /// 即時台詞多半馬上開口；萬一要排隊（別人剛開口、發言權的停頓、玩家在講話）：最多等 LiveTalkDirector.MaxQueueSeconds，
        /// 玩家出手時也丟掉（觸發的情境不在了）。log：[LiveTalk] dropped &lt;誰&gt; line=&lt;id&gt; (queued 1.5 s|player acted)。
        /// </summary>
        void MarkLive(SpeechChannel ch, LiveDecision d, Utterance u = null)
        {
            u ??= ch.LastSubmitted;
            if (u == null || u.LineId != d.Line.Id) return;
            u.Tag = LiveTag;
            u.MaxWait = LiveTalkDirector.MaxQueueSeconds;
            string who = ch.Who, id = d.Line.Id;
            u.OnDropped = why =>
            {
                _liveSpokeThisTurn.Remove(who);   // 沒講出來：不算這一回合講過
                Debug.Log($"[LiveTalk] dropped {who} line={id} (" +
                          (why == "stale" || why == "floor busy" ? FormattableString.Invariant($"queued {LiveTalkDirector.MaxQueueSeconds:0.0} s") : why) + ")");
            };
        }

        // ================= 讀人：log 與一手結束的揭曉 =================

        /// <summary>AI 的決策用到了讀人（OnAiDecided 呼叫，主執行緒）。</summary>
        void LogRead(AiDecision d, int seat)
        {
            if (d == null || Math.Abs(d.ReadShift) < 1e-6) return;
            var ai = Session.AiAt(seat);
            var b = ai?.Read.Belief;
            string from = b != null ? string.Join("+", b.Cues.ConvertAll(c => c.ToString())) : "legacy";
            Debug.Log(FormattableString.Invariant($"[Read] {ai?.Personality.Name} belief={(b != null ? b.Belief : (d.ReadShift > 0 ? "weak" : "strong"))} from {from} effect={d.ReadShift:+0.000;-0.000} equity {d.RawEquity:0.00}->{d.Equity:0.00} action={d.Command?.GetType().Name}"));
        }

        bool _liveRevealDone;

        /// <summary>一手結束（HandEnded 那批事件，或 EnterHandOver）：用了判斷的 AI 裡影響最大的那個，揭曉它讀到什麼；攤牌知道對錯時講一句收尾。</summary>
        void OnLiveHandOver()
        {
            if (_liveRevealDone) return;   // 一手結束的事件裡已經揭曉過（EnterHandOver 不再重複）
            _liveRevealDone = true;
            if (InTutorialHand || InFoolRoccoHand) return;
            AiPlayer best = null;
            foreach (var seat in Session.AiSeats)
            {
                var ai = Session.AiAt(seat);
                var b = ai.LastReadBelief;
                if (b == null || !b.Used) continue;
                if (best == null || Math.Abs(b.Effect) > Math.Abs(best.LastReadBelief.Effect)) best = ai;
            }
            if (best == null) return;
            var belief = best.LastReadBelief;
            var outcome = best.LastReadOutcome;
            string text = RevealText(best.Personality.Name, belief, outcome);
            ShowReadReveal(best.Personality.Name, text);
            if (outcome == ReadOutcome.Fooled) Playtest.Fooled(best.Personality.Name, "game", Session.State.HandNumber);
            if (outcome == ReadOutcome.None || !LiveTalkEnabled) return;
            var line = LiveTalk.Closing(best.Personality.Archetype, outcome == ReadOutcome.Fooled);
            var c = Character(best.Seat);
            if (line == null || c == null) return;
            Debug.Log($"[LiveTalk] closing {LiveLines.VoiceOf(best.Personality.Archetype)} line={line.Id} ({outcome})");
            c.Speech.Say(line.Text, SpeechPriority.Chatter, line.Id);
        }

        /// <summary>
        /// 揭曉卡的字（文字標籤，不是台詞）：一行看懂「誰讀你、讀成什麼、對不對」，例如「Rocco read you: weak — right」
        /// （2026-11-03 使用者：舊的「Rocco read: long think, re-peek → weak hand. Right.」太長難懂；讀到的線索在 [Read] log 裡還有）。
        /// </summary>
        public static string RevealText(string who, LiveBelief b, ReadOutcome outcome) =>
            $"{who} read you: {(b.Belief == "weak" ? "weak" : "strong")}{RevealTail(outcome)}";

        /// <summary>對錯的尾巴：讀對「 — right」、被騙「 — wrong!」、還不知道就沒有。</summary>
        public static string RevealTail(ReadOutcome outcome) => outcome == ReadOutcome.Right ? " — right" : outcome == ReadOutcome.Fooled ? " — wrong!" : "";

        /// <summary>教學第 2 手和 Fool Rocco 一樣，讓 Rocco 比一般牌桌更常開口（等的那幾秒他會嗆你）。</summary>
        public const float TutorialChattiness = 1.8f;

        /// <summary>底池上方的「Rocco read you: …」揭曉卡（一般牌局與教學共用）。</summary>
        void ShowReadReveal(string who, string text)
        {
            Debug.Log($"[Read] reveal {who}: {text}");
            Playtest.Event("read_reveal", "who=" + who + " text=\"" + text + "\"");
            _readReveal.Show(text, ReadRevealSpot);
            if (Head != null) _readReveal.Face(Head.position);
            _readRevealT = ReadRevealSeconds;
        }

        void HideReadReveal()
        {
            _readRevealT = 0f;
            _readReveal?.Hide();
        }
    }
}
