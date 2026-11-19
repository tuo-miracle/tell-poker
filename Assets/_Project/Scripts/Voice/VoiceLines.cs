using System.Collections.Generic;
using Poker.AI;

namespace Poker.Voice
{
    /// <summary>
    /// 語音對話這一側的固定台詞（每個角色各一份，全部預錄）：等回話時的「Hm?」、聽不懂的罐頭話、
    /// 離線備案的回答（有數字的用片段＋數字字拼：「The pot is」＋three hundred forty）。
    /// </summary>
    public static class VoiceLines
    {
        static readonly VoiceCharacter[] Cast = { VoiceCharacter.Pip, VoiceCharacter.Rocco, VoiceCharacter.Vex, VoiceCharacter.Dot, VoiceCharacter.Silas };
        static readonly VoiceCharacter[] Players = { VoiceCharacter.Pip, VoiceCharacter.Rocco, VoiceCharacter.Vex, VoiceCharacter.Silas };

        /// <summary>送出問題、等回話時角色先出聲（蓋過等待）。</summary>
        public static string Filler(VoiceCharacter c)
        {
            switch (c)
            {
                case VoiceCharacter.Pip: return "Hm?";
                case VoiceCharacter.Rocco: return "Yeah?";
                case VoiceCharacter.Vex: return "Hm.";
                case VoiceCharacter.Silas: return "Go on.";
                default: return "Yes?";
            }
        }

        public const string NothingToCall = "Nothing to call. You can check.";
        public const string NobodyUp = "Nobody's up right now.";
        public const string YourTurn = "It's your turn.";
        public const string MyTurn = "It's my turn.";
        public static string TurnOf(string name) => "It's " + name + "'s turn.";

        /// <summary>這個角色會說的全部句子與片段。</summary>
        public static IEnumerable<SpeechLine> Lines(VoiceCharacter c)
        {
            string v = VoiceCast.Id(c);
            SpeechLine L(string id, string text) => new SpeechLine(v + "." + id, v, text);
            yield return L("filler", Filler(c));
            for (int n = 0; n < 2; n++) yield return L("canned." + (n + 1), VoiceFallback.CannedLine(c, n));
            yield return L("fb.nothing_to_call", NothingToCall);
            yield return L("fb.nobody_up", NobodyUp);
            yield return L("fb.your_turn", YourTurn);
            if (c != VoiceCharacter.Dot) yield return L("fb.my_turn", MyTurn);
            foreach (var p in Players)
            {
                if (p == c) continue;
                string name = VoiceCast.DisplayName(p);
                yield return L("fb.turn_" + VoiceCast.Id(p), TurnOf(name));
                yield return L("seg." + VoiceCast.Id(p) + "_has", name + " has");
            }
            yield return L("seg.pot_is", "The pot is");
            yield return L("seg.you_need", "You need");
            yield return L("seg.to_call", "to call");
            yield return L("seg.blinds_are", "Blinds are");
            yield return L("seg.and", "and");
            yield return L("seg.you_have", "You have");
            if (c != VoiceCharacter.Dot) yield return L("seg.i_have", "I have");
        }

        public static IEnumerable<SpeechLine> All()
        {
            foreach (var c in Cast)
                foreach (var l in Lines(c)) yield return l;
        }
    }

    /// <summary>
    /// 全部預錄台詞的清單（LineBook ＋ 荷官 ＋ 語音對話 ＋ 每個聲音的數字字），匯出成 tools/voice-proxy/bake/lines.json 給合成腳本。
    /// 同一個聲音、同一句原文只錄一次（留第一個 id）。
    /// 寫實角色（2026-11-02）：預設陣容（RealisticPreview.DefaultLineup）每個座位的角色，用自己的聲音再錄一次那個座位的全部句子
    /// （id「marcus:pip.value.01」）；聲音和座位一樣的（Hank 坐 Rocco 座）不用重錄。
    /// </summary>
    public static class SpeechInventory
    {
        public static List<SpeechLine> All()
        {
            var list = new List<SpeechLine>();
            var seen = new HashSet<string>();
            void Add(SpeechLine l) { if (seen.Add(l.Voice + "\n" + l.Text)) list.Add(l); }
            foreach (var l in LineBook.SpeechLines()) Add(l);
            foreach (var l in Poker.Game.DealerLines.Lines()) Add(l);
            foreach (var l in FoolRocco.Lines()) Add(l);   // Fool Rocco 原型（2026-10-14）
            foreach (var l in LiveLines.All()) Add(l);     // 即時牌桌對話（2026-10-14 live talk）
            foreach (var l in VoiceLines.All()) Add(l);
            foreach (var v in SpeechText.Voices)
                foreach (var w in SpeechText.AllNumberWords) Add(new SpeechLine(v + ".num." + w, v, w));
            var seats = new List<SpeechLine>(list);
            foreach (var l in CastLines(Poker.Characters.RealisticPreview.DefaultLineup.Split(','), Poker.Characters.RealisticPreview.LineupSeats, seats)) Add(l);
            return list;
        }

        /// <summary>陣容（names 依 seats 的座位順序；null／「-」／不認得＝卡通）每個寫實角色用自己的聲音說座位的句子。</summary>
        public static IEnumerable<SpeechLine> CastLines(IList<string> names, IList<Poker.Characters.CharacterKind> seats, IList<SpeechLine> seatLines)
        {
            for (int i = 0; i < names.Count && i < seats.Count; i++)
            {
                string seatVoice = Poker.Characters.SpeechVoices.ForSeat(seats[i]);
                string voice = Poker.Characters.SpeechVoices.ForCharacter(names[i], seats[i]);
                if (voice == seatVoice) continue;
                foreach (var l in seatLines)
                    if (l.Voice == seatVoice) yield return new SpeechLine(voice + ":" + l.Id, voice, l.Text);
            }
        }

        /// <summary>穩定格式的 JSON（一行一句，方便看 diff）。</summary>
        public static string ToJson()
        {
            var sb = new System.Text.StringBuilder();
            sb.Append("{\n \"note\": \"Generated by SpeechInventoryTests (SPEECH_EXPORT=1). Do not edit; bake with: node bake/bake.ts\",\n \"lines\": [\n");
            var all = All();
            for (int i = 0; i < all.Count; i++)
            {
                var l = all[i];
                sb.Append("  {\"id\": ");
                JsonText.Str(sb, l.Id);
                sb.Append(", \"voice\": ");
                JsonText.Str(sb, l.Voice);
                sb.Append(", \"text\": ");
                JsonText.Str(sb, l.Text);
                sb.Append(i + 1 < all.Count ? "},\n" : "}\n");
            }
            sb.Append(" ]\n}\n");
            return sb.ToString();
        }
    }
}
