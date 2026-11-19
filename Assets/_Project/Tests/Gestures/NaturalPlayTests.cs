using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using NUnit.Framework;
using Poker.Gestures;

namespace Poker.Tests
{
    /// <summary>
    /// 2026-10-12 實機試玩 log（Logs/gesture-dump2.log 的 [Gesture] 診斷，牌桌座標）中，使用者「自然」的 All-in 與推牌動作。
    /// 數值都取自 log 的手掌軌跡（時間標在各測試）。舊規則要求雙手夾住籌碼堆、雙手掌越過下注線，真人做不到。
    /// </summary>
    public class NaturalPlayTests
    {
        /// <summary>log 裡左手休息的位置：推牌時另一隻手一直放在這裡（146–171 s）。</summary>
        static readonly Vector3 LeftResting = new Vector3(-0.22f, 0.035f, 0.22f);

        /// <summary>跑腳本並收集手勢診斷，失敗時印出來才看得到卡在哪個條件。</summary>
        static (List<GestureEvent> ev, string trace) RunTraced(SyntheticHands s)
        {
            var r = new GestureRecognizer(G.Layout);
            var lines = new List<string>();
            r.Trace = lines.Add;
            var ev = new List<GestureEvent>();
            foreach (var f in s.Frames) ev.AddRange(r.Update(f, G.Ctx()));
            return (ev, ev.Dump() + "\n--- trace\n" + string.Join("\n", lines.Where(l => !l.Contains("knock"))));
        }

        static int AllInEvents(List<GestureEvent> ev) =>
            ev.Count(GestureKind.AllInPushStarted) + ev.Count(GestureKind.AllInConfirmed);

        // ───────────── All-in ─────────────

        [Test]
        public void All_in_with_both_hands_in_front_of_the_player_not_around_the_stack()
        {
            // log 7.9 s：左手 x≈−0.17、右手 x≈+0.13（夾不住 x=0.24 的籌碼堆），貼桌、一起往前推
            var s = GestureScripts.AllIn(new SyntheticHands(), gap: 0.30f, distance: 0.26f, duration: 1.2,
                center: new Vector3(-0.02f, 0f, 0.12f));
            var (ev, trace) = RunTraced(s);
            Assert.That(ev.Count(GestureKind.AllInConfirmed), Is.EqualTo(1), trace);
        }

        [Test]
        public void All_in_pushed_from_the_table_edge_toward_the_pot_with_palms_stopping_short_of_the_bet_line()
        {
            // log 48.5–48.9、49.8–50.1、51.0–51.3、52.2–52.7、53.3–53.8 s（連續 5 次都沒觸發）：
            // 雙手從桌緣（z≈−0.03）出發、手掌高 5–11 cm，0.3–0.5 s 內一起往前推 28 cm 並往中間收攏，
            // 手掌停在 z 0.25／0.27（下注線 0.30 前），手指已經越過下注線
            var s = new SyntheticHands();
            var l0 = new Vector3(0.12f, 0.068f, -0.03f);
            var r0 = new Vector3(0.33f, 0.09f, -0.02f);
            s.Move(0.5, HandTarget.At(r0 + new Vector3(0, 0.04f, 0)), HandTarget.At(l0 + new Vector3(0, 0.04f, 0)));
            s.Move(0.25, HandTarget.At(r0), HandTarget.At(l0));
            s.Wait(0.3);
            s.Move(0.4, HandTarget.At(new Vector3(0.17f, 0.065f, 0.27f)), HandTarget.At(new Vector3(0.03f, 0.05f, 0.25f)));
            s.Wait(1.4);
            var (ev, trace) = RunTraced(s);
            Assert.That(ev.Count(GestureKind.AllInConfirmed), Is.EqualTo(1), trace);
            Assert.That(ev.Count(GestureKind.FoldPushed), Is.Zero, trace);
        }

        [Test]
        public void Two_hands_pushing_together_with_one_starting_on_the_cards_is_all_in_not_a_fold()
        {
            // log 92.4–93.0 s：左手從牌上出發（佔住了推牌），右手在籌碼堆旁，兩手一起往前推到 z 0.29／0.32
            var s = new SyntheticHands();
            var l0 = new Vector3(0.08f, 0.05f, 0.10f);
            var r0 = new Vector3(0.30f, 0.04f, 0.08f);
            s.Move(0.5, HandTarget.At(r0 + new Vector3(0, 0.05f, 0)), HandTarget.At(l0 + new Vector3(0, 0.05f, 0)));
            s.Move(0.25, HandTarget.At(r0), HandTarget.At(l0));
            s.Wait(0.3);
            s.Move(0.5, HandTarget.At(new Vector3(0.16f, 0.05f, 0.32f)), HandTarget.At(new Vector3(0.04f, 0.05f, 0.29f)));
            s.Wait(1.4);
            var (ev, trace) = RunTraced(s);
            Assert.That(ev.Count(GestureKind.AllInConfirmed), Is.EqualTo(1), trace);
            Assert.That(ev.Count(GestureKind.FoldPushed), Is.Zero, trace);
        }

