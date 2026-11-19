using System;
using System.Collections.Generic;
using Poker.Core;

namespace Poker.AI
{
    /// <summary>
    /// 整桌的台詞協調：「大家都有反應」的時候（玩家長考、一手結束的輸贏、翻出公共牌、被盯）只挑一個 AI 開口，其他人只演表情
    /// （2026-10-13 實機：玩家長考時三個 AI 和荷官同一瞬間開口）。
    /// 用法：GameSession 把它設給每個 AI 的 TellDirector.Table；同一批反應（一次 SignalPlayer／一批事件）各 AI 只「提議」台詞，
    /// 這批結束時呼叫 Resolve：依多話程度（Personality.Talkativeness）加權抽一個，上一次開口的人這次不開口（輪流；一對一不輪），
    /// 情境有整桌冷卻（長考 8 s）。被挑中的才真的講（LineSpeaker.Commit，洗牌袋與冷卻才算數），台詞寫回它的 PerformanceCue。
    /// AI 自己輪到時的決策台詞只有一個人，不經過這裡。
    /// </summary>
    public sealed class TableTalk
    {
        sealed class Offer
        {
            public LineSpeaker Speaker;
            public string Who;
            public float Weight;
            public PerformanceCue Cue;
            public Line Line;
            public double Now;
        }

        readonly Pcg32 _rng;
        readonly List<Offer> _offers = new List<Offer>();
        readonly Dictionary<LineTrigger, double> _tableTime = new Dictionary<LineTrigger, double>();
        readonly HashSet<string> _voices = new HashSet<string>();

        /// <summary>建立（同一個種子、同樣的提議序列 → 同樣的挑選）。</summary>
        public TableTalk(ulong seed) { _rng = new Pcg32(seed, 21); }

        /// <summary>同一情境整桌的冷卻（秒）：長考的催促 8 s 內只會有一個人講。</summary>
        public static float TableCooldown(LineTrigger t) => t == LineTrigger.PlayerTanks ? 8f : 0f;

        /// <summary>除錯訊息（遊戲層接到 log）。</summary>
        public Action<string> Trace;
        /// <summary>
        /// 遊戲層的否決（回傳原因＝這句不講，null＝可以）：例如這一回合已經用即時台詞講過話的角色，不再補一句長考台詞（2026-10-14）。
        /// 被否決的提議不算講過（不進冷卻、洗牌袋不動）。
        /// </summary>
        public Func<Line, string> Veto;
        /// <summary>上一個被挑中開口的人（聲音 id：pip／rocco／vex；沒有是 null）。</summary>
        public string LastSpeaker { get; private set; }
        /// <summary>還沒決定的提議數。</summary>
        public int Pending => _offers.Count;

        /// <summary>某個 AI 提議在這批反應裡講一句（台詞先不寫進 cue）。</summary>
        internal void Propose(LineSpeaker speaker, Personality p, PerformanceCue cue, Line line, double now)
        {
            if (speaker == null || cue == null || line == null) return;
            _voices.Add(LineBook.VoiceOf(line));
            _offers.Add(new Offer { Speaker = speaker, Who = LineBook.VoiceOf(line), Weight = Math.Max(0.01f, p.Talkativeness), Cue = cue, Line = line, Now = now });
        }

        /// <summary>這批反應結束：最多挑一個人開口，回傳他講的那句（沒人講就是 null）。</summary>
        public Line Resolve()
        {
            if (_offers.Count == 0) return null;
            var all = new List<Offer>(_offers);
            _offers.Clear();
            var ok = new List<Offer>();
            string why = null;
            foreach (var o in all)
            {
                if (o.Who == LastSpeaker && _voices.Count > 1) { why = "rotation"; continue; }   // 一對一時只有一個人，不輪
                if (_tableTime.TryGetValue(o.Line.Trigger, out var t) && o.Now - t < TableCooldown(o.Line.Trigger)) { why = "table cooldown"; continue; }
                var veto = Veto?.Invoke(o.Line);
                if (veto != null) { why = veto; continue; }
                ok.Add(o);
            }
            string names = string.Join(",", all.ConvertAll(o => o.Who));
            if (ok.Count == 0)
            {
                Trace?.Invoke($"chorus: {all[0].Line.Trigger} -> none ({why}) of {names}");
                return null;
            }
            float sum = 0f;
            foreach (var o in ok) sum += o.Weight;
            double r = Equity.Next01(_rng) * sum;
            var pick = ok[ok.Count - 1];
            foreach (var o in ok)
            {
                r -= o.Weight;
                if (r < 0) { pick = o; break; }
            }
            pick.Speaker.Commit(pick.Line, pick.Now);
            pick.Cue.LineId = pick.Line.Id;
            pick.Cue.Subtitle = pick.Line.Text;
            _tableTime[pick.Line.Trigger] = pick.Now;
            LastSpeaker = pick.Who;
            Trace?.Invoke($"chorus: {pick.Line.Trigger} -> {pick.Who} of {names}");
            return pick.Line;
        }
    }
}
