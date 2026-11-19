using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Poker.Core;

namespace Poker.Tests
{
    public class PlayerViewTests
    {
        [Test]
        public void Filter_KeepsOwnHoleCards_DropsOthers_KeepsPublicEvents()
        {
            var t = Table.New(1000, 1000, 1000).Start(0, holes: "0:AsAh 1:KsKh 2:QsQh", board: "2c 3d 7h 8s Jc");
            t.Call(0).Call(1).Check(2).CheckDown();
            var view = PlayerView.Filter(t.Events, 1).ToList();
            var holes = view.OfType<HoleCardsDealt>().ToList();
            Assert.AreEqual(1, holes.Count);
            Assert.AreEqual(1, holes[0].Seat);
            Assert.AreEqual(t.Events.Count - 2, view.Count);
            Assert.AreEqual(1, view.OfType<ShowdownRevealed>().Count(), "攤牌是公開資訊");
            Assert.IsTrue(view.OfType<HandStarted>().Any());
        }

        [Test]
        public void Spectator_SeesNoHoleCards()
        {
            var t = Table.New(1000, 1000).Start(0);
            Assert.IsFalse(PlayerView.Filter(t.Events, -1).OfType<HoleCardsDealt>().Any());
            Assert.IsTrue(PlayerView.Filter(t.Events, -1).OfType<BlindPosted>().Any());
        }

        [Test]
        public void IsVisibleTo_OnlyPrivateEventsAreRestricted()
        {
            var t = Table.New(1000, 1000).Start(0);
            foreach (var e in t.Events)
            {
                if (e is HoleCardsDealt h)
                {
                    Assert.IsTrue(PlayerView.IsVisibleTo(e, h.Seat));
                    Assert.IsFalse(PlayerView.IsVisibleTo(e, 1 - h.Seat));
                }
                else
                {
                    Assert.IsTrue(PlayerView.IsVisibleTo(e, 0));
                    Assert.IsTrue(PlayerView.IsVisibleTo(e, -1));
                }
            }
        }
    }

    /// <summary>隨機合法／非法指令下的不變量（研究筆記 §2.2-7）。</summary>
    public class EnginePropertyTests
    {
        [Test]
        public void RandomPlay_InvariantsHold([Values(1UL, 2UL, 3UL)] ulong seed)
        {
            var rng = new Pcg32(seed, 7);
            int hands = 0;
            for (int table = 0; table < 60; table++)
            {
                int n = 2 + (int)rng.NextBounded(8);
                var stacks = new int[n];
                for (int i = 0; i < n; i++)
                {
                    uint kind = rng.NextBounded(10);
                    stacks[i] = kind == 0 ? 0 : kind < 3 ? 1 + (int)rng.NextBounded(150) : 100 + (int)rng.NextBounded(5000);
                }
                var s = HandState.NewTable(stacks);
                int total = stacks.Sum();
                for (int h = 0; h < 15; h++)
                {
                    var start = Engine.Apply(s, new StartHand(rng.NextUInt(), null, 10, 20 + 5 * (int)rng.NextBounded(5)));
                    if (!start.IsSuccess) { Assert.AreEqual(EngineErrorCode.NotEnoughPlayers, start.Error.Code); break; }
                    hands++;
                    var events = new List<GameEvent>(start.Events);
                    s = start.State;
                    int guard = 0;
                    while (s.Phase == HandPhase.Betting)
                    {
                        Assert.Less(guard++, 500, "牌局沒有結束");
                        int seat = s.ToAct;
                        var la = Engine.GetLegalActions(s, seat);
                        Assert.IsTrue(la.IsTurn);
                        // 偶爾送非法指令：必須被拒絕且狀態不變
                        if (rng.NextBounded(8) == 0)
                        {
                            int other = (seat + 1 + (int)rng.NextBounded((uint)s.SeatCount - 1)) % s.SeatCount;
                            var bad = Engine.Apply(s, new Check(other));
                            Assert.IsFalse(bad.IsSuccess);
                            Assert.AreSame(s, bad.State);
                        }
                        var cmd = RandomLegal(rng, la, seat);
                        var r = Engine.Apply(s, cmd);
                        Assert.IsTrue(r.IsSuccess, r.Error?.ToString());
                        s = r.State;
                        events.AddRange(r.Events);
                        Assert.AreEqual(total, s.TotalChips(), "籌碼守恆");
                        for (int i = 0; i < s.SeatCount; i++) Assert.GreaterOrEqual(s.Seat(i).Stack, 0);
                    }
                    CheckHandEvents(events, s);
                }
                Assert.AreEqual(total, s.Stacks().Sum());
            }
            Assert.Greater(hands, 300);
        }

        private static void CheckHandEvents(List<GameEvent> events, HandState s)
        {
            var folded = new HashSet<int>(events.OfType<PlayerActed>().Where(a => a.Action == ActionType.Fold).Select(a => a.Seat));
            var lastPots = events.OfType<PotsUpdated>().LastOrDefault();
            var awards = events.OfType<PotAwarded>().ToList();
            Assert.IsNotNull(lastPots);
            Assert.AreEqual(lastPots.Pots.Count, awards.Count);
            for (int k = 0; k < awards.Count; k++)
            {
                var pot = lastPots.Pots[k];
                Assert.AreEqual(pot.Amount, awards[k].Amount);
                Assert.AreEqual(pot.Amount, awards[k].Shares.Sum(x => x.Amount));
                foreach (var sh in awards[k].Shares)
                {
                    Assert.IsFalse(folded.Contains(sh.Seat), "棄牌者不得贏池");
                    CollectionAssert.Contains(pot.EligibleSeats, sh.Seat, "贏家必須有資格");
                }
                int min = awards[k].Shares.Min(x => x.Amount), max = awards[k].Shares.Max(x => x.Amount);
                Assert.LessOrEqual(max - min, 1);
            }
            var ended = events.OfType<HandEnded>().Single();
            CollectionAssert.AreEqual(s.Stacks(), ended.FinalStacks);
            Assert.IsInstanceOf<HandEnded>(events.Last());
            Assert.AreEqual(HandPhase.Idle, s.Phase);
        }

        internal static PlayerCommand RandomLegal(Pcg32 rng, LegalActions la, int seat)
        {
            while (true)
            {
                switch (rng.NextBounded(7))
                {
                    case 0: if (la.CanFold && !la.CanCheck) return new Fold(seat); break;
                    case 1: if (la.CanCheck) return new Check(seat); break;
                    case 2: case 3: if (la.CanCall) return new Call(seat); break;
                    case 4: if (la.CanAllIn) return new AllIn(seat); break;
                    case 5:
                    case 6:
                        if (la.CanBet || la.CanRaise)
                        {
                            int to = rng.NextBounded(2) == 0 ? la.MinTo : la.MinTo + (int)rng.NextBounded((uint)(la.MaxTo - la.MinTo + 1));
                            return la.CanBet ? (PlayerCommand)new Bet(seat, to) : new Raise(seat, to);
                        }
                        break;
                }
            }
        }
    }
}
