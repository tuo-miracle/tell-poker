using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;
using Poker.AI;
using Poker.Core;

namespace Poker.Tests.AI
{
    /// <summary>
    /// Fool Rocco 原型（2026-10-14）的劇本邏輯：Rocco 讀玩家真的習慣（伸手延遲、看牌次數、籌碼上方逗留）並說出來；
    /// 第 2 手 Dot 教玩家「慢慢伸手」騙他；第 3 手同一招他就不信了（被同一招騙過一次，下一次反著讀）。
    /// 四人桌：玩家 0、Pip 1、Rocco 2（按鈕）、Vex 3；Pip、Vex 每手翻牌前都棄牌。
    /// </summary>
    public class FoolRoccoTests
    {
        static FoolSignals Sig(float reach = 1f, bool chips = true, int peeks = 1, float hover = 0f) => new FoolSignals(reach, chips, peeks, hover);

        // ───────────── 挑最明顯的訊號 ─────────────

        [Test]
        public void Callout_PicksTheMostSalientRealSignal()
        {
            Assert.AreEqual(FoolCallout.Peeks, FoolRocco.PickCallout(Sig(reach: 1f, peeks: 3)));
            Assert.AreEqual(FoolCallout.SlowReach, FoolRocco.PickCallout(Sig(reach: 4.5f, peeks: 2)), "4.5/3 s beats 2/2 looks");
            Assert.AreEqual(FoolCallout.SlowMove, FoolRocco.PickCallout(Sig(reach: 4.5f, chips: false, peeks: 1)), "slow, but not toward the chips");
            Assert.AreEqual(FoolCallout.Hover, FoolRocco.PickCallout(Sig(reach: 0.8f, peeks: 1, hover: 2.5f)));
            Assert.AreEqual(FoolCallout.Peeks, FoolRocco.PickCallout(Sig(reach: 3f, peeks: 2, hover: 1.5f)), "ties: peeks first");
            Assert.AreEqual(FoolCallout.Quick, FoolRocco.PickCallout(Sig(reach: 1.2f, peeks: 1, hover: 0.4f)), "nothing salient");
            Assert.AreEqual(FoolCallout.Quick, FoolRocco.PickCallout(Sig(reach: 2.9f, peeks: 1, hover: 1.4f)), "just under every threshold");
        }

        [Test]
        public void CalloutLines_SayTheRealCountInWords_AndMatchTheSpecWording()
        {
            Assert.AreEqual("You looked at your cards twice. Not loving that hand, huh?", FoolRocco.CalloutLine(FoolCallout.Peeks, 2).Text);
            Assert.AreEqual("You looked at your cards three times. Not loving that hand, huh?", FoolRocco.CalloutLine(FoolCallout.Peeks, 3).Text);
            Assert.AreEqual("You looked at your cards four times. Not loving that hand, huh?", FoolRocco.CalloutLine(FoolCallout.Peeks, 4).Text);
            Assert.AreEqual("You keep looking at your cards. Not loving that hand, huh?", FoolRocco.CalloutLine(FoolCallout.Peeks, 9).Text);
            Assert.AreEqual("Took you a while to reach for those chips. Weak.", FoolRocco.CalloutLine(FoolCallout.SlowReach, 1).Text);
            Assert.AreEqual("Fiddling with your chips? Nervous.", FoolRocco.CalloutLine(FoolCallout.Hover, 1).Text);
            Assert.AreEqual("Quick decision. Confident, huh?", FoolRocco.CalloutLine(FoolCallout.Quick, 1).Text);
            foreach (FoolCallout c in System.Enum.GetValues(typeof(FoolCallout)))
                if (c != FoolCallout.None)
                    for (int n = 0; n < 12; n++) Assert.AreEqual("rocco", FoolRocco.CalloutLine(c, n).Voice);
        }