        // ───────────── 推牌棄牌 ─────────────

        /// <summary>log 146–171 s 的 9 次棄牌是同一種動作：右手從牌上（手掌 z≈0.10）往右前方斜拖到棄牌堆旁，
        /// 0.2–0.3 s 內 Δ≈(+0.18, +0.18)（約 48°），途中手掌高 10–12 cm，最後放在 (0.22–0.24, 0.27–0.29)。左手一直放在桌上休息。</summary>
        static SyntheticHands DragToMuck(Vector3 mid, float midRoll = 0f, Vector3? end = null)
        {
            var s = new SyntheticHands().Set(Hand.Left, LeftResting);
            var start = new Vector3(0.04f, 0.04f, 0.10f);
            s.Move(Hand.Right, 0.5, HandTarget.At(start + new Vector3(0.08f, 0.05f, 0.02f)));
            s.Move(Hand.Right, 0.25, HandTarget.At(start));
            s.Wait(0.3);
            s.Move(Hand.Right, 0.12, HandTarget.At(mid).WithRoll(midRoll));
            s.Move(Hand.Right, 0.15, HandTarget.At(end ?? new Vector3(0.23f, 0.10f, 0.28f)).WithRoll(0));
            s.Move(Hand.Right, 0.1, HandTarget.At(new Vector3((end ?? new Vector3(0.24f, 0, 0)).X, 0.04f, 0.28f)));
            s.Wait(0.6);
            return s;
        }

        [Test]
        public void Fold_by_dragging_the_cards_diagonally_to_the_muck()
        {
            // log 147.5、150.8、159.0、160.9、170.4 s（舊規則有觸發）
            var (ev, trace) = RunTraced(DragToMuck(new Vector3(0.12f, 0.105f, 0.18f)));
            Assert.That(ev.Count(GestureKind.FoldPushed), Is.EqualTo(1), trace);
            Assert.That(ev.Count(e => e.IsAction), Is.EqualTo(1), trace);
        }

        [Test]
        public void Fold_dragged_to_the_muck_at_49_degrees_is_not_rejected_as_sideways()
        {
            // log 146.4 s：從 (0.04, 0.10) 到 (0.25, 0.28)，側移 21 cm、往前 18 cm；舊錐形（0.10＋0.6×往前）只差 2 mm 就判成 sideways
            var (ev, trace) = RunTraced(DragToMuck(new Vector3(0.10f, 0.11f, 0.15f), end: new Vector3(0.25f, 0.106f, 0.28f)));
            Assert.That(ev.Count(GestureKind.FoldPushed), Is.EqualTo(1), trace);
        }

        [Test]
        public void Fold_dragged_to_the_muck_with_the_palm_briefly_rising_and_tilting_still_folds()
        {
            // log 164.8、166.5、168.6 s：同樣的斜拖，途中手掌短暫拱到 20–23 cm、掌心沒朝下（舊規則掌心角度要 ≥110°）→ "lifted"；
            // 0.2 s 後同樣落在 (0.20–0.24, 0.28–0.29)，下一次（170.4 s）同樣的動作就觸發了
            var (ev, trace) = RunTraced(DragToMuck(new Vector3(0.12f, 0.235f, 0.16f), midRoll: 85f));
            Assert.That(ev.Count(GestureKind.FoldPushed), Is.EqualTo(1), trace);
        }

        [Test]
        public void Fold_by_lifting_the_cards_and_arcing_them_to_the_muck_lands_without_touching_the_table()
        {
            // 使用者確認（2026-10-12）：拿起牌劃弧線放到棄牌堆就是棄牌。log 落下時手掌最低 0.057–0.073 m（取樣 30–390 ms 一點）
            var s = GestureScripts.FoldCarryArc(new SyntheticHands().Set(Hand.Left, LeftResting));
            var (ev, trace) = RunTraced(s);
            Assert.That(ev.Count(GestureKind.FoldPushed), Is.EqualTo(1), trace);
            Assert.That(ev.Count(e => e.IsAction), Is.EqualTo(1), trace);
        }

        [Test]
        public void Lifting_the_cards_high_and_putting_them_back_on_the_cards_is_not_a_fold()
        {
            var s = GestureScripts.FoldCarryArc(new SyntheticHands().Set(Hand.Left, LeftResting), backToCards: true);
            var (ev, trace) = RunTraced(s);
            Assert.That(ev.Count(GestureKind.FoldPushed), Is.Zero, trace);
        }

