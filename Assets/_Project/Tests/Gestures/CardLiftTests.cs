using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using NUnit.Framework;
using Poker.Gestures;

namespace Poker.Tests
{
    /// <summary>
    /// 拿起底牌看（2026-10-13 開發者回饋：「除了咪牌，我想把牌拿起來看」）。
    /// 規則：在自己的牌上捏住 → 捏合點抬高 CardLiftHeight 以上 → CardsLifted（牌跟著手）；放開 → CardsReturned（牌蓋回原位）。
    /// 棄牌只有兩種：還沒進入「看牌」就拿到棄牌堆落下（原本的拿牌棄牌），或拿著牌在棄牌堆範圍內、低處放開。
    /// 其他地方放開（包括棄牌堆上方高處）一律把牌放回。看牌不是行動、不論輪到誰都可以。
    /// </summary>
    public class CardLiftTests
    {
        static readonly Vector3 Muck = G.Layout.MuckCenter;

        /// <summary>逐幀跑辨識器，順便記下每幀 HoldingCards 的結果。</summary>
        sealed class Run
        {
            public readonly List<GestureEvent> Events = new List<GestureEvent>();
            public readonly List<(double t, bool held, Vector3 grip, Hand hand)> Held = new List<(double, bool, Vector3, Hand)>();

            public Run(SyntheticHands s, TableContext? ctx = null, System.Func<double, TableContext> ctxAt = null)
            {
                var r = new GestureRecognizer(G.Layout);
                foreach (var f in s.Frames)
                {
                    var c = ctxAt != null ? ctxAt(f.Time) : ctx ?? G.Ctx();
                    Events.AddRange(r.Update(f, c));
                    bool held = r.HoldingCards(out var hand, out var grip, out _);
                    Held.Add((f.Time, held, grip, hand));
                }
            }

            public int Count(GestureKind k) => Events.Count(k);
            public string Dump() => Events.Dump();
        }

        static void AssertLookedAndReturned(Run r, Hand h = Hand.Right)
        {
            Assert.That(r.Count(GestureKind.CardsLifted), Is.EqualTo(1), r.Dump());
            Assert.That(r.Count(GestureKind.CardsReturned), Is.EqualTo(1), r.Dump());
            var lifted = r.Events.First(GestureKind.CardsLifted);
            var back = r.Events.First(GestureKind.CardsReturned);
            Assert.That(lifted.Hand, Is.EqualTo(h));
            Assert.That(back.Time, Is.GreaterThan(lifted.Time));
        }

        [Test]
        public void Pinch_both_cards_lift_toward_the_head_hold_and_release_looks_then_returns_without_folding()
        {
            var s = GestureScripts.LiftCards(new SyntheticHands(), Hand.Right, lookAt: new Vector3(0f, 0.22f, 0.04f), hold: 1.5);
            var r = new Run(s);
            AssertLookedAndReturned(r);
            Assert.That(r.Events.AnyAction(), Is.False, r.Dump());
            Assert.That(r.Count(GestureKind.ChipsPicked), Is.Zero, r.Dump());

            // 拿著的期間每幀都回報握點（牌桌座標）：停住時就在 lookAt 附近
            var lifted = r.Events.First(GestureKind.CardsLifted).Time;
            var back = r.Events.First(GestureKind.CardsReturned).Time;
            var during = r.Held.Where(x => x.t > lifted && x.t < back).ToList();
            Assert.That(during, Is.Not.Empty);
            Assert.That(during.All(x => x.held && x.hand == Hand.Right), Is.True, "held every frame between lift and return");
            var holdFrame = during[during.Count / 2];
            Assert.That(Vector3.Distance(holdFrame.grip, new Vector3(0f, 0.22f, 0.04f)), Is.LessThan(0.04f), holdFrame.grip.ToString());
            Assert.That(r.Held.Where(x => x.t > back + 0.05).All(x => !x.held), Is.True, "not held after release");
            Assert.That(r.Held.Where(x => x.t < lifted).All(x => !x.held), Is.True, "not held before the lift");
        }

        [Test]
        public void Left_hand_can_lift_the_cards_too()
        {
            var r = new Run(GestureScripts.LiftCards(new SyntheticHands(), Hand.Left));
            AssertLookedAndReturned(r, Hand.Left);
            Assert.That(r.Events.AnyAction(), Is.False, r.Dump());
        }

