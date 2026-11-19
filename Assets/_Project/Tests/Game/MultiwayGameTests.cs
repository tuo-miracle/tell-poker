using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using Poker.AI;
using Poker.Characters;
using Poker.Core;
using Poker.Game;
using Poker.Gestures;
using UnityEngine;
using Hand = Poker.Gestures.Hand;
using NV3 = System.Numerics.Vector3;

namespace Poker.Tests.Game
{
    /// <summary>端到端：玩家對 Pip／Rocco／Vex（荷官 Dot）。座位、多人 all-in 與邊池、各座位的動畫與表演、注視、一局結束、AI 對 AI 長跑。</summary>
    public class MultiwayGameTests
    {
        GameTestRig _rig;
        [TearDown] public void TearDown() { _rig?.Dispose(); _rig = null; }

        static GameConfig Four(ulong seed = 5, int? button = 0) => new GameConfig { Seed = seed, FirstButton = button, MonteCarloTrials = 40 };

        static readonly Vector3 PlayerHead = new Vector3(0f, 0.45f, -0.3f);

        static readonly Archetype[][] Lineups = { new GameConfig().Opponents, GameConfig.FiveHanded };

        /// <summary>
        /// 真實尺寸的長圓桌（2026-11-02）：角色坐在桌子另一頭與兩端，根節點離玩家側桌緣中央 0.8–1.1 m（舊的小桌是 0.5–0.75 m）；
        /// 兩端的人在玩家視線 ±58° 內（使用者接受稍微轉頭，RealTableSeatingTests）。
        /// </summary>
        [TestCase(0), TestCase(1)]
        public void Seats_AisAndDot_AroundTheRealTable(int lineup)
        {
            var cfg = Four();
            cfg.Opponents = Lineups[lineup];
            _rig = new GameTestRig(cfg);
            var g = _rig.Game;
            int n = cfg.Opponents.Length;
            for (int seat = 1; seat <= n; seat++)
            {
                var kind = GameController.KindOf(cfg.Opponents[seat - 1]);
                Assert.AreEqual(kind, g.Character(seat).Kind, "seat " + seat);
                Assert.Less(Vector3.Distance(g.Character(seat).transform.localPosition, TableSeats.For(kind).position), 1e-4f, "seat " + seat + " sits at its TableSeats seat");
            }
            Assert.AreSame(g.Character(1), g.Pip);
            var all = new List<CharacterPerformer> { g.Dealer };
            for (int seat = 1; seat <= n; seat++) all.Add(g.Character(seat));
            var center = new Vector3(0f, 0f, g.Spots.Layout.FeltSize.Y * 0.5f);
            foreach (var c in all)
            {
                Assert.AreEqual(_rig.Table.transform, c.transform.parent, c.name + " follows the table");
                var root = c.transform.localPosition;
                float d = new Vector2(root.x, root.z).magnitude;
                Assert.That(d, Is.InRange(0.8f, 1.1f), c.name + " sits at the far end or the round ends of the real table");
                Assert.Greater(root.z, 0.45f, c.name + " is on the far half of the table, not beside the player");
                var head = c.Rig.Head.position;
                var flat = new Vector3(head.x - PlayerHead.x, 0f, head.z - PlayerHead.z);
                Assert.Greater(flat.magnitude, 0.85f, c.name + " head is not in the player's face");
                Assert.Less(Vector3.Angle(Vector3.forward, flat), 58f, c.name + " is in front, at most a slight head turn");
                var toCenter = (center - root).normalized;
                Assert.Greater(Vector3.Dot(c.transform.localRotation * Vector3.forward, toCenter), 0.7f, c.name + " faces the table");
            }
            for (int i = 0; i < all.Count; i++)
                for (int j = i + 1; j < all.Count; j++)
                    Assert.Greater(Vector3.Distance(all[i].Rig.Head.position, all[j].Rig.Head.position), 0.5f, all[i].name + " vs " + all[j].name);
            // 每個 AI 的牌、籌碼堆、下注都在自己面前，不壓到玩家的區域與公共牌
            var s = g.Spots;
            var l = s.Layout;
            for (int seat = 1; seat <= n; seat++)
            {
                foreach (var p in new[] { s.CardOf(seat, 0), s.CardOf(seat, 1), s.StackOf(seat), s.BetOf(seat) })
                {
                    // 玩家區＝下注線以前、下注區左右外 10 cm（舊的小桌就是整條近端；長圓桌兩端的人靠玩家那側的籌碼離玩家很遠）
                    Assert.IsFalse(l.PlayerZone.Contains(new NV3(p.x, 0f, p.z), 0.02f), "seat " + seat + " " + p + " out of the player's zone");
                    Assert.IsTrue(l.InFelt(new NV3(p.x, 0f, p.z), 0.02f), "seat " + seat + " " + p + " on the felt");
                    for (int b = 0; b < 5; b++)
                        Assert.Greater(Vector3.Distance(p, s.Board(b)), 0.07f, "seat " + seat + " clear of board card " + b);
                }
                Assert.Less(Vector3.Distance(s.CardOf(seat, 0), g.Character(seat).transform.localPosition), 0.12f, "cards in front of seat " + seat);
            }
        }

