using System.Collections.Generic;
using Poker.AI;

namespace Poker.Game
{
    /// <summary>正式牌局第一次用得到時才教的事（教學 v4 不教）。</summary>
    public enum JitTip { Check, Showdown, AllIn, Gaze, Voice }

    /// <summary>
    /// 及時提示（2026-10-14，教學 v4 變短之後）：敲桌過牌、攤牌亮牌、All-in、盯人讀破綻、語音對話不在教學裡教，
    /// 而是正式牌局第一次用得到的時候，Dot 說一句（有聲音＋同一句字幕）、示範手做一次。每次開機每種只教一次（不存檔：
    /// 隔天再玩的評審、換人玩都會再聽到一次）。觸發的時機由 GameController.Tips.cs 決定；這裡只記「教過了沒」與台詞、示範手。
    /// </summary>
    public sealed class JustInTimeTips
    {
        /// <summary>設定的「Hints」關掉時不教。</summary>
        public bool Enabled = true;
        readonly HashSet<JitTip> _used = new HashSet<JitTip>();

        public bool Used(JitTip t) => _used.Contains(t);

        /// <summary>還沒教過（而且開著）就記下並回傳 true。</summary>
        public bool TryUse(JitTip t) => Enabled && _used.Add(t);

        // 沿用 v3 教學預錄過的句子（同一個 id，不用重新合成）：過牌、語音
        const string CheckLine = "Nobody bet. Knock twice to check.";
        const string VoiceLine = "Ask me anything about the table — try: how much is in the pot?";
        const string ShowdownLine = "Toss your cards forward to show.";
        const string AllInLine = "Big hand? Push all your chips with both hands to go all-in.";
        const string GazeLine = "Keep watching. Tells get bigger when you stare.";

        public static string Line(JitTip t)
        {
            switch (t)
            {
                case JitTip.Check: return CheckLine;
                case JitTip.Showdown: return ShowdownLine;
                case JitTip.AllIn: return AllInLine;
                case JitTip.Gaze: return GazeLine;
                default: return VoiceLine;
            }
        }

        public static GhostGesture GhostOf(JitTip t)
        {
            switch (t)
            {
                case JitTip.Check: return GhostGesture.Knock;
                case JitTip.Showdown: return GhostGesture.Flip;
                case JitTip.AllIn: return GhostGesture.AllIn;
                case JitTip.Gaze: return GhostGesture.Gaze;
                default: return GhostGesture.Talk;
            }
        }

        public static IEnumerable<SpeechLine> Lines()
        {
            yield return new SpeechLine("dot.tutorial.check", "dot", CheckLine);
            yield return new SpeechLine("dot.tutorial.voice", "dot", VoiceLine);
            yield return new SpeechLine("dot.jit.showdown", "dot", ShowdownLine);
            yield return new SpeechLine("dot.jit.allin", "dot", AllInLine);
            yield return new SpeechLine("dot.jit.gaze", "dot", GazeLine);
        }
    }
}