        [Test]
        public void Lines_AreUnique_RoccoOrDot_AndCoverEveryLineTheScriptCanSay()
        {
            var all = FoolRocco.Lines().ToList();
            CollectionAssert.AllItemsAreUnique(all.Select(l => l.Id));
            CollectionAssert.AllItemsAreUnique(all.Select(l => l.Voice + "\n" + l.Text));
            foreach (var l in all) CollectionAssert.Contains(new[] { "rocco", "dot" }, l.Voice, l.ToString());
            var texts = new HashSet<string>(all.Select(l => l.Voice + ":" + l.Text));
            foreach (FoolCallout c in System.Enum.GetValues(typeof(FoolCallout)))
                if (c != FoolCallout.None)
                    for (int n = 0; n < 12; n++) Assert.IsTrue(texts.Contains("rocco:" + FoolRocco.CalloutLine(c, n).Text), c + " " + n);
            foreach (var l in new[] { FoolRocco.RoccoCall, FoolRocco.RoccoFold, FoolRocco.RoccoPlayed, FoolRocco.RoccoDistrust })
                Assert.IsTrue(texts.Contains("rocco:" + l.Text), l.ToString());
            foreach (var l in new[] { FoolRocco.DotNextHand, FoolRocco.DotIntro, FoolRocco.DotCoach, FoolRocco.DotFooled, FoolRocco.DotRetry, FoolRocco.DotLearns })
                Assert.IsTrue(texts.Contains("dot:" + l.Text), l.ToString());
            Assert.AreEqual("Rocco thinks a slow reach means a weak hand. Try it — wait a few seconds before you bet.", FoolRocco.DotCoach.Text);
            Assert.AreEqual("Quick bet. You've got it. I'm out.", FoolRocco.RoccoFold.Text);
            Assert.AreEqual("Not this time.", FoolRocco.RoccoDistrust.Text);
            Assert.AreEqual("He learns. Mix it up.", FoolRocco.DotLearns.Text);
        }

        // ───────────── 牌局（真的 Engine＋腳本 AI） ─────────────

        sealed class Run
        {
            public HandState S;
            public readonly List<(int seat, PlayerCommand cmd)> Acts = new List<(int, PlayerCommand)>();
            public readonly List<GameEvent> Events = new List<GameEvent>();
        }

        /// <summary>開 fool 這一手（固定牌、Rocco 按鈕），跑到輪到玩家或這一手結束。</summary>
        static Run Deal(FoolRocco fool, int seats = FoolRocco.Seats)
        {
            var r = new Run { S = HandState.NewTable(Enumerable.Repeat(1000, seats).ToArray()) };
            var a = Engine.Apply(r.S, new StartHand(7, FoolRocco.ButtonFor(seats), 5, 10, FoolRocco.Preset(fool.Hand)));
            Assert.IsTrue(a.IsSuccess, "" + a.Error);
            r.S = a.State;
            r.Events.AddRange(a.Events);
            Advance(fool, r);
            return r;
        }

        static void Apply(Run r, PlayerCommand c)
        {
            var a = Engine.Apply(r.S, c);
            Assert.IsTrue(a.IsSuccess, c + ": " + a.Error);
            r.S = a.State;
            r.Acts.Add((c.Seat, c));
            r.Events.AddRange(a.Events);
        }

        static void Advance(FoolRocco fool, Run r)
        {
            int guard = 0;
            while (r.S.Phase == HandPhase.Betting && r.S.ToAct != 0 && guard++ < 50) Apply(r, fool.Ai(r.S, r.S.ToAct));
        }

        /// <summary>玩家行動（之後讓 AI 跑到再輪到玩家或一手結束）。</summary>
        static void Player(FoolRocco fool, Run r, PlayerCommand c)
        {
            Apply(r, c);
            Advance(fool, r);
        }

        /// <summary>決定之後：玩家每條街都過牌（遊戲層自動替玩家過牌）。</summary>
        static void CheckDown(FoolRocco fool, Run r)
        {
            int guard = 0;
            while (r.S.Phase == HandPhase.Betting && guard++ < 10) Player(fool, r, new Check(0));
        }

