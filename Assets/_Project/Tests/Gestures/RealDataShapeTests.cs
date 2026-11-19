using System;
using System.Linq;
using System.Numerics;
using NUnit.Framework;
using Poker.Gestures;

namespace Poker.Tests
{
    /// <summary>
    /// 依 Quest 3 真實錄製（2026-10-12，v1＋v2）修正的動作形態：
    /// 翻牌時手掌會被帶離牌 8–13 cm、手指在牌上而手掌在牌前；推牌常斜推 30–37°；
    /// 推的時候手指自然彎曲，捏合強度到 0.6–0.7；All-in 手掌最高到 10.7 cm；
    /// 咪牌時指尖壓在牌上、手腕抬高，掌心角度掉到 90–115°。
    /// </summary>
    public class RealDataShapeTests
    {
        const float Rest = 0.025f;
        static TableRect Rect => G.Layout.CardsRect;
        static TableContext Showdown => G.Ctx(phase: TablePhase.Showdown, canCheck: false);

        // ── 翻牌 ──
        [Test]
        public void Flip_that_carries_the_palm_off_the_cards_still_flips()
        {
            var s = new SyntheticHands();
            var p = G.Cards + new Vector3(0, 0.05f, 0);
            s.Move(Hand.Right, 0.5, HandTarget.At(p));
            s.Wait(0.3);
            s.Move(Hand.Right, 0.35, HandTarget.At(p + new Vector3(0.16f, 0.03f, 0.16f)).WithRoll(180));
            s.Wait(0.4);
            var ev = G.Run(s, Showdown);
            Assert.That(ev.Count(GestureKind.ShowdownFlip), Is.EqualTo(1), ev.Dump());
        }

        [Test]
        public void Flip_with_fingertips_on_the_cards_and_palm_in_front_counts()
        {
            var at = new Vector3(0, 0, Rect.MinZ - 0.08f);
            var ev = G.Run(GestureScripts.Flip(new SyntheticHands(), Hand.Right, at: at), Showdown);
            Assert.That(ev.Count(GestureKind.ShowdownFlip), Is.EqualTo(1), ev.Dump());
        }

        // ── 推牌棄牌 ──
        static SyntheticHands PushFromCards(Vector3 delta, SyntheticHands s = null, double duration = 0.5)
        {
            s = s ?? new SyntheticHands();
            var start = G.Cards + new Vector3(0, Rest, 0);
            s.Move(Hand.Right, 0.5, HandTarget.At(start + new Vector3(0, 0.06f, 0)));
            s.Move(Hand.Right, 0.3, HandTarget.At(start));
            s.Wait(0.3);
            s.Move(Hand.Right, duration, HandTarget.At(start + delta));
            s.Wait(0.3);
            return s;
        }

        [Test]
        public void Diagonal_push_at_35_degrees_folds()
        {
            var ev = G.Run(PushFromCards(new Vector3(0.21f, 0, 0.30f)));
            Assert.That(ev.Count(GestureKind.FoldPushed), Is.EqualTo(1), ev.Dump());
        }

        [Test]
        public void Sideways_sweep_at_60_degrees_does_not_fold() =>
            Assert.That(G.Run(PushFromCards(new Vector3(0.30f, 0, 0.17f))).Count(GestureKind.FoldPushed), Is.Zero);

        [Test]
        public void Push_with_curled_fingers_reading_pinch_0_65_still_folds()
        {
            var s = new SyntheticHands().WithPinchCap().Set(Hand.Right, SyntheticHands.RightRest, pinch: 0.93f);
            var ev = G.Run(PushFromCards(new Vector3(0, 0, 0.20f), s));
            Assert.That(ev.Count(GestureKind.FoldPushed), Is.EqualTo(1), ev.Dump());
        }

