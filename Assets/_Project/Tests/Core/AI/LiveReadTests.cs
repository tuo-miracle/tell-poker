using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Poker.AI;
using Poker.Core;

namespace Poker.Tests.AI
{
    /// <summary>
    /// 讀人改版（2026-10-14 live talk）：AI 把玩家這一回合真的做了的事（想很久、又看牌、伸手又縮回、玩籌碼、手放到牌上又沒棄、秒下）
    /// 讀成「玩家弱／強」的信念，對決策有感但有上限（±LiveMaxShift）。每個 AI 有自己這一局的習慣模型：
    /// 照某個訊號判斷、攤牌發現被騙，下次那個訊號就打折（像 Fool Rocco 被同一招騙過就不信）；讀對了慢慢恢復。
    /// </summary>
    public class LiveReadTests
    {
        static PlayerReadSettings On() => new PlayerReadSettings();

        static PlayerSignals Honest(double t, float think = 9f, int looks = 2, int pullBacks = 1) => new PlayerSignals
        {
            Live = true, ThinkSeconds = think, HesitationSeconds = think, Looks = looks, PullBacks = pullBacks, Aggressive = true, Time = t,
        };

        static PlayerSignals SnapBet(double t) => new PlayerSignals { Live = true, ThinkSeconds = 0.9f, HesitationSeconds = 0.9f, Aggressive = true, Delivery = ChipDelivery.Placed, Time = t };

        [Test]
        public void HesitationRePeekAndReachRetreat_ReadAsWeak_WithAFeelableButBoundedEffect()
        {
            var r = new PlayerRead(2, 1f, On());
            r.BeginHand();
            r.Observe(Honest(10));
            var b = r.Belief;
            Assert.IsNotNull(b);
            Assert.AreEqual("weak", b.Belief);
            CollectionAssert.IsSupersetOf(b.Cues, new[] { ReadCue.LongThink, ReadCue.ReLook, ReadCue.ReachRetreat });
            Assert.That(r.CurrentShift(10), Is.InRange(0.10, PlayerReadSettings.LiveMaxShift + 1e-9), "players can feel it");
            Assert.AreEqual(r.CurrentShift(10), b.Effect, 1e-9);
            StringAssert.Contains("long think", b.Describe());
            StringAssert.Contains("reach-then-retreat", b.Describe());
        }

        [Test]
        public void ASnapBet_ReadsAsStrong()
        {
            var r = new PlayerRead(2, 1f, On());
            r.BeginHand();
            r.Observe(SnapBet(3));
            Assert.AreEqual("strong", r.Belief.Belief);
            CollectionAssert.AreEqual(new[] { ReadCue.Snap }, r.Belief.Cues);
            Assert.Less(r.CurrentShift(3), -0.04);
        }

        [Test]
        public void FiddlingAndTowardCards_AreWeakSignsToo_AFoldIsNotRead()
        {
            var r = new PlayerRead(2, 1f, On());
            r.BeginHand();
            r.Observe(new PlayerSignals { Live = true, ThinkSeconds = 4, PutBacks = 1, HeldCount = 1, TowardCards = true, Time = 1 });
            CollectionAssert.AreEquivalent(new[] { ReadCue.Fiddle, ReadCue.TowardCards }, r.Belief.Cues);
            Assert.Greater(r.CurrentShift(1), 0.05);
            var f = new PlayerRead(2, 1f, On());
            f.BeginHand();
            f.Observe(new PlayerSignals { Live = true, ThinkSeconds = 9, Looks = 3, Folded = true, Time = 1 });
            Assert.IsNull(f.Belief, "the player folded: nothing to play against");
        }

        [Test]
        public void TheEffect_IsAlwaysBounded_EvenWithEverySignalAndLegacyNotices()
        {
            var rng = new Pcg32(3);
            for (int i = 0; i < 2000; i++)
            {
                var r = new PlayerRead(1, 1f, On());
                r.BeginHand();
                for (int k = 0; k < 4; k++)
                    r.Observe(new PlayerSignals
                    {
                        Live = rng.NextBounded(2) == 0, ThinkSeconds = rng.NextBounded(300) / 10f, HesitationSeconds = rng.NextBounded(300) / 10f,
                        Looks = (int)rng.NextBounded(6), PullBacks = (int)rng.NextBounded(4), PutBacks = (int)rng.NextBounded(3), HeldCount = (int)rng.NextBounded(3),
                        TowardCards = rng.NextBounded(2) == 0, Aggressive = rng.NextBounded(2) == 0, PeekSeconds = rng.NextBounded(100) / 10f,
                        Delivery = (ChipDelivery)rng.NextBounded(3), ThrowSpeed = rng.NextBounded(40) / 10f, Time = k,
                    });
                Assert.LessOrEqual(Math.Abs(r.CurrentShift(4)), PlayerReadSettings.LiveMaxShift + 1e-9);
            }
            Assert.That(PlayerReadSettings.LiveMaxShift, Is.InRange(0.1f, 0.2f), "feelable, not a different game");
        }

