using System;
using System.Collections.Generic;
using Poker.AI;

namespace Poker.Characters
{
    /// <summary>
    /// 聲音鍵（預錄音檔的資料夾名、中繼選 ElevenLabs 聲音的鍵）：卡通角色用座位聲音（pip／rocco／vex／dot），
    /// 寫實角色坐進座位時台詞仍依座位個性，聲音換成角色自己的（2026-11-02，docs/specs/2026-10-13-voice-chat-design.md §6.1）。
    /// 沿用座位聲音的角色：Hank＝Callum＝"rocco"、Reginald＝Bill＝老手座位 Silas 的 "silas"；坐那個座位時現有音檔直接能用。
    /// 新增角色：這裡加名字 → tools/voice-proxy/src/elevenlabs.ts 的 CAST_VOICES 加同名聲音鍵。
    /// </summary>
    public static class SpeechVoices
    {
        /// <summary>寫實角色自己的聲音鍵（不含沿用座位聲音的 Hank、Reginald）。</summary>
        public static readonly string[] Cast = { "marcus", "jace", "erik" };

        static readonly Dictionary<string, string> ByName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "Marcus", "marcus" },
            { "Hank", "rocco" },
            { "Jace", "jace" },
            { "Reginald", "silas" },
            { "Erik", "erik" },
        };

        /// <summary>座位原本（卡通角色）的聲音。</summary>
        public static string ForSeat(CharacterKind k) =>
            k == CharacterKind.Rookie ? "pip" : k == CharacterKind.Loudmouth ? "rocco" : k == CharacterKind.Cold ? "vex" : k == CharacterKind.Veteran ? "silas" : "dot";

        /// <summary>這個寫實角色（GLB 名字）的聲音；不認得的名字（或卡通）維持座位聲音。</summary>
        public static string ForCharacter(string name, CharacterKind seat) =>
            name != null && ByName.TryGetValue(name.Trim(), out var v) ? v : ForSeat(seat);

        public static bool IsKnown(string voice) => Array.IndexOf(SpeechText.Voices, voice) >= 0 || Array.IndexOf(Cast, voice) >= 0;
    }
}
