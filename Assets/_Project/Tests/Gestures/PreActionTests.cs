using System.IO;
using System.Linq;
using System.Numerics;
using NUnit.Framework;
using Poker.Gestures;

namespace Poker.Tests
{
    /// <summary>
    /// 提前動作（2026-10-13 開發者 Quest 3 回饋：「把牌拿起來之後很難棄牌」，pid 21470 log 641–644 s）：
    /// 1. 還沒輪到、但還在這一手（TableContext.CanActEarly）：棄牌手勢照樣發 FoldPushed（遊戲層記下、輪到時自動棄牌）。
    /// 2. 籌碼隨時可以拿、拿著、放回；沒輪到時丟進下注區不算下注，籌碼退回並發 ActionTooEarly；敲桌也發 ActionTooEarly。
    /// 3. 拿著牌往前放開（越過棄牌線，或往前丟）＝棄牌；放回自己牌位、或在臉前高處放開＝放回。
    /// </summary>
    public class PreActionTests
    {
        static TableContext Early(bool canCheck = true) { var c = G.Ctx(turn: false, canCheck: canCheck); c.CanActEarly = true; return c; }
        static TableContext OffTurn => G.Ctx(turn: false);

        /// <summary>捏住牌（log 641.034：捏合點 −0.03, −0.01, 0.15）、0.2 s 抬起（log 641.229：−0.08, 0.089, 0.03），
        /// 可選停 hold 秒（看牌），再花 travel 秒移到 release（捏合點）停住放開。</summary>
        static SyntheticHands LiftThenRelease(Vector3 release, double hold = 0, double travel = 0.28, Vector3? liftTo = null, Hand h = Hand.Right)
        {
            var s = GestureScripts.GrabCards(new SyntheticHands(), h);
            s.Move(h, 0.2, HandTarget.At(SyntheticHands.PalmForPinch(h, liftTo ?? new Vector3(-0.08f, 0.089f, 0.03f))));
            if (hold > 0) s.Wait(hold);
            s.Move(h, travel, HandTarget.At(SyntheticHands.PalmForPinch(h, release)));
            s.Move(h, 0.12, HandTarget.Pinch(0f));
            s.Move(h, 0.3, HandTarget.At(s.Palm(h) + new Vector3(0, 0.06f, 0)));
            s.Move(h, 0.4, HandTarget.At(SyntheticHands.RightRest));
            s.Wait(0.5);
            return s;
        }

        /// <summary>拿起牌後往前丟：捏合點從 from 以 velocity 移動，途中放開（放開時捏合點約在 from＋velocity×0.1 s）。</summary>
        static SyntheticHands LiftThenToss(Vector3 from, Vector3 velocity, Hand h = Hand.Right)
        {
            var s = GestureScripts.GrabCards(new SyntheticHands(), h);
            s.Move(h, 0.25, HandTarget.At(SyntheticHands.PalmForPinch(h, from)));
            s.Wait(0.1);
            var palm = SyntheticHands.PalmForPinch(h, from);
            s.Move(h, 0.08, HandTarget.At(palm + velocity * 0.05f), Ease.In);
            s.Move(h, 0.06, HandTarget.At(palm + velocity * 0.11f).WithPinch(0f), Ease.Linear);
            s.Move(h, 0.1, HandTarget.At(palm + velocity * 0.15f), Ease.Out);
            s.Move(h, 0.4, HandTarget.At(SyntheticHands.RightRest));
            s.Wait(0.5);
            return s;
        }

        // ── 3. 拿著牌放開：log 的三個案例 ──

        static readonly Vector3 Log641 = new Vector3(0.13f, 0.068f, 0.33f);
        static readonly Vector3 Log642 = new Vector3(0.18f, 0.102f, 0.31f);

        [Test]
        public void Log_641_and_642_releases_past_the_fold_line_fold_on_turn()
        {
            foreach (var p in new[] { Log641, Log642 })
            {
                var ev = G.Run(LiftThenRelease(p));
                Assert.That(ev.Count(GestureKind.FoldPushed), Is.EqualTo(1), p + "\n" + ev.Dump());
                Assert.That(ev.Where(e => e.IsAction).All(e => e.Kind == GestureKind.FoldPushed), Is.True, ev.Dump());
                Assert.That(ev.Count(GestureKind.CardsReturned), Is.EqualTo(1), "hold ends");
            }
        }

        [Test]
        public void Log_641_and_642_releases_fold_early_when_not_yet_the_players_turn()
        {
            foreach (var p in new[] { Log641, Log642 })
            {
                var ev = G.Run(LiftThenRelease(p), Early());
                Assert.That(ev.Count(GestureKind.FoldPushed), Is.EqualTo(1), p + "\n" + ev.Dump());
            }
        }