        [Test]
        public void Sensitivity_PipReadsLessThanRocco_AndTheSwitchTurnsItOff()
        {
            var rocco = new PlayerRead(2, Personality.Loudmouth.ReadSensitivity, On());
            var pip = new PlayerRead(1, Personality.Rookie.ReadSensitivity, On());
            foreach (var r in new[] { rocco, pip }) { r.BeginHand(); r.Observe(Honest(0)); }
            Assert.AreEqual(rocco.CurrentShift(0) * Personality.Rookie.ReadSensitivity, pip.CurrentShift(0), 1e-6);
            var off = new PlayerRead(2, 1f, new PlayerReadSettings { Enabled = false });
            off.BeginHand();
            off.Observe(Honest(0));
            Assert.AreEqual(0, off.CurrentShift(0));
        }

        [Test]
        public void TheBelief_LastsTheHand_AndIsClearedAtTheNextHand()
        {
            var r = new PlayerRead(2, 1f, On());
            r.BeginHand();
            r.Observe(Honest(0));
            Assert.Greater(r.CurrentShift(30), 0.1, "still this hand 30 s later");
            r.BeginHand();
            Assert.IsNull(r.Belief);
            Assert.AreEqual(0, r.CurrentShift(31), 1e-9);
        }

        [Test]
        public void AntiExploit_FooledBySlowPlay_DiscountsThoseSignals_ReadingRightRestoresTrust()
        {
            var r = new PlayerRead(2, 1f, On());
            r.BeginHand();
            r.Observe(Honest(0));
            double first = r.CurrentShift(0);
            r.MarkUsed();
            Assert.AreEqual(ReadOutcome.Fooled, r.Learn(playerWasStrong: true), "acted on 'weak', the player showed a monster");

            r.BeginHand();
            r.Observe(Honest(10));
            double second = r.CurrentShift(10);
            Assert.Less(second, first * 0.6, "the same act works much less the second time");
            r.MarkUsed();
            r.Learn(playerWasStrong: true);
            r.BeginHand();
            r.Observe(Honest(20));
            double third = r.CurrentShift(20);
            Assert.Less(third, second * 0.6);
            Assert.Less(r.Trust(ReadCue.LongThink), 0.3);
            // 被「演弱」騙過的人，演強多半也是演的：秒下也打折，但比較少
            Assert.Less(r.Trust(ReadCue.Snap), 1.0);
            Assert.Greater(r.Trust(ReadCue.Snap), r.Trust(ReadCue.LongThink) * 1.5);

            for (int i = 0; i < 10; i++)
            {
                r.BeginHand();
                r.Observe(Honest(30 + i));
                r.MarkUsed();
                Assert.AreEqual(ReadOutcome.Right, r.Learn(playerWasStrong: false));
            }
            Assert.Greater(r.Trust(ReadCue.LongThink), 0.9, "honest tells again: trust comes back");
        }

        [Test]
        public void NoLearning_WhenTheBeliefWasNotUsed_OrThereWasNoBelief()
        {
            var r = new PlayerRead(2, 1f, On());
            r.BeginHand();
            r.Observe(Honest(0));
            Assert.AreEqual(ReadOutcome.None, r.Learn(true), "never acted on it");
            Assert.AreEqual(1.0, r.Trust(ReadCue.LongThink), 1e-9);
            r.BeginHand();
            Assert.AreEqual(ReadOutcome.None, r.Learn(true));
        }

        [Test]
        public void ASnapReadThatWasWrong_DiscountsSnap()
        {
            var r = new PlayerRead(2, 1f, On());
            r.BeginHand();
            r.Observe(SnapBet(0));
            r.MarkUsed();
            Assert.AreEqual(ReadOutcome.Fooled, r.Learn(playerWasStrong: false), "a fast bet with air");
            Assert.Less(r.Trust(ReadCue.Snap), 0.6);
        }
    }
}

namespace Poker.Tests.AI
{
    /// <summary>AiPlayer 接上即時讀人：決策用到判斷、攤牌看到玩家的牌之後自己學（被騙就打折），演出的「講不講」狀態不碰牌給任何人。</summary>
    public class LiveReadAiTests
    {
        static PlayerSignals Honest(double t) => new PlayerSignals
        {
            Live = true, ThinkSeconds = 9f, HesitationSeconds = 9f, Looks = 2, PullBacks = 1, Aggressive = true, Time = t,
        };