        [Test]
        public void FiveHanded_SilasSitsRightOfTheDealer_AndHandsPlayOut_ChipsConserved()
        {
            var cfg = Four();
            cfg.Opponents = GameConfig.FiveHanded;
            _rig = new GameTestRig(cfg, g =>
            {
                g.AiOverride = st => Engine.GetLegalActions(st, st.ToAct).CanCheck ? (PlayerCommand)new Check(st.ToAct) : new Call(st.ToAct);
                g.AutoPlayer = st => Engine.GetLegalActions(st, 0).CanCheck ? (PlayerCommand)new Check(0) : new Call(0);
            });
            var game = _rig.Game;
            Assert.AreEqual(5, game.Session.SeatCount);
            Assert.AreEqual("Silas", game.Session.NameOf(3));
            Assert.AreEqual(Archetype.Veteran, game.Session.AiAt(3).Personality.Archetype);
            Assert.AreEqual(CharacterKind.Veteran, game.Character(3).Kind);
            Assert.Greater(game.Character(3).transform.localPosition.x, 0.3f, "right of the dealer");
            int total = 5 * cfg.StartStack;
            Assert.IsTrue(_rig.IdleUntil(() =>
            {
                Assert.AreEqual(total, game.Session.TotalChips);
                return game.Session.HandsPlayed >= 2;
            }, 120), "two hands finished\n" + _rig.Dump());
            Assert.IsTrue(_rig.Actions.Any(a => a.seat == 3), "Silas acted");
            Assert.IsTrue(_rig.Actions.Any(a => a.seat == 4), "Vex acted");
        }

        [Test]
        public void FourHanded_EveryoneAllIn_SidePots_ChipsConserved_StacksMatchCore()
        {
            var events = new List<GameEvent>();
            var cfg = Four();
            cfg.StartChipCounts = new[] { 100, 30, 60, 100 };
            _rig = new GameTestRig(cfg, g =>
            {
                g.AiOverride = st => new AllIn(st.ToAct);
                g.AutoPlayer = st => new AllIn(0);
                g.AutoPlayerObserver = ev => events.AddRange(ev);
            });
            int total = 2900;
            Assert.IsTrue(_rig.IdleUntil(() =>
            {
                Assert.AreEqual(total, _rig.Game.Session.TotalChips);
                return _rig.Game.Session.HandsPlayed == 1 && (_rig.Game.Flow == GameFlow.HandOver || _rig.Game.Flow == GameFlow.GameOver);
            }, 60), "hand finished\n" + _rig.Dump());
            _rig.Idle(0.2);
            var pots = events.OfType<PotAwarded>().ToList();
            Assert.GreaterOrEqual(pots.Count, 3, "main pot + two side pots");
            Assert.AreEqual(total, pots.Sum(p => p.Amount));
            Assert.AreEqual(total, _rig.Game.DisplayedTotalChips, "chips on the table add up");
            for (int seat = 0; seat < 4; seat++)
                Assert.AreEqual(_rig.Game.Session.StackOf(seat), _rig.Game.Props.Stack[seat].Chips * 10, "seat " + seat + " pile matches Core");
            Assert.AreEqual(0, _rig.Game.Props.Pot.Chips, "pot empty");
            for (int seat = 1; seat <= 3; seat++)
                Assert.IsTrue(_rig.Game.CardsOf(seat)[0].FaceUp && _rig.Game.CardsOf(seat)[1].FaceUp, "seat " + seat + " shown");
            Assert.IsTrue(_rig.Game.PlayerCard(0).FaceUp, "all-in: player's cards turned up too");
        }