        [Test]
        public void Release_past_the_fold_line_out_of_the_hand_only_returns_the_cards()
        {
            // 沒輪到、也不能提前動作（已 All-in、攤牌…）：牌放回，不棄牌
            var ev = G.Run(LiftThenRelease(Log641), OffTurn);
            Assert.That(ev.Count(GestureKind.FoldPushed), Is.Zero, ev.Dump());
            Assert.That(ev.Count(GestureKind.CardsReturned), Is.EqualTo(1), ev.Dump());
        }

        [Test]
        public void Log_624_looked_three_seconds_and_put_back_returns()
        {
            // log 620.326 拿起、621.132 看牌、624.021 在 (0.01, 0.068, 0.11) 放開：放回原位
            foreach (var ctx in new[] { G.Ctx(), Early() })
            {
                var ev = G.Run(LiftThenRelease(new Vector3(0.01f, 0.068f, 0.11f), hold: 3.0, travel: 0.5, liftTo: new Vector3(0f, 0.20f, 0.04f)), ctx);
                Assert.That(ev.Count(GestureKind.FoldPushed), Is.Zero, ev.Dump());
                Assert.That(ev.AnyAction(), Is.False, ev.Dump());
                Assert.That(ev.Count(GestureKind.CardsReturned), Is.EqualTo(1), ev.Dump());
            }
        }

        [Test]
        public void Looked_then_released_past_the_fold_line_folds()
        {
            // 看過（停 1.5 s）再往前拿到棄牌線外放開：以前只有棄牌堆中心 12 cm 內、手掌 ≤ 8 cm 才算
            var ev = G.Run(LiftThenRelease(new Vector3(0.10f, 0.15f, 0.30f), hold: 1.5, travel: 0.4, liftTo: new Vector3(0f, 0.20f, 0.04f)));
            Assert.That(ev.Count(GestureKind.FoldPushed), Is.EqualTo(1), ev.Dump());
        }

        [Test]
        public void Forward_toss_in_front_of_the_cards_folds()
        {
            // 捏合點在牌前方（z 0.20，還沒到棄牌線）往前 0.8 m/s 丟出
            var ev = G.Run(LiftThenToss(new Vector3(0.05f, 0.12f, 0.17f), new Vector3(0.2f, 0f, 0.8f)));
            Assert.That(ev.Count(GestureKind.FoldPushed), Is.EqualTo(1), ev.Dump());
            var early = G.Run(LiftThenToss(new Vector3(0.05f, 0.12f, 0.17f), new Vector3(0.2f, 0f, 0.8f)), Early());
            Assert.That(early.Count(GestureKind.FoldPushed), Is.EqualTo(1), early.Dump());
        }

        [Test]
        public void Setting_the_cards_down_on_their_spot_while_moving_forward_returns()
        {
            // 從臉前往前下方放回自己的牌位（放開時還在往前移）：不是丟
            var ev = G.Run(LiftThenToss(new Vector3(0f, 0.08f, 0.08f), new Vector3(0f, -0.3f, 0.5f)));
            Assert.That(ev.Count(GestureKind.FoldPushed), Is.Zero, ev.Dump());
            Assert.That(ev.Count(GestureKind.CardsReturned), Is.EqualTo(1), ev.Dump());
        }

        [Test]
        public void Releasing_high_in_front_of_the_face_returns()
        {
            // 牌舉到 30 cm 高、往前伸到棄牌線外放開（像拿近一點看）：高於 CardFoldReleaseMaxHeight 不算棄牌
            var ev = G.Run(LiftThenRelease(new Vector3(0.05f, 0.30f, 0.30f), hold: 0.5, travel: 0.4, liftTo: new Vector3(0f, 0.25f, 0.06f)));
            Assert.That(ev.Count(GestureKind.FoldPushed), Is.Zero, ev.Dump());
        }

        [Test]
        public void Stationary_release_short_of_the_fold_line_returns()
        {
            var ev = G.Run(LiftThenRelease(new Vector3(0.05f, 0.10f, 0.22f), hold: 1.0));
            Assert.That(ev.Count(GestureKind.FoldPushed), Is.Zero, ev.Dump());
        }

