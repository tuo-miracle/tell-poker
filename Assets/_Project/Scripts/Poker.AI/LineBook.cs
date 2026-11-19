using System.Collections.Generic;
using System.Linq;
using Poker.Core;

namespace Poker.AI
{
    /// <summary>台詞觸發情境。Dealer 開頭的給荷官 Dot（由遊戲層直接取用）。</summary>
    public enum LineTrigger
    {
        ValueBet, Nervous, Unsure, Fold,
        PlayerTanks, PlayerSlam, PlayerBigBet, StaredAt,
        StreetGood, StreetBad, Win, Lose,
        DealerWelcome, DealerYourTurn, DealerFlop, DealerTurn, DealerRiver, DealerShowdown, DealerWinner, DealerAllIn,
    }

    /// <summary>一句台詞（英文字幕＋id，id 對應咕噥聲）。</summary>
    public sealed class Line
    {
        internal Line(string id, LineTrigger trigger, string text) { Id = id; Trigger = trigger; Text = text; }
        /// <summary>台詞 id，格式 "角色.情境.編號"。</summary>
        public string Id { get; }
        /// <summary>觸發情境。</summary>
        public LineTrigger Trigger { get; }
        /// <summary>英文字幕。</summary>
        public string Text { get; }
    }

    /// <summary>全部台詞（原創）。Pip＝緊張新手、Rocco＝大嘴巴老手、Vex＝冷面賭徒、Silas＝話少的老手（乾冷幽默）、Dot＝荷官。</summary>
    public static class LineBook
    {
        static Line L(string id, LineTrigger t, string text) => new Line(id, t, text);

