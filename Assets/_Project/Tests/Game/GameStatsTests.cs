using System.Linq;
using System.Collections.Generic;
using NUnit.Framework;
using Poker.AI;
using Poker.Core;
using Poker.Game;

namespace Poker.Tests.Game
{
    /// <summary>讀心判定（AI 下注時的真實意圖 vs 玩家的回應）、每手結果、一局結算。事件來自真的 Core。</summary>
    public class GameStatsTests
    {
        HandState _s;
        GameStats _stats;
        readonly Dictionary<int, HandReveal> _reveals = new Dictionary<int, HandReveal>();

        void Start(int[] stacks, int button = 0)
        {
            _reveals.Clear();
            _stats = new GameStats(0, seat => _reveals.TryGetValue(seat, out var r) ? r : null);
            _s = HandState.NewTable(stacks);
            Do(new StartHand(77, button, 5, 10));
        }

        void Do(Command c)
        {
            var r = Engine.Apply(_s, c);
            Assert.IsTrue(r.IsSuccess, c + " " + r.Error);
            _s = r.State;
            foreach (var e in r.Events) _stats.Observe(e);
        }

        void Reveal(int seat, params Intent[] intents)
        {
            var m = new List<RevealMoment>();
            foreach (var i in intents) m.Add(new RevealMoment { Intent = i });
            _reveals[seat] = new HandReveal { HandNumber = _s.HandNumber, Seat = seat, Name = "S" + seat, Moments = m, Headline = HandReveal.HeadlineFor(intents[intents.Length - 1]) };
        }

        [Test]
        public void ReadJudge_Rules()
        {
            Assert.AreEqual(true, ReadJudge.Judge(Intent.Value, playerFolded: true));
            Assert.AreEqual(false, ReadJudge.Judge(Intent.Value, playerFolded: false));
            Assert.AreEqual(true, ReadJudge.Judge(Intent.Bluff, playerFolded: false));
            Assert.AreEqual(false, ReadJudge.Judge(Intent.Bluff, playerFolded: true));
            Assert.AreEqual(true, ReadJudge.Judge(Intent.SemiBluff, playerFolded: false));
            Assert.IsNull(ReadJudge.Judge(Intent.Marginal, playerFolded: true), "no clear read");
        }

        [Test]
        public void CallingABluff_IsACorrectRead_AndThePlayerWinsTheHand()
        {
            Start(new[] { 1000, 1000, 1000, 1000 });
            // 翻牌前：Vex 跟、玩家跟、Pip 補、Rocco 過
            Do(new Call(3)); Do(new Call(0)); Do(new Call(1)); Do(new Check(2));
            // 翻牌：Pip 下注（Pip 的第 2 次決策），Rocco、Vex 棄，玩家跟
            Do(new Bet(1, 40)); Do(new Fold(2)); Do(new Fold(3)); Do(new Call(0));
            // 轉牌：Pip 過，玩家下注，Pip 棄
            Do(new Check(1));
            Reveal(1, Intent.Marginal, Intent.Bluff, Intent.GiveUp, Intent.GiveUp);
            Do(new Bet(0, 100)); Do(new Fold(1));
            Assert.AreEqual(HandPhase.Idle, _s.Phase);
            var h = _stats.LastHand;
            Assert.IsNotNull(h);
            Assert.IsTrue(h.PlayerWon);
            Assert.AreEqual(1, h.Reads.Count, "one AI bet was answered");
            Assert.AreEqual(1, h.Reads[0].Seat);
            Assert.AreEqual(Intent.Bluff, h.Reads[0].AiIntent);
            Assert.IsTrue(h.Reads[0].Correct);
            Assert.AreEqual(new[] { 0, 1, 2, 3 }, h.SeatsDealt);
            Assert.Greater(h.PotWon, 0);
        }

