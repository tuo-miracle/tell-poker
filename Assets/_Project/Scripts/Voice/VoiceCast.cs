using System.Text.RegularExpressions;

namespace Poker.Voice
{
    public enum VoiceCharacter { None = -1, Pip, Rocco, Vex, Dot, Silas }

    /// <summary>角色名稱與「誰來回話」：中繼回傳的 speaker（逐字稿叫到的名字）優先，其次逐字稿裡的名字，再來是盯著的角色，都沒有就是荷官 Dot。</summary>
    public static class VoiceCast
    {
        static readonly Regex Name = new Regex(@"\b(pip|rocco|vex|silas|dot|dealer)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        public static string Id(VoiceCharacter c)
        {
            switch (c)
            {
                case VoiceCharacter.Pip: return "pip";
                case VoiceCharacter.Rocco: return "rocco";
                case VoiceCharacter.Vex: return "vex";
                case VoiceCharacter.Dot: return "dot";
                case VoiceCharacter.Silas: return "silas";
                default: return "auto";
            }
        }

        public static string DisplayName(VoiceCharacter c) => c == VoiceCharacter.None ? "" : char.ToUpperInvariant(Id(c)[0]) + Id(c).Substring(1);

        public static VoiceCharacter Parse(string s)
        {
            switch ((s ?? "").Trim().ToLowerInvariant())
            {
                case "pip": return VoiceCharacter.Pip;
                case "rocco": return VoiceCharacter.Rocco;
                case "vex": return VoiceCharacter.Vex;
                case "dot": case "dealer": return VoiceCharacter.Dot;
                case "silas": return VoiceCharacter.Silas;
                default: return VoiceCharacter.None;
            }
        }

        public static VoiceCharacter NameIn(string transcript)
        {
            if (string.IsNullOrEmpty(transcript)) return VoiceCharacter.None;
            var m = Name.Match(transcript);
            return m.Success ? Parse(m.Groups[1].Value) : VoiceCharacter.None;
        }

        /// <summary>
        /// 給中繼的「座位 → 聲音鍵」（寫實角色坐的座位用角色自己的聲音配音，例 {"pip":"marcus"}）；聲音和座位一樣的不列。
        /// </summary>
        public static string VoicesJson(System.Collections.Generic.IEnumerable<(VoiceCharacter seat, string voice)> seats)
        {
            var sb = new System.Text.StringBuilder("{");
            foreach (var (seat, voice) in seats)
            {
                if (seat == VoiceCharacter.None || string.IsNullOrEmpty(voice) || voice == Id(seat)) continue;
                if (sb.Length > 1) sb.Append(',');
                JsonText.Str(sb, Id(seat));
                sb.Append(':');
                JsonText.Str(sb, voice);
            }
            return sb.Append('}').ToString();
        }

        public static VoiceCharacter Resolve(string proxySpeaker, string transcript, VoiceCharacter gazed)
        {
            var p = Parse(proxySpeaker);
            if (p != VoiceCharacter.None) return p;
            var n = NameIn(transcript);
            if (n != VoiceCharacter.None) return n;
            return gazed != VoiceCharacter.None ? gazed : VoiceCharacter.Dot;
        }
    }
}
