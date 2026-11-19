using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using NUnit.Framework;
using Poker.Gestures;

namespace Poker.Tests
{
    /// <summary>
    /// 抗雜訊統計：每個手勢在合理範圍內隨機化動作參數＋1 cm 雜訊跑 200 次，正確率 ≥ 95%；
    /// 500 段日常雜動（含 Showdown 階段、輪到玩家且可過牌，閘門全開）誤觸發率 ≤ 1%。
    /// 「正確」＝預期事件恰好出現（次數正確）且沒有其他行動事件。種子固定，結果可重現。
    /// 捏合強度依 Quest 3 實機封頂 0.70（WithPinchCap），避免合成手比真機好捏而掩蓋門檻錯誤。
    /// </summary>
    [Category("Stats")] // 約 30 秒；Unity 內可用 Category 篩掉
    public class RobustnessStatsTests
    {
        const int Trials = 200;
        const int FidgetRuns = 500;

        static float U(Random r, float a, float b) => a + (float)r.NextDouble() * (b - a);
        static Hand AnyHand(Random r) => r.Next(2) == 0 ? Hand.Left : Hand.Right;

        static SyntheticHands Noisy(int seed, bool white) =>
            (white ? new SyntheticHands(seed: seed).WithWhiteNoise(0.01f) : new SyntheticHands(seed: seed).WithNoise(0.01f)).WithPinchCap();

        static bool OnlyActions(List<GestureEvent> ev, params GestureKind[] allowed) =>
            ev.Where(e => e.IsAction).All(e => allowed.Contains(e.Kind));