        /// <summary>所有台詞。</summary>
        public static readonly IReadOnlyList<Line> All = new[]
        {
            // Pip（20）
            L("pip.value.01", LineTrigger.ValueBet, "Oh! Um, I'll... put some in."),
            L("pip.value.02", LineTrigger.ValueBet, "Is this good? I think this is good."),
            L("pip.nervous.01", LineTrigger.Nervous, "Totally normal wager. Nothing to see here."),
            L("pip.nervous.02", LineTrigger.Nervous, "Why is it so warm in here?"),
            L("pip.unsure.01", LineTrigger.Unsure, "Hmm. Hmm hmm hmm."),
            L("pip.unsure.02", LineTrigger.Unsure, "Wait, what beats what again?"),
            L("pip.fold.01", LineTrigger.Fold, "Nope. Not today."),
            L("pip.fold.02", LineTrigger.Fold, "I'll just... sit this one out."),
            L("pip.tank.01", LineTrigger.PlayerTanks, "Take your time! No pressure. Ha."),
            L("pip.slam.01", LineTrigger.PlayerSlam, "Eep! You scared me!"),
            L("pip.slam.02", LineTrigger.PlayerSlam, "Whoa, okay, okay!"),
            L("pip.bigbet.01", LineTrigger.PlayerBigBet, "That's... a lot of chips."),
            L("pip.stare.01", LineTrigger.StaredAt, "Why are you looking at me?"),
            L("pip.stare.02", LineTrigger.StaredAt, "Stop it, I can feel you staring."),
            L("pip.good.01", LineTrigger.StreetGood, "Ooh! Ooh. I mean... okay."),
            L("pip.bad.01", LineTrigger.StreetBad, "Aw, come on, cards."),
            L("pip.win.01", LineTrigger.Win, "I won? I actually won!"),
            L("pip.win.02", LineTrigger.Win, "Beginner's luck, maybe?"),
            L("pip.lose.01", LineTrigger.Lose, "Okay. Learning experience."),
            L("pip.lose.02", LineTrigger.Lose, "I'll get the hang of this."),
            // Rocco（23）
            L("rocco.value.01", LineTrigger.ValueBet, "Pay up, friend. Pay up."),
            L("rocco.value.02", LineTrigger.ValueBet, "You know what time it is."),
            L("rocco.value.03", LineTrigger.ValueBet, "Chips in. Feeling generous?"),
            L("rocco.nervous.01", LineTrigger.Nervous, "Big bet. Bigger heart."),
            L("rocco.nervous.02", LineTrigger.Nervous, "Go ahead, call. I dare you."),
            L("rocco.unsure.01", LineTrigger.Unsure, "Eh, let's see where this goes."),
            L("rocco.fold.01", LineTrigger.Fold, "Fine. Your pot. Enjoy it."),
            L("rocco.fold.02", LineTrigger.Fold, "I'm saving my ammo."),
            L("rocco.tank.01", LineTrigger.PlayerTanks, "Thinking hard, huh? That's a tell."),
            L("rocco.tank.02", LineTrigger.PlayerTanks, "Clock's ticking, champ."),
            L("rocco.tank.03", LineTrigger.PlayerTanks, "Long think. Somebody's scared."),
            L("rocco.slam.01", LineTrigger.PlayerSlam, "Hey! Easy on the felt!"),
            L("rocco.slam.02", LineTrigger.PlayerSlam, "Whoa, you break it, you buy it."),
            L("rocco.bigbet.01", LineTrigger.PlayerBigBet, "Oh, so we're doing this now?"),
            L("rocco.bigbet.02", LineTrigger.PlayerBigBet, "Cute bet. Real cute."),
            L("rocco.stare.01", LineTrigger.StaredAt, "Like what you see?"),
            L("rocco.stare.02", LineTrigger.StaredAt, "Staring won't save you."),
            L("rocco.good.01", LineTrigger.StreetGood, "Now that's my kind of card."),
            L("rocco.bad.01", LineTrigger.StreetBad, "Who ordered that card?"),
            L("rocco.win.01", LineTrigger.Win, "Rocco eats again!"),
            L("rocco.win.02", LineTrigger.Win, "Told you. I always tell you."),
            L("rocco.lose.01", LineTrigger.Lose, "Lucky. Pure dumb luck."),
            L("rocco.lose.02", LineTrigger.Lose, "Enjoy it. It won't last."),
            // Vex（18）
            L("vex.value.01", LineTrigger.ValueBet, "Bet."),
            L("vex.value.02", LineTrigger.ValueBet, "Your move."),
            L("vex.nervous.01", LineTrigger.Nervous, "Let's make it interesting."),
            L("vex.nervous.02", LineTrigger.Nervous, "I've decided."),
            L("vex.unsure.01", LineTrigger.Unsure, "Interesting."),
            L("vex.fold.01", LineTrigger.Fold, "Not worth it."),
            L("vex.fold.02", LineTrigger.Fold, "Pass."),
            L("vex.tank.01", LineTrigger.PlayerTanks, "Take all the time you need."),
            L("vex.tank.02", LineTrigger.PlayerTanks, "Doubt is expensive."),
            L("vex.slam.01", LineTrigger.PlayerSlam, "Careful."),
            L("vex.bigbet.01", LineTrigger.PlayerBigBet, "Noted."),
            L("vex.bigbet.02", LineTrigger.PlayerBigBet, "Is that supposed to scare me?"),
            L("vex.stare.01", LineTrigger.StaredAt, "Find anything?"),
            L("vex.stare.02", LineTrigger.StaredAt, "Keep looking."),
            L("vex.good.01", LineTrigger.StreetGood, "Good."),
            L("vex.bad.01", LineTrigger.StreetBad, "Hm."),
            L("vex.win.01", LineTrigger.Win, "As expected."),
            L("vex.lose.01", LineTrigger.Lose, "This time."),
            // Silas（20，2026-11-02 第 4 位對手；只有文字，音檔之後統一 bake）
            L("silas.value.01", LineTrigger.ValueBet, "Bet. Don't take it personally."),
            L("silas.value.02", LineTrigger.ValueBet, "I'll keep this simple."),
            L("silas.nervous.01", LineTrigger.Nervous, "I've seen worse hands win."),
            L("silas.nervous.02", LineTrigger.Nervous, "Go ahead. Call."),
            L("silas.unsure.01", LineTrigger.Unsure, "Hm. We'll see."),
            L("silas.fold.01", LineTrigger.Fold, "Not my hand. Not my night."),
            L("silas.fold.02", LineTrigger.Fold, "Folding. Write that down."),
            L("silas.tank.01", LineTrigger.PlayerTanks, "Take your time. I'm not getting younger."),
            L("silas.tank.02", LineTrigger.PlayerTanks, "I've waited longer for worse."),
            L("silas.slam.01", LineTrigger.PlayerSlam, "The table didn't do anything to you."),
            L("silas.bigbet.01", LineTrigger.PlayerBigBet, "Bold. I remember being bold."),
            L("silas.bigbet.02", LineTrigger.PlayerBigBet, "That's a lot of conviction."),
            L("silas.stare.01", LineTrigger.StaredAt, "I don't blink much, kid."),
            L("silas.stare.02", LineTrigger.StaredAt, "Forty years of poker face."),
            L("silas.good.01", LineTrigger.StreetGood, "That'll do."),
            L("silas.bad.01", LineTrigger.StreetBad, "Of course it is."),
            L("silas.win.01", LineTrigger.Win, "Experience pays. Slowly."),
            L("silas.win.02", LineTrigger.Win, "Nothing personal."),
            L("silas.lose.01", LineTrigger.Lose, "Happens. Rarely."),
            L("silas.lose.02", LineTrigger.Lose, "Enjoy it. I did, once."),
            // Dot 荷官（16）
            L("dot.welcome.01", LineTrigger.DealerWelcome, "Welcome to the table!"),
            L("dot.welcome.02", LineTrigger.DealerWelcome, "Fresh deck. Good luck, everyone."),
            L("dot.yourturn.01", LineTrigger.DealerYourTurn, "Action is on you."),
            L("dot.yourturn.02", LineTrigger.DealerYourTurn, "Your call, friend."),
            L("dot.yourturn.03", LineTrigger.DealerYourTurn, "Whenever you're ready."),
            L("dot.flop.01", LineTrigger.DealerFlop, "Here comes the flop."),
            L("dot.flop.02", LineTrigger.DealerFlop, "Three on the board."),
            L("dot.turn.01", LineTrigger.DealerTurn, "The turn."),
            L("dot.turn.02", LineTrigger.DealerTurn, "Fourth card coming up."),
            L("dot.river.01", LineTrigger.DealerRiver, "And the river."),
            L("dot.river.02", LineTrigger.DealerRiver, "Last card."),
            L("dot.showdown.01", LineTrigger.DealerShowdown, "Cards up, please."),
            L("dot.showdown.02", LineTrigger.DealerShowdown, "Let's see them."),
            L("dot.winner.01", LineTrigger.DealerWinner, "Pot goes to the winner!"),
            L("dot.winner.02", LineTrigger.DealerWinner, "Chips coming your way."),
            L("dot.allin.01", LineTrigger.DealerAllIn, "We have an all-in!"),
        };

