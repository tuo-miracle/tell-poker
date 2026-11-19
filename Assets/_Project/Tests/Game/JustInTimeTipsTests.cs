using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Poker.AI;
using Poker.Core;
using Poker.Game;
using Poker.Gestures;
using UnityEngine;
using Hand = Poker.Gestures.Hand;

namespace Poker.Tests.Game
{
    /// <summary>
    /// 教學 v4 不教的手勢改在正式牌局「第一次用得到」時教（2026-10-14）：Dot 一句有聲音的提示＋示範手，每次開機每種只教一次——
    /// 第一次可以過牌 → 「Nobody bet. Knock twice to check.」＋敲桌示範；第一次攤牌 → 「Toss your cards forward to show.」＋翻牌示範（這一次等久一點）；
    /// 第一次拿到 Strong 的牌 → All-in 的提示；第一次盯著 AI 滿 1 秒 → 破綻會放大；語音對話開著時第一次盯著 Dot → 怎麼問她。
    /// 教學那一手、Fool Rocco、自動玩家、提示關掉、或那個手勢已經做會了（GestureHints.SuppressAfter 次）都不教。
    /// </summary>
    public class JustInTimeTipsTests
    {
        GameTestRig _rig;
        readonly List<string> _dot = new List<string>();

        [TearDown]
        public void TearDown()
        {
            _rig?.Dispose();
            _rig = null;
            _dot.Clear();
        }

        static PlayerCommand CheckOrCall(HandState st)
        {
            var la = Engine.GetLegalActions(st, st.ToAct);
            return la.CanCheck ? new Check(st.ToAct) : (PlayerCommand)new Call(st.ToAct);
        }

        GameTestRig NewRig(System.Action<GameController> extra = null)
        {
            var rig = new GameTestRig(new GameConfig { Seed = 19, MonteCarloTrials = 40 }, c =>
            {
                c.AiOverride = CheckOrCall;
                extra?.Invoke(c);
            });
            rig.Game.Dealer.Speech.Started += u => _dot.Add(u.Text);
            return rig;
        }

        int Count(JitTip t) => _dot.Count(x => x == JustInTimeTips.Line(t));

        /// <summary>輪到玩家就過牌或跟（用手勢事件）；直到這一手結束。攤牌等翻牌時翻。</summary>
        void PlayHandCheckingDown(GameController g, System.Action onTurn = null, System.Action onShowdown = null)
        {
            if (g.Session.HandsStarted == 0) _rig.IdleUntil(() => g.Session.HandsStarted > 0, 5);
            int hand = g.Session.HandsStarted;
            for (int guard = 0; guard < 20; guard++)
            {
                if (!_rig.IdleUntil(() => g.Flow == GameFlow.PlayerTurn || g.Flow == GameFlow.ShowdownWait || g.Flow == GameFlow.HandOver || g.Session.HandsStarted != hand, 40)) Assert.Fail(_rig.Dump());
                if (g.Flow == GameFlow.HandOver || g.Session.HandsStarted != hand) return;
                if (g.Flow == GameFlow.ShowdownWait) { onShowdown?.Invoke(); g.HandleGesture(new GestureEvent(GestureKind.ShowdownFlip, 0, Hand.Right)); _rig.Idle(0.5); continue; }
                onTurn?.Invoke();
                var la = g.Session.Legal(0);
                if (la.CanCheck) g.HandleGesture(new GestureEvent(GestureKind.KnockCheck, 0, Hand.Right));
                else g.HandleGesture(new GestureEvent(GestureKind.BetCommitted, 0, Hand.Right, System.Math.Max(1, (la.CallAmount + 9) / 10), 0f, new System.Numerics.Vector3(0f, 0f, 0.4f), true));
                _rig.Idle(0.3);
            }
        }

