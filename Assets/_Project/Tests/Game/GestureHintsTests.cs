using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine.TestTools.Constraints;
using Is = NUnit.Framework.Is;
using Poker.Game;

namespace Poker.Tests.Game
{
    /// <summary>牌局中的手勢提示：輪到玩家 5 秒沒動作 → 合法動作的示範手；動手就淡出；成功 3 次後不再提示；選單、語音收音時不顯示。</summary>
    public class GestureHintsTests
    {
        static HintContext Turn(bool canCheck = true) => new HintContext
        {
            PlayerTurn = true, CanCheck = canCheck, CanCall = !canCheck, CanBet = true, CanFold = true, CanAllIn = true,
        };

        static void Run(GestureHints h, HintContext c, float seconds)
        {
            for (float t = 0; t < seconds; t += 0.1f) h.Tick(0.1f, c);
        }

        [Test]
        public void ShowsAfterFiveIdleSeconds_OnThePlayersTurn()
        {
            var h = new GestureHints();
            Run(h, Turn(), GestureHints.IdleSeconds - 0.3f);
            Assert.IsFalse(h.Visible, "not yet");
            Run(h, Turn(), 0.5f);
            Assert.IsTrue(h.Visible);

            var off = new GestureHints();
            Run(off, new HintContext { PlayerTurn = false, CanCheck = true, CanBet = true, CanFold = true, CanAllIn = true }, 10f);
            Assert.IsFalse(off.Visible, "only on the player's turn");
        }

        [Test]
        public void LegalActions_MapToGestures()
        {
            var list = new List<GhostGesture>();
            GestureHints.LegalGhosts(Turn(canCheck: true), list);
            CollectionAssert.AreEqual(new[] { GhostGesture.Knock, GhostGesture.Bet, GhostGesture.AllIn }, list, "free to check: no fold hint");
            GestureHints.LegalGhosts(Turn(canCheck: false), list);
            CollectionAssert.AreEqual(new[] { GhostGesture.Bet, GhostGesture.Fold, GhostGesture.AllIn }, list, "facing a bet: call, fold or shove");
            GestureHints.LegalGhosts(new HintContext { PlayerTurn = true, CanFold = true, CanAllIn = true }, list);
            CollectionAssert.AreEqual(new[] { GhostGesture.Fold, GhostGesture.AllIn }, list, "short stack: can only shove or fold");
        }

        [Test]
        public void MovingIntoAGesture_HidesTheHint_AndRestartsTheWait()
        {
            var h = new GestureHints();
            Run(h, Turn(), 6f);
            Assert.IsTrue(h.Visible);
            h.NoteActivity();
            h.Tick(0.1f, Turn());
            Assert.IsFalse(h.Visible);
            Run(h, Turn(), 4f);
            Assert.IsFalse(h.Visible);
            Run(h, Turn(), 1.5f);
            Assert.IsTrue(h.Visible, "idle again for 5 s");
        }

        [Test]
        public void NewTurn_RestartsTheWait()
        {
            var h = new GestureHints();
            Run(h, Turn(), 4f);
            h.Tick(0.1f, new HintContext());
            Run(h, Turn(), 3f);
            Assert.IsFalse(h.Visible, "the previous turn's idle time doesn't carry over");
        }

        [Test]
        public void GesturesDoneThreeTimes_AreNoLongerHinted()
        {
            var done = new Dictionary<GhostGesture, int> { { GhostGesture.Knock, 3 }, { GhostGesture.Bet, 2 } };
            var h = new GestureHints { Successes = g => done.TryGetValue(g, out var n) ? n : 0 };
            Run(h, Turn(canCheck: true), 6f);
            CollectionAssert.AreEqual(new[] { GhostGesture.Bet, GhostGesture.AllIn }, h.Gestures);
            done[GhostGesture.Bet] = GestureHints.SuppressAfter;
            done[GhostGesture.AllIn] = 5;
            h.Tick(0.1f, Turn(canCheck: true));
            Assert.IsFalse(h.Visible, "everything learned: nothing to show");
            Assert.IsEmpty(h.Gestures);
        }

        [Test]
        public void HiddenWhileMenuOpen_OrVoiceCapturing_OrTutorial_OrSwitchedOff()
        {
            foreach (var block in new[] { "menu", "voice", "tutorial", "off" })
            {
                var h = new GestureHints();
                var c = Turn();
                if (block == "menu") c.MenuOpen = true;
                if (block == "voice") c.VoiceBusy = true;
                if (block == "tutorial") c.TutorialActive = true;
                if (block == "off") h.Enabled = false;
                Run(h, c, 10f);
                Assert.IsFalse(h.Visible, block);
            }

            // 選單開著的時間不算閒置，而且關掉後重新等滿 5 秒
            var m = new GestureHints();
            Run(m, Turn(), 3f);
            var menu = Turn();
            menu.MenuOpen = true;
            Run(m, menu, 10f);
            Run(m, Turn(), 4f);
            Assert.IsFalse(m.Visible);
            Run(m, Turn(), 1.5f);
            Assert.IsTrue(m.Visible);
            menu.MenuOpen = true;
            m.Tick(0.1f, menu);
            Assert.IsFalse(m.Visible, "opening the menu hides it at once");
        }

        [Test]
        public void Tick_DoesNotAllocate()
        {
            var h = new GestureHints();
            Run(h, Turn(), 6f);
            var a = Turn(true);
            var b = Turn(false);
            Assert.That(() => { for (int i = 0; i < 500; i++) h.Tick(0.016f, i % 2 == 0 ? a : b); },
                Is.Not.AllocatingGCMemory());
        }
    }
}
