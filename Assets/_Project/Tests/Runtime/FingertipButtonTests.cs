using NUnit.Framework;
using Poker.Runtime;
using UnityEngine;

namespace Poker.Tests.Runtime
{
    public class FingertipButtonTests
    {
        // 按鈕在牌桌座標 (0.3, 0, 0.1)，8 × 6 cm
        static FingertipButton Make() => new FingertipButton(new Vector3(0.3f, 0f, 0.1f), new Vector2(0.04f, 0.03f));

        [Test]
        public void PressFromAbove_FiresOnce()
        {
            var b = Make();
            Assert.That(b.Step(new Vector3(0.3f, 0.06f, 0.1f), 0.00), Is.False);   // 懸停在上方
            Assert.That(b.Step(new Vector3(0.3f, 0.01f, 0.1f), 0.05), Is.True);    // 往下按
            Assert.That(b.Step(new Vector3(0.3f, 0.005f, 0.1f), 0.10), Is.False);  // 按住不重複
        }

        [Test]
        public void SlidingInAtTableHeight_DoesNotFire()
        {
            var b = Make();
            Assert.That(b.Step(new Vector3(0.1f, 0.01f, 0.1f), 0.0), Is.False);
            Assert.That(b.Step(new Vector3(0.3f, 0.01f, 0.1f), 0.1), Is.False, "沒有從上方進入，不算按");
        }

        [Test]
        public void MustLiftBeforeSecondPress_AndRespectsCooldown()
        {
            var b = Make();
            b.Step(new Vector3(0.3f, 0.06f, 0.1f), 0.0);
            Assert.That(b.Step(new Vector3(0.3f, 0.01f, 0.1f), 0.1), Is.True);
            b.Step(new Vector3(0.3f, 0.06f, 0.1f), 0.2);
            Assert.That(b.Step(new Vector3(0.3f, 0.01f, 0.1f), 0.3), Is.False, "冷卻中");
            b.Step(new Vector3(0.3f, 0.06f, 0.1f), 1.0);
            Assert.That(b.Step(new Vector3(0.3f, 0.01f, 0.1f), 1.1), Is.True);
        }

        [Test]
        public void LostTracking_Disarms()
        {
            var b = Make();
            b.Step(new Vector3(0.3f, 0.06f, 0.1f), 0.0);
            b.Step(null, 0.05);
            Assert.That(b.Step(new Vector3(0.3f, 0.01f, 0.1f), 0.1), Is.False);
        }

        [Test]
        public void OutsideFootprint_DoesNotFire()
        {
            var b = Make();
            b.Step(new Vector3(0.4f, 0.06f, 0.1f), 0.0);
            Assert.That(b.Step(new Vector3(0.4f, 0.01f, 0.1f), 0.1), Is.False);
        }

        /// <summary>手勢備援按鈕（2026-10-14）：要在按鈕上方懸停滿 ArmDwell 秒才上膛，手從上面直接落下（放手、敲桌）不算按。</summary>
        [Test]
        public void ArmDwell_QuickDropDoesNotFire_HoverThenPressDoes()
        {
            var b = Make();
            b.ArmDwell = 0.25;
            b.Step(new Vector3(0.3f, 0.08f, 0.1f), 0.00);
            b.Step(new Vector3(0.3f, 0.05f, 0.1f), 0.10);
            Assert.That(b.IsReady, Is.False, "懸停還不夠久");
            Assert.That(b.Step(new Vector3(0.3f, 0.01f, 0.1f), 0.15), Is.False, "0.15 s 就落下：不算");
            // 抬起、懸停夠久再按
            b.Step(new Vector3(0.3f, 0.06f, 0.1f), 0.40);
            b.Step(new Vector3(0.3f, 0.06f, 0.1f), 0.70);
            Assert.That(b.IsReady, Is.True, "懸停滿 0.25 s：上膛（顯示用）");
            Assert.That(b.Step(new Vector3(0.3f, 0.01f, 0.1f), 0.80), Is.True);
        }

        [Test]
        public void ArmDwell_LeavingTheFootprintRestartsTheDwell()
        {
            var b = Make();
            b.ArmDwell = 0.25;
            b.Step(new Vector3(0.3f, 0.06f, 0.1f), 0.00);
            b.Step(new Vector3(0.3f, 0.06f, 0.1f), 0.20);
            b.Step(new Vector3(0.5f, 0.06f, 0.1f), 0.22);   // 滑出去
            b.Step(new Vector3(0.3f, 0.06f, 0.1f), 0.30);
            Assert.That(b.Step(new Vector3(0.3f, 0.01f, 0.1f), 0.40), Is.False, "重新計時：只懸停了 0.1 s");
        }
    }
}
