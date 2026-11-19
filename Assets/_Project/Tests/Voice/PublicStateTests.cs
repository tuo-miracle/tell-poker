using System.Collections.Generic;
using NUnit.Framework;
using Poker.Core;
using Poker.Game;
using Poker.Voice;

namespace Poker.Tests.Voice
{
    /// <summary>送給語言模型的牌局狀態：只有公開資訊，任何人的底牌都不能出現（每一街、攤牌、一手結束後都檢查）。</summary>
    public class PublicStateTests
    {
        GameSession _s;
        VoiceActionLog _log;
        string[] _names;

        [SetUp] public void SetUp()
        {
            _s = new GameSession(new GameConfig { MonteCarloTrials = 20, Seed = 777 });
            _names = new string[_s.SeatCount];
            for (int i = 0; i < _s.SeatCount; i++) _names[i] = i == _s.HumanSeat ? "you" : _s.NameOf(i).ToLowerInvariant();
            _log = new VoiceActionLog();
        }

        string Json() => PublicTableState.From(_s.State, _names, _s.HumanSeat, _log.Actions).ToJson();

        void AssertNoHoleCards(string json, string when)
        {
            StringAssert.DoesNotContain("hole", json.ToLowerInvariant(), when);
            for (int seat = 0; seat < _s.SeatCount; seat++)
                foreach (var c in _s.HoleOf(seat))
                    StringAssert.DoesNotContain("\"" + c + "\"", json, $"{when}: seat {seat} hole card {c} leaked");
        }

        void Observe(IReadOnlyList<GameEvent> events) => _log.Observe(events, _names);

        /// <summary>每個人都過牌或跟注，一路打到攤牌；每一步都檢查。</summary>
        [Test] public void NoHoleCardsOnAnyStreetIncludingShowdown()
        {
            Observe(_s.StartHand());
            var streets = new HashSet<string>();
            int guard = 0;
            while (!_s.IsHandOver && guard++ < 100)
            {
                var json = Json();
                streets.Add(_s.State.Street.ToString());
                AssertNoHoleCards(json, "street " + _s.State.Street);
                int seat = _s.State.ToAct;
                var la = _s.Legal(seat);
                var r = _s.Apply(la.CanCheck ? (PlayerCommand)new Check(seat) : new Call(seat));
                Assert.IsTrue(r.IsSuccess, "" + r.Error);
                Observe(r.Events);
            }
            Assert.IsTrue(_s.IsHandOver);
            CollectionAssert.IsSupersetOf(streets, new[] { "Preflop", "Flop", "Turn", "River" });
            // 攤牌事件（ShowdownRevealed 含所有亮牌）已經被動作紀錄看過，狀態仍不能帶底牌
            AssertNoHoleCards(Json(), "after showdown");
            StringAssert.Contains("\"phase\":\"between_hands\"", Json());
        }

        [Test] public void PreflopFieldsAreFilledFromCore()
        {
            Observe(_s.StartHand());
            var st = PublicTableState.From(_s.State, _names, _s.HumanSeat, _log.Actions);
            Assert.AreEqual("preflop", st.Street);
            Assert.AreEqual("betting", st.Phase);
            Assert.AreEqual(_s.State.SmallBlind, st.SmallBlind);
            Assert.AreEqual(_s.State.BigBlind, st.BigBlind);
            Assert.AreEqual(_s.Pot, st.Pot);
            Assert.AreEqual(4, st.Seats.Count);
            Assert.AreEqual("you", st.Seats[0].Name);
            Assert.AreEqual(_s.StackOf(0), st.Seats[0].Stack);
            var me = _s.State.Seat(0);
            Assert.AreEqual(System.Math.Min(me.Stack, System.Math.Max(0, _s.State.CurrentBet - me.StreetBet)), st.YouToCall);
            Assert.AreEqual(_names[_s.State.ToAct], st.ToAct);
            Assert.IsEmpty(st.Board);
            var json = st.ToJson();
            StringAssert.Contains("\"blinds\":{\"small\":" + _s.State.SmallBlind + ",\"big\":" + _s.State.BigBlind + "}", json);
            AssertNoHoleCards(json, "preflop");
        }

        [Test] public void FoldedSeatsAndActionsAreListed()
        {
            Observe(_s.StartHand());
            int seat = _s.State.ToAct;
            var r = _s.Apply(new Fold(seat));
            Observe(r.Events);
            var st = PublicTableState.From(_s.State, _names, _s.HumanSeat, _log.Actions);
            Assert.AreEqual("folded", st.Seats[seat].Status);
            Assert.AreEqual(3, st.Actions.Count, "小盲、大盲、棄牌");
            Assert.AreEqual("small_blind", st.Actions[0].Action);
            Assert.AreEqual(_s.State.SmallBlind, st.Actions[0].Amount);
            Assert.AreEqual("big_blind", st.Actions[1].Action);
            var last = st.Actions[2];
            Assert.AreEqual(_names[seat], last.Who);
            Assert.AreEqual("fold", last.Action);
            Assert.AreEqual("preflop", last.Street);
        }

        [Test] public void NewHandClearsActionLog()
        {
            Observe(_s.StartHand());
            int seat = _s.State.ToAct;
            Observe(_s.Apply(new Fold(seat)).Events);
            while (!_s.IsHandOver)
            {
                int t = _s.State.ToAct;
                Observe(_s.Apply(new Fold(t)).Events);
            }
            Observe(_s.StartHand());
            Assert.AreEqual(2, _log.Actions.Count, "新的一手只剩兩個盲注");
            foreach (var a in _log.Actions) StringAssert.EndsWith("_blind", a.Action);
        }

        [Test] public void BoardUsesShortCardNames()
        {
            Observe(_s.StartHand());
            while (_s.State.Street == Street.Preflop && !_s.IsHandOver)
            {
                int seat = _s.State.ToAct;
                var la = _s.Legal(seat);
                Observe(_s.Apply(la.CanCheck ? (PlayerCommand)new Check(seat) : new Call(seat)).Events);
            }
            var st = PublicTableState.From(_s.State, _names, _s.HumanSeat, _log.Actions);
            Assert.AreEqual(3, st.Board.Count);
            foreach (var c in st.Board) StringAssert.IsMatch("^[2-9TJQKA][cdhs]$", c);
        }
    }
}