        [Test]
        public void Picking_the_cards_at_their_front_edge_and_setting_them_down_just_past_the_line_returns()
        {
            // 合成雜動（RobustnessStats fidget run 131/400/487）：在牌前緣（z 0.20）捏起、就近往前 8 cm 放下（z 0.28）——沒有往前送牌
            var s = GestureScripts.GrabCards(new SyntheticHands(), grip: new Vector3(0.02f, 0.01f, 0.20f));
            s.Move(Hand.Right, 0.3, HandTarget.At(SyntheticHands.PalmForPinch(Hand.Right, new Vector3(0.03f, 0.09f, 0.26f))));
            s.Wait(0.8);
            s.Move(Hand.Right, 0.4, HandTarget.At(SyntheticHands.PalmForPinch(Hand.Right, new Vector3(0.03f, 0.02f, 0.28f))));
            s.Move(Hand.Right, 0.12, HandTarget.Pinch(0f));
            s.Move(Hand.Right, 0.3, HandTarget.At(s.Palm(Hand.Right) + new Vector3(0, 0.07f, 0)));
            s.Wait(0.5);
            var ev = G.Run(s);
            Assert.That(ev.Count(GestureKind.CardsLifted), Is.EqualTo(1), ev.Dump());
            Assert.That(ev.Count(GestureKind.FoldPushed), Is.Zero, ev.Dump());
        }

        // ── 1. 提前棄牌：其他棄牌手勢 ──

        [Test]
        public void Push_slide_and_carry_fold_early_when_still_in_the_hand()
        {
            foreach (var (name, s) in new[]
                     {
                         ("push", GestureScripts.FoldPush(new SyntheticHands())),
                         ("slide", GestureScripts.FoldSlide(new SyntheticHands())),
                         ("arc", GestureScripts.FoldCarryArc(new SyntheticHands())),
                         ("carry", GestureScripts.CarryCardsToMuck(new SyntheticHands())),
                     })
            {
                var early = G.Run(s, Early());
                Assert.That(early.Count(GestureKind.FoldPushed), Is.EqualTo(1), name + "\n" + early.Dump());
                var off = G.Run(s, OffTurn);
                Assert.That(off.Count(GestureKind.FoldPushed), Is.Zero, name + " (not in the hand)\n" + off.Dump());
            }
        }

        [Test]
        public void Looking_peeking_and_idling_early_never_fold()
        {
            var look = G.Run(GestureScripts.LiftCards(new SyntheticHands()), Early());
            Assert.That(look.AnyAction(), Is.False, look.Dump());
            var peek = G.Run(GestureScripts.Peek(new SyntheticHands()), Early());
            Assert.That(peek.AnyAction(), Is.False, peek.Dump());
            var idle = G.Run(new SyntheticHands(seed: 3).WithNoise(0.01f).Wait(4.0), Early());
            Assert.That(idle.Count(GestureKind.FoldPushed) + idle.Count(GestureKind.ActionTooEarly), Is.Zero, idle.Dump());
        }

        // ── 2. 籌碼隨時可以拿；沒輪到時丟進下注區退回 ──

        [Test]
        public void Chips_can_be_picked_held_and_put_back_early()
        {
            var s = GestureScripts.PickChips(new SyntheticHands(), lift: 0.05f);
            GestureScripts.PlaceAt(s, Hand.Right, G.Stack + new Vector3(0f, 0.02f, 0f));
            s.Wait(1.0);
            var ev = G.Run(s, Early());
            Assert.That(ev.Count(GestureKind.ChipsPicked), Is.EqualTo(1), ev.Dump());
            Assert.That(ev.Count(GestureKind.ChipsReleased), Is.EqualTo(1), ev.Dump());
            Assert.That(ev.Count(GestureKind.ActionTooEarly), Is.Zero, "put back on the stack is not a bet attempt\n" + ev.Dump());
            Assert.That(ev.AnyAction(), Is.False, ev.Dump());
        }

        [Test]
        public void Chips_tossed_into_the_bet_zone_early_go_back_and_say_too_early()
        {
            var s = GestureScripts.PickChips(new SyntheticHands(), lift: 0.05f);
            GestureScripts.PlaceAt(s, Hand.Right, new Vector3(0.05f, 0.03f, 0.42f));
            s.Wait(2.0);
            var ev = G.Run(s, Early());
            var rel = ev.First(GestureKind.ChipsReleased);
            Assert.That(rel.InBetZone, Is.False, "not a bet: chips go back\n" + ev.Dump());
            Assert.That(ev.Count(GestureKind.BetCommitted), Is.Zero, ev.Dump());
            Assert.That(ev.Count(GestureKind.ActionTooEarly), Is.EqualTo(1), ev.Dump());
            var early = ev.First(GestureKind.ActionTooEarly);
            Assert.That(early.Count, Is.EqualTo(rel.Count), "carries the chip count");
            Assert.That(early.IsAction, Is.False);
        }

