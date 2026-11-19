using System.Linq;
using System.Numerics;
using NUnit.Framework;
using Poker.Gestures;

namespace Poker.Tests
{
    /// <summary>
    /// 2026-10-12 第二次實機：開發者要 All-in 更好觸發。雙手只推 8–9 cm、指尖剛過下注線也要算；
    /// 單手推、雙手推不到下注線、雙手只往前挪一點仍然不算。
    /// </summary>
    public class AllInSoftTests
    {
        // 雙手先靜止放在起點（手掌 z = endTip − 0.10 − 推的距離），再一起往前推；推完時中指指尖（手掌前 10 cm）在 endTip。
        // 不用 GestureScripts.AllIn：它從桌邊把手移過來的那段本身就是往前移動，反例會被那段觸發。
        static SyntheticHands Push(float left, float right, float endTip, double duration = 0.6, float gap = 0.18f)
        {
            var s = new SyntheticHands().WithNoise(0.01f);
            float z = endTip - 0.10f - System.Math.Max(left, right);
            var l = new Vector3(0.05f - gap / 2, 0.03f, z);
            var r = new Vector3(0.05f + gap / 2, 0.03f, z);
            s.Set(Hand.Left, l);
            s.Set(Hand.Right, r);
            s.Wait(0.8);
            s.Move(duration, HandTarget.At(r + new Vector3(0, 0, right)), HandTarget.At(l + new Vector3(0, 0, left)));
            s.Wait(1.4);
            return s;
        }

        static SyntheticHands Soft(float distance, float endTip, double duration = 0.6, float gap = 0.18f) => Push(distance, distance, endTip, duration, gap);

        [Test]
        public void Hands_only_7cm_apart_can_push_all_in()
        {
            // 實機 log 雙手常靠在一起（手掌距 1–10 cm，多在桌緣後方休息）；推的途中雙手也會往中間收攏
            var ev = G.Run(Soft(0.12f, 0.36f, gap: 0.07f));
            Assert.That(ev.Count(GestureKind.AllInConfirmed), Is.EqualTo(1), ev.Dump());
        }

        [Test]
        public void Two_hands_pushing_only_8_5cm_with_fingers_past_the_bet_line_is_all_in()
        {
            var ev = G.Run(Soft(0.085f, 0.32f));
            Assert.That(ev.Count(GestureKind.AllInPushStarted), Is.EqualTo(1), ev.Dump());
            Assert.That(ev.Count(GestureKind.AllInConfirmed), Is.EqualTo(1), ev.Dump());
            Assert.That(ev.Count(GestureKind.FoldPushed), Is.EqualTo(0), ev.Dump());
        }

        [Test]
        public void A_slower_soft_push_still_counts()
        {
            // 9 cm 花 0.8 秒（平滑加減速：中段 0.5 秒只走 7.3 cm）
            var ev = G.Run(Soft(0.09f, 0.32f, duration: 0.8));
            Assert.That(ev.Count(GestureKind.AllInConfirmed), Is.EqualTo(1), ev.Dump());
        }

        [Test]
        public void Two_hands_pushing_9cm_but_stopping_short_of_the_bet_line_is_not_all_in()
        {
            // 指尖停在 z 0.25（下注線前 5 cm）
            var ev = G.Run(Soft(0.09f, 0.25f));
            Assert.That(ev.Count(GestureKind.AllInConfirmed), Is.EqualTo(0), ev.Dump());
        }

        [Test]
        public void Two_hands_creeping_forward_5cm_does_not_even_start()
        {
            var ev = G.Run(Soft(0.05f, 0.33f));
            Assert.That(ev.Count(GestureKind.AllInPushStarted), Is.EqualTo(0), ev.Dump());
            Assert.That(ev.Count(GestureKind.AllInConfirmed), Is.EqualTo(0), ev.Dump());
        }

        [Test]
        public void One_hand_pushing_9cm_past_the_line_is_not_all_in()
        {
            var s = Push(0f, 0.09f, 0.32f);
            var ev = G.Run(s);
            Assert.That(ev.Count(GestureKind.AllInPushStarted) + ev.Count(GestureKind.AllInConfirmed), Is.EqualTo(0), ev.Dump());
        }
    }
}
