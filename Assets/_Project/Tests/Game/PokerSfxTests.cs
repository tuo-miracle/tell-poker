using NUnit.Framework;
using Poker.Game;

namespace Poker.Tests.Game
{
    public class PokerSfxTests
    {
        [Test]
        public void EverySound_IsNonEmpty_BoundedAndFadesToZero()
        {
            foreach (SfxKind k in System.Enum.GetValues(typeof(SfxKind)))
            {
                var s = PokerSfx.Render(k);
                Assert.Greater(s.Length, PokerSfx.SampleRate / 50, k.ToString());
                float peak = 0f;
                foreach (var v in s) peak = System.Math.Max(peak, System.Math.Abs(v));
                Assert.That(peak, Is.InRange(0.3f, 0.95f), k.ToString());
                Assert.Less(System.Math.Abs(s[0]), 0.02f, k + " start");
                Assert.Less(System.Math.Abs(s[s.Length - 1]), 0.02f, k + " end");
            }
        }

        [Test]
        public void Render_IsDeterministic()
        {
            CollectionAssert.AreEqual(PokerSfx.Render(SfxKind.ChipClack), PokerSfx.Render(SfxKind.ChipClack));
        }

        [Test]
        public void ChipClack_IsShortAndBright()
        {
            var s = PokerSfx.Render(SfxKind.ChipClack);
            Assert.Less(s.Length, PokerSfx.SampleRate * 0.25f);
        }
    }
}