        [Test]
        public void Chips_picked_early_and_tossed_on_the_turn_bet_normally()
        {
            var s = GestureScripts.PickChips(new SyntheticHands(), lift: 0.05f);
            double turnAt = s.Time;
            GestureScripts.PlaceAt(s, Hand.Right, new Vector3(0.05f, 0.03f, 0.42f));
            s.Wait(2.0);
            var ev = G.Run(s, t => t < turnAt ? Early() : G.Ctx());
            Assert.That(ev.Count(GestureKind.ChipsPicked), Is.EqualTo(1), ev.Dump());
            Assert.That(ev.Count(GestureKind.BetCommitted), Is.EqualTo(1), ev.Dump());
            Assert.That(ev.Count(GestureKind.ActionTooEarly), Is.Zero, ev.Dump());
        }

        [Test]
        public void Knock_early_says_too_early_instead_of_checking()
        {
            var ev = G.Run(GestureScripts.Knock(new SyntheticHands(), Hand.Right, GestureScripts.KnockSpot(Hand.Right)), Early());
            Assert.That(ev.Count(GestureKind.KnockCheck), Is.Zero, ev.Dump());
            Assert.That(ev.Count(GestureKind.ActionTooEarly), Is.EqualTo(1), ev.Dump());
        }

        [Test]
        public void Two_hand_all_in_push_early_is_not_an_early_fold_and_says_too_early()
        {
            // 還沒輪到時雙手推籌碼：不能因為 All-in 被擋，從牌上出發的那隻手就變成提前棄牌（錄製 allin_* 以前會）
            foreach (var (name, s) in new[]
                     {
                         ("allin", GestureScripts.AllIn(new SyntheticHands())),
                         ("allin_edge", GestureScripts.AllInFromEdge(new SyntheticHands())),
                     })
            {
                var ev = G.Run(s, Early());
                Assert.That(ev.Count(GestureKind.FoldPushed), Is.Zero, name + "\n" + ev.Dump());
                Assert.That(ev.Count(GestureKind.AllInPushStarted) + ev.Count(GestureKind.AllInConfirmed) + ev.Count(GestureKind.AllInCancelled), Is.Zero, name + "\n" + ev.Dump());
                Assert.That(ev.Count(GestureKind.ActionTooEarly), Is.EqualTo(1), name + "\n" + ev.Dump());
            }
        }

        [Test]
        public void Recordings_replayed_before_the_turn_fold_early_only_where_they_fold_on_the_turn()
        {
            // 全部真實錄製改成「還沒輪到、還在這一手」重播：提前棄牌只能出現在輪到時也會棄牌的那幾段（不能多出誤觸）
            var dir = RecordingCheck.FindRecordingsDir();
            if (dir == null) Assert.Ignore("no recordings");
            int files = 0;
            foreach (var f in Directory.GetFiles(dir, "*.hands").OrderBy(x => x, System.StringComparer.Ordinal))
            {
                var rec = HandRecording.Load(f);
                var onTurn = RecordingExpectation.Parse(Path.GetFileName(f), rec.Note).Context;
                if (onTurn.Phase == TablePhase.Showdown) continue;
                var early = onTurn;
                early.IsPlayerTurn = false;
                early.CanActEarly = true;
                bool foldsOnTurn = ReplayRunner.Run(rec, _ => onTurn).Any(e => e.Kind == GestureKind.FoldPushed);
                var ev = ReplayRunner.Run(rec, _ => early);
                Assert.That(ev.Any(e => e.Kind == GestureKind.FoldPushed), Is.EqualTo(foldsOnTurn), Path.GetFileName(f) + "\n" + ev.Dump());
                Assert.That(ev.Any(e => e.IsAction), Is.EqualTo(foldsOnTurn), "no other action before the turn: " + Path.GetFileName(f) + "\n" + ev.Dump());
                files++;
            }
            Assert.That(files, Is.GreaterThan(50));
        }

        [Test]
        public void Out_of_the_hand_nothing_changes()
        {
            // CanActEarly 為 false（已棄牌、All-in、攤牌、一手之間）：照舊什麼都不發
            Assert.That(G.Run(GestureScripts.PickChips(new SyntheticHands()), OffTurn).Count(GestureKind.ChipsPicked), Is.Zero);
            Assert.That(G.Run(GestureScripts.Knock(new SyntheticHands(), Hand.Right, GestureScripts.KnockSpot(Hand.Right)), OffTurn).Count(GestureKind.ActionTooEarly), Is.Zero);
        }
    }
}