        [Test]
        public void Push_with_fingertips_on_the_table_and_palm_raised_folds()
        {
            // foldpush_v2：指尖貼桌（中位數 1–5 cm）、手掌抬高 5–9 cm，從牌的右前角往前推
            float pitch = 35f;
            float palmY = Rest + 0.10f * (float)Math.Sin(pitch * Math.PI / 180);
            var start = new Vector3(Rect.MaxX + 0.05f, palmY, Rect.MinZ - 0.07f);
            var s = new SyntheticHands();
            s.Move(Hand.Right, 0.5, HandTarget.At(start + new Vector3(0, 0.05f, 0)).WithPitch(pitch));
            s.Move(Hand.Right, 0.3, HandTarget.At(start));
            s.Wait(0.3);
            s.Move(Hand.Right, 0.6, HandTarget.At(start + new Vector3(0.04f, 0, 0.25f)));
            s.Wait(0.3);
            var ev = G.Run(s);
            Assert.That(ev.Count(GestureKind.FoldPushed), Is.EqualTo(1), ev.Dump());
        }

        /// <summary>foldpush_v2：手壓在牌上 → 抬起（手掌 12–22 cm）往右前方 → 在 (0.20, 0.30) 放下，旁邊就是遊戲畫的棄牌堆。</summary>
        static SyntheticHands CarryToMuck(bool setDown)
        {
            var s = new SyntheticHands();
            var on = G.Cards + new Vector3(0, Rest, -0.03f);
            s.Move(Hand.Right, 0.5, HandTarget.At(on + new Vector3(0, 0.05f, 0)));
            s.Move(Hand.Right, 0.25, HandTarget.At(on));
            s.Wait(0.25);
            s.Move(Hand.Right, 0.3, HandTarget.At(new Vector3(0.12f, 0.18f, 0.20f)));
            s.Move(Hand.Right, 0.25, HandTarget.At(new Vector3(0.20f, setDown ? Rest : 0.15f, 0.30f)));
            s.Wait(0.4);
            return s;
        }

        [Test]
        public void Carrying_the_cards_to_the_muck_and_setting_them_down_folds()
        {
            var ev = G.Run(CarryToMuck(setDown: true));
            Assert.That(ev.Count(GestureKind.FoldPushed), Is.EqualTo(1), ev.Dump());
        }

        [Test]
        public void Lifting_off_the_cards_without_setting_down_does_not_fold() =>
            Assert.That(G.Run(CarryToMuck(setDown: false)).Count(GestureKind.FoldPushed), Is.Zero);

        [Test]
        public void Lifting_off_the_cards_and_setting_down_away_from_the_muck_does_not_fold()
        {
            // 雜動：手放在牌上，抬起移到別處（左前方，越過棄牌線）放下 → 不是棄牌
            var s = new SyntheticHands();
            var on = G.Cards + new Vector3(0, Rest, -0.03f);
            s.Move(Hand.Right, 0.5, HandTarget.At(on + new Vector3(0, 0.05f, 0)));
            s.Move(Hand.Right, 0.25, HandTarget.At(on));
            s.Wait(0.25);
            s.Move(Hand.Right, 0.3, HandTarget.At(new Vector3(-0.05f, 0.18f, 0.22f)));
            s.Move(Hand.Right, 0.25, HandTarget.At(new Vector3(-0.12f, Rest, 0.34f)));
            s.Wait(0.4);
            Assert.That(G.Run(s).Count(GestureKind.FoldPushed), Is.Zero);
        }

        [Test]
        public void Reaching_forward_from_beside_the_cards_does_not_fold()
        {
            var s = new SyntheticHands();
            var beside = new Vector3(0.24f, Rest, 0.05f);
            s.Move(Hand.Right, 0.5, HandTarget.At(beside));
            s.Wait(0.3);
            s.Move(Hand.Right, 0.3, HandTarget.At(new Vector3(0.22f, 0.15f, 0.20f)));
            s.Move(Hand.Right, 0.25, HandTarget.At(new Vector3(0.20f, Rest, 0.32f)));
            s.Wait(0.4);
            Assert.That(G.Run(s).Count(GestureKind.FoldPushed), Is.Zero);
        }

        // ── All-in ──
        [Test]
        public void All_in_with_curled_fingers_still_confirms()
        {
            var s = new SyntheticHands().WithPinchCap()
                .Set(Hand.Right, SyntheticHands.RightRest, pinch: 0.93f)
                .Set(Hand.Left, SyntheticHands.LeftRest, pinch: 0.93f);
            var ev = G.Run(GestureScripts.AllIn(s));
            Assert.That(ev.Count(GestureKind.AllInConfirmed), Is.EqualTo(1), ev.Dump());
            Assert.That(ev.Count(GestureKind.ChipsPicked), Is.Zero, ev.Dump());
        }