        static bool PlayerWon(Run r) => r.Events.OfType<PotAwarded>().Any(p => p.Shares.Any(s => s.Seat == 0));
        static bool Folded(Run r, int seat) => r.Acts.Any(a => a.seat == seat && a.cmd is Fold);

        static FoolEnd End(FoolRocco fool, Run r) => fool.OnHandEnded(PlayerWon(r), Folded(r, FoolRocco.RoccoSeat), Folded(r, 0));

        [Test]
        public void FiveSeatTable_TheVeteranFoldsToo_SameHeadsUpWithRocco()
        {
            // 寫實預覽是 5 人桌（荷官右側多一位老手 Silas，2026-11-02）：按鈕往後一位，玩家仍是大盲、Vex 小盲，老手和 Pip、Vex 一樣翻牌前棄牌
            Assert.AreEqual(3, FoolRocco.ButtonFor(5));
            Assert.AreEqual(FoolRocco.Button, FoolRocco.ButtonFor(4));
            var fool = new FoolRocco();
            fool.BeginHand();
            var r = Deal(fool, 5);
            Assert.AreEqual(0, r.S.ToAct, "player's turn");
            foreach (var seat in new[] { 1, 3, 4 }) Assert.IsTrue(Folded(r, seat), "seat " + seat + " folds preflop");
            Assert.IsTrue(r.Acts.Any(a => a.seat == FoolRocco.RoccoSeat && a.cmd is Raise x && x.ToAmount == 30), "Rocco raises to 30");
        }

        [Test]
        public void Hand1_PipAndVexFold_RoccoRaises_TheCalloutComesAfterThePlayerActs()
        {
            var fool = new FoolRocco();
            fool.BeginHand();
            Assert.AreEqual(1, fool.Hand);
            Assert.AreEqual(FoolRocco.DotIntro, fool.HandIntro());
            var r = Deal(fool);
            Assert.AreEqual(0, r.S.ToAct, "player's turn");
            Assert.IsTrue(Folded(r, FoolRocco.PipSeat) && Folded(r, FoolRocco.VexSeat), "Pip and Vex fold preflop");
            Assert.IsTrue(r.Acts.Any(a => a.seat == FoolRocco.RoccoSeat && a.cmd is Raise x && x.ToAmount == 30), "Rocco bets (raises to 30)");
            CollectionAssert.AreEqual(Card.ParseMany(FoolRocco.Hand1PlayerHole), new[] { r.S.Seat(0).Hole1, r.S.Seat(0).Hole2 });

            var line = fool.OnPlayerAction(FoolAction.Call, Sig(reach: 1f, peeks: 3));
            Assert.AreEqual("You looked at your cards three times. Not loving that hand, huh?", line.Text);
            Assert.IsTrue(fool.Decided);
            Player(fool, r, new Call(0));
            CheckDown(fool, r);
            Assert.AreEqual(HandPhase.Idle, r.S.Phase);
            Assert.IsFalse(PlayerWon(r), "Rocco's read was right: his kings win");
            var end = End(fool, r);
            Assert.AreEqual("rocco-won", end.Outcome);
            Assert.IsNull(end.Reveal);
            StringAssert.IsMatch(@"^\[FoolRocco\] hand=1 signals reach=1\.0 peeks=3 hover=0\.0 callout=peeks player=fast outcome=rocco-won$", end.Log);
            Assert.IsFalse(fool.IsDone);
            fool.BeginHand();
            Assert.AreEqual(2, fool.Hand);
        }