        [Test]
        public void Lifted_event_carries_the_grip_point_and_hand_rotation()
        {
            var r = new Run(GestureScripts.LiftCards(new SyntheticHands()));
            var e = r.Events.First(GestureKind.CardsLifted);
            Assert.That(e.Position.Y, Is.GreaterThanOrEqualTo(new GestureTuning().CardLiftHeight - 0.005f), e.ToString());
            Assert.That(e.Rotation.Length(), Is.EqualTo(1f).Within(1e-3f), "palm rotation, not default(Quaternion)");
        }

        [Test]
        public void Looking_is_allowed_off_turn_and_at_showdown_and_never_flips()
        {
            var off = new Run(GestureScripts.LiftCards(new SyntheticHands()), G.Ctx(turn: false));
            AssertLookedAndReturned(off);
            var sd = new Run(GestureScripts.LiftCards(new SyntheticHands()), G.Ctx(phase: TablePhase.Showdown, canCheck: false));
            AssertLookedAndReturned(sd);
            Assert.That(sd.Events.AnyAction(), Is.False, "holding the cards up at showdown is not a reveal\n" + sd.Dump());
        }

        [Test]
        public void Pick_up_and_carry_low_into_the_muck_folds()
        {
            foreach (var h in new[] { Hand.Right, Hand.Left })
            {
                var r = new Run(GestureScripts.CarryCardsToMuck(new SyntheticHands(), h));
                Assert.That(r.Count(GestureKind.FoldPushed), Is.EqualTo(1), h + "\n" + r.Dump());
                Assert.That(r.Events.Where(e => e.IsAction).All(e => e.Kind == GestureKind.FoldPushed), Is.True, r.Dump());
                // 拿著的牌（若已抬起）在棄牌後結束：之後不再回報拿著
                Assert.That(r.Held.Last().held, Is.False);
                Assert.That(r.Count(GestureKind.CardsReturned), Is.EqualTo(r.Count(GestureKind.CardsLifted)), r.Dump());
            }
        }

        [Test]
        public void Lift_high_then_move_over_the_muck_and_release_high_returns_the_cards()
        {
            // 高處放開＝不是放進棄牌堆；牌回原位。放開後手再落到棄牌堆也不算（牌已經不在手上）。
            // 2026-10-13 起往前低處（≤ CardFoldReleaseMaxHeight 25 cm）放開就算棄牌，所以「高處」改成 30 cm（原本 20 cm）
            var s = new SyntheticHands();
            GestureScripts.GrabCards(s);
            s.Move(Hand.Right, 0.4, HandTarget.At(SyntheticHands.PalmForPinch(Hand.Right, new Vector3(0f, 0.30f, 0.10f))));
            s.Wait(0.4);
            s.Move(Hand.Right, 0.4, HandTarget.At(SyntheticHands.PalmForPinch(Hand.Right, Muck + new Vector3(0f, 0.30f, 0f))));
            s.Move(Hand.Right, 0.12, HandTarget.Pinch(0f));
            s.Move(Hand.Right, 0.2, HandTarget.At(Muck + new Vector3(0f, 0.03f, 0f)));
            s.Wait(0.6);
            var r = new Run(s);
            AssertLookedAndReturned(r);
            Assert.That(r.Count(GestureKind.FoldPushed), Is.Zero, r.Dump());
            Assert.That(r.Events.AnyAction(), Is.False, r.Dump());
        }

        [Test]
        public void After_looking_setting_the_cards_down_low_in_the_muck_and_releasing_folds()
        {
            var r = new Run(GestureScripts.LiftCards(new SyntheticHands(), hold: 1.0, releaseAt: Muck + new Vector3(-0.03f, 0.01f, -0.04f)));
            Assert.That(r.Count(GestureKind.FoldPushed), Is.EqualTo(1), r.Dump());
            Assert.That(r.Count(GestureKind.CardsReturned), Is.EqualTo(1), "hold ends");
            Assert.That(r.Events.First(GestureKind.FoldPushed).Time, Is.LessThanOrEqualTo(r.Events.First(GestureKind.CardsReturned).Time));
        }

        [Test]
        public void Releasing_low_in_the_muck_off_turn_only_returns_the_cards()
        {
            var r = new Run(GestureScripts.LiftCards(new SyntheticHands(), hold: 1.0, releaseAt: Muck + new Vector3(-0.03f, 0.01f, -0.04f)), G.Ctx(turn: false));
            AssertLookedAndReturned(r);
            Assert.That(r.Events.AnyAction(), Is.False, r.Dump());
        }