        [Test]
        public void Unit_EachTipOnce_LinesAndGhosts()
        {
            var t = new JustInTimeTips();
            Assert.IsTrue(t.TryUse(JitTip.Check));
            Assert.IsFalse(t.TryUse(JitTip.Check), "once per session");
            Assert.IsTrue(t.Used(JitTip.Check));
            Assert.IsFalse(t.Used(JitTip.Showdown));
            Assert.AreEqual("Nobody bet. Knock twice to check.", JustInTimeTips.Line(JitTip.Check));
            Assert.AreEqual("Toss your cards forward to show.", JustInTimeTips.Line(JitTip.Showdown));
            Assert.AreEqual(GhostGesture.Knock, JustInTimeTips.GhostOf(JitTip.Check));
            Assert.AreEqual(GhostGesture.Flip, JustInTimeTips.GhostOf(JitTip.Showdown));
            Assert.AreEqual(GhostGesture.AllIn, JustInTimeTips.GhostOf(JitTip.AllIn));
            Assert.AreEqual(GhostGesture.Talk, JustInTimeTips.GhostOf(JitTip.Voice));
            var ids = JustInTimeTips.Lines().Select(l => l.Id).ToList();
            CollectionAssert.AllItemsAreUnique(ids);
            CollectionAssert.Contains(ids, "dot.tutorial.check", "the v3 knock line keeps its id (already baked)");
            CollectionAssert.Contains(ids, "dot.tutorial.voice");
            foreach (JitTip k in System.Enum.GetValues(typeof(JitTip)))
                Assert.IsTrue(JustInTimeTips.Lines().Any(l => l.Text == JustInTimeTips.Line(k)), k.ToString());
            t.Enabled = false;
            Assert.IsFalse(t.TryUse(JitTip.Showdown), "hints off: no tips");
        }

        [Test]
        public void FirstCheck_GetsTheKnockTipAndGhost_Once_FirstShowdown_GetsTheShowTip_Once()
        {
            _rig = NewRig();
            var g = _rig.Game;
            g.Session.NextHandButton = 2;   // 玩家是大盲：大家都跛入 → 第一次輪到就可以過牌
            bool sawKnockGhost = false, waitedLonger = false, sawFlipGhost = false;
            PlayHandCheckingDown(g,
                onTurn: () => { _rig.Idle(0.2); if (g.GhostShown == GhostGesture.Knock) sawKnockGhost = true; },
                onShowdown: () =>
                {
                    _rig.Idle(0.5);
                    if (g.GhostShown == GhostGesture.Flip) sawFlipGhost = true;
                    _rig.Idle(g.ShowdownFlipTimeout + 1.0);
                    waitedLonger = g.Flow == GameFlow.ShowdownWait;
                });
            Assert.AreEqual(1, Count(JitTip.Check), "knock tip once\n" + string.Join(" | ", _dot));
            Assert.IsTrue(sawKnockGhost, "knock ghost right away (not after the 5 s idle hint)");
            Assert.IsTrue(g.Tips.Used(JitTip.Check));
            Assert.AreEqual(1, Count(JitTip.Showdown), "show tip at the first showdown\n" + string.Join(" | ", _dot));
            Assert.IsTrue(sawFlipGhost, "flip ghost");
            Assert.IsTrue(waitedLonger, "the first showdown waits longer than the usual auto-flip");

            // 第二手：不再教
            Assert.IsTrue(_rig.IdleUntil(() => g.Session.HandsStarted == 2 && g.Flow != GameFlow.Presenting, 20));
            PlayHandCheckingDown(g);
            Assert.AreEqual(1, Count(JitTip.Check));
            Assert.AreEqual(1, Count(JitTip.Showdown));
        }

        [Test]
        public void FirstStareAtAnAi_GetsTheGazeTip_Once()
        {
            _rig = NewRig();
            var g = _rig.Game;
            Assert.IsTrue(_rig.WaitPlayerTurn(), _rig.Dump());
            _rig.LookAt(g.Character(2).Rig.Head.position);
            Assert.IsTrue(_rig.IdleUntil(() => Count(JitTip.Gaze) == 1, 4), "gaze tip after ~1 s\n" + string.Join(" | ", _dot));
            _rig.LookFromSeat(new Vector3(0f, 0.18f, 0.55f));
            _rig.Idle(1.5);
            _rig.LookAt(g.Character(1).Rig.Head.position);
            _rig.Idle(3);
            Assert.AreEqual(1, Count(JitTip.Gaze), "once");
        }