        static string Prefix(Archetype a) => LiveLines.VoiceOf(a) + ".";

        /// <summary>某 AI 角色在某情境的台詞。</summary>
        public static IEnumerable<Line> For(Archetype a, LineTrigger t) { var p = Prefix(a); return All.Where(l => l.Trigger == t && l.Id.StartsWith(p)); }
        /// <summary>荷官在某情境的台詞。</summary>
        public static IEnumerable<Line> ForDealer(LineTrigger t) => All.Where(l => l.Trigger == t && l.Id.StartsWith("dot."));
        /// <summary>這句台詞用誰的聲音（id 開頭：pip／rocco／vex／silas／dot）。</summary>
        public static string VoiceOf(Line l) => l.Id.Substring(0, l.Id.IndexOf('.'));
        /// <summary>全部台詞轉成預錄清單的格式。</summary>
        public static IEnumerable<SpeechLine> SpeechLines() => All.Select(l => new SpeechLine(l.Id, VoiceOf(l), l.Text));
    }

    /// <summary>
    /// 決定「這次要不要講、講哪句」：角色冷卻時間＋情境冷卻＋機率；同一情境用洗牌袋輪流（每句都講過才重來，換輪也不連續同一句），
    /// 同一句至少隔 RepeatSeconds（2026-10-13 實機：Pip 一分鐘講了三次同一句長考台詞）。
    /// Propose 只是提議（不算講過），Commit 才記下；TrySpeak＝兩者一起（只有一個人講話的場合）。
    /// </summary>
    public sealed class LineSpeaker
    {
        /// <summary>同一個角色的同一句台詞至少相隔的秒數（只有一種講法的情境也不會一直重複）。</summary>
        public const float RepeatSeconds = 60f;

