using System;
using System.Collections.Generic;
using Poker.Core;

namespace Poker.AI
{
    /// <summary>玩家回合中的一個即時訊號（Time＝遊戲時鐘；Count：想了幾秒／第幾次看牌／幾枚籌碼；Seat：盯著的 AI）。</summary>
    public readonly struct LiveSignal
    {
        public readonly LiveCue Cue;
        public readonly double Time;
        public readonly int Count;
        public readonly int Seat;
        public LiveSignal(LiveCue cue, double time, int count = 0, int seat = -1) { Cue = cue; Time = time; Count = count; Seat = seat; }
        public override string ToString() => Cue + (Count != 0 ? "(" + Count + ")" : "") + (Seat >= 0 ? "@" + Seat : "");
    }

    /// <summary>可能開口的人：AI（Archetype 有值）或荷官（null）。Stance＝這一回合演出來的內心狀態（TellDirector 依可信度濾過，不是真的牌力）。</summary>
    public sealed class LiveSpeaker
    {
        public int Seat;
        public Archetype? Archetype;
        public InnerState Stance = InnerState.Unsure;
        public string Voice => Archetype.HasValue ? LiveLines.VoiceOf(Archetype.Value) : "dot";
    }

    /// <summary>做決定當下牌桌的狀況（遊戲層每次填）。</summary>
    public sealed class LiveTalkContext
    {
        public double Now;
        /// <summary>有人正在講、或有話在排隊（整桌的發言權）。</summary>
        public bool FloorBusy;
        /// <summary>上一句話講完多久了（秒）。</summary>
        public float SinceSpeech = 999f;
        /// <summary>玩家正在講話（語音對話收音中）。</summary>
        public bool PlayerSpeaking;
        /// <summary>教學那一手：教學的話優先，即時對話不講。</summary>
        public bool Tutorial;
        /// <summary>還在這一手裡（沒棄牌）的 AI，加上荷官（Archetype＝null）。</summary>
        public readonly List<LiveSpeaker> Speakers = new List<LiveSpeaker>();
        /// <summary>整體多話倍率（Fool Rocco 情境讓 Rocco 更常開口）。</summary>
        public float Chattiness = 1f;
        /// <summary>
        /// 一定講（教學 v4 第 1 手：保證玩家聽到 Rocco 讀他）：跳過「這次不講」的骰子；一次一人、間隔、冷卻、不蓋過玩家照舊。
        /// </summary>
        public bool Force;
    }

    /// <summary>一次決定：講（Line、Who、Seat）或不講（Skip＝原因，給 log）。</summary>
    public sealed class LiveDecision
    {
        public LiveSignal Signal;
        public SpeechLine Line;
        public string Who;
        public int Seat = -1;
        public string Skip;
        public bool Spoke => Line != null;
    }

    /// <summary>
    /// 即時牌桌對話的導演（live talk，2026-10-14；實機回饋「AI 只在我下注之後講一句，不像真人」）：
    /// 玩家還在想、伸手、縮手的時候，每個訊號最多挑一個人（AI 或荷官）講一句。規則：
    /// - 一次一個人：有人在講或有話在排隊（FloorBusy）、或別人剛講完不到 QuietAfterSpeech 秒，就不講。
    /// - 整桌兩句即時台詞至少隔 TableGap 秒；每個角色另有冷卻（Rocco 最短、Vex 最長）。
    /// - 每個角色每個觸發一個洗牌袋：每一句都講過才重來，換輪時也不連續同一句。
    /// - 玩家在講話（語音對話）時、教學那一手：不講（教學的話優先）。
    /// - 誰講：依個性的基本機率（Rocco 嗆最多、Pip 話多、Vex 很少）× 這一回合演出來的狀態（StanceFactor）。
    ///   演出來的狀態由 TellDirector.LiveStance 依可信度濾過（Pip 92%、Rocco 65%、Vex 35% 且多半反向），所以開不開口本身就是破綻；
    ///   這裡完全不碰牌。盯著某個 AI（Gaze）只有被盯的那個會回；荷官只在想很久（12 s）時給中性的提醒。
    /// 同一個種子、同樣的訊號序列 → 同樣的結果。
    /// </summary>
    public sealed class LiveTalkDirector
    {
        /// <summary>整桌兩句即時台詞之間至少相隔的秒數。</summary>
        public const float TableGap = 3f;
        /// <summary>即時台詞排隊最多等幾秒（遊戲層設給那一句的 Utterance.MaxWait）：過了就丟，不晚講（2026-10-14）。</summary>
        public const float MaxQueueSeconds = 1.5f;
        /// <summary>任何人講完之後至少安靜這麼久才接話。</summary>
        public const float QuietAfterSpeech = 0.8f;