        [Test]
        public void Hand1_ARaise_IsCalledByRocco_AFold_EndsTheHand()
        {
            var fool = new FoolRocco();
            fool.BeginHand();
            var r = Deal(fool);
            fool.OnPlayerAction(FoolAction.Raise, Sig(reach: 5f));
            Player(fool, r, new Raise(0, 90));
            Assert.IsTrue(r.Acts.Any(a => a.seat == FoolRocco.RoccoSeat && a.cmd is Call), "Rocco calls the raise in hand 1");
            Assert.AreEqual(Street.Flop, r.S.Street);

            var fool2 = new FoolRocco();
            fool2.BeginHand();
            var r2 = Deal(fool2);
            var line = fool2.OnPlayerAction(FoolAction.Fold, Sig(reach: 0.6f, peeks: 1, hover: 0f));
            Assert.AreEqual(FoolRocco.CalloutLine(FoolCallout.Quick, 1).Text, line.Text);
            Player(fool2, r2, new Fold(0));
            var end = End(fool2, r2);
            Assert.AreEqual("player-folded", end.Outcome);
            StringAssert.Contains("callout=quick player=fast outcome=player-folded", end.Log);
        }

        /// <summary>第 2 手：玩家等了 3 秒以上才下注 → Rocco 讀成虛張而跟注 → 攤牌輸 → 「You played me!」＋揭曉卡＋Dot。第 3 手同一招 → 不信、棄牌。</summary>
        [Test]
        public void WaitedThenBet_FoolsRocco_ThenTheSameTrickInHand3_MakesHimFold()
        {
            var fool = new FoolRocco();
            fool.BeginHand(); var r1 = Deal(fool);
            fool.OnPlayerAction(FoolAction.Fold, Sig()); Player(fool, r1, new Fold(0)); End(fool, r1);

            fool.BeginHand();
            Assert.AreEqual(2, fool.Hand);
            Assert.AreEqual(FoolRocco.DotCoach, fool.HandIntro());
            var r = Deal(fool);
            Assert.IsTrue(Folded(r, FoolRocco.PipSeat) && Folded(r, FoolRocco.VexSeat));
            Assert.IsTrue(r.Acts.Any(a => a.seat == FoolRocco.RoccoSeat && a.cmd is Call), "Rocco just calls (limps): the player gets to bet");
            CollectionAssert.AreEqual(Card.ParseMany(FoolRocco.Hand2PlayerHole), new[] { r.S.Seat(0).Hole1, r.S.Seat(0).Hole2 });

            var line = fool.OnPlayerAction(FoolAction.Raise, Sig(reach: 3.6f, peeks: 1));
            Assert.AreEqual(FoolRocco.RoccoCall, line);
            Assert.IsTrue(fool.Waited);
            Player(fool, r, new Raise(0, 50));
            Assert.IsTrue(r.Acts.Any(a => a.seat == FoolRocco.RoccoSeat && a.cmd is Call && r.S.Street != Street.Preflop), "Rocco calls the bet");
            CheckDown(fool, r);
            Assert.IsTrue(PlayerWon(r), "the monster wins at showdown");
            var end = End(fool, r);
            Assert.AreEqual("fooled", end.Outcome);
            Assert.AreEqual(FoolRocco.RoccoPlayed, end.Rocco);
            Assert.AreEqual(FoolRocco.DotFooled, end.Dot);
            Assert.AreEqual("Rocco read you: weak — wrong!", end.Reveal);   // 2026-11-03 揭曉卡縮成一行
            StringAssert.IsMatch(@"^\[FoolRocco\] hand=2 signals reach=3\.6 peeks=1 hover=0\.0 callout=call player=waited outcome=fooled$", end.Log);

            fool.BeginHand();
            Assert.AreEqual(3, fool.Hand);
            Assert.IsNull(fool.HandIntro(), "no coaching in hand 3: the player repeats the trick on their own");
            var r3 = Deal(fool);
            CollectionAssert.AreEqual(Card.ParseMany(FoolRocco.Hand3PlayerHole), new[] { r3.S.Seat(0).Hole1, r3.S.Seat(0).Hole2 });
            var line3 = fool.OnPlayerAction(FoolAction.Raise, Sig(reach: 4.0f));
            Assert.AreEqual(FoolRocco.RoccoDistrust, line3, "fooled once by a slow reach: he no longer trusts it");
            Player(fool, r3, new Raise(0, 50));
            Assert.IsTrue(Folded(r3, FoolRocco.RoccoSeat), "Rocco folds");
            var end3 = End(fool, r3);
            Assert.AreEqual("folded", end3.Outcome);
            Assert.AreEqual(FoolRocco.DotLearns, end3.Dot);
            StringAssert.Contains("hand=3 ", end3.Log);
            StringAssert.Contains("callout=distrust player=waited outcome=folded", end3.Log);
            Assert.IsTrue(fool.IsDone);
        }