        readonly Archetype _a;
        readonly Pcg32 _rng;
        readonly Dictionary<LineTrigger, List<string>> _bag = new Dictionary<LineTrigger, List<string>>();
        readonly Dictionary<LineTrigger, string> _lastByTrigger = new Dictionary<LineTrigger, string>();
        readonly Dictionary<LineTrigger, double> _triggerTime = new Dictionary<LineTrigger, double>();
        readonly Dictionary<string, double> _lineTime = new Dictionary<string, double>();
        double _lastTime = double.NegativeInfinity;

        /// <summary>建立。</summary>
        public LineSpeaker(Personality p, Pcg32 rng)
        {
            _a = p.Archetype; _rng = rng;
            CooldownSeconds = _a == Archetype.Loudmouth ? 9f : _a == Archetype.Rookie ? 14f : _a == Archetype.Veteran ? 18f : 20f;
        }

        /// <summary>兩句台詞之間至少相隔的秒數。</summary>
        public float CooldownSeconds { get; set; }

        /// <summary>同一個角色同一情境的冷卻（秒）：長考的催促 20 s（不然玩家一想事情就被唸個不停）。</summary>
        public static float TriggerCooldown(LineTrigger t) => t == LineTrigger.PlayerTanks ? 20f : 0f;

        /// <summary>依機率 <paramref name="chance"/> 嘗試講一句並記下；冷卻中或沒抽中回傳 null。</summary>
        public Line TrySpeak(LineTrigger t, double now, float chance)
        {
            var line = Propose(t, now, chance);
            if (line != null) Commit(line, now);
            return line;
        }

        /// <summary>提議一句（冷卻、機率、洗牌袋都檢查），但不算講過：沒被挑中就當沒發生。</summary>
        public Line Propose(LineTrigger t, double now, float chance)
        {
            if (now - _lastTime < CooldownSeconds) return null;
            if (_triggerTime.TryGetValue(t, out var tt) && now - tt < TriggerCooldown(t)) return null;
            if (Equity.Next01(_rng) >= chance) return null;
            var all = LineBook.For(_a, t).ToList();
            if (all.Count == 0) return null;
            var bag = Bag(t, all);
            _lastByTrigger.TryGetValue(t, out var last);
            var options = all.Where(l => bag.Contains(l.Id) && !TooSoon(l, now) && (all.Count == 1 || l.Id != last)).ToList();
            if (options.Count == 0) return null;
            return options[(int)_rng.NextBounded((uint)options.Count)];
        }

        /// <summary>這句真的講出來了：從洗牌袋拿掉、記冷卻。</summary>
        public void Commit(Line line, double now)
        {
            if (line == null) return;
            var t = line.Trigger;
            var all = LineBook.For(_a, t).ToList();
            var bag = Bag(t, all);
            bag.Remove(line.Id);
            _lastByTrigger[t] = line.Id;
            _triggerTime[t] = now;
            _lineTime[line.Id] = now;
            _lastTime = now;
        }

        bool TooSoon(Line l, double now) => _lineTime.TryGetValue(l.Id, out var t) && now - t < RepeatSeconds;

        /// <summary>這個情境這一輪還沒講過的句子；講完一輪就重裝（剛講的那句不會是新一輪的第一句，見 Propose）。</summary>
        List<string> Bag(LineTrigger t, List<Line> all)
        {
            if (!_bag.TryGetValue(t, out var bag)) _bag[t] = bag = new List<string>();
            if (bag.Count == 0) foreach (var l in all) bag.Add(l.Id);
            return bag;
        }
    }
}