        /// <summary>單挑：玩家（座位 0）對 Rocco（座位 1，按鈕）；固定牌。玩家每次行動前把訊號送給 Rocco，行動用 player()。</summary>
        static (AiPlayer ai, List<AiDecision> decisions, HandState end) PlayHand(string playerHole, string aiHole, string board, Func<HandState, PlayerCommand> player,
            Func<PlayerSignals> signals, AiPlayer ai = null, ulong seed = 5)
        {
            ai = ai ?? new AiPlayer(Personality.Loudmouth, 1, 77, AiKit.Fast(300)) { HumanSeat = 0 };
            var preset = CardPreset.Parse(new Dictionary<int, string> { { 0, playerHole }, { 1, aiHole } }, board);
            var state = HandState.NewTable(new[] { 1000, 1000 });
            var r = Engine.Apply(state, new StartHand(seed, 1, 5, 10, preset));
            state = r.State;
            ai.OnEvents(PlayerView.Filter(r.Events, 1));
            var decisions = new List<AiDecision>();
            double clock = 0;
            while (state.Phase == HandPhase.Betting && state.ToAct >= 0)
            {
                PlayerCommand cmd;
                if (state.ToAct == 0)
                {
                    cmd = player(state);
                    var s = signals?.Invoke();
                    if (s != null)
                    {
                        s.Time = clock;
                        s.Aggressive = cmd is Bet || cmd is Raise || cmd is AllIn;
                        s.Folded = cmd is Fold;
                        ai.OnPlayerSignals(s);
                    }
                }
                else
                {
                    ai.Observations.Now = clock;
                    var d = ai.Decide(state);
                    decisions.Add(d);
                    cmd = d.Command;
                }
                clock += 3;
                var ar = Engine.Apply(state, cmd);
                Assert.IsTrue(ar.IsSuccess, ar.Error?.ToString());
                state = ar.State;
                ai.OnEvents(PlayerView.Filter(ar.Events, 1));
            }
            return (ai, decisions, state);
        }

        static PlayerCommand CallDown(HandState s)
        {
            var la = Engine.GetLegalActions(s, 0);
            if (s.Street == Street.River && la.CanBet) return new Bet(0, la.MinTo);
            return la.CanCheck ? (PlayerCommand)new Check(0) : new Call(0);
        }

        [Test]
        public void TheDecision_UsesTheLiveBelief_ThenTheShowdownTeachesIt()
        {
            // 玩家有 AA（強），卻演得很猶豫：Rocco 讀成弱、照著打 → 攤牌看到 AA → 被騙，那些線索打折
            var (ai, decisions, _) = PlayHand("AsAh", "Kd9c", "Ac7d2h5s9s", CallDown, () => Honest(0));
            Assert.IsTrue(decisions.Any(d => d.ReadShift > 0.09), "decisions shifted by the read: " + string.Join(",", decisions.Select(d => d.ReadShift.ToString("0.000"))));
            Assert.AreEqual(ReadOutcome.Fooled, ai.LastReadOutcome);
            Assert.IsNotNull(ai.LastReadBelief);
            Assert.AreEqual("weak", ai.LastReadBelief.Belief);
            Assert.Less(ai.Read.Trust(ReadCue.LongThink), 0.5);
        }

        [Test]
        public void ReadingAWeakPlayerRight_KeepsTrust()
        {
            var (ai, _, _) = PlayHand("7c2d", "KdQc", "Kh8d3s4c9h", CallDown, () => Honest(0));
            Assert.AreEqual(ReadOutcome.Right, ai.LastReadOutcome);
            Assert.AreEqual(1.0, ai.Read.Trust(ReadCue.LongThink), 1e-9);
        }

        [Test]
        public void NoSignals_NoReadShift()
        {
            var (ai, decisions, _) = PlayHand("AsAh", "Kd9c", "Ac7d2h5s9s", CallDown, null);
            Assert.IsTrue(decisions.All(d => d.ReadShift == 0));
            Assert.AreEqual(ReadOutcome.None, ai.LastReadOutcome);
        }

        [Test]
        public void LiveStance_IsAnInnerStateOnly_StrongWithAMonster_WeakWithAir()
        {
            int Strong(string aiHole, string board)
            {
                int n = 0;
                for (ulong seed = 0; seed < 60; seed++)
                {
                    var ai = new AiPlayer(Personality.Rookie, 1, seed, AiKit.Fast(200)) { HumanSeat = 0 };
                    var preset = CardPreset.Parse(new Dictionary<int, string> { { 0, "7c2d" }, { 1, aiHole } }, board);
                    var r = Engine.Apply(HandState.NewTable(new[] { 1000, 1000 }), new StartHand(seed, 1, 5, 10, preset));
                    ai.OnEvents(PlayerView.Filter(r.Events, 1));
                    if (ai.LiveStance() == InnerState.Strong) n++;
                }
                return n;
            }
            int monster = Strong("AsAh", "Ac7d2h5s9s"), air = Strong("3c4d", "Ac7d2h5s9s");
            Assert.Greater(monster, 45, "Pip (honest) shows strength with aces");
            Assert.Less(air, 15);
            Assert.AreEqual(typeof(InnerState), typeof(AiPlayer).GetMethod("LiveStance").ReturnType, "no cards leave the AI");
        }
    }
}