        [Test]
        public void AiBets_SlideFromTheirOwnStack_ToTheirOwnBetSpot()
        {
            // 翻牌前 Vex（UTG）加注到 40，其他人跟
            _rig = new GameTestRig(Four(), g =>
            {
                g.AiOverride = st => st.ToAct == 3 && st.Street == Street.Preflop && st.CurrentBet < 40 ? new Raise(3, 40) : Engine.GetLegalActions(st, st.ToAct).CanCheck ? (PlayerCommand)new Check(st.ToAct) : new Call(st.ToAct);
            });
            Assert.IsTrue(_rig.IdleUntil(() => _rig.Actions.Any(a => a.seat == 3), 10));
            Assert.IsTrue(_rig.IdleUntil(() => _rig.Game.Props.Bet[3].Chips == 4, 3), "Vex's 40 lands in front of Vex");
            Assert.Less(Vector3.Distance(_rig.Game.Props.Bet[3].Position, _rig.Game.Spots.BetOf(3)), 0.02f);
        }

        [Test]
        public void Cues_GoToTheRightCharacter()
        {
            var played = new List<(int seat, PerformanceCue cue)>();
            var auto = new AiPlayer(Personality.Cold, 0, 3, new AiSettings { MonteCarloTrials = 30, ReactionTrials = 20 });
            _rig = new GameTestRig(Four(11, null), g =>
            {
                g.AutoPlayer = st => auto.Decide(st, 0, auto.Observations).Command;
                g.AutoPlayerObserver = ev => auto.OnEvents(PlayerView.Filter(ev, 0));
                g.AiThinkScale = 0.1f;
                g.CuePlayed += (seat, cue) => played.Add((seat, cue));
            });
            Assert.IsTrue(_rig.IdleUntil(() => _rig.Game.Session.HandsPlayed >= 3, 120));
            Assert.IsNotEmpty(played);
            foreach (var (seat, cue) in played) Assert.AreEqual(cue.Seat, seat, "cue routed to its own seat");
            Assert.GreaterOrEqual(played.Select(p => p.seat).Distinct().Count(), 2, "more than one AI performed");
        }

        [Test]
        public void Gaze_TellsWhichAiYouAreLookingAt()
        {
            _rig = new GameTestRig(Four());
            var g = _rig.Game;
            _rig.LookFromSeat(new Vector3(0f, 0f, 0.1f));
            _rig.Idle(0.5);
            _rig.LookAt(g.Character(2).Rig.Head.position);
            _rig.Idle(1.3);
            Assert.IsTrue(g.Character(2).IsGazedAt, "Rocco");
            Assert.IsFalse(g.Character(1).IsGazedAt);
            Assert.IsFalse(g.Character(3).IsGazedAt);
            Assert.IsTrue(_rig.Signals.Any(x => x.s.GazeSeat == 2));
            Assert.AreEqual(2, g.GazedSeat);
            _rig.LookAt(g.Character(3).Rig.Head.position);
            _rig.Idle(1.5);
            Assert.IsTrue(g.Character(3).IsGazedAt, "Vex");
            Assert.IsFalse(g.Character(2).IsGazedAt, "no longer Rocco");
            Assert.IsTrue(g.Session.AiAt(3).IsGazed);
            Assert.IsFalse(g.Session.AiAt(2).IsGazed);
            _rig.LookAt(g.Dealer.Rig.Head.position);
            _rig.Idle(1.5);
            Assert.IsTrue(g.Dealer.IsGazedAt);
            Assert.AreEqual(-1, g.GazedSeat, "the dealer is not an opponent");
        }