        /// <summary>每個角色兩句即時台詞之間的冷卻（秒）。</summary>
        public static float Cooldown(string who) => who == "rocco" ? 6f : who == "pip" ? 9f : who == "vex" || who == "silas" ? 15f : 12f;

        /// <summary>演出來的狀態 → 開口的倍率：有牌（或演成有牌）話多，沒牌（或演成沒牌）安靜。</summary>
        public static float StanceFactor(InnerState s)
        {
            switch (s)
            {
                case InnerState.Strong: return 1.5f;
                case InnerState.Bluffing: return 1.2f;
                case InnerState.Unsure: return 0.8f;
                case InnerState.Weak: return 0.3f;
                default: return 0.55f;   // 撲克臉
            }
        }

        /// <summary>個性的基本機率（演出狀態倍率之前）。</summary>
        public static float Chance(string who, LiveCue c)
        {
            switch (who)
            {
                case "rocco":
                    switch (c)
                    {
                        case LiveCue.Think3: return 0.35f;
                        case LiveCue.Think7: return 0.7f;
                        case LiveCue.Think12: return 0.6f;
                        case LiveCue.ReLook: return 0.75f;
                        case LiveCue.Reach: return 0.8f;
                        case LiveCue.PullBack: return 0.9f;
                        case LiveCue.ChipsHeld: return 0.7f;
                        case LiveCue.PutBack: return 0.85f;
                        case LiveCue.TowardCards: return 0.8f;
                        case LiveCue.Gaze: return 0.7f;
                    }
                    break;
                case "pip":
                    switch (c)
                    {
                        case LiveCue.Think3: return 0.2f;
                        case LiveCue.Think7: return 0.35f;
                        case LiveCue.Think12: return 0.4f;
                        case LiveCue.ReLook: return 0.35f;
                        case LiveCue.Reach: return 0.45f;
                        case LiveCue.PullBack: return 0.4f;
                        case LiveCue.ChipsHeld: return 0.7f;
                        case LiveCue.PutBack: return 0.45f;
                        case LiveCue.TowardCards: return 0.4f;
                        case LiveCue.Gaze: return 0.55f;
                    }
                    break;
                case "vex":
                    switch (c)
                    {
                        case LiveCue.Think3: return 0.04f;
                        case LiveCue.Think7: return 0.08f;
                        case LiveCue.Think12: return 0.15f;
                        case LiveCue.ReLook: return 0.3f;
                        case LiveCue.Reach: return 0.06f;
                        case LiveCue.PullBack: return 0.2f;
                        case LiveCue.ChipsHeld: return 0.08f;
                        case LiveCue.PutBack: return 0.1f;
                        case LiveCue.TowardCards: return 0.1f;
                        case LiveCue.Gaze: return 0.35f;
                    }
                    break;
                case "dot":
                    return c == LiveCue.Think12 ? 0.7f : 0f;
            }
            return 0f;
        }

        /// <summary>單一角色一次開口的機率上限。</summary>
        public const float MaxChance = 0.9f;

        readonly Pcg32 _rng;
        readonly Dictionary<string, double> _lastBy = new Dictionary<string, double>();
        readonly Dictionary<(string, LiveCue), List<SpeechLine>> _bags = new Dictionary<(string, LiveCue), List<SpeechLine>>();
        readonly Dictionary<(string, LiveCue), string> _lastLine = new Dictionary<(string, LiveCue), string>();
        double _lastAny = double.NegativeInfinity;

