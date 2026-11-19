using System.Collections.Generic;
using Poker.AI;
using Poker.Characters;

namespace Poker.Game
{
    /// <summary>
    /// 荷官台詞：LineBook.ForDealer 的句子輪流用，另外是 DealerLines 的固定句（"Check."、"Pot goes to Pip!"、教學提示）。
    /// 每種台詞各自有冷卻；兩句之間至少隔 MinGap 秒，重要的句子（force）不受限。
    /// 要說的話交給荷官的 SpeechChannel：預錄聲音和頭上的字幕同時出現；force 的句子是 Info（排在閒聊前面、可打斷閒聊），
    /// 其他是 Chatter。荷官正在講語音對話的回話時，這些句子排隊等回話講完。
    /// </summary>
    public sealed class DealerVoice
    {
        public float MinGap = 1.2f;
        public float DefaultCooldown = 4f;
        /// <summary>最近一次決定要說的句子（排隊中的也算）。</summary>
        public string LastText { get; private set; }
        /// <summary>這一局說過的句子（最多 64 句，測試與除錯用）。</summary>
        public IReadOnlyList<string> History => _history;

        readonly Dictionary<string, double> _lastSaid = new Dictionary<string, double>();
        readonly Dictionary<LineTrigger, int> _rotation = new Dictionary<LineTrigger, int>();
        readonly List<string> _history = new List<string>();
        double _lastAny = double.NegativeInfinity;
        CharacterPerformer _dealer;

        public void Bind(CharacterPerformer dealer) => _dealer = dealer;

        public bool Said(string text) => _history.Contains(text);

        /// <summary>荷官還在講或還有句子排隊（all-in 跑牌等荷官講完才發下一張）。</summary>
        public bool Busy => _dealer != null && _dealer.Speech != null && (_dealer.Speech.IsSpeaking || _dealer.Speech.QueuedCount > 0);

        public bool Say(LineTrigger trigger, double now, float cooldown = -1f, bool force = false)
        {
            var lines = new List<Line>(LineBook.ForDealer(trigger));
            if (lines.Count == 0) return false;
            _rotation.TryGetValue(trigger, out int i);
            var line = lines[i % lines.Count];
            if (!Say(line.Text, now, trigger.ToString(), line.Id, cooldown, force)) return false;
            _rotation[trigger] = i + 1;
            return true;
        }

        public bool Say(string text, double now, string key, string lineId = null, float cooldown = -1f, bool force = false, Mood mood = Mood.Neutral)
        {
            if (cooldown < 0f) cooldown = DefaultCooldown;
            if (!force)
            {
                if (now - _lastAny < MinGap) return false;
                if (_lastSaid.TryGetValue(key, out var t) && now - t < cooldown) return false;
            }
            _lastSaid[key] = now;
            _lastAny = now;
            LastText = text;
            _history.Add(text);
            if (_history.Count > 64) _history.RemoveAt(0);
            if (_dealer == null) return true;
            // 表情與看玩家（不帶字：字和聲音由 SpeechChannel 一起處理）
            _dealer.Play(new PerformanceCue
            {
                Seat = -1, Mood = mood, Intensity = 0.5f, Look = LookTarget.Player,
                LineId = lineId ?? "dot.custom", Duration = 2.4f,
            });
            _dealer.Speech.Say(text, force ? SpeechPriority.Info : SpeechPriority.Chatter, lineId);
            return true;
        }
    }
}
