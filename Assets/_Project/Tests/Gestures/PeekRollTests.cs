using System.Linq;
using System.Numerics;
using NUnit.Framework;
using Poker.Gestures;

namespace Poker.Tests
{
    /// <summary>
    /// 2026-10-13 實機 log 3646.9–3648.5：開發者把手放在自己的牌上、翻腕把牌邊掀起來看（掌心轉到朝上 39–53°、手掌 4.6–7.4 cm 高、
    /// 指尖 2.9–4.8 cm），被判「palm not down」。掌心朝下在牌上就位（咪牌已進入）後翻腕也算咪牌，翻越多牌角掀越高；
    /// 沒先在牌上掌心朝下就位、掌心朝上放著的手不算。攤牌時翻牌照常亮牌，其他時候只咪牌。
    /// </summary>
    public class PeekRollTests
    {
        static readonly Vector3 OverCards = GestureScripts.RollPeekSpot;
        static SyntheticHands RollPeek(SyntheticHands s, Hand h = Hand.Right, float roll = 135f, double duration = 0.5, double hold = 0.6, float lift = 0.025f) =>
            GestureScripts.RollPeek(s, h, roll, duration, hold, lift);

        static float MaxPeek(System.Collections.Generic.List<GestureEvent> ev, Hand h) =>
            ev.Where(e => e.Kind == GestureKind.PeekChanged && e.Hand == h).Select(e => e.Amount).DefaultIfEmpty(0f).Max();

        [Test]
        public void Turning_the_palm_up_over_own_cards_peeks([Values(Hand.Right, Hand.Left)] Hand h)
        {
            var ev = G.Run(RollPeek(new SyntheticHands(seed: 31).WithNoise(0.01f).WithPinchCap(), h));
            Assert.That(MaxPeek(ev, h), Is.GreaterThanOrEqualTo(0.6f), ev.Dump());
            Assert.That(ev.Where(e => e.Kind == GestureKind.PeekChanged && e.Hand == h).Last().Amount, Is.EqualTo(0f), "離開後放平");
            Assert.That(ev.AnyAction(), Is.False, "不在攤牌：只咪牌，不亮牌、不棄牌\n" + ev.Dump());
        }

        [Test]
        public void Half_turn_peeks_less_than_full_turn()
        {
            float half = MaxPeek(G.Run(RollPeek(new SyntheticHands(), roll: 100f)), Hand.Right);
            float full = MaxPeek(G.Run(RollPeek(new SyntheticHands(), roll: 135f)), Hand.Right);
            Assert.That(full, Is.GreaterThan(half + 0.2f), $"half {half:0.00} full {full:0.00}");
        }

        [Test]
        public void Palm_up_hand_resting_by_the_cards_does_not_peek()
        {
            // 手一開始就掌心朝上（例：攤開手放在桌上），移到牌旁放著
            var s = new SyntheticHands(seed: 32).WithNoise(0.01f).WithPinchCap();
            s.Set(Hand.Right, SyntheticHands.RightRest + new Vector3(0, 0.03f, 0), roll: 140f);
            s.Move(Hand.Right, 0.6, HandTarget.At(OverCards + new Vector3(0, 0.01f, 0)));
            s.Wait(1.5);
            var ev = G.Run(s);
            Assert.That(MaxPeek(ev, Hand.Right), Is.LessThan(0.3f), ev.Dump());
            Assert.That(ev.AnyAction(), Is.False, ev.Dump());
        }

        [Test]
        public void Showdown_flip_still_reveals()
        {
            var ev = G.Run(GestureScripts.Flip(new SyntheticHands(seed: 33).WithNoise(0.01f)), G.Ctx(phase: TablePhase.Showdown, canCheck: false));
            Assert.That(ev.Count(GestureKind.ShowdownFlip), Is.EqualTo(1), ev.Dump());
        }

        [Test]
        public void Roll_peek_at_showdown_may_flip_but_outside_showdown_never_reveals()
        {
            var ev = G.Run(RollPeek(new SyntheticHands(seed: 34), duration: 0.3), G.Ctx(phase: TablePhase.River));
            Assert.That(ev.Count(GestureKind.ShowdownFlip), Is.Zero, ev.Dump());
            Assert.That(ev.Count(GestureKind.FoldPushed), Is.Zero, ev.Dump());
        }
    }
}