        /// <summary>第 2 手沒等（很快就下注）→ Rocco 棄牌、Dot 說下次等等看 → 這一手重玩一次；重玩還是很快就結束（不進第 3 手）。</summary>
        [Test]
        public void FastBet_RoccoFolds_TheHandIsReplayedOnce()
        {
            var fool = new FoolRocco();
            fool.BeginHand(); var r1 = Deal(fool);
            fool.OnPlayerAction(FoolAction.Fold, Sig()); Player(fool, r1, new Fold(0)); End(fool, r1);

            fool.BeginHand();
            var r = Deal(fool);
            var line = fool.OnPlayerAction(FoolAction.Raise, Sig(reach: 0.9f));
            Assert.AreEqual(FoolRocco.RoccoFold, line);
            Assert.IsFalse(fool.Waited);
            Player(fool, r, new Raise(0, 50));
            Assert.IsTrue(Folded(r, FoolRocco.RoccoSeat));
            var end = End(fool, r);
            Assert.AreEqual("folded", end.Outcome);
            Assert.AreEqual(FoolRocco.DotRetry, end.Dot);
            Assert.IsNull(end.Rocco);
            Assert.AreEqual("Rocco read you: strong — right", end.Reveal);
            StringAssert.Contains("callout=fold player=fast outcome=folded", end.Log);
            Assert.IsFalse(fool.IsDone);

            fool.BeginHand();
            Assert.AreEqual(2, fool.Hand, "replay");
            Assert.IsTrue(fool.Replaying);
            Assert.AreEqual(FoolRocco.DotCoach, fool.HandIntro(), "Dot coaches again");
            var again = Deal(fool);
            fool.OnPlayerAction(FoolAction.Raise, Sig(reach: 1.1f));
            Player(fool, again, new Raise(0, 50));
            End(fool, again);
            Assert.IsTrue(fool.IsDone, "only one replay");
        }

        [Test]
        public void FastThenWaitedOnTheReplay_FoolsHim_AndGoesToHand3()
        {
            var fool = new FoolRocco();
            fool.BeginHand(); var r1 = Deal(fool);
            fool.OnPlayerAction(FoolAction.Fold, Sig()); Player(fool, r1, new Fold(0)); End(fool, r1);
            fool.BeginHand(); var r2 = Deal(fool);
            fool.OnPlayerAction(FoolAction.Raise, Sig(reach: 0.5f)); Player(fool, r2, new Raise(0, 50)); End(fool, r2);
            fool.BeginHand(); var r3 = Deal(fool);
            Assert.AreEqual(FoolRocco.RoccoCall, fool.OnPlayerAction(FoolAction.Raise, Sig(reach: 3.2f)));
            Player(fool, r3, new Raise(0, 50)); CheckDown(fool, r3);
            Assert.AreEqual("fooled", End(fool, r3).Outcome);
            fool.BeginHand();
            Assert.AreEqual(3, fool.Hand);
        }

        [Test]
        public void Hand3_AFastBet_IsReadAsStrength_Too()
        {
            var fool = Fooled();
            fool.BeginHand(); var r = Deal(fool);
            Assert.AreEqual(FoolRocco.RoccoFold, fool.OnPlayerAction(FoolAction.Raise, Sig(reach: 0.7f)));
            Player(fool, r, new Raise(0, 50));
            var end = End(fool, r);
            Assert.AreEqual(FoolRocco.DotLearns, end.Dot);
            Assert.IsTrue(fool.IsDone);
        }