        // 每個手勢：給 (亂數, 種子) 回傳這次是否正確
        static readonly Dictionary<string, Func<Random, int, bool, bool>> Cases = new Dictionary<string, Func<Random, int, bool, bool>>
        {
            ["peek"] = (r, seed, white) =>
            {
                var h = AnyHand(r);
                var s = GestureScripts.Peek(Noisy(seed, white), h, U(r, -0.02f, 0.02f), U(r, -0.025f, 0.025f), U(r, 28, 40), U(r, 0.01f, 0.03f), U(r, 0.3f, 0.8f));
                s.Move(h, 0.5, HandTarget.At(h == Hand.Right ? SyntheticHands.RightRest : SyntheticHands.LeftRest));
                s.Wait(0.3);
                var ev = G.Run(s).Where(e => e.Kind == GestureKind.PeekChanged && e.Hand == h).ToList();
                return ev.Count > 0 && ev.Max(e => e.Amount) >= 0.5f && ev.Last().Amount == 0f;
            },
            ["peek_lift"] = (r, seed, white) =>
            {
                // 整隻手抬起（peek_v2_153106）：手掌抬 5–7 cm、手腕俯仰 25–38°、掌心側轉到接近垂直（角度約 85–120°）
                var h = AnyHand(r);
                var s = GestureScripts.Peek(Noisy(seed, white), h, U(r, -0.02f, 0.02f), U(r, -0.02f, 0.02f), U(r, 25, 38), U(r, 0.05f, 0.07f), U(r, 0.4f, 0.8f), U(r, 70, 100));
                return PeekOk(s, h);
            },
            ["peek_shallow"] = (r, seed, white) =>
            {
                // 手腕只比指尖高 5–6 cm：要掀到教學判定的 0.45（Tutorial.PeekThreshold）
                var h = AnyHand(r);
                var s = GestureScripts.Peek(Noisy(seed, white), h, U(r, -0.02f, 0.02f), U(r, -0.02f, 0.02f), GestureScripts.PitchForWristOverTip(U(r, 0.05f, 0.06f)), U(r, 0f, 0.01f), U(r, 0.4f, 0.8f));
                return PeekOk(s, h, 0.45f);
            },
            ["peek_roll"] = (r, seed, white) =>
            {
                // 翻腕掀牌（2026-10-13 實機）：掌心轉到 40–60°（roll 120–140）、抬 1.5–3.5 cm（手掌 4.5–6.5 cm）；要掀到 0.5 以上、不亮牌不棄牌
                var h = AnyHand(r);
                var s = GestureScripts.RollPeek(Noisy(seed, white), h, U(r, 120f, 140f), U(r, 0.3f, 0.7f), U(r, 0.3f, 0.8f), U(r, 0.015f, 0.035f));
                var all = G.Run(s);
                var ev = all.Where(e => e.Kind == GestureKind.PeekChanged && e.Hand == h).ToList();
                return ev.Count > 0 && ev.Max(e => e.Amount) >= 0.5f && ev.Last().Amount == 0f && !all.AnyAction();
            },
            ["chips"] = (r, seed, white) =>
            {
                float lift = U(r, 0f, 0.12f);
                var s = GestureScripts.PickChips(Noisy(seed, white), Hand.Right, lift, new Vector3(U(r, -0.02f, 0.02f), 0, U(r, -0.02f, 0.02f)));
                var ev = G.Run(s);
                int expected = 1 + (int)Math.Floor(lift / 0.025f + 1e-4f);
                int final = ev.Where(e => e.Kind == GestureKind.ChipsPicked || e.Kind == GestureKind.ChipCountChanged).Select(e => e.Count).LastOrDefault();
                return ev.Count(GestureKind.ChipsPicked) == 1 && Math.Abs(final - expected) <= 1 && !ev.AnyAction();
            },
            ["chips_weak"] = (r, seed, white) =>
            {
                // 輕捏：強度 0.45–0.55、指尖距 1.6–2.7 cm（合成手 pinch 0.64–0.79）
                var at = G.Stack + new Vector3(U(r, -0.02f, 0.02f), 0.02f, U(r, -0.02f, 0.02f));
                var s = GestureScripts.WeakPinchAt(Noisy(seed, white), Hand.Right, at, U(r, 0.643f, 0.786f), U(r, 0f, 0.04f));
                var ev = G.Run(s);
                return ev.Count(GestureKind.ChipsPicked) == 1 && !ev.AnyAction();
            },
            ["bet"] = (r, seed, white) =>
            {
                var s = GestureScripts.PickChips(Noisy(seed, white), Hand.Right, U(r, 0f, 0.06f));
                if (r.Next(2) == 0)
                    GestureScripts.PlaceAt(s, Hand.Right, new Vector3(U(r, -0.25f, 0.25f), 0.02f, U(r, 0.36f, 0.6f)), U(r, 0.4f, 0.9f));
                else
                    GestureScripts.Throw(s, Hand.Right, new Vector3(U(r, 0.05f, 0.2f), U(r, 0.10f, 0.18f), U(r, 0.08f, 0.14f)), new Vector3(U(r, -0.2f, 0.1f), U(r, -0.1f, 0.2f), U(r, 0.9f, 1.5f)));
                s.Wait(2.0);
                var ev = G.Run(s);
                return ev.Count(GestureKind.BetCommitted) == 1 && OnlyActions(ev, GestureKind.BetCommitted);
            },
            // 2026-10-13 實機沒算到的丟法：偏短（落在牌前緣與下注線之間）、偏右（超出下注區右緣）、丟過頭（超出遠端桌緣）
            ["bet_toss"] = (r, seed, white) =>
            {
                var s = GestureScripts.PickChips(Noisy(seed, white), Hand.Right, U(r, 0f, 0.06f));
                switch (r.Next(3))
                {
                    case 0:
                        GestureScripts.Throw(s, Hand.Right, new Vector3(U(r, 0f, 0.15f), U(r, 0.05f, 0.10f), U(r, 0.16f, 0.19f)), new Vector3(U(r, -0.1f, 0.1f), 0f, U(r, 0.5f, 0.7f)));
                        break;
                    case 1:
                        GestureScripts.Throw(s, Hand.Right, new Vector3(U(r, 0.20f, 0.26f), U(r, 0.10f, 0.14f), U(r, 0.14f, 0.18f)), new Vector3(U(r, 0.7f, 1.0f), 0.1f, U(r, 0.8f, 1.1f)));
                        break;
                    default:
                        GestureScripts.Throw(s, Hand.Right, new Vector3(U(r, -0.05f, 0.10f), U(r, 0.15f, 0.20f), 0.15f), new Vector3(U(r, -0.2f, 0.1f), U(r, 0.3f, 0.5f), U(r, 2.0f, 2.6f)));
                        break;
                }
                s.Wait(2.0);
                var ev = G.Run(s);
                return ev.Count(GestureKind.BetCommitted) == 1 && OnlyActions(ev, GestureKind.BetCommitted);
            },
            ["retract"] = (r, seed, white) =>
            {
                var s = GestureScripts.PickChips(Noisy(seed, white), Hand.Right, U(r, 0f, 0.06f));
                var spot = new Vector3(U(r, -0.2f, 0.2f), 0.02f, U(r, 0.36f, 0.55f));
                GestureScripts.PlaceAt(s, Hand.Right, spot, U(r, 0.4f, 0.7f));
                GestureScripts.Retract(s, Hand.Right, spot + new Vector3(U(r, -0.02f, 0.02f), 0, U(r, -0.02f, 0.02f)));
                s.Wait(2.0);
                var ev = G.Run(s);
                return ev.Count(GestureKind.BetRetracted) == 1 && !ev.AnyAction();
            },
            ["knock"] = (r, seed, white) =>
            {
                var h = AnyHand(r);
                var spot = GestureScripts.KnockSpot(h) + new Vector3(U(r, -0.04f, 0.04f), 0, U(r, -0.04f, 0.04f));
                var s = GestureScripts.Knock(Noisy(seed, white), h, spot, 2, U(r, 0.035f, 0.06f), U(r, 0.08f, 0.15f), U(r, 0.06f, 0.10f), U(r, 0.03f, 0.15f));
                var ev = G.Run(s);
                return ev.Count(GestureKind.KnockCheck) == 1 && OnlyActions(ev, GestureKind.KnockCheck);
            },
            ["knock_gentle"] = (r, seed, white) =>
            {
                // 實機 log pid 22510 的輕敲：第一下 3–5 cm、第二下只回升 1.3–2 cm、落點再低 0.5–2 cm（第二下落差 2.3–3.9 cm、峰值 0.41–0.61 m/s）
                var h = AnyHand(r);
                var spot = GestureScripts.KnockSpot(h) + new Vector3(U(r, -0.04f, 0.04f), 0, U(r, -0.04f, 0.04f));
                var s = GestureScripts.GentleKnock(Noisy(seed, white), h, spot, U(r, 0.03f, 0.05f), U(r, 0.08f, 0.12f), U(r, 0.013f, 0.02f), U(r, 0.008f, 0.02f),
                    U(r, 0.06f, 0.09f), U(r, 0.03f, 0.08f));
                var ev = G.Run(s);
                return ev.Count(GestureKind.KnockCheck) == 1 && OnlyActions(ev, GestureKind.KnockCheck);
            },
            ["knock_soft2"] = (r, seed, white) =>
            {
                // 2026-10-13 實機 log 3586–3599（張開手）：第一下清楚（落差 4–6 cm），第二下只有 0.24–0.34 m/s；
                // 合成：第一下抬 4–6 cm、0.08–0.2 s 落下，第二下抬 3.5–6 cm、末速 0.24–0.34 m/s
                var h = AnyHand(r);
                var spot = GestureScripts.KnockSpot(h) + new Vector3(U(r, -0.04f, 0.04f), 0, U(r, -0.04f, 0.04f));
                float h2 = U(r, 0.035f, 0.06f);
                var s = GestureScripts.SoftSecondKnock(Noisy(seed, white), h, spot, U(r, 0.04f, 0.06f), U(r, 0.08f, 0.2f), h2, 2 * h2 / (U(r, 0.24f, 0.34f) * 1.1f), U(r, 0.1f, 0.15f), U(r, 0.02f, 0.08f));
                var ev = G.Run(s);
                return ev.Count(GestureKind.KnockCheck) == 1 && OnlyActions(ev, GestureKind.KnockCheck);
            },
            ["fingertap"] = (r, seed, white) =>
            {
                // 真實錄製：指尖回升 5.7–10 cm、敲下峰值 1.17–1.7 m/s；這裡抬 6–8 cm、0.045–0.07 s 敲下
                var h = AnyHand(r);
                var spot = GestureScripts.KnockSpot(h) + new Vector3(U(r, -0.04f, 0.04f), 0, U(r, -0.04f, 0.04f));
                var s = GestureScripts.FingerTap(Noisy(seed, white), h, spot, 2, U(r, 0.75f, 1f), U(r, 0.08f, 0.14f), U(r, 0.045f, 0.07f), U(r, 0.03f, 0.10f));
                var ev = G.Run(s);
                return ev.Count(GestureKind.KnockCheck) == 1 && OnlyActions(ev, GestureKind.KnockCheck);
            },
            ["slam_motion"] = (r, seed, white) =>
            {
                // 摔牌已移除（反例）：往牌上拍下去（1/3 捏著牌拍）不能產生任何行動
                var s = GestureScripts.Slam(Noisy(seed, white), AnyHand(r), U(r, 0.2f, 0.35f), U(r, 0.11f, 0.17f), U(r, -0.03f, 0.03f), U(r, -0.03f, 0.03f), r.Next(3) == 0);
                return !G.Run(s).AnyAction();
            },
            ["foldpush"] = (r, seed, white) =>
            {
                var s = GestureScripts.FoldPush(Noisy(seed, white), AnyHand(r), U(r, 0.15f, 0.22f), U(r, 0.35f, 0.8f), 0.025f, U(r, -0.02f, 0.02f));
                var ev = G.Run(s);
                return ev.Count(GestureKind.FoldPushed) == 1 && OnlyActions(ev, GestureKind.FoldPushed);
            },
            ["allin"] = (r, seed, white) =>
            {
                var s = GestureScripts.AllIn(Noisy(seed, white), U(r, 0.14f, 0.24f), U(r, 0.24f, 0.32f), U(r, 0.4f, 0.8f));
                var ev = G.Run(s);
                return ev.Count(GestureKind.AllInPushStarted) == 1 && ev.Count(GestureKind.AllInConfirmed) == 1 &&
                       OnlyActions(ev, GestureKind.AllInPushStarted, GestureKind.AllInConfirmed);
            },
            ["foldslide"] = (r, seed, white) =>
            {
                // 慢慢滑 13–16 cm、1.2–2.0 秒；手掌從 z 0.05–0.07 出發、停在棄牌線前，推完指尖（牌）在 z 0.28–0.33
                var s = GestureScripts.FoldSlide(Noisy(seed, white), AnyHand(r), U(r, 0.13f, 0.16f), U(r, 1.2f, 2.0f), U(r, 0.05f, 0.07f), U(r, -0.02f, 0.02f));
                var ev = G.Run(s);
                return ev.Count(GestureKind.FoldPushed) == 1 && OnlyActions(ev, GestureKind.FoldPushed);
            },
            ["allin_soft"] = (r, seed, white) =>
            {
                // 雙手只推 8.5–10 cm，推完中指指尖剛過下注線 1–4 cm（手掌停在 z 0.21–0.24）
                float dist = U(r, 0.085f, 0.10f);
                float endTip = U(r, 0.31f, 0.34f);
                var center = new Vector3(U(r, -0.05f, 0.20f), 0f, endTip - 0.10f - dist + 0.01f);
                var s = GestureScripts.AllIn(Noisy(seed, white), U(r, 0.14f, 0.24f), dist, U(r, 0.4f, 0.8f), center: center);
                var ev = G.Run(s);
                return ev.Count(GestureKind.AllInPushStarted) == 1 && ev.Count(GestureKind.AllInConfirmed) == 1 &&
                       OnlyActions(ev, GestureKind.AllInPushStarted, GestureKind.AllInConfirmed);
            },
            ["allin_edge"] = (r, seed, white) =>
            {
                // 2026-10-13 實機：從桌緣準備姿勢（手掌 z 0.02–0.045）雙手一起推 12–15 cm、0.3–0.7 s，指尖停在下注線前
                float z = U(r, 0.02f, 0.045f);
                float dist = U(r, 0.12f, 0.15f);
                var s = GestureScripts.AllInFromEdge(Noisy(seed, white), z, dist, U(r, 0.3f, 0.7f), U(r, 0.22f, 0.34f), U(r, -0.04f, 0.06f), desync: U(r, -0.03f, 0.03f));
                var ev = G.Run(s);
                return ev.Count(GestureKind.AllInPushStarted) == 1 && ev.Count(GestureKind.AllInConfirmed) == 1 &&
                       OnlyActions(ev, GestureKind.AllInPushStarted, GestureKind.AllInConfirmed);
            },
            ["cards_look"] = (r, seed, white) =>
            {
                // 拿起底牌看：捏在牌上（±1.5 cm），抬到臉前（高 15–28 cm、往回拉 2–14 cm），停 0.6–2 s，放開；不能有任何行動
                var h = AnyHand(r);
                var grip = GestureScripts.CardGrip + new Vector3(U(r, -0.015f, 0.015f), 0f, U(r, -0.015f, 0.015f));
                var to = new Vector3(U(r, -0.08f, 0.08f), U(r, 0.15f, 0.28f), U(r, -0.02f, 0.10f));
                var s = GestureScripts.LiftCards(Noisy(seed, white), h, to, U(r, 0.6f, 2.0f), U(r, 0.35f, 0.8f), pitch: U(r, -40f, 0f), grip: grip);
                var ev = G.Run(s);
                return ev.Count(GestureKind.CardsLifted) == 1 && ev.Count(GestureKind.CardsReturned) == 1 && !ev.AnyAction();
            },
            ["cards_carry"] = (r, seed, white) =>
            {
                // 捏起底牌拿到棄牌堆放下（手掌最高 10–20 cm、落到 3–7 cm）：棄牌一次
                var s = GestureScripts.CarryCardsToMuck(Noisy(seed, white), AnyHand(r), U(r, 0.10f, 0.20f), U(r, 0.03f, 0.07f), U(r, 0.2f, 0.4f));
                var ev = G.Run(s);
                return ev.Count(GestureKind.FoldPushed) == 1 && OnlyActions(ev, GestureKind.FoldPushed);
            },
            ["cards_release_fwd"] = (r, seed, white) =>
            {
                // 拿起底牌、往前移到棄牌線外低處放開（2026-10-13 實機：放開在 x 0.13–0.18、z 0.27–0.33、高 6.8–10.2 cm）：棄牌一次
                var h = AnyHand(r);
                var s = GestureScripts.GrabCards(Noisy(seed, white), h);
                s.Move(h, U(r, 0.15f, 0.3f), HandTarget.At(SyntheticHands.PalmForPinch(h, new Vector3(U(r, -0.08f, 0.02f), U(r, 0.09f, 0.14f), U(r, 0.0f, 0.08f)))));
                if (r.Next(2) == 0) s.Wait(U(r, 0.5f, 2.0f)); // 一半先看一下
                var at = new Vector3(U(r, 0.0f, 0.22f), U(r, 0.05f, 0.15f), U(r, 0.29f, 0.36f));
                s.Move(h, U(r, 0.25f, 0.5f), HandTarget.At(SyntheticHands.PalmForPinch(h, at)));
                s.Move(h, 0.12, HandTarget.Pinch(0f));
                s.Move(h, 0.3, HandTarget.At(s.Palm(h) + new Vector3(0, 0.06f, 0)));
                s.Move(h, 0.4, HandTarget.At(h == Hand.Right ? SyntheticHands.RightRest : SyntheticHands.LeftRest));
                s.Wait(0.5);
                var ev = G.Run(s);
                return ev.Count(GestureKind.FoldPushed) == 1 && OnlyActions(ev, GestureKind.FoldPushed);
            },
            ["flip"] = (r, seed, white) =>
            {
                var s = GestureScripts.Flip(Noisy(seed, white), AnyHand(r), U(r, 160, 200), U(r, 0.25f, 0.45f), U(r, 0.04f, 0.08f),
                    G.Cards + new Vector3(U(r, -0.02f, 0.02f), 0, U(r, -0.02f, 0.02f)));
                var ev = G.Run(s, G.Ctx(phase: TablePhase.Showdown, canCheck: false));
                return ev.Count(GestureKind.ShowdownFlip) == 1 && OnlyActions(ev, GestureKind.ShowdownFlip);
            },
        };