        [Test]
        public void VoiceTip_OnlyWithVoiceChat_WhenLookingAtDot()
        {
            var voice = new FakeVoice();
            _rig = NewRig(c => c.TutorialVoice = voice);
            var g = _rig.Game;
            Assert.IsTrue(_rig.WaitPlayerTurn(), _rig.Dump());
            _rig.LookAt(g.Dealer.Rig.Head.position);
            _rig.Idle(2.5);
            Assert.AreEqual(0, Count(JitTip.Voice), "voice chat off: never mentioned");
            _rig.LookFromSeat(new Vector3(0f, 0.18f, 0.55f));
            _rig.Idle(1);
            voice.Available = true;
            _rig.LookAt(g.Character(2).Rig.Head.position);
            _rig.Idle(2.5);
            Assert.AreEqual(0, Count(JitTip.Gaze), "voice chat on: staring at an AI is for talking to them, Dot doesn't butt in");
            _rig.LookAt(g.Dealer.Rig.Head.position);
            Assert.IsTrue(_rig.IdleUntil(() => Count(JitTip.Voice) == 1, 4), string.Join(" | ", _dot));
        }

        [Test]
        public void StrongHand_GetsTheAllInTip_Once()
        {
            _rig = NewRig();
            var g = _rig.Game;
            g.Session.NextHandPreset = CardPreset.Parse(new Dictionary<int, string> { { 0, "AsAh" } }, "Ad7c2hKs9d");
            Assert.IsTrue(_rig.IdleUntil(() => g.Session.HandsStarted == 1 && g.PlayerCard(0).gameObject.activeSelf && g.Flow != GameFlow.Presenting, 10));
            g.HandleGesture(new GestureEvent(GestureKind.PeekChanged, 0, Hand.Right, 0, 0.8f));
            Assert.AreEqual(HandHint.Strong, g.HandHintShown);
            bool ghost = false;
            // 第一次可以過牌的回合先教敲桌（過牌優先）；之後還是 Strong 的回合教 All-in
            PlayHandCheckingDown(g, onTurn: () => { _rig.Idle(0.3); if (g.GhostShown == GhostGesture.AllIn) ghost = true; });
            Assert.AreEqual(1, Count(JitTip.AllIn), "all-in tip on a strong hand at the player's turn\n" + string.Join(" | ", _dot));
            Assert.IsTrue(ghost, "with the two-hand push ghost");
        }

        [Test]
        public void NoTips_InTheTutorial_WithTheAutoPlayer_OrWithHintsOff()
        {
            _rig = NewRig(c => c.HintsEnabled = false);
            var g = _rig.Game;
            g.Session.NextHandButton = 2;
            PlayHandCheckingDown(g);
            Assert.AreEqual(0, Count(JitTip.Check), "hints off");
            Assert.AreEqual(0, Count(JitTip.Showdown));
            _rig.Dispose();
            _dot.Clear();

            _rig = NewRig(c => c.AutoPlayer = CheckOrCall);
            _rig.Game.Session.NextHandButton = 2;
            Assert.IsTrue(_rig.IdleUntil(() => _rig.Game.Session.HandsStarted == 2, 60));
            Assert.AreEqual(0, _dot.Count(x => JustInTimeTips.Lines().Any(l => l.Text == x)), "auto player: no tips");
        }

        [Test]
        public void KnownGesture_IsNotTaught()
        {
            _rig = NewRig();
            var g = _rig.Game;
            for (int i = 0; i < GestureHints.SuppressAfter; i++) g.Progress.RecordSuccess(GhostGesture.Knock);
            g.Session.NextHandButton = 2;
            PlayHandCheckingDown(g);
            Assert.AreEqual(0, Count(JitTip.Check), "this player already knocks fine");
        }

        sealed class FakeVoice : ITutorialVoice
        {
            public bool Available { get; set; }
            public bool Busy { get; set; }
            public int Utterances { get; set; }
        }
    }
}
