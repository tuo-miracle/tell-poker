using System.Collections.Generic;
using Poker.AI;
using UnityEngine;

namespace Poker.Game
{
    /// <summary>
    /// 荷官 Dot 的固定台詞（LineBook 的 Dealer* 之外）：遊戲流程、教學、動作不合法、誰贏、開場白。
    /// 每一句都有預錄聲音（tools/voice-proxy/bake），所以要說的話都從這裡拿，不要在呼叫點寫新的字串
    /// （SpeechInventoryTests 會掃原始碼）。有數字的句子（開場白的戰績）用 Segments 的片段＋數字字拼起來播。
    /// </summary>
    public static class DealerLines
    {
        public const string Voice = "dot";

        public const string Check = "Check.";
        public const string FirstHello = "Hi, I'm Dot. Let's play one together.";
        /// <summary>完成過舊版教學的玩家：這次再教新版（2026-10-13 新增教學版本）。</summary>
        public const string NewTutorialIntro = "Welcome back! Let me show you the new gestures.";
        public const string ReadyForFullGame = "Welcome back! Ready for a full game?";
        public const string TutorialNextHand = "Tutorial next hand!";
        public const string SmallBetOnly = "Just a few chips for now.";
        public const string YouTookTheTable = "You took the table!";
        public const string Busted = "Out of chips. Knock for a rematch.";
        public const string GameOver = "That's the game! Knock to go again.";
        public const string NewGame = "New game! Chips reset.";
        public const string SplitPot = "Split pot.";
        public const string SideSplit = "Side pot is split.";
        /// <summary>還沒輪到就做了棄牌手勢：牌先進棄牌堆，輪到時自動棄牌（2026-10-13）。</summary>
        public const string FoldNoted = "Fold noted.";
        /// <summary>還沒輪到就把籌碼丟進下注區（籌碼退回）、敲桌或雙手推：GameController.WaitForTurnCooldown 秒內只講一次。</summary>
        public const string WaitForTurn = "Wait for your turn.";

        /// <summary>可能贏得底池的人（Session.NameOf：玩家是 "you"，AI 是個性的名字）。</summary>
        public static readonly string[] WinnerNames = { "you", "Pip", "Rocco", "Vex", "Silas" };

        /// <summary>牌力提示：這次開機第一次顯示時 Dot 小聲說一次（之後只有牌旁邊的字）。</summary>
        public static string HandHintWhisper(HandHint h)
        {
            switch (h)
            {
                case HandHint.Strong: return "Psst — that's a strong hand.";
                case HandHint.Decent: return "Psst — that's a decent hand.";
                case HandHint.Weak: return "Psst — that's a weak hand.";
                default: return "Psst — you're drawing. It could improve.";
            }
        }

        public static string PotTo(string who, bool side) => (side ? "Side pot to " : "Pot goes to ") + who + "!";

        /// <summary>
        /// 荷官宣布誰贏：名字有預錄（WinnerNames）就說「Pot goes to X!」，還沒錄的名字
        /// 說已經錄好的「Pot goes to the winner!」：每句話都要有聲音。錄好之後把名字加進 WinnerNames。
        /// </summary>
        public static string WinnerLine(string who, bool side) =>
            System.Array.IndexOf(WinnerNames, who) >= 0 ? PotTo(who, side) : GenericWinner;

        /// <summary>同 LineBook 的 dot.winner.01（已預錄）。</summary>
        public const string GenericWinner = "Pot goes to the winner!";

        public static string Invalid(InvalidReason r) =>
            r == InvalidReason.CannotCheck ? "Can't check here. Call or fold."
            : r == InvalidReason.NotYourTurn ? "Not your turn yet."
            : "That bet doesn't work.";

        /// <summary>
        /// 開場白：第一次玩 → 自我介紹；完成過舊版教學、這次要教新版 → 說明要教新手勢；之後 → 勝場（有數字，用片段拼）。
        /// tutorialComing：這次真的會教（GameController 傳；沒給就依存檔判斷）。
        /// </summary>
        public static string Greeting(ProgressData p, bool? tutorialComing = null)
        {
            if (p == null || p.CompletedTutorialVersion <= 0) return FirstHello;
            if (tutorialComing ?? Tutorial.IsNeeded(p)) return NewTutorialIntro;
            if (p.GamesPlayed <= 0) return ReadyForFullGame;
            string wins = p.GamesWon + (p.GamesWon == 1 ? " win" : " wins") + " in " + p.GamesPlayed;
            // 2026-11-04 不再講「tells read N%」（和結算看板一樣：定義是棄掉價值注，跟玩家想的「讀到對手」對不上）
            return "Welcome back! " + wins + " so far.";
        }

        /// <summary>有數字的句子拆開後的文字片段（各自預錄；數字字見 SpeechText.AllNumberWords）。</summary>
        public static readonly (string id, string text)[] Segments =
        {
            ("dot.seg.welcome_back", "Welcome back!"),
            ("dot.seg.wins_in", "wins in"),
            ("dot.seg.win_in", "win in"),
            ("dot.seg.tells_read", "tells read"),
            ("dot.seg.so_far", "so far"),
        };

        /// <summary>所有固定句子（不含數字的完整句，和 LineBook 的 Dealer* 分開）。</summary>
        public static IEnumerable<string> Fixed()
        {
            foreach (var l in Lines()) yield return l.Text;
        }

        /// <summary>荷官要預錄的全部句子（id、原文）：固定句、教學、動作不合法、誰贏、開場白的片段。</summary>
        public static IEnumerable<SpeechLine> Lines()
        {
            SpeechLine L(string id, string text) => new SpeechLine(id, Voice, text);
            yield return L("dot.check", Check);
            yield return L("dot.hello.first", FirstHello);
            yield return L("dot.hello.newtutorial", NewTutorialIntro);
            yield return L("dot.hello.fullgame", ReadyForFullGame);
            yield return L("dot.tutorial.nexthand", TutorialNextHand);
            yield return L("dot.tutorial.smallbet", SmallBetOnly);
            // 教學 v4（2026-10-14）：沒有「我幫你做」的句子（不自動跳過）；沒教的手勢在正式牌局第一次用到時教（JustInTimeTips）
            foreach (var (id, text) in Tutorial.Lines()) yield return L(id, text);
            foreach (var l in JustInTimeTips.Lines()) yield return l;
            foreach (HandHint h in System.Enum.GetValues(typeof(HandHint)))
                yield return L("dot.hint." + h.ToString().ToLowerInvariant(), HandHintWhisper(h));
            foreach (InvalidReason r in System.Enum.GetValues(typeof(InvalidReason)))
                yield return L("dot.invalid." + r.ToString().ToLowerInvariant(), Invalid(r));
            foreach (var w in WinnerNames)
            {
                yield return L("dot.pot." + w.ToLowerInvariant(), PotTo(w, false));
                yield return L("dot.sidepot." + w.ToLowerInvariant(), PotTo(w, true));
            }
            yield return L("dot.pot.split", SplitPot);
            yield return L("dot.sidepot.split", SideSplit);
            yield return L("dot.gameover.won", YouTookTheTable);
            yield return L("dot.gameover.busted", Busted);
            yield return L("dot.gameover.end", GameOver);
            yield return L("dot.newgame", NewGame);
            yield return L("dot.prefold", FoldNoted);
            yield return L("dot.waitturn", WaitForTurn);
            foreach (var (id, text) in Segments) yield return L(id, text);
        }
    }
}
