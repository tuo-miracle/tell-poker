using System;
using System.Collections.Generic;
using System.Globalization;
using Poker.Core;

namespace Poker.AI
{
    /// <summary>玩家這一次的動作（給 Fool Rocco 劇本用；Raise 含下注與全下）。</summary>
    public enum FoolAction { Fold, Check, Call, Raise }

    /// <summary>Rocco 第 1 手說出來的訊號。</summary>
    public enum FoolCallout { None, Peeks, SlowReach, SlowMove, Hover, Quick }

    /// <summary>玩家做決定那一回合的訊號（PlayerHabits 量的；這裡只放數值，Poker.AI 不依賴手勢組件）。</summary>
    public readonly struct FoolSignals
    {
        /// <summary>伸手延遲（秒）。</summary>
        public readonly float Reach;
        /// <summary>第一次伸手是伸向籌碼／下注區。</summary>
        public readonly bool ReachChips;
        /// <summary>這一手看牌次數。</summary>
        public readonly int Peeks;
        /// <summary>籌碼上方逗留秒數。</summary>
        public readonly float Hover;
        public FoolSignals(float reach, bool reachChips, int peeks, float hover) { Reach = reach; ReachChips = reachChips; Peeks = peeks; Hover = hover; }
    }

    /// <summary>一手結束時劇本要做的事：Rocco／Dot 的台詞（可為 null）、揭曉卡（null＝不顯示）、評估用的 log。</summary>
    public sealed class FoolEnd
    {
        public string Outcome, Reveal, Log;
        public SpeechLine Rocco, Dot;
    }

    /// <summary>
    /// 「Fool Rocco」原型情境（2026-10-14）：驗證「AI 讀你真的手、說出來，而你可以演一個假破綻騙他」這個核心玩法。
    /// 四人桌照常（玩家 0、Pip 1、Rocco 2 當按鈕、Vex 3），Pip 和 Vex 每手翻牌前都棄牌，等於玩家單挑 Rocco；Dot 發牌。
    /// - 第 1 手（普通的牌）：Rocco 開注，玩家照自己的方式打；玩家一行動，Rocco 說出最明顯的真實訊號（看牌次數／伸手慢／玩籌碼／很快）。
    /// - 第 2 手（怪物牌）：Dot 教「Rocco 以為伸手慢＝牌弱，等幾秒再下注」。等了（伸手 ≥ SlowReachSeconds）→ Rocco 讀成虛張而跟注 → 攤牌輸 →
    ///   「You played me!」＋揭曉卡；沒等 → Rocco 棄牌、Dot 說下次等等看，這一手重玩一次。
    /// - 第 3 手（又是好牌）：同一招再來 → Rocco「Not this time.」棄牌（被同一招騙過一次，下一次就不信那個訊號）→ Dot「He learns. Mix it up.」。
    /// 決定之後的街一律由遊戲層替玩家過牌（Rocco 也只過牌），讓每手很短。純邏輯：牌局用 Core，畫面與聲音在 GameController.FoolRocco.cs。
    /// </summary>
    public sealed class FoolRocco
    {
        public const int Human = 0, PipSeat = 1, RoccoSeat = 2, VexSeat = 3, Seats = 4;
        /// <summary>Rocco 當按鈕：Vex 小盲、玩家大盲，Pip 先行動（棄）→ Rocco → Vex（棄）→ 玩家。</summary>
        public const int Button = RoccoSeat;
        /// <summary>
        /// 按鈕位：玩家（0 號）大盲、最後一位（Vex）小盲，所以按鈕是倒數第二位——4 人桌是 Rocco，
        /// 5 人桌（寫實預覽，荷官右側多一位老手，2026-11-02）是老手；兩種桌玩家與 Rocco 的行動順序一樣，老手和 Pip、Vex 一樣先棄牌。
        /// </summary>
        public static int ButtonFor(int seats) => seats - 2;
        /// <summary>伸手延遲 ≥ 這個值算「等了」（Rocco 讀成「慢＝弱」）。</summary>
        public const float SlowReachSeconds = 3f;
        /// <summary>看牌 ≥ 2 次、逗留 ≥ 1.5 秒才算明顯（看一次牌很正常）。</summary>
        public const int PeeksSalient = 2;
        public const float HoverSalient = 1.5f;