        [Test]
        public void After_looking_lowering_through_the_muck_without_releasing_does_not_fold()
        {
            // 拿到臉前看了 1 秒（超過 FoldCarryMaxTime＝進入看牌）後手往下掃過棄牌堆、沒放開又抬起：拿牌棄牌那條路徑已關閉
            var s = new SyntheticHands();
            GestureScripts.GrabCards(s);
            s.Move(Hand.Right, 0.4, HandTarget.At(SyntheticHands.PalmForPinch(Hand.Right, new Vector3(0f, 0.18f, 0.03f))));
            s.Wait(1.0);
            s.Move(Hand.Right, 0.3, HandTarget.At(Muck + new Vector3(0f, 0.05f, 0f)));
            s.Move(Hand.Right, 0.3, HandTarget.At(SyntheticHands.PalmForPinch(Hand.Right, new Vector3(0f, 0.20f, 0.04f))));
            s.Wait(0.5);
            s.Move(Hand.Right, 0.12, HandTarget.Pinch(0f));
            s.Wait(0.5);
            var r = new Run(s);
            AssertLookedAndReturned(r);
            Assert.That(r.Count(GestureKind.FoldPushed), Is.Zero, r.Dump());
        }

        [Test]
        public void Quick_lift_above_the_carry_arc_then_dropping_into_the_muck_without_releasing_does_not_fold()
        {
            // 手掌抬到 28 cm（拿牌棄牌的弧線最高 23.5 cm）就算看牌：0.3 s 後落到棄牌堆、沒放開，不棄牌
            var s = new SyntheticHands();
            GestureScripts.GrabCards(s);
            s.Move(Hand.Right, 0.2, HandTarget.At(new Vector3(0.05f, 0.28f, 0.12f)));
            s.Move(Hand.Right, 0.3, HandTarget.At(Muck + new Vector3(0f, 0.05f, 0f)));
            s.Wait(0.3);
            // 抬回臉前（牌前緣之後）才放開：棄牌線外低處放開會算棄牌（2026-10-13 起），這裡只測「沒放開」
            s.Move(Hand.Right, 0.3, HandTarget.At(SyntheticHands.PalmForPinch(Hand.Right, new Vector3(0.05f, 0.20f, 0.08f))));
            s.Move(Hand.Right, 0.12, HandTarget.Pinch(0f));
            s.Wait(0.5);
            var r = new Run(s);
            AssertLookedAndReturned(r);
            Assert.That(r.Count(GestureKind.FoldPushed), Is.Zero, r.Dump());
        }

        [Test]
        public void Putting_the_cards_back_on_their_spot_returns_them()
        {
            var r = new Run(GestureScripts.LiftCards(new SyntheticHands(), hold: 0.8, releaseAt: GestureScripts.CardGrip));
            AssertLookedAndReturned(r);
            Assert.That(r.Events.AnyAction(), Is.False, r.Dump());
        }

        [Test]
        public void Tracking_lost_while_holding_returns_the_cards_after_a_timeout()
        {
            var s = new SyntheticHands();
            GestureScripts.GrabCards(s);
            s.Move(Hand.Right, 0.4, HandTarget.At(SyntheticHands.PalmForPinch(Hand.Right, new Vector3(0f, 0.22f, 0.04f))));
            s.Wait(0.3);
            s.Track(Hand.Right, false).Wait(0.2).Track(Hand.Right, true).Wait(0.3);
            var shortLoss = new Run(s);
            Assert.That(shortLoss.Count(GestureKind.CardsReturned), Is.Zero, "short loss keeps the cards\n" + shortLoss.Dump());
            Assert.That(shortLoss.Held.Last().held, Is.True);

            s.Track(Hand.Right, false).Wait(1.5);
            var longLoss = new Run(s);
            Assert.That(longLoss.Count(GestureKind.CardsReturned), Is.EqualTo(1), longLoss.Dump());
            Assert.That(longLoss.Held.Last().held, Is.False);
        }

        [Test]
        public void Cards_gone_means_nothing_to_pick_up_and_held_cards_go_back()
        {
            var gone = G.Ctx();
            gone.HoleCardsGone = true;
            var none = new Run(GestureScripts.LiftCards(new SyntheticHands()), gone);
            Assert.That(none.Count(GestureKind.CardsLifted), Is.Zero, none.Dump());

            var s = GestureScripts.LiftCards(new SyntheticHands(), hold: 2.0);
            double cut = 1.8;
            var r = new Run(s, ctxAt: t => t < cut ? G.Ctx() : gone);
            Assert.That(r.Count(GestureKind.CardsLifted), Is.EqualTo(1), r.Dump());
            var back = r.Events.First(GestureKind.CardsReturned);
            Assert.That(back.Time, Is.EqualTo(cut).Within(0.05), "returned as soon as the cards are gone");
        }

