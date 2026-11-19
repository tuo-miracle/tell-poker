using System.Text.RegularExpressions;

namespace Poker.Voice
{
    public enum LocalIntent { None, Pot, Stack, ToCall, Blinds, Turn }

    /// <summary>
    /// 斷網、逾時或中繼沒給回話時的備案：有逐字稿就用關鍵字判斷（pot／stack／chips／call／blind／whose turn）並用真實數字回答；
    /// 沒有逐字稿（本機沒有語音辨識）就說一句角色個性的罐頭台詞。都只顯示字幕。
    /// </summary>
    public static class VoiceFallback
    {
        const RegexOptions Opt = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;
        static readonly Regex ToCallRx = new Regex(@"\bcall\b|\bto call\b|\bowe\b", Opt);
        static readonly Regex PotRx = new Regex(@"\bpot\b", Opt);
        static readonly Regex BlindRx = new Regex(@"\bblinds?\b", Opt);
        static readonly Regex TurnRx = new Regex(@"\bwhose turn\b|\bwho'?s (up|turn|next)\b|\bmy turn\b|\bturn is it\b", Opt);
        static readonly Regex StackRx = new Regex(@"\bstacks?\b|\bchips?\b|\bhow much (do|does|have)\b", Opt);
        static readonly Regex SelfRx = new Regex(@"\b(i|me|my|mine)\b", Opt);
        static readonly Regex NameRx = new Regex(@"\b(pip|rocco|vex|silas)\b", Opt);

        static readonly string[][] Canned =
        {
            new[] { "Um, sorry, I didn't catch that.", "Oh gosh, could you say that again?" },   // Pip
            new[] { "Speak up, kid.", "What? Mumbling won't win you chips." },                     // Rocco
            new[] { "Hm.", "Say again." },                                                         // Vex
            new[] { "Sorry, I didn't catch that.", "Could you repeat that, please?" },             // Dot
            new[] { "Didn't catch that, kid.", "Once more. Slower." },                             // Silas（老手，2026-11-02）
        };

        public static LocalIntent Classify(string transcript)
        {
            if (string.IsNullOrWhiteSpace(transcript)) return LocalIntent.None;
            if (TurnRx.IsMatch(transcript)) return LocalIntent.Turn;
            if (BlindRx.IsMatch(transcript)) return LocalIntent.Blinds;
            if (PotRx.IsMatch(transcript)) return LocalIntent.Pot;
            if (ToCallRx.IsMatch(transcript)) return LocalIntent.ToCall;
            if (StackRx.IsMatch(transcript)) return LocalIntent.Stack;
            return LocalIntent.None;
        }

        public static string Answer(LocalIntent intent, PublicTableState s, VoiceCharacter speaker, string transcript)
        {
            switch (intent)
            {
                case LocalIntent.Pot: return $"The pot is {s.Pot}.";
                case LocalIntent.ToCall: return s.YouToCall > 0 ? $"You need {s.YouToCall} to call." : "Nothing to call. You can check.";
                case LocalIntent.Blinds: return $"Blinds are {s.SmallBlind} and {s.BigBlind}.";
                case LocalIntent.Turn:
                    if (string.IsNullOrEmpty(s.ToAct)) return "Nobody's up right now.";
                    if (s.ToAct == "you") return "It's your turn.";
                    if (s.ToAct == VoiceCast.Id(speaker)) return "It's my turn.";
                    return $"It's {VoiceCast.DisplayName(VoiceCast.Parse(s.ToAct))}'s turn.";
                case LocalIntent.Stack:
                    string who = StackOwner(transcript, speaker);
                    var seat = s.Seat(who);
                    if (seat == null) return null;
                    if (who == "you") return $"You have {seat.Stack}.";
                    if (who == VoiceCast.Id(speaker)) return $"I have {seat.Stack}.";
                    return $"{VoiceCast.DisplayName(VoiceCast.Parse(who))} has {seat.Stack}.";
                default: return null;
            }
        }

        /// <summary>問的是誰的籌碼：句中提到的 AI 名字（最後一個，開頭叫人的不算）→ 我／我的 → 回話的角色自己（荷官就當成問玩家）。</summary>
        static string StackOwner(string transcript, VoiceCharacter speaker)
        {
            var t = transcript ?? "";
            string named = null;
            foreach (Match m in NameRx.Matches(t))
            {
                bool addressing = m.Index == 0 && t.Length > m.Length && t[m.Length] == ',';
                if (!addressing) named = m.Value.ToLowerInvariant();
            }
            if (named != null) return named;
            if (SelfRx.IsMatch(t)) return "you";
            return speaker == VoiceCharacter.Dot || speaker == VoiceCharacter.None ? "you" : VoiceCast.Id(speaker);
        }

        public static string CannedLine(VoiceCharacter c, int n)
        {
            var lines = Canned[c == VoiceCharacter.None ? (int)VoiceCharacter.Dot : (int)c];
            return lines[(n % lines.Length + lines.Length) % lines.Length];
        }

        public static string Reply(string transcript, PublicTableState s, VoiceCharacter speaker, int n)
        {
            var intent = Classify(transcript);
            var a = intent == LocalIntent.None || s == null ? null : Answer(intent, s, speaker, transcript);
            return a ?? CannedLine(speaker, n);
        }
    }
}