        public const string Hand1PlayerHole = "Td8c", Hand1RoccoHole = "AhKs", Hand1Board = "Kd9c4h2s5c";
        public const string Hand2PlayerHole = "AsAh", Hand2RoccoHole = "KcQc", Hand2Board = "9d7c2hKs4d";
        public const string Hand3PlayerHole = "KhKd", Hand3RoccoHole = "Jc9s", Hand3Board = "Ac8d3s6h2c";

        // ───────────── 台詞（每一句都預錄：Rocco＝Callum、Dot＝Eric） ─────────────

        static SpeechLine R(string id, string text) => new SpeechLine("rocco.fool." + id, "rocco", text);
        static SpeechLine D(string id, string text) => new SpeechLine("dot.fool." + id, "dot", text);

        static readonly SpeechLine[] PeekLines =
        {
            R("peeks.2", "You looked at your cards twice. Not loving that hand, huh?"),
            R("peeks.3", "You looked at your cards three times. Not loving that hand, huh?"),
            R("peeks.4", "You looked at your cards four times. Not loving that hand, huh?"),
            R("peeks.many", "You keep looking at your cards. Not loving that hand, huh?"),
        };
        public static readonly SpeechLine CalloutSlowReach = R("slowreach", "Took you a while to reach for those chips. Weak.");
        public static readonly SpeechLine CalloutSlowMove = R("slowmove", "Took you a while to make a move. Weak.");
        public static readonly SpeechLine CalloutHover = R("hover", "Fiddling with your chips? Nervous.");
        public static readonly SpeechLine CalloutQuick = R("quick", "Quick decision. Confident, huh?");
        /// <summary>第 2 手：等了才下注 → 讀成虛張、跟注（規格原句的「again」拿掉：第 1 手不一定慢，重玩時也不一定）。</summary>
        public static readonly SpeechLine RoccoCall = R("call", "Took your time… you're bluffing. I call.");
        public static readonly SpeechLine RoccoFold = R("fold", "Quick bet. You've got it. I'm out.");
        public static readonly SpeechLine RoccoPlayed = R("played", "You played me!");
        public static readonly SpeechLine RoccoDistrust = R("distrust", "Not this time.");

        public static readonly SpeechLine DotNextHand = D("nexthand", "Fool Rocco next hand!");
        public static readonly SpeechLine DotIntro = D("intro", "Rocco reads your hands. Just play this one.");
        public static readonly SpeechLine DotCoach = D("coach", "Rocco thinks a slow reach means a weak hand. Try it — wait a few seconds before you bet.");
        public static readonly SpeechLine DotFooled = D("fooled", "You fooled him.");
        public static readonly SpeechLine DotRetry = D("retry", "Try waiting next time.");
        public static readonly SpeechLine DotLearns = D("learns", "He learns. Mix it up.");

        /// <summary>揭曉卡（文字標籤，不是台詞）。</summary>
        // 揭曉卡（同 GameController.RevealText 的格式：誰讀你、讀成什麼、對不對；2026-11-03 拿掉推理鏈）
        public const string RevealFooled = "Rocco read you: weak — wrong!";
        public const string RevealQuick = "Rocco read you: strong — right";
        public const string RevealDistrust = "Rocco read you: a trick — right";

        /// <summary>這個情境會說的每一句（匯出給 bake）。</summary>
        public static IEnumerable<SpeechLine> Lines()
        {
            foreach (var l in PeekLines) yield return l;
            yield return CalloutSlowReach;
            yield return CalloutSlowMove;
            yield return CalloutHover;
            yield return CalloutQuick;
            yield return RoccoCall;
            yield return RoccoFold;
            yield return RoccoPlayed;
            yield return RoccoDistrust;
            yield return DotNextHand;
            yield return DotIntro;
            yield return DotCoach;
            yield return DotFooled;
            yield return DotRetry;
            yield return DotLearns;
        }

        /// <summary>
        /// 最明顯的真實訊號：各訊號除以自己的門檻（看牌 2 次、伸手 3 秒、逗留 1.5 秒），取最大且 ≥ 1 的；同分依看牌、伸手、逗留。
        /// 都不明顯 → Quick。
        /// </summary>
        public static FoolCallout PickCallout(FoolSignals s)
        {
            float peeks = s.Peeks / (float)PeeksSalient, reach = s.Reach / SlowReachSeconds, hover = s.Hover / HoverSalient;
            var best = FoolCallout.Quick;
            float score = 1f - 1e-4f;
            if (peeks > score) { best = FoolCallout.Peeks; score = peeks; }
            if (reach > score + 1e-4f) { best = s.ReachChips ? FoolCallout.SlowReach : FoolCallout.SlowMove; score = reach; }
            if (hover > score + 1e-4f) best = FoolCallout.Hover;
            return best;
        }

