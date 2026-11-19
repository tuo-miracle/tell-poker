using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Poker.Core;
using Poker.Game;

namespace Poker.Tests.Game
{
    /// <summary>
    /// 2026-11-04 實機錄影（preview-r7）：翻牌圈玩家 all-in、Rocco 跟，結算看板、攤牌牌子、「Rocco read you」三個同時出現，
    /// 之後荷官才說「Fourth card coming up.」「And the river.」「Pot goes to you!」——還沒發完牌就知道輸贏。
    /// 根因：all-in 的一批事件（攤牌、轉牌、河牌、分池、一手結束）動畫只要兩三秒，荷官的話排隊在後面講。
    /// 正確順序：荷官講完每條街、牌發完 → 攤牌牌子／揭曉卡 → 分池（「Pot goes to …」）→ 結算看板。
    /// </summary>
    public class AllInRunoutOrderTests
    {
        GameTestRig _rig;

        [TearDown]
        public void TearDown()
        {
            _rig?.Dispose();
            _rig = null;
        }

        static PlayerCommand Passive(HandState st) =>
            Engine.GetLegalActions(st, st.ToAct).CanCheck ? (PlayerCommand)new Check(st.ToAct) : new Call(st.ToAct);

        [Test]
        public void AllInOnTheFlop_DealerCallsTheRunout_BeforeAnyResultShows()
        {
            var started = new List<(int frame, string text)>();
            _rig = new GameTestRig(new GameConfig { Seed = 41, MonteCarloTrials = 40, Opponents = GameConfig.HeadsUp }, g =>
            {
                g.AiOverride = Passive;
                g.AutoPlayer = st => st.Street == Street.Flop ? new AllIn(0) : Passive(st);
            });
            var g = _rig.Game;
            int frame = 0;
            g.Dealer.Speech.Started += u => started.Add((frame + 1, u.Text));   // 正在跑的是第 frame+1 步（條件在每步之後才數）
            int headlineAt = -1, revealAt = -1, summaryAt = -1, boardDoneAt = -1;
            Assert.IsTrue(_rig.IdleUntil(() =>
            {
                frame++;
                if (boardDoneAt < 0 && g.Props.Board.Count(c => c.gameObject.activeSelf && c.FaceUp) == 5) boardDoneAt = frame;
                if (headlineAt < 0 && !string.IsNullOrEmpty(g.RevealHeadline)) headlineAt = frame;
                if (revealAt < 0 && g.ReadReveal != null) revealAt = frame;
                if (summaryAt < 0 && g.Props.SummaryVisible) summaryAt = frame;
                return summaryAt >= 0;
            }, 60), "game over summary\n" + _rig.Dump());

            string Log() => string.Join(" | ", started.Select(s => s.frame + ":" + s.text)) +
                            $"\nboard5@{boardDoneAt} headline@{headlineAt} reveal@{revealAt} summary@{summaryAt}";
            int StartOf(string trigger)
            {
                var lines = Poker.AI.LineBook.ForDealer((Poker.AI.LineTrigger)System.Enum.Parse(typeof(Poker.AI.LineTrigger), trigger)).Select(l => l.Text).ToList();
                var hit = started.FirstOrDefault(s => lines.Contains(s.text));
                return hit.text == null ? -1 : hit.frame;
            }
            int turn = StartOf("DealerTurn"), river = StartOf("DealerRiver");
            int pot = started.FirstOrDefault(s => s.text.StartsWith("Pot goes to") || s.text.Contains("split")).frame;
            Assert.Greater(turn, 0, "dealer called the turn\n" + Log());
            Assert.Greater(river, turn, "then the river\n" + Log());
            Assert.Greater(boardDoneAt, river, "the river card comes out after the dealer calls it\n" + Log());
            Assert.Greater(headlineAt, boardDoneAt, "showdown labels only after the whole board is out\n" + Log());
            if (revealAt >= 0) Assert.Greater(revealAt, boardDoneAt, "read reveal only after the board\n" + Log());
            Assert.Greater(pot, 0, "dealer announced the pot\n" + Log());
            Assert.GreaterOrEqual(pot, headlineAt, "pot announced with or after the labels, never before\n" + Log());
            Assert.Greater(summaryAt, pot, "summary is the very last thing\n" + Log());
            if (revealAt >= 0) Assert.Greater(summaryAt, revealAt, Log());
        }
    }
}