        [Test]
        public void FoldingToValue_IsCorrect_CallingValue_IsWrong()
        {
            Start(new[] { 1000, 1000, 1000 });
            // 3 人：按鈕 0、小盲 1、大盲 2；翻牌前 0 先動
            Do(new Call(0)); Do(new Call(1)); Do(new Check(2));
            Do(new Bet(1, 30)); // Pip 的第 2 次決策：有牌
            Reveal(1, Intent.Marginal, Intent.Value);
            Reveal(2, Intent.Marginal, Intent.Value);
            Do(new Raise(2, 90)); // Rocco 加注（第 2 次決策）
            Do(new Fold(0));      // 玩家面對 Rocco 的加注棄牌 → 讀 Rocco
            Do(new Call(1));
            // 打完：過到底
            while (_s.Phase == HandPhase.Betting)
            {
                var la = Engine.GetLegalActions(_s, _s.ToAct);
                Do(la.CanCheck ? (Command)new Check(_s.ToAct) : new Call(_s.ToAct));
            }
            var h = _stats.LastHand;
            Assert.AreEqual(1, h.Reads.Count, "only the bet the player actually answered");
            Assert.AreEqual(2, h.Reads[0].Seat);
            Assert.IsTrue(h.Reads[0].Correct, "folding to a value raise is a good read");
            Assert.IsFalse(h.PlayerWon);

            // 下一手：玩家跟注對方的價值注 → 讀錯
            Do(new StartHand(78, null, 5, 10));
            Do(new Call(1)); Do(new Call(2)); Do(new Check(0)); // 按鈕輪到 1；這手 1 是按鈕
            Assert.AreEqual(Street.Flop, _s.Street);
            int first = _s.ToAct;
            Assert.AreNotEqual(0, first);
            Do(new Bet(first, 30));
            Reveal(first, Intent.Marginal, Intent.Value);
            while (_s.ToAct != 0) Do(new Call(_s.ToAct));
            Do(new Call(0));
            while (_s.Phase == HandPhase.Betting)
            {
                var la = Engine.GetLegalActions(_s, _s.ToAct);
                Do(la.CanCheck ? (Command)new Check(_s.ToAct) : new Call(_s.ToAct));
            }
            Assert.AreEqual(1, _stats.LastHand.Reads.Count);
            Assert.IsFalse(_stats.LastHand.Reads[0].Correct);
        }

        [Test]
        public void Summary_CountsHandsWins_Reads_BiggestPot_AndPlace()
        {
            Start(new[] { 1000, 1000, 1000, 1000 });
            Do(new Call(3)); Do(new Call(0)); Do(new Call(1)); Do(new Check(2));
            Do(new Bet(1, 40)); Do(new Fold(2)); Do(new Fold(3)); Do(new Call(0));
            Do(new Check(1));
            Reveal(1, Intent.Marginal, Intent.Bluff, Intent.GiveUp, Intent.GiveUp);
            Do(new Bet(0, 100)); Do(new Fold(1));
            int won = _stats.LastHand.PotWon;
            // 第二手玩家直接棄牌
            Do(new StartHand(79, null, 5, 10));
            while (_s.ToAct != 0) Do(new Call(_s.ToAct));
            Do(new Fold(0));
            while (_s.Phase == HandPhase.Betting)
            {
                var la = Engine.GetLegalActions(_s, _s.ToAct);
                Do(la.CanCheck ? (Command)new Check(_s.ToAct) : new Call(_s.ToAct));
            }
            var sum = _stats.Summarize(_s.Stacks(), GameEndReason.HandLimit);
            Assert.AreEqual(2, sum.HandsPlayed);
            Assert.AreEqual(1, sum.HandsWon);
            Assert.AreEqual(1, sum.ReadsCorrect);
            Assert.AreEqual(1, sum.ReadsTotal);
            Assert.AreEqual(won, sum.BiggestPotWon);
            Assert.AreEqual(1, sum.BiggestPotHand);
            Assert.AreEqual(MatchSchedule.PlaceOf(_s.Stacks(), 0), sum.Place);
            Assert.AreEqual(sum.Place == 1, sum.Won);
            Assert.AreEqual(GameEndReason.HandLimit, sum.Reason);
            // 2026-11-04 使用者：「Tells read」實機永遠 0（定義是棄掉價值注，不是讀到肢體訊號），看板不顯示；計數照留
            Assert.AreEqual(new[] { "Hands won: 1 of 2", "Biggest pot: " + won + " (hand 1)" }, sum.Lines(), "two short lines on the table");
            Assert.IsFalse(sum.Lines().Any(l => l.Contains("Tells")), "no Tells read line");
        }
    }
}