        /// <summary>咪牌成功：掀到 0.5 以上、手離開後歸 0、沒有任何行動事件，也沒有捏起籌碼。</summary>
        static bool PeekOk(SyntheticHands s, Hand h, float min = 0.5f)
        {
            s.Move(h, 0.5, HandTarget.At(h == Hand.Right ? SyntheticHands.RightRest : SyntheticHands.LeftRest));
            s.Wait(0.3);
            var all = G.Run(s);
            var ev = all.Where(e => e.Kind == GestureKind.PeekChanged && e.Hand == h).ToList();
            return ev.Count > 0 && ev.Max(e => e.Amount) >= min && ev.Last().Amount == 0f && !all.AnyAction() && all.Count(GestureKind.ChipsPicked) == 0;
        }

        public static double Rate(string gesture, bool white = false, int trials = Trials)
        {
            // 種子固定（不用 string.GetHashCode，換執行環境會變）
            int seed = gesture.Aggregate(17, (h, ch) => h * 31 + ch) & 0xffff;
            var rng = new Random(seed ^ (white ? 0x5a5a : 0));
            int ok = 0;
            for (int i = 0; i < trials; i++)
                if (Cases[gesture](rng, 1000 + i, white)) ok++;
            return ok / (double)trials;
        }

        /// <summary>只在報表列出、不設 95% 門檻的案例：knock_gentle 是實機 log 的輕敲（回升只有 1.3–2 cm），
        /// 放寬回升門檻會讓日常雜動誤觸超過 1%，目前刻意不辨識（見 GestureTuning.KnockMinRise）。
        /// knock_soft2（2026-10-13 第二下慢）：1 cm 合成雜訊下，0.22–0.34 m/s 的慢落下常被雜訊切成幾段「落差不足」的下敲，
        /// 只列報表；實機數值的重現在 KnockSoftSecondTests。</summary>
        static readonly HashSet<string> InfoOnly = new HashSet<string> { "knock_gentle", "knock_soft2" };