        // ── 反例：不能變成拿牌 ──

        [Test]
        public void Idle_hands_do_not_lift_cards()
        {
            var r = new Run(new SyntheticHands(seed: 3).WithNoise(0.01f).Wait(3.0));
            Assert.That(r.Count(GestureKind.CardsLifted), Is.Zero, r.Dump());
        }

        [TestCase(0.02f, 35f, 0.6, TestName = "Peek_wrist_lift_is_not_a_card_lift")]
        [TestCase(0.06f, 30f, 0.8, TestName = "Peek_whole_hand_lift_is_not_a_card_lift")]
        public void Peeking_is_not_lifting(float lift, float pitch, double hold)
        {
            var r = new Run(GestureScripts.Peek(new SyntheticHands(), Hand.Right, pitch: pitch, lift: lift, hold: hold));
            Assert.That(r.Count(GestureKind.CardsLifted), Is.Zero, r.Dump());
            Assert.That(r.Events.Count(GestureKind.PeekChanged), Is.GreaterThan(0));
        }

        [Test]
        public void Roll_peek_is_not_lifting()
        {
            var r = new Run(GestureScripts.RollPeek(new SyntheticHands()));
            Assert.That(r.Count(GestureKind.CardsLifted), Is.Zero, r.Dump());
        }

        [Test]
        public void Picking_chips_next_to_the_cards_is_not_lifting_the_cards()
        {
            var r = new Run(GestureScripts.PickChips(new SyntheticHands(), Hand.Right, lift: 0.12f));
            Assert.That(r.Count(GestureKind.CardsLifted), Is.Zero, r.Dump());
            Assert.That(r.Count(GestureKind.ChipsPicked), Is.EqualTo(1), r.Dump());
        }

        [Test]
        public void Knocking_beside_the_cards_is_not_lifting()
        {
            var at = G.Cards + new Vector3(0.12f, 0f, 0.02f);
            var r = new Run(GestureScripts.Knock(new SyntheticHands(), Hand.Right, at));
            Assert.That(r.Count(GestureKind.CardsLifted), Is.Zero, r.Dump());
            Assert.That(r.Count(GestureKind.KnockCheck), Is.EqualTo(1), r.Dump());
        }

        [Test]
        public void Pinching_in_the_air_above_the_cards_and_lifting_is_not_lifting_the_cards()
        {
            // 捏合在牌上方 15 cm 開始（沒有碰到牌）
            var r = new Run(GestureScripts.LiftCards(new SyntheticHands(), grip: G.Cards + new Vector3(0f, 0.15f, 0f)));
            Assert.That(r.Count(GestureKind.CardsLifted), Is.Zero, r.Dump());
        }

        [Test]
        public void Slapping_the_cards_while_pinching_is_not_lifting()
        {
            var r = new Run(GestureScripts.Slam(new SyntheticHands(), pinchCards: true));
            Assert.That(r.Count(GestureKind.CardsLifted), Is.Zero, r.Dump());
        }

        [Test]
        public void Slide_and_push_folds_are_unchanged_and_never_lift()
        {
            var push = new Run(GestureScripts.FoldPush(new SyntheticHands()));
            Assert.That(push.Count(GestureKind.FoldPushed), Is.EqualTo(1), push.Dump());
            Assert.That(push.Count(GestureKind.CardsLifted), Is.Zero, push.Dump());
            var slide = new Run(GestureScripts.FoldSlide(new SyntheticHands()));
            Assert.That(slide.Count(GestureKind.FoldPushed), Is.EqualTo(1), slide.Dump());
            Assert.That(slide.Count(GestureKind.CardsLifted), Is.Zero, slide.Dump());
            var arc = new Run(GestureScripts.FoldCarryArc(new SyntheticHands()));
            Assert.That(arc.Count(GestureKind.FoldPushed), Is.EqualTo(1), arc.Dump());
        }

        [Test]
        public void Look_survives_1cm_noise()
        {
            for (int seed = 0; seed < 20; seed++)
            {
                var r = new Run(GestureScripts.LiftCards(new SyntheticHands(seed: seed).WithNoise(0.01f).WithPinchCap()));
                Assert.That(r.Count(GestureKind.CardsLifted), Is.EqualTo(1), $"seed {seed}\n" + r.Dump());
                Assert.That(r.Count(GestureKind.CardsReturned), Is.EqualTo(1), $"seed {seed}\n" + r.Dump());
                Assert.That(r.Events.AnyAction(), Is.False, $"seed {seed}\n" + r.Dump());
            }
        }
    }
}