        [Test]
        public void All_in_whose_palms_rise_to_12cm_during_the_push_confirms()
        {
            // allin_v2：在籌碼堆兩側就位時手掌低，推的途中手掌升到 8–14 cm
            var s = new SyntheticHands();
            var c = G.Stack + new Vector3(0, 0.03f, -0.01f);
            var l = c - new Vector3(0.09f, 0, 0);
            var r = c + new Vector3(0.09f, 0, 0);
            s.Move(0.5, HandTarget.At(r + new Vector3(0, 0.05f, 0)), HandTarget.At(l + new Vector3(0, 0.05f, 0)));
            s.Move(0.25, HandTarget.At(r), HandTarget.At(l));
            s.Wait(0.3);
            var fwd = new Vector3(0, 0.09f, 0.26f);
            s.Move(0.55, HandTarget.At(r + fwd), HandTarget.At(l + fwd));
            s.Wait(1.4);
            var ev = G.Run(s);
            Assert.That(ev.Count(GestureKind.AllInConfirmed), Is.EqualTo(1), ev.Dump());
        }

        [Test]
        public void All_in_followed_by_lifting_the_hands_away_still_confirms()
        {
            // allin_v2：推過線 0.2–0.3 s 後就把手抬起收回 8–9 cm（手掌 9–13 cm 高），不是要撤銷
            var s = GestureScripts.AllIn(new SyntheticHands(), holdAfter: 0.25);
            var back = new Vector3(0, 0.08f, -0.25f);
            s.Move(0.35, HandTarget.At(G.Stack + new Vector3(0.09f, 0.03f, 0.25f) + back), HandTarget.At(G.Stack + new Vector3(-0.09f, 0.03f, 0.25f) + back));
            s.Wait(1.0);
            var ev = G.Run(s);
            Assert.That(ev.Count(GestureKind.AllInConfirmed), Is.EqualTo(1), ev.Dump());
        }

        // ── 咪牌 ──
        /// <summary>真人咪牌：手掌在牌前、指尖蓋在牌上，抬起手腕（俯仰 pitch 度）時指尖仍壓著牌。</summary>
        static SyntheticHands NaturalPeek(out double liftStart, float pitch = 50f, double lift = 0.4, double hold = 0.6)
        {
            var s = new SyntheticHands();
            var flat = new Vector3(0, Rest, Rect.MinZ - 0.05f);
            s.Move(Hand.Right, 0.5, HandTarget.At(flat + new Vector3(0, 0.05f, 0)));
            s.Move(Hand.Right, 0.25, HandTarget.At(flat));
            s.Wait(0.2);
            liftStart = s.Time;
            // 指尖（手掌前方 10 cm）壓著桌面：俯仰時手掌要跟著抬高
            float rise = 0.10f * (float)Math.Sin(pitch * Math.PI / 180);
            s.Move(Hand.Right, lift, HandTarget.At(flat + new Vector3(0, rise, 0)).WithPitch(pitch));
            s.Wait(hold);
            return s;
        }

        [Test]
        public void Natural_peek_with_fingertips_on_cards_lifts_the_corner_past_0_6_quickly()
        {
            var s = NaturalPeek(out double liftStart);
            var ev = G.Run(s).Where(e => e.Kind == GestureKind.PeekChanged && e.Hand == Hand.Right).ToList();
            Assert.That(ev.Count, Is.GreaterThan(0));
            Assert.That(ev.Max(e => e.Amount), Is.GreaterThanOrEqualTo(0.6f), ev.Dump());
            var first = ev.First(e => e.Amount >= 0.5f);
            Assert.That(first.Time - liftStart, Is.LessThanOrEqualTo(0.5), "抬手腕後 0.5 s 內牌角要過半");
        }

        [Test]
        public void Flat_hand_hovering_above_the_cards_does_not_lift_the_corner()
        {
            var s = new SyntheticHands();
            s.Move(Hand.Right, 0.5, HandTarget.At(G.Cards + new Vector3(0, 0.10f, 0)));
            s.Wait(1.0);
            var ev = G.Run(s).Where(e => e.Kind == GestureKind.PeekChanged).ToList();
            Assert.That(ev.Count == 0 || ev.Max(e => e.Amount) < 0.2f, Is.True, ev.Dump());
        }
    }
}