        public static SpeechLine CalloutLine(FoolCallout c, int peeks)
        {
            switch (c)
            {
                case FoolCallout.Peeks: return PeekLines[Math.Max(0, Math.Min(PeekLines.Length - 1, peeks - 2))];
                case FoolCallout.SlowReach: return CalloutSlowReach;
                case FoolCallout.SlowMove: return CalloutSlowMove;
                case FoolCallout.Hover: return CalloutHover;
                default: return CalloutQuick;
            }
        }

        static string IdOf(FoolCallout c) =>
            c == FoolCallout.Peeks ? "peeks" : c == FoolCallout.SlowReach ? "slowreach" : c == FoolCallout.SlowMove ? "slowmove" : c == FoolCallout.Hover ? "hover" : "quick";

        // ───────────── 狀態 ─────────────

        /// <summary>目前是第幾手（1–3；BeginHand 之前是 0）。</summary>
        public int Hand { get; private set; }
        /// <summary>這一手是第 2 手的重玩。</summary>
        public bool Replaying { get; private set; }
        /// <summary>情境結束（之後遊戲層把牌桌還原）。</summary>
        public bool IsDone { get; private set; }
        /// <summary>這一手的關鍵決定已經做了（第 1 手：第一個行動；第 2、3 手：下注／加注或棄牌）。之後遊戲層自動過牌。</summary>
        public bool Decided { get; private set; }
        /// <summary>做決定的那一回合伸手延遲 ≥ SlowReachSeconds。</summary>
        public bool Waited { get; private set; }
        /// <summary>面對玩家的下注 Rocco 跟不跟（第 2、3 手讀牌的結果；null＝還沒讀）。</summary>
        public bool? RoccoCalls { get; private set; }
        /// <summary>「伸手慢」這招已經騙過 Rocco 幾次（騙過一次，下一次就不信）。</summary>
        public int TimesFooled { get; private set; }
        public FoolCallout Callout { get; private set; }
        /// <summary>
        /// 2026-10-14 live talk：玩家還在做關鍵決定時，Rocco 用即時系統嗆（想很久、伸手、縮手、再看牌…），不只等你下注之後講；
        /// 決定之後（自動過牌那段）不嗆。
        /// </summary>
        public bool LiveTalk => Hand > 0 && !IsDone && !Decided;
        /// <summary>情境的重點就是 Rocco 在讀你：比一般牌桌更常開口（LiveTalkContext.Chattiness）。</summary>
        public float LiveChattiness => 1.8f;
        /// <summary>最近一回合的訊號（做了決定就是決定那一回合的）。</summary>
        public FoolSignals Signals { get; private set; }

        int _next = 1;
        bool _replayUsed;
        string _responseId;

        /// <summary>開下一手（第一次呼叫是第 1 手）。</summary>
        public void BeginHand()
        {
            if (IsDone) return;
            Replaying = Hand == 2 && _next == 2;
            Hand = _next;
            Decided = Waited = false;
            RoccoCalls = null;
            Callout = FoolCallout.None;
            Signals = default;
            _responseId = null;
        }

        /// <summary>牌發好之後 Dot 說的話：第 1 手介紹、第 2 手（含重玩）教怎麼騙；第 3 手不說（玩家自己再用一次）。</summary>
        public SpeechLine HandIntro() => Hand == 1 ? DotIntro : Hand == 2 ? DotCoach : null;

        /// <summary>玩家行動了（遊戲層在 Core 接受之後呼叫）。回傳 Rocco 馬上要說的話（null＝不說）。</summary>
        public SpeechLine OnPlayerAction(FoolAction a, FoolSignals s)
        {
            if (Decided || IsDone || Hand == 0) return null;
            Signals = s;
            if (Hand == 1)
            {
                Decided = true;
                Waited = s.Reach >= SlowReachSeconds;
                Callout = PickCallout(s);
                _responseId = IdOf(Callout);
                return CalloutLine(Callout, s.Peeks);
            }
            // 第 2、3 手：過牌（或跟注）不是決定，下一回合重新量
            if (a == FoolAction.Check || a == FoolAction.Call) return null;
            Decided = true;
            Waited = s.Reach >= SlowReachSeconds;
            if (a == FoolAction.Fold) return null;
            if (!Waited)
            {
                RoccoCalls = false;
                _responseId = "fold";
                return RoccoFold;
            }
            if (TimesFooled > 0)
            {
                RoccoCalls = false;
                _responseId = "distrust";
                return RoccoDistrust;
            }
            RoccoCalls = true;
            _responseId = "call";
            return RoccoCall;
        }