        static IEnumerable<string> GestureNames => Cases.Keys.Where(k => !InfoOnly.Contains(k));
        public static IEnumerable<string> CaseNames => Cases.Keys;

        [TestCaseSource(nameof(GestureNames))]
        public void Gesture_is_recognised_at_least_95_percent_under_1cm_noise(string gesture)
        {
            double rate = Rate(gesture);
            TestContext.Progress.WriteLine($"[stats] {gesture,-9} 1cm noise: {rate:P1} ({Math.Round(rate * Trials)}/{Trials})");
            Assert.That(rate, Is.GreaterThanOrEqualTo(0.95));
        }

        [Test]
        public void Report_white_noise_stress_rates()
        {
            // 純白雜訊（每幀獨立 1 cm）比真實追蹤嚴苛很多，只記錄、不設門檻
            foreach (var g in Cases.Keys)
                TestContext.Progress.WriteLine($"[stats] {g,-9} white 1cm (stress, info only): {Rate(g, white: true, trials: 100):P0}");
            Assert.Pass();
        }

        // ── 日常雜動 ──
        /// <summary>
        /// 一段約 6–9 秒的雜動：雙手各自隨機做「在空中移動」「慢慢放到桌上靜止」「拿起東西再放下」「停住」。
        /// 刻意不做的事（因為那就是真的手勢）：手貼桌面往前滑、在牌上方翻掌、快速往下拍、連敲桌面、雙手一起推。
        /// 手從桌面移開時先抬起再移動（真人也是如此）。
        /// </summary>
        public static SyntheticHands Fidget(int seed, bool white = false)
        {
            var r = new Random(seed);
            var s = Noisy(seed, white);
            var rest = new[] { SyntheticHands.LeftRest, SyntheticHands.RightRest };
            var pos = new[] { rest[0], rest[1] };
            var roll = new float[2];
            int steps = r.Next(6, 11);
            for (int k = 0; k < steps; k++)
            {
                var h = AnyHand(r);
                int i = (int)h;
                float side = h == Hand.Right ? 1f : -1f;
                int kind = r.Next(4);
                if (kind != 3 && pos[i].Y < 0.06f)
                {
                    // 手從桌面移開：先抬起再移動
                    pos[i].Y += U(r, 0.05f, 0.10f);
                    s.Move(h, U(r, 0.2f, 0.4f), HandTarget.At(pos[i]));
                }
                switch (kind)
                {
                    case 0: // 在空中移動（手腕轉一點）
                    {
                        var to = new Vector3(side * U(r, -0.1f, 0.45f), U(r, 0.08f, 0.30f), U(r, 0.0f, 0.55f));
                        float speed = U(r, 0.15f, 0.8f);
                        roll[i] = side * U(r, -40f, 40f);
                        s.Move(h, Math.Max(0.15, Vector3.Distance(pos[i], to) / speed), HandTarget.At(to).WithRoll(roll[i]).WithPitch(U(r, -20f, 20f)));
                        pos[i] = to;
                        break;
                    }
                    case 1: // 慢慢放到桌上，靜止
                    {
                        var above = new Vector3(side * U(r, -0.05f, 0.42f), Math.Max(pos[i].Y, 0.10f), U(r, 0.0f, 0.5f));
                        s.Move(h, Math.Max(0.2, Vector3.Distance(pos[i], above) / U(r, 0.15f, 0.5f)), HandTarget.At(above));
                        var down = new Vector3(above.X, 0.025f, above.Z);
                        roll[i] = side * U(r, -15f, 15f);
                        s.Move(h, (above.Y - down.Y) / U(r, 0.08f, 0.25f), HandTarget.At(down).WithRoll(roll[i]).WithPitch(0));
                        s.Wait(U(r, 0.5f, 2.0f));
                        pos[i] = down;
                        break;
                    }
                    case 2: // 拿起東西再放下（就近）
                    {
                        var at = new Vector3(side * U(r, -0.05f, 0.42f), 0.03f, U(r, 0.0f, 0.2f));
                        var above = at + new Vector3(0, Math.Max(0.08f, pos[i].Y - 0.03f), 0);
                        s.Move(h, Math.Max(0.2, Vector3.Distance(pos[i], above) / U(r, 0.2f, 0.5f)), HandTarget.At(above).WithRoll(0).WithPitch(0));
                        s.Move(h, (above.Y - at.Y) / U(r, 0.08f, 0.25f), HandTarget.At(at));
                        s.Move(h, 0.15, HandTarget.Pinch(1f));
                        var lifted = at + new Vector3(U(r, -0.08f, 0.08f), U(r, 0.05f, 0.15f), U(r, -0.05f, 0.08f));
                        s.Move(h, U(r, 0.3f, 0.7f), HandTarget.At(lifted));
                        var put = new Vector3(lifted.X, 0.03f, lifted.Z);
                        s.Move(h, (lifted.Y - put.Y) / U(r, 0.08f, 0.25f), HandTarget.At(put));
                        s.Move(h, 0.12, HandTarget.Pinch(0f));
                        s.Move(h, 0.3, HandTarget.At(put + new Vector3(0, 0.07f, 0)));
                        pos[i] = put + new Vector3(0, 0.07f, 0);
                        roll[i] = 0;
                        break;
                    }
                    default:
                        s.Wait(U(r, 0.3f, 1.0f));
                        break;
                }
            }
            s.Wait(1.6); // 讓任何待確認的下注或倒數跑完
            return s;
        }