        [Test]
        public void Gaze_ReachesTheAi_EvenWhenItsBackgroundDecisionIsSlow()
        {
            // 2026-10-13 全套 PlayMode 偶發失敗（上一個測試 line 150：AiAt(3).IsGazed 為 false）的根因：
            // 測試桌同步地一幀接一幀跑（1 秒模擬時間只花幾毫秒），Vex 的 Monte Carlo 在背景執行緒跑；
            // 機器忙的時候背景決策還沒算完，盯著 Vex 的訊號在排隊（GameController.SendSignals），斷言就看到 false。
            // 實機一幀 14 ms、決策幾毫秒就完成。這裡把執行緒池塞滿 0.4 s，重現「背景決策比平常慢」。
            System.Threading.ThreadPool.GetMinThreads(out int workers, out _);
            var blockers = new List<System.Threading.Tasks.Task>();
            try
            {
                _rig = new GameTestRig(Four());
                var g = _rig.Game;
                _rig.LookFromSeat(new Vector3(0f, 0f, 0.1f));
                _rig.Idle(1.8);
                for (int i = 0; i < workers * 2; i++) blockers.Add(System.Threading.Tasks.Task.Run(() => System.Threading.Thread.Sleep(400)));
                _rig.LookAt(g.Character(3).Rig.Head.position);
                _rig.Idle(1.5);
                Assert.IsTrue(g.Character(3).IsGazedAt, "Vex");
                Assert.IsTrue(g.Session.AiAt(3).IsGazed, "the gaze signal reached Vex's AI");
            }
            finally { System.Threading.Tasks.Task.WaitAll(blockers.ToArray()); }
        }

        [Test]
        public void GameEnds_AfterTheHandLimit_ShowsSummary_SavesProgress_KnockStartsANewGame()
        {
            var cfg = Four(7, null);
            cfg.Schedule = new MatchSchedule { MaxHands = 2 };
            cfg.NextHandSeconds = 0.3f;
            cfg.SummarySeconds = 60f;
            _rig = new GameTestRig(cfg, g =>
            {
                g.AutoPlayer = st => Engine.GetLegalActions(st, 0).CanCheck ? (PlayerCommand)new Check(0) : new Fold(0);
                g.AiThinkScale = 0.1f;
            });
            var g0 = _rig.Game;
            Assert.IsTrue(_rig.IdleUntil(() => g0.Flow == GameFlow.GameOver, 120), "game over\n" + _rig.Dump());
            Assert.AreEqual(2, g0.Session.HandsThisGame);
            var sum = g0.LastSummary;
            Assert.IsNotNull(sum);
            Assert.AreEqual(2, sum.HandsPlayed);
            Assert.AreEqual(GameEndReason.HandLimit, sum.Reason);
            Assert.IsTrue(g0.Props.SummaryVisible);
            StringAssert.Contains("Hands won:", g0.Props.SummaryText);
            StringAssert.DoesNotContain("Tells read", g0.Props.SummaryText);   // 2026-11-04 拿掉（計數還在 LastSummary）
            Assert.AreEqual(3, g0.Props.SummaryText.Split('\n').Length, "title + two lines\n" + g0.Props.SummaryText);
            var rows = g0.Props.SummaryRows;
            Assert.AreEqual(3, rows.Count, "no hidden third line slot left on the board");
            Assert.AreEqual(rows[0].y - rows[1].y > 0f, true, "lines go down from the title");
            Assert.AreEqual(rows[1].y - rows[2].y, 0.046f, 0.002f, "even line spacing");
            StringAssert.Contains("Biggest pot", g0.Props.SummaryText);
            StringAssert.Contains(sum.Title, g0.Props.SummaryText);
            Assert.AreEqual(1, g0.Progress.GamesPlayed);
            Assert.AreEqual(2, g0.Progress.HandsPlayed);
            Assert.AreEqual(1, new ProgressStore(_rig.ProgressPath).Load().GamesPlayed, "saved to disk");
            Assert.AreEqual(2, new ProgressStore(_rig.ProgressPath).Load().Opponent("Rocco").HandsPlayed);

            _rig.Idle(2.0);
            Assert.AreEqual(GameFlow.GameOver, g0.Flow, "summary waits");
            g0.HandleGesture(new GestureEvent(GestureKind.KnockCheck, 0, Hand.Right, 0, 0f));
            Assert.IsFalse(g0.Props.SummaryVisible);
            Assert.AreEqual(2, g0.Session.GameNumber);
            Assert.AreEqual(4000, g0.Session.TotalChips);
            Assert.IsTrue(_rig.IdleUntil(() => g0.Session.HandsThisGame == 1, 5));
        }