        /// <summary>這一手結束：決定下一手、要說的話、揭曉卡與 log。</summary>
        public FoolEnd OnHandEnded(bool playerWon, bool roccoFolded, bool playerFolded)
        {
            var end = new FoolEnd();
            if (Hand == 1)
            {
                end.Outcome = playerFolded ? "player-folded" : playerWon ? "player-won" : "rocco-won";
                _next = 2;
            }
            else
            {
                bool fooled = RoccoCalls == true && playerWon;
                if (!Decided) end.Outcome = "checked";
                else if (playerFolded) end.Outcome = "player-folded";
                else if (fooled) end.Outcome = "fooled";
                else if (roccoFolded) end.Outcome = "folded";
                else end.Outcome = playerWon ? "player-won" : "rocco-won";

                if (fooled)
                {
                    TimesFooled++;
                    end.Rocco = RoccoPlayed;
                    end.Dot = DotFooled;
                    end.Reveal = RevealFooled;
                }
                else if (roccoFolded && Decided) end.Reveal = _responseId == "distrust" ? RevealDistrust : RevealQuick;

                if (Hand == 3)
                {
                    end.Dot = DotLearns;
                    IsDone = true;
                }
                else if (fooled) _next = 3;
                else
                {
                    end.Dot = DotRetry;
                    if (_replayUsed) IsDone = true;
                    else { _replayUsed = true; _next = 2; }
                }
            }
            end.Log = LogLine(Hand, Signals, _responseId, Waited, end.Outcome);
            return end;
        }

        public static string LogLine(int hand, FoolSignals s, string responseId, bool waited, string outcome) =>
            string.Format(CultureInfo.InvariantCulture, "[FoolRocco] hand={0} signals reach={1:0.0} peeks={2} hover={3:0.0} callout={4} player={5} outcome={6}",
                hand, s.Reach, s.Peeks, s.Hover, responseId ?? "-", waited ? "waited" : "fast", outcome);

        // ───────────── 牌與腳本 AI ─────────────

        /// <summary>第 hand 手的固定牌（只指定玩家、Rocco 與公共牌；Pip、Vex 從洗好的牌補）。</summary>
        public static CardPreset Preset(int hand)
        {
            string p = hand == 1 ? Hand1PlayerHole : hand == 2 ? Hand2PlayerHole : Hand3PlayerHole;
            string r = hand == 1 ? Hand1RoccoHole : hand == 2 ? Hand2RoccoHole : Hand3RoccoHole;
            string b = hand == 1 ? Hand1Board : hand == 2 ? Hand2Board : Hand3Board;
            return CardPreset.Parse(new Dictionary<int, string> { { Human, p }, { RoccoSeat, r } }, b);
        }

        /// <summary>
        /// 腳本化的 AI：Pip、Vex 面對下注就棄（翻牌前一定棄）；Rocco 第 1 手翻牌前開到 3 個大盲、面對玩家加注就跟，
        /// 第 2、3 手翻牌前只跟大盲、面對玩家的下注依讀牌（RoccoCalls）跟或棄；其他時候只過牌（不會對玩家下注）。
        /// </summary>
        public PlayerCommand Ai(HandState s, int seat)
        {
            var la = Engine.GetLegalActions(s, seat);
            if (seat != RoccoSeat) return la.CanCheck ? new Check(seat) : (PlayerCommand)new Fold(seat);
            if (!la.CanCheck && s.LastAggressor == Human)
            {
                bool call = Hand == 1 || RoccoCalls == true;
                if (!call) return new Fold(seat);
                return la.CanCall ? new Call(seat) : la.CanAllIn ? new AllIn(seat) : (PlayerCommand)new Fold(seat);
            }
            int bb = s.BigBlind;
            if (Hand == 1 && s.Street == Street.Preflop && la.CanRaise && s.CurrentBet == bb)
                return new Raise(seat, Math.Min(la.MaxTo, Math.Max(la.MinTo, 3 * bb)));
            if (la.CanCheck) return new Check(seat);
            return la.CanCall ? new Call(seat) : (PlayerCommand)new Fold(seat);
        }
    }
}