        static readonly TablePhase[] Phases = { TablePhase.Preflop, TablePhase.Flop, TablePhase.Turn, TablePhase.River, TablePhase.Showdown };

        /// <summary>early：還沒輪到、但還在這一手（CanActEarly）；誤觸算提前棄牌與「太早了」（ActionTooEarly）。</summary>
        public static (int falseRuns, int frames, List<string> samples) FidgetFalseTriggers(int runs, bool white = false, bool early = false)
        {
            int bad = 0, frames = 0;
            var samples = new List<string>();
            for (int i = 0; i < runs; i++)
            {
                var s = Fidget(50000 + i, white);
                frames += s.Frames.Count;
                var ctx = G.Ctx(turn: !early, phase: early ? Phases[i % 4] : Phases[i % Phases.Length]);
                ctx.CanActEarly = early;
                var ev = G.Run(s, ctx);
                var actions = ev.Where(e => e.IsAction || e.Kind == GestureKind.ActionTooEarly).ToList();
                if (actions.Count > 0)
                {
                    bad++;
                    if (samples.Count < 8) samples.Add($"run {i}: " + string.Join(" | ", actions));
                }
            }
            return (bad, frames, samples);
        }

        [Test]
        public void Everyday_fidgeting_false_trigger_rate_is_at_most_1_percent()
        {
            var (bad, frames, samples) = FidgetFalseTriggers(FidgetRuns);
            double rate = bad / (double)FidgetRuns;
            TestContext.Progress.WriteLine($"[stats] fidget 1cm noise: {bad}/{FidgetRuns} runs with an action event = {rate:P2} ({frames} frames, {frames / 72.0 / 60.0:0.0} min)");
            foreach (var x in samples) TestContext.Progress.WriteLine("[stats]   " + x);
            Assert.That(rate, Is.LessThanOrEqualTo(0.01));
        }

