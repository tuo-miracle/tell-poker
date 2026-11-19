using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace Poker.AI
{
    /// <summary>
    /// 一句話的重要程度（同一個角色同時只講一句；SpeechChannel 依這個決定排隊或打斷）。
    /// Filler：等回話時的「Hm?」，忙就不講；Chatter：AI 台詞、荷官報牌，排隊但很快過期；
    /// Info：教學、動作不合法、誰贏，排隊、打斷 Chatter；Reply：語音對話的回話，打斷其他一切。
    /// </summary>
    public enum SpeechPriority { Filler, Chatter, Info, Reply }

    /// <summary>一句預錄台詞：id、哪個聲音（pip／rocco／vex／dot）、字幕原文。</summary>
    public sealed class SpeechLine
    {
        public SpeechLine(string id, string voice, string text) { Id = id; Voice = voice; Text = text; }
        public string Id { get; }
        public string Voice { get; }
        public string Text { get; }
        public override string ToString() => Voice + ":" + Id + " \"" + Text + "\"";
    }

    /// <summary>
    /// 台詞文字的工具（純邏輯）：有數字的句子拆成「預錄片段＋數字英文字」（播放時把預錄音檔接起來，字幕照原句），
    /// 以及比對預錄片段用的正規化鍵。數字用美式唸法、不加 and（340 → three hundred forty）。
    /// </summary>
    public static class SpeechText
    {
        /// <summary>四個角色的聲音 id（ElevenLabs 對照在 tools/voice-proxy/src/elevenlabs.ts）。</summary>
        public static readonly string[] Voices = { "pip", "rocco", "vex", "dot", "silas" };

        static readonly string[] Ones =
        {
            "zero", "one", "two", "three", "four", "five", "six", "seven", "eight", "nine", "ten",
            "eleven", "twelve", "thirteen", "fourteen", "fifteen", "sixteen", "seventeen", "eighteen", "nineteen",
        };
        static readonly string[] Tens = { "", "", "twenty", "thirty", "forty", "fifty", "sixty", "seventy", "eighty", "ninety" };

        /// <summary>要預錄的數字字（每個聲音一份）。</summary>
        public static readonly string[] AllNumberWords = Build();

        static string[] Build()
        {
            var l = new List<string>(Ones);
            for (int i = 2; i < Tens.Length; i++) l.Add(Tens[i]);
            l.Add("hundred"); l.Add("thousand"); l.Add("million"); l.Add("percent");
            return l.ToArray();
        }

        public static IReadOnlyList<string> NumberWords(int n)
        {
            var w = new List<string>();
            if (n < 0) n = 0;
            if (n == 0) { w.Add("zero"); return w; }
            if (n >= 1000000) { Under1000(n / 1000000 % 1000, w); w.Add("million"); n %= 1000000; }
            if (n >= 1000) { Under1000(n / 1000, w); w.Add("thousand"); n %= 1000; }
            if (n > 0) Under1000(n, w);
            return w;
        }

        static void Under1000(int n, List<string> w)
        {
            if (n >= 100) { w.Add(Ones[n / 100]); w.Add("hundred"); n %= 100; }
            if (n >= 20) { w.Add(Tens[n / 10]); n %= 10; if (n > 0) w.Add(Ones[n]); }
            else if (n > 0) w.Add(Ones[n]);
        }

        static readonly Regex Number = new Regex(@"(\d{1,3}(?:,\d{3})+|\d+)(%?)", RegexOptions.CultureInvariant);

        public static bool HasNumber(string text) => !string.IsNullOrEmpty(text) && Number.IsMatch(text);

        /// <summary>拆成要依序播放的片段：文字片段（原樣、去頭尾空白）與數字字；只有標點的片段丟掉。</summary>
        public static IReadOnlyList<string> Pieces(string text)
        {
            var list = new List<string>();
            if (string.IsNullOrEmpty(text)) return list;
            int at = 0;
            foreach (Match m in Number.Matches(text))
            {
                AddPhrase(list, text.Substring(at, m.Index - at));
                if (int.TryParse(m.Groups[1].Value.Replace(",", ""), out int n)) list.AddRange(NumberWords(n));
                if (m.Groups[2].Length > 0) list.Add("percent");
                at = m.Index + m.Length;
            }
            AddPhrase(list, text.Substring(at));
            return list;
        }

        static void AddPhrase(List<string> list, string s)
        {
            s = s.Trim();
            if (Key(s).Length > 0) list.Add(s);
        }

        /// <summary>比對用的鍵：小寫、只留字母數字與撇號、空白合併。</summary>
        public static string Key(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";
            var sb = new StringBuilder(text.Length);
            bool space = false;
            foreach (var raw in text)
            {
                char c = char.ToLowerInvariant(raw == '’' ? '\'' : raw);
                if (char.IsLetterOrDigit(c) || c == '\'')
                {
                    if (space && sb.Length > 0) sb.Append(' ');
                    space = false;
                    sb.Append(c);
                }
                else space = true;
            }
            return sb.ToString();
        }
    }
}