        /// <summary>第 2 手只過牌沒下注：決定沒發生（每次輪到都重新量），一路過牌到攤牌 → checked，重玩一次。</summary>
        [Test]
        public void Hand2_CheckingInsteadOfBetting_IsNotTheDecision_AndGetsAReplay()
        {
            var fool = new FoolRocco();
            fool.BeginHand(); var r1 = Deal(fool);
            fool.OnPlayerAction(FoolAction.Fold, Sig()); Player(fool, r1, new Fold(0)); End(fool, r1);
            fool.BeginHand(); var r = Deal(fool);
            Assert.IsNull(fool.OnPlayerAction(FoolAction.Check, Sig(reach: 2f)));
            Assert.IsFalse(fool.Decided);
            Player(fool, r, new Check(0));
            Assert.AreEqual(Street.Flop, r.S.Street);
            Assert.IsNull(fool.OnPlayerAction(FoolAction.Check, Sig(reach: 1.5f, peeks: 2)));
            Player(fool, r, new Check(0));
            CheckDown(fool, r);
            var end = End(fool, r);
            Assert.AreEqual("checked", end.Outcome);
            StringAssert.Contains("signals reach=1.5 peeks=2", end.Log, "the last turn's signals");
            StringAssert.Contains("callout=-", end.Log);
            Assert.AreEqual(FoolRocco.DotRetry, end.Dot);
            fool.BeginHand();
            Assert.IsTrue(fool.Replaying);
        }

        [Test]
        public void TheScriptNeverBetsIntoThePlayer_AfterTheDecision()
        {
            var fool = new FoolRocco();
            fool.BeginHand();
            var r = Deal(fool);
            fool.OnPlayerAction(FoolAction.Call, Sig());
            Player(fool, r, new Call(0));
            while (r.S.Phase == HandPhase.Betting)
            {
                Assert.IsTrue(Engine.GetLegalActions(r.S, 0).CanCheck, "after the decision the player can always check (auto check-down)");
                Player(fool, r, new Check(0));
            }
        }

        /// <summary>
        /// 2026-10-14 live talk：Rocco 不只在你下注之後講——你還在想、伸手、縮手時就嗆（即時系統）；關鍵決定之後（自動過牌那段）不嗆。
        /// 三手的劇情照舊（下注後的讀牌、揭曉卡、Dot 的教學與收尾）。
        /// </summary>
        [Test]
        public void LiveTalk_RoccoNeedlesDuringTheDecision_NotAfter_AndMoreThanAtANormalTable()
        {
            var fool = new FoolRocco();
            Assert.IsFalse(fool.LiveTalk, "before the first hand");
            fool.BeginHand();
            var r = Deal(fool);
            Assert.IsTrue(fool.LiveTalk, "hand 1: Rocco reads you live");
            Assert.Greater(fool.LiveChattiness, 1.2f, "the scenario is about Rocco reading you: he talks more than at a normal table");
            fool.OnPlayerAction(FoolAction.Call, Sig());
            Assert.IsFalse(fool.LiveTalk, "after the decision the hand checks down quietly");
            Player(fool, r, new Call(0));
            CheckDown(fool, r);
            End(fool, r);
            fool.BeginHand();
            Assert.IsTrue(fool.LiveTalk, "hand 2: needling while you wait is part of the pressure");
        }

        static FoolRocco Fooled()
        {
            var fool = new FoolRocco();
            fool.BeginHand(); var r1 = Deal(fool);
            fool.OnPlayerAction(FoolAction.Fold, Sig()); Player(fool, r1, new Fold(0)); End(fool, r1);
            fool.BeginHand(); var r2 = Deal(fool);
            fool.OnPlayerAction(FoolAction.Raise, Sig(reach: 3.5f)); Player(fool, r2, new Raise(0, 50)); CheckDown(fool, r2);
            Assert.AreEqual("fooled", End(fool, r2).Outcome);
            return fool;
        }
    }
}