        [Test]
        public void Everyday_fidgeting_before_the_players_turn_rarely_folds_early_or_says_too_early()
        {
            var (bad, _, samples) = FidgetFalseTriggers(FidgetRuns, early: true);
            double rate = bad / (double)FidgetRuns;
            TestContext.Progress.WriteLine($"[stats] fidget early (CanActEarly) 1cm noise: {bad}/{FidgetRuns} runs with an early fold or too-early event = {rate:P2}");
            foreach (var x in samples) TestContext.Progress.WriteLine("[stats]   " + x);
            Assert.That(rate, Is.LessThanOrEqualTo(0.01));
        }

        [Test]
        public void Report_fidget_white_noise_stress()
        {
            var (bad, _, samples) = FidgetFalseTriggers(200, white: true);
            TestContext.Progress.WriteLine($"[stats] fidget white 1cm (stress, info only): {bad}/200 = {bad / 200.0:P1}");
            foreach (var x in samples.Take(3)) TestContext.Progress.WriteLine("[stats]   " + x);
            Assert.Pass();
        }

        [Test]
        public void Per_frame_cost_is_well_under_budget()
        {
            var frames = new List<HandsFrame>();
            for (int i = 0; i < 60; i++) frames.AddRange(Fidget(90000 + i).Frames);
            for (int i = 0; i < 20; i++) frames.AddRange(GestureScripts.AllIn(new SyntheticHands(seed: i).WithNoise(0.01f)).Frames);
            // 時間必須單調：串接時重編時間
            for (int i = 0; i < frames.Count; i++) frames[i].Time = i / 72.0;
            var ctx = G.Ctx();
            var warm = new GestureRecognizer(G.Layout);
            for (int i = 0; i < 2000; i++) warm.Update(frames[i], ctx);

            var r = new GestureRecognizer(G.Layout);
            var sw = Stopwatch.StartNew();
            int n = 0;
            for (int rep = 0; rep < 3; rep++)
                foreach (var f in frames) { r.Update(f, ctx); n++; }
            sw.Stop();
            double usPerFrame = sw.Elapsed.TotalMilliseconds * 1000.0 / n;
            TestContext.Progress.WriteLine($"[perf] {n} Update() calls, mean {usPerFrame:0.00} µs/frame (Mono, {Environment.ProcessorCount} cores host)");
            Assert.That(usPerFrame, Is.LessThan(200.0));
        }
    }
}