        [Test]
        public void GameEnds_WhenTimeIsUp_AfterTheCurrentHand()
        {
            var cfg = Four(7, null);
            cfg.Schedule = new MatchSchedule { MaxHands = 50, TimeLimitSeconds = 20f };
            cfg.NextHandSeconds = 0.3f;
            _rig = new GameTestRig(cfg, g =>
            {
                g.AutoPlayer = st => Engine.GetLegalActions(st, 0).CanCheck ? (PlayerCommand)new Check(0) : new Fold(0);
                g.AiThinkScale = 0.1f;
            });
            Assert.IsTrue(_rig.IdleUntil(() => _rig.Game.Flow == GameFlow.GameOver, 120), _rig.Dump());
            Assert.AreEqual(GameEndReason.TimeUp, _rig.Game.LastSummary.Reason);
            Assert.GreaterOrEqual(_rig.Game.Now, 20.0);
            Assert.AreEqual(_rig.Game.Session.HandsThisGame, _rig.Game.LastSummary.HandsPlayed);
        }

        [Test]
        public void TimeRunsOut_WhileWaitingForTheNextHand_StillShowsTheSummary()
        {
            // 第一手在時間內打完；等下一手的那段時間超過上限 → 開下一手前要先結算，不能悄悄重開
            var cfg = Four(7, 1);
            cfg.Schedule = new MatchSchedule { MaxHands = 50, TimeLimitSeconds = 12f };
            cfg.NextHandSeconds = 20f;
            cfg.SummarySeconds = 60f;
            _rig = new GameTestRig(cfg, g =>
            {
                g.AiOverride = st => new Fold(st.ToAct);
                g.AutoPlayer = st => Engine.GetLegalActions(st, 0).CanCheck ? (PlayerCommand)new Check(0) : new Call(0);
                g.AiThinkScale = 0.1f;
            });
            Assert.IsTrue(_rig.IdleUntil(() => _rig.Game.Flow == GameFlow.HandOver, 12), "first hand inside the time limit\n" + _rig.Dump());
            Assert.Less(_rig.Game.Now, 12.0);
            Assert.IsTrue(_rig.IdleUntil(() => _rig.Game.Flow == GameFlow.GameOver, 30), "summary instead of a silent reset\n" + _rig.Dump());
            Assert.AreEqual(GameEndReason.TimeUp, _rig.Game.LastSummary.Reason);
            Assert.AreEqual(1, _rig.Game.LastSummary.HandsPlayed);
            Assert.AreEqual(1, _rig.Game.Progress.GamesPlayed);
            Assert.AreEqual(1, _rig.Game.Session.GameNumber, "not reset yet");
        }

        [Test]
        public void ThirtyHands_AutoPlayerVsThreeAis_NeverStall_ConserveChips()
        {
            var auto = new AiPlayer(Personality.Loudmouth, 0, 77, new AiSettings { MonteCarloTrials = 30, ReactionTrials = 20 });
            int summaries = 0;
            var cfg = new GameConfig { Seed = 9, MonteCarloTrials = 30, NextHandSeconds = 0.3f, SummarySeconds = 1f };
            _rig = new GameTestRig(cfg, g =>
            {
                g.AutoPlayer = st => auto.Decide(st, 0, auto.Observations).Command;
                g.AutoPlayerObserver = ev => auto.OnEvents(PlayerView.Filter(ev, 0));
                g.AiThinkScale = 0.05f;
                g.GameEnded += s => summaries++;
            });
            int steps = 0;
            while (_rig.Game.Session.HandsPlayed < 30 && steps++ < 72 * 60 * 40)
            {
                _rig.StepIdle();
                Assert.AreEqual(4000, _rig.Game.Session.TotalChips);
            }
            Assert.GreaterOrEqual(_rig.Game.Session.HandsPlayed, 30, $"stalled in {_rig.Game.Flow}\n{_rig.Dump()}");
            Assert.GreaterOrEqual(summaries, 2, "12-hand games end and restart");
            // 存檔在這一手的演出播完、進入 HandOver 時才記
            Assert.IsTrue(_rig.IdleUntil(() => _rig.Game.Progress.HandsPlayed >= 30, 60), "progress recorded every hand");
        }
    }
}