        public LiveTalkDirector(ulong seed) { _rng = new Pcg32(seed, 31); }

        /// <summary>上一句即時台詞的時間（沒有是 -∞）。</summary>
        public double LastLineAt => _lastAny;

        /// <summary>一個訊號：最多挑一個人講一句（講了就記下冷卻與洗牌袋；遊戲層照著講）。</summary>
        public LiveDecision React(LiveSignal s, LiveTalkContext ctx)
        {
            var d = new LiveDecision { Signal = s };
            if (ctx == null) { d.Skip = "no context"; return d; }
            if (ctx.Tutorial) { d.Skip = "tutorial"; return d; }
            if (s.Cue == LiveCue.PlayerSpoke || ctx.PlayerSpeaking) { d.Skip = "player speaking"; return d; }
            if (s.Cue == LiveCue.TurnStart || s.Cue == LiveCue.ReadRight || s.Cue == LiveCue.ReadFooled) { d.Skip = "no line"; return d; }
            if (s.Cue == LiveCue.ReLook && s.Count < 2) { d.Skip = "first look"; return d; }
            if (ctx.FloorBusy) { d.Skip = "floor busy"; return d; }
            if (ctx.SinceSpeech < QuietAfterSpeech) { d.Skip = "just spoke"; return d; }
            if (ctx.Now - _lastAny < TableGap) { d.Skip = "table gap"; return d; }

            var who = new List<LiveSpeaker>();
            var p = new List<float>();
            string cooling = null;
            foreach (var sp in ctx.Speakers)
            {
                if (sp == null) continue;
                string v = sp.Voice;
                if (LiveLines.For(v, s.Cue).Count == 0) continue;
                if (s.Cue == LiveCue.Gaze && sp.Seat != s.Seat) continue;   // 只有被盯的那個回
                if (_lastBy.TryGetValue(v, out var t) && ctx.Now - t < Cooldown(v)) { cooling = cooling ?? "cooldown " + v; continue; }
                float c = Chance(v, s.Cue) * (sp.Archetype.HasValue ? StanceFactor(sp.Stance) * Math.Max(0f, ctx.Chattiness) : 1f);
                if (c <= 0f) continue;
                who.Add(sp);
                p.Add(Math.Min(MaxChance, c));
            }
            if (who.Count == 0) { d.Skip = cooling ?? "nobody"; return d; }
            double quiet = 1;
            float sum = 0f;
            foreach (var x in p) { quiet *= 1 - x; sum += x; }
            if (!ctx.Force && Equity.Next01(_rng) < quiet) { d.Skip = "silent"; return d; }
            double r = Equity.Next01(_rng) * sum;
            int k = who.Count - 1;
            for (int i = 0; i < who.Count; i++) { r -= p[i]; if (r < 0) { k = i; break; } }
            var pick = who[k];
            d.Line = Draw(pick.Voice, s.Cue);
            d.Who = pick.Voice;
            d.Seat = pick.Seat;
            _lastBy[pick.Voice] = ctx.Now;
            _lastAny = ctx.Now;
            return d;
        }

        /// <summary>一手結束時的收尾（讀對了／被騙了）：不受即時規則限制（遊戲層自己決定時機），只用洗牌袋。</summary>
        public SpeechLine Closing(Archetype a, bool fooled) => Draw(LiveLines.VoiceOf(a), fooled ? LiveCue.ReadFooled : LiveCue.ReadRight);

        SpeechLine Draw(string who, LiveCue c)
        {
            var all = LiveLines.For(who, c);
            if (all.Count == 0) return null;
            var key = (who, c);
            if (!_bags.TryGetValue(key, out var bag) || bag.Count == 0) _bags[key] = bag = new List<SpeechLine>(all);
            _lastLine.TryGetValue(key, out var last);
            var options = bag.FindAll(l => all.Count == 1 || l.Id != last);
            var line = options[(int)_rng.NextBounded((uint)options.Count)];
            bag.Remove(line);
            _lastLine[key] = line.Id;
            return line;
        }
    }
}