        [Test]
        public void Lifting_the_hand_straight_up_from_the_cards_is_not_a_fold()
        {
            // 咪牌完把手抬起來、或只是從牌上拿開手：不能算棄牌
            var s = new SyntheticHands();
            var start = G.Cards + new Vector3(0.03f, 0.025f, -0.05f);
            s.Move(Hand.Right, 0.5, HandTarget.At(start + new Vector3(0, 0.06f, 0)));
            s.Move(Hand.Right, 0.3, HandTarget.At(start));
            s.Wait(0.3);
            s.Move(Hand.Right, 0.4, HandTarget.At(start + new Vector3(0.02f, 0.22f, -0.02f)));
            s.Wait(0.8);
            var ev = G.Run(s);
            Assert.That(ev.Count(GestureKind.FoldPushed), Is.Zero, ev.Dump());
        }

        [Test]
        public void Lifting_the_cards_up_and_forward_without_setting_them_down_is_not_a_fold()
        {
            // 「拿起往前丟」沒有實機證據（log 裡那 3 次是上面的斜拖），不算棄牌：抬起往前 20 cm、停在半空
            var s = new SyntheticHands();
            var start = G.Cards + new Vector3(0.03f, 0.025f, -0.05f);
            s.Move(Hand.Right, 0.5, HandTarget.At(start + new Vector3(0, 0.06f, 0)));
            s.Move(Hand.Right, 0.3, HandTarget.At(start));
            s.Wait(0.3);
            s.Move(Hand.Right, 0.25, HandTarget.At(start + new Vector3(0.05f, 0.20f, 0.06f)));
            s.Move(Hand.Right, 0.3, HandTarget.At(start + new Vector3(0.09f, 0.22f, 0.20f)));
            s.Wait(0.8);
            var (ev, trace) = RunTraced(s);
            Assert.That(ev.Count(GestureKind.FoldPushed), Is.Zero, trace);
        }

        // ───────────── 推牌與 All-in 互相干擾 ─────────────

        /// <summary>右手從牌上往前推 18 cm；左手放在 leftStart，同時往前滑 leftForward。</summary>
        static SyntheticHands PushWithOtherHand(Vector3 leftStart, float leftForward)
        {
            var s = new SyntheticHands().Set(Hand.Left, leftStart);
            var start = G.Cards + new Vector3(0, 0.025f, 0);
            s.Move(Hand.Right, 0.5, HandTarget.At(start + new Vector3(0, 0.06f, 0)));
            s.Move(Hand.Right, 0.3, HandTarget.At(start));
            s.Wait(0.3);
            s.Move(0.5, HandTarget.At(start + new Vector3(0, 0, 0.18f)), HandTarget.At(leftStart + new Vector3(0, 0, leftForward)));
            s.Wait(1.6);
            return s;
        }

        [Test]
        public void One_hand_pushing_the_cards_while_the_other_rests_on_the_table_is_a_fold()
        {
            // 左手放在桌上（與右手距離 20–25 cm、都貼桌＝符合 All-in 準備條件），只有右手推
            var (ev, trace) = RunTraced(PushWithOtherHand(new Vector3(-0.20f, 0.03f, 0.12f), 0f));
            Assert.That(ev.Count(GestureKind.FoldPushed), Is.EqualTo(1), trace);
            Assert.That(AllInEvents(ev), Is.Zero, trace);
        }

        [Test]
        public void One_hand_pushing_while_the_resting_hand_drifts_2cm_is_still_a_fold()
        {
            // 模糊（偏推牌）：休息的手跟著動了一點點（2 cm，追蹤誤差級）
            var (ev, trace) = RunTraced(PushWithOtherHand(new Vector3(-0.20f, 0.03f, 0.12f), 0.02f));
            Assert.That(ev.Count(GestureKind.FoldPushed), Is.EqualTo(1), trace);
            Assert.That(AllInEvents(ev), Is.Zero, trace);
        }

        [Test]
        public void One_hand_pushing_18cm_while_the_other_slides_8cm_is_neither_fold_nor_all_in()
        {
            // 模糊（兩者之間）：另一隻手也明顯往前（8 cm），但不到 All-in 的推送距離。寧可什麼都不做，讓使用者重做
            var (ev, trace) = RunTraced(PushWithOtherHand(new Vector3(-0.20f, 0.03f, 0.12f), 0.08f));
            Assert.That(ev.Count(GestureKind.FoldPushed), Is.Zero, trace);
            Assert.That(ev.Count(GestureKind.AllInConfirmed), Is.Zero, trace);
        }

        [Test]
        public void Both_hands_pushing_the_full_way_with_one_on_the_cards_is_all_in()
        {
            // 雙手同步：右手從牌上、左手在牌左邊，一起往前推 18 cm
            var (ev, trace) = RunTraced(PushWithOtherHand(new Vector3(-0.20f, 0.03f, 0.12f), 0.18f));
            Assert.That(ev.Count(GestureKind.AllInConfirmed), Is.EqualTo(1), trace);
            Assert.That(ev.Count(GestureKind.FoldPushed), Is.Zero, trace);
        }
    }
}
