using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using NUnit.Framework;
using Poker.Gestures;

namespace Poker.Tests
{
    /// <summary>
    /// 換位置校準（2026-10-15 實機：「校準過的桌子，轉向或換房間後就改不了」）：桌子鎖定（校準過／錨點放回）時，
    /// 雙手平放在「明顯不同的地方」靜止 2 秒就把桌子搬過去，不用先叫出提示。在自己的牌桌上正常玩絕對不能觸發。
    /// </summary>
    public class TableRelocateTests
    {
        const double Dt = 1.0 / 72.0;
        const float Rest = 0.025f;
        /// <summary>頭在玩家座位（牌桌座標）：桌緣後 30 cm、桌面上 45 cm。</summary>
        static readonly Vector3 SeatHead = new Vector3(0f, 0.45f, -0.3f);
        static readonly Vector3 HandsArea = new Vector3(0f, 0f, G.Cards.Z);

        /// <summary>
        /// 雙手平放靜止 seconds 秒：先排成「坐在目前牌桌前」的樣子（手牌兩側，各離中線 half），
        /// 再整組繞手牌區轉 yaw 度、平移 offset（＝另一個位置的另一張桌子）。手腕／指尖跟著轉，所以有手指方向。
        /// </summary>
        static SyntheticHands FlatAt(float yaw, Vector3 offset, double seconds, float half = 0.15f, float noise = 0f, int seed = 1)
        {
            var s = noise > 0 ? new SyntheticHands(seed: seed).WithNoise(noise, 0.5f, 0f) : new SyntheticHands(seed: seed);
            s.Set(Hand.Left, new Vector3(-half, Rest, HandsArea.Z)).Set(Hand.Right, new Vector3(half, Rest, HandsArea.Z));
            s.Wait(seconds);
            foreach (var f in s.Frames) Move(f, yaw, offset);
            return s;
        }

        static Quaternion Yaw(float deg) => Quaternion.CreateFromAxisAngle(Vector3.UnitY, deg * (float)Math.PI / 180f);

        /// <summary>把整個畫面的雙手繞手牌區（y 軸）轉 yaw、再平移 offset。</summary>
        static void Move(HandsFrame f, float yaw, Vector3 offset)
        {
            var q = Yaw(yaw);
            foreach (var h in new[] { f.Left, f.Right })
                for (int j = 0; j < HandPose.JointCount; j++)
                {
                    h.Positions[j] = HandsArea + Vector3.Transform(h.Positions[j] - HandsArea, q) + offset;
                    h.Rotations[j] = Quaternion.Concatenate(h.Rotations[j], q);
                }
        }

        /// <summary>同一個轉換套在頭上（換位置時頭跟著人走）。</summary>
        static Vector3 HeadAt(float yaw, Vector3 offset) => HandsArea + Vector3.Transform(SeatHead - HandsArea, Yaw(yaw)) + offset;

        sealed class Run
        {
            public TableRelocation? Result;
            public double At = -1;
            public int Triggers;
            public readonly List<string> Log = new List<string>();
        }

        static Run Feed(IEnumerable<HandsFrame> frames, Vector3? head, TableRelocateDetector d = null)
        {
            var run = new Run();
            d = d ?? new TableRelocateDetector(G.Layout);
            d.Log = run.Log.Add;
            double t0 = double.NaN;
            foreach (var f in frames)
            {
                if (double.IsNaN(t0)) t0 = f.Time;
                var r = d.Step(f, head);
                if (!r.HasValue) continue;
                run.Triggers++;
                if (run.Result == null) { run.Result = r; run.At = f.Time - t0; }
            }
            return run;
        }

        // 另一個房間的桌子：往左 1.2 m、低 8 cm、轉 90°（向左轉）
        static readonly Vector3 OtherRoom = new Vector3(-1.2f, -0.08f, 0.4f);

        [Test]
        public void Holding_both_palms_flat_at_a_clearly_different_spot_relocates_after_about_2_seconds()
        {
            var run = Feed(FlatAt(-90f, OtherRoom, 3.0).Frames, HeadAt(-90f, OtherRoom));
            Assert.IsTrue(run.Result.HasValue, string.Join("\n", run.Log));
            Assert.That(run.At, Is.EqualTo(TableRelocateDetector.HoldSeconds).Within(0.05));
            var r = run.Result.Value;
            Assert.That(r.Calibration.SurfaceY, Is.EqualTo(-0.08f + Rest - SurfaceCalibrator.PalmThickness).Within(0.002f));
            Assert.That(r.Calibration.HandsCenter.X, Is.EqualTo(-1.2f).Within(0.01f));
            Assert.That(r.Calibration.HandsCenter.Z, Is.EqualTo(HandsArea.Z + 0.4f).Within(0.01f));
            Assert.That(r.YawDegrees, Is.EqualTo(-90f).Within(3f));
            Assert.That(r.Distance, Is.EqualTo(new Vector2(-1.2f, 0.4f).Length()).Within(0.01f));
            Assert.That(run.Triggers, Is.EqualTo(1), "放著不動只觸發一次");
        }

        [Test]
        public void Hold_is_longer_than_the_prompted_calibration_and_shorter_holds_do_nothing()
        {
            Assert.That(TableRelocateDetector.HoldSeconds, Is.GreaterThan(SurfaceCalibrator.DwellSeconds + 0.5));
            Assert.That(TableRelocateDetector.HoldSeconds, Is.GreaterThanOrEqualTo(2.0));
            var run = Feed(FlatAt(-90f, OtherRoom, TableRelocateDetector.HoldSeconds - 0.15).Frames, HeadAt(-90f, OtherRoom));
            Assert.IsFalse(run.Result.HasValue);
        }

        // 新的桌面比目前的低 12 cm（不在目前的牌桌上），不轉：只有距離決定
        [TestCase(0.25f, false)]
        [TestCase(0.28f, false)]
        [TestCase(0.33f, true)]
        [TestCase(0.60f, true)]
        public void Distance_from_the_current_hands_area_must_exceed_30_cm(float dist, bool relocates)
        {
            var off = new Vector3(dist, -0.12f, 0f);
            var run = Feed(FlatAt(0f, off, 3.0).Frames, HeadAt(0f, off));
            Assert.That(run.Result.HasValue, Is.EqualTo(relocates), string.Join("\n", run.Log));
        }

        // 同一張桌子、同一個位置，人轉了方向（坐姿轉了）：只有 yaw 決定
        [TestCase(15f, false)]
        [TestCase(22f, false)]
        [TestCase(30f, true)]
        [TestCase(-30f, true)]
        [TestCase(90f, true)]
        [TestCase(180f, true)]
        public void Yaw_must_differ_by_more_than_25_degrees(float yaw, bool relocates)
        {
            var run = Feed(FlatAt(yaw, Vector3.Zero, 3.0).Frames, HeadAt(yaw, Vector3.Zero));
            Assert.That(run.Result.HasValue, Is.EqualTo(relocates), string.Join("\n", run.Log));
            if (relocates) Assert.That(Math.Abs(DeltaAngle(run.Result.Value.YawDegrees, yaw)), Is.LessThan(3f));
        }

        static float DeltaAngle(float a, float b)
        {
            float d = (a - b) % 360f;
            if (d > 180f) d -= 360f;
            if (d < -180f) d += 360f;
            return d;
        }

        [Test]
        public void Resting_both_hands_on_own_cards_for_a_long_time_never_relocates_and_logs_too_close_once()
        {
            var run = Feed(FlatAt(0f, Vector3.Zero, 12.0).Frames, SeatHead);
            Assert.IsFalse(run.Result.HasValue);
            var close = run.Log.Where(l => l.Contains("(too close)")).ToList();
            Assert.That(close.Count, Is.EqualTo(1), string.Join("\n", run.Log));
            StringAssert.StartsWith("[Table] relocate candidate held=", close[0]);
        }

        [Test]
        public void Both_hands_flat_far_out_on_the_current_felt_is_still_the_current_table()
        {
            // 雙手平放在彩池附近（離手牌區 40 cm，但就在目前的絨布上、同高度）
            var run = Feed(FlatAt(0f, new Vector3(0f, 0f, 0.40f), 4.0).Frames, SeatHead);
            Assert.IsFalse(run.Result.HasValue, string.Join("\n", run.Log));
            // 籌碼那一側（雙手都在右邊）
            run = Feed(FlatAt(0f, new Vector3(0.30f, 0f, 0.05f), 4.0).Frames, SeatHead);
            Assert.IsFalse(run.Result.HasValue, string.Join("\n", run.Log));
        }

        /// <summary>坐著等 AI 時雙手平放在大腿上／扶手上（比桌面低、在身前）：不是另一張桌子。</summary>
        [TestCase(0f, -0.20f, -0.15f, 0.17f, 0f)]     // 大腿
        [TestCase(0f, -0.25f, -0.05f, 0.15f, 0f)]     // 大腿靠膝蓋
        [TestCase(0f, -0.15f, -0.30f, 0.30f, 0f)]     // 扶手
        [TestCase(0f, -0.20f, -0.15f, 0.17f, 40f)]    // 坐歪了、手在大腿上也轉了
        [TestCase(0.15f, -0.18f, -0.20f, 0.17f, -35f)]
        public void Hands_flat_on_the_lap_or_armrests_never_relocate(float x, float y, float z, float half, float yaw)
        {
            var off = new Vector3(x, y, z - HandsArea.Z);
            var run = Feed(FlatAt(yaw, off, 4.0, half: half).Frames, SeatHead);
            Assert.IsFalse(run.Result.HasValue, string.Join("\n", run.Log));
            run = Feed(FlatAt(yaw, off, 4.0, half: half).Frames, null);
            Assert.IsFalse(run.Result.HasValue, "without head: " + string.Join("\n", run.Log));
        }

        [Test]
        public void Hands_resting_at_different_depths_with_fingers_forward_do_not_count_as_turned()
        {
            // 左手在手牌旁、右手往前放在籌碼前方：兩掌連線斜了，但手指都朝前（沒轉身）
            var s = new SyntheticHands();
            s.Set(Hand.Left, new Vector3(-0.12f, Rest, 0.10f)).Set(Hand.Right, new Vector3(0.20f, Rest, 0.30f));
            s.Wait(4.0);
            var run = Feed(s.Frames, SeatHead);
            Assert.IsFalse(run.Result.HasValue, string.Join("\n", run.Log));
        }

        [Test]
        public void Moving_hands_untracked_hand_or_palms_up_do_not_relocate()
        {
            var moving = FlatAt(-90f, OtherRoom, 3.0);
            for (int i = 0; i < moving.Frames.Count; i++)
            {
                var d = new Vector3(0.03f * (float)Math.Sin(i * Dt * 6), 0f, 0f);
                foreach (var h in new[] { moving.Frames[i].Left, moving.Frames[i].Right })
                    for (int j = 0; j < HandPose.JointCount; j++) h.Positions[j] += d;
            }
            Assert.IsFalse(Feed(moving.Frames, null).Result.HasValue, "moving");

            var lost = FlatAt(-90f, OtherRoom, 3.0);
            for (int i = 0; i < lost.Frames.Count; i += 100) lost.Frames[i].Left.Tracked = false;   // 每 1.4 s 掉一幀
            Assert.IsFalse(Feed(lost.Frames, null).Result.HasValue, "tracking drops restart the hold");

            var up = FlatAt(-90f, OtherRoom, 3.0);
            var flip = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, (float)Math.PI);
            foreach (var f in up.Frames)
                for (int j = 0; j < HandPose.JointCount; j++) f.Right.Rotations[j] = Quaternion.Concatenate(flip, f.Right.Rotations[j]);
            Assert.IsFalse(Feed(up.Frames, null).Result.HasValue, "palm up");
        }

        [Test]
        public void After_a_relocation_the_hands_must_lift_before_it_can_trigger_again()
        {
            var d = new TableRelocateDetector(G.Layout);
            var frames = FlatAt(-90f, OtherRoom, 6.0).Frames;
            var run = Feed(frames, null, d);
            Assert.That(run.Triggers, Is.EqualTo(1));
            d.Reset();   // 桌子搬過去之後 HandSurfaceCalibration 會 Reset：還是要先抬手
            var again = Feed(FlatAt(-90f, OtherRoom, 3.0).Frames.Select(f => { f.Time += 10; return f; }), null, d);
            Assert.That(again.Triggers, Is.EqualTo(0), "same hold continuing after Reset");
            var lifted = new HandsFrame { Time = 20 };
            d.Step(lifted, null);
            var third = Feed(FlatAt(-90f, OtherRoom, 3.0).Frames.Select(f => { f.Time += 21; return f; }), null, d);
            Assert.That(third.Triggers, Is.EqualTo(1));
        }

        [Test]
        public void Logs_still_holding_for_a_new_spot_and_released_when_lifted_early()
        {
            var s = FlatAt(-90f, OtherRoom, 1.5);
            var frames = s.Frames.ToList();
            frames.Add(new HandsFrame { Time = frames[^1].Time + Dt });
            var run = Feed(frames, null);
            Assert.IsFalse(run.Result.HasValue);
            Assert.That(run.Log.Count(l => l.StartsWith("[Table] relocate candidate held=1.0s (still holding)")), Is.EqualTo(1), string.Join("\n", run.Log));
            Assert.That(run.Log.Count(l => l.StartsWith("[Table] relocate candidate held=1.5s (released)")), Is.EqualTo(1), string.Join("\n", run.Log));
        }

        [Test]
        public void Hands_above_the_eyes_or_far_below_are_not_a_desk()
        {
            var high = new Vector3(-1.2f, 0.40f, 0.4f);   // 手比眼睛低不到 15 cm
            Assert.IsFalse(Feed(FlatAt(-90f, high, 3.0).Frames, SeatHead).Result.HasValue);
            var floor = new Vector3(-1.2f, -0.6f, 0.4f);   // 手比眼睛低 1 m（地上）
            Assert.IsFalse(Feed(FlatAt(-90f, floor, 3.0).Frames, SeatHead).Result.HasValue);
        }

        [Test]
        public void Classify_reports_distance_and_yaw()
        {
            var run = Feed(FlatAt(40f, Vector3.Zero, 2.5).Frames, SeatHead);
            Assert.IsTrue(run.Result.HasValue);
            var spot = TableRelocateDetector.Classify(run.Result.Value.Calibration, SeatHead, G.Layout, out var dist, out var yaw, out _);
            Assert.That(spot, Is.EqualTo(RelocateSpot.NewSpot));
            Assert.That(dist, Is.LessThan(0.02f));
            Assert.That(yaw, Is.EqualTo(40f).Within(2f));
        }

        // ---------- 正常遊戲不能觸發 ----------

        static IEnumerable<string> Recordings()
        {
            var dir = RecordingCheck.FindRecordingsDir();
            return dir == null ? Enumerable.Empty<string>() : Directory.GetFiles(dir, "*.hands").OrderBy(f => f, StringComparer.Ordinal);
        }

        [Test]
        public void Real_recordings_never_relocate()
        {
            var files = Recordings().ToList();
            if (files.Count == 0) Assert.Ignore("沒有錄製檔");
            var hits = new List<string>();
            int frames = 0, closeHolds = 0, candidates = 0;
            foreach (var path in files)
            {
                var rec = HandRecording.Load(path);
                frames += rec.Frames.Count;
                foreach (var head in new Vector3?[] { SeatHead, null })
                {
                    var run = Feed(rec.Frames, head);
                    closeHolds += run.Log.Count(l => l.Contains("(too close)"));
                    candidates += run.Log.Count(l => l.Contains("(still holding)"));
                    if (run.Triggers > 0) hits.Add($"{Path.GetFileName(path)} head={(head.HasValue ? "seat" : "none")} t={run.At:0.00} dist={run.Result.Value.Distance:0.00} yaw={run.Result.Value.YawDegrees:0}");
                }
            }
            TestContext.Progress.WriteLine($"[stats] relocate on {files.Count} real recordings ({frames} frames, {frames / 72.0 / 60.0:0.0} min) x2 head modes: {hits.Count} relocations, {closeHolds} too-close holds, {candidates} new-spot candidates");
            Assert.IsEmpty(hits, string.Join("\n", hits));
        }

        static readonly Func<SyntheticHands>[] GameScripts =
        {
            () => GestureScripts.Peek(new SyntheticHands(seed: 2).WithNoise(0.003f), Hand.Right, hold: 2.5),
            () => GestureScripts.RollPeek(new SyntheticHands(seed: 3).WithNoise(0.003f), Hand.Left, hold: 2.5),
            () => GestureScripts.Knock(new SyntheticHands(seed: 4).WithNoise(0.003f), Hand.Right, GestureScripts.KnockSpot(Hand.Right)).Wait(3),
            () => GestureScripts.AllIn(new SyntheticHands(seed: 5).WithNoise(0.003f), holdAfter: 4.0),
            () => GestureScripts.AllInFromEdge(new SyntheticHands(seed: 6).WithNoise(0.003f), holdAfter: 4.0),
            () => GestureScripts.RestingDrift(new SyntheticHands(seed: 7).WithNoise(0.003f)).Wait(4),
            () => GestureScripts.FoldPush(new SyntheticHands(seed: 8).WithNoise(0.003f)).Wait(3),
            () => GestureScripts.PickChips(new SyntheticHands(seed: 9).WithNoise(0.003f)).Wait(3),
            () => GestureScripts.GrabCards(new SyntheticHands(seed: 10).WithNoise(0.003f)).Wait(3),
            () => AssistPress(new SyntheticHands(seed: 11).WithNoise(0.003f)),
            () => new SyntheticHands(seed: 12).WithNoise(0.003f).Wait(10),   // 雙手放桌邊休息（預設姿勢）
        };

        /// <summary>按桌緣左側的備援按鈕：右手食指朝下按、左手放在手牌旁休息，按住 2.5 s。</summary>
        static SyntheticHands AssistPress(SyntheticHands s)
        {
            s.Set(Hand.Left, new Vector3(-0.15f, Rest, G.Cards.Z));
            s.Move(Hand.Right, 0.6, HandTarget.At(-0.33f, 0.06f, 0.05f).WithPitch(45f));
            s.Move(Hand.Right, 0.3, HandTarget.At(-0.33f, 0.035f, 0.05f).WithPitch(45f));
            s.Wait(2.5);
            return s;
        }

        [Test]
        public void Scripted_game_gestures_never_relocate()
        {
            var hits = new List<string>();
            for (int i = 0; i < GameScripts.Length; i++)
            {
                var run = Feed(GameScripts[i]().Frames, SeatHead);
                if (run.Triggers > 0) hits.Add($"script {i}: dist={run.Result.Value.Distance:0.00} yaw={run.Result.Value.YawDegrees:0}");
            }
            Assert.IsEmpty(hits, string.Join("\n", hits));
        }

        static float U(Random r, float a, float b) => a + (float)r.NextDouble() * (b - a);

        /// <summary>
        /// 在目前的絨布上隨機平放雙手休息 4 s（低雜訊才停得住）：位置在伸手可及的範圍、兩手前後不一、各自轉 ±20°。
        /// 這就是「在自己牌桌上把手放著」的各種樣子。
        /// </summary>
        static SyntheticHands RandomRestOnFelt(Random r, int seed)
        {
            var s = new SyntheticHands(seed: seed).WithNoise(0.002f, 0.5f, 0f);
            var l = new Vector3(U(r, -0.40f, 0.05f), Rest, U(r, -0.02f, 0.45f));
            var rt = new Vector3(U(r, Math.Max(l.X + 0.15f, -0.1f), 0.42f), Rest, U(r, -0.02f, 0.45f));
            s.Move(0.5, HandTarget.At(rt + new Vector3(0, 0.06f, 0)), HandTarget.At(l + new Vector3(0, 0.06f, 0)));
            s.Move(0.3, HandTarget.At(rt).WithRoll(U(r, -15, 15)), HandTarget.At(l).WithRoll(U(r, -15, 15)));
            s.Wait(4.0);
            // 各自轉 ±20°（繞手掌）：手指方向各不相同
            float yl = U(r, -20, 20), yr = U(r, -20, 20);
            foreach (var f in s.Frames)
            {
                TurnHand(f.Left, yl);
                TurnHand(f.Right, yr);
            }
            return s;
        }

        static void TurnHand(HandPose h, float yaw)
        {
            var q = Yaw(yaw);
            var palm = h.Position(HandJoint.Palm);
            for (int j = 0; j < HandPose.JointCount; j++)
            {
                h.Positions[j] = palm + Vector3.Transform(h.Positions[j] - palm, q);
                h.Rotations[j] = Quaternion.Concatenate(h.Rotations[j], q);
            }
        }

        [Test, Category("Stats")]
        public void Resting_anywhere_on_the_current_felt_never_relocates()
        {
            const int Runs = 1000;
            var r = new Random(4242);
            var hits = new List<string>();
            int closeHolds = 0;
            for (int i = 0; i < Runs; i++)
            {
                var run = Feed(RandomRestOnFelt(r, 70000 + i).Frames, SeatHead);
                closeHolds += run.Log.Count(l => l.Contains("(too close)"));
                if (run.Triggers > 0 && hits.Count < 10) hits.Add($"run {i}: dist={run.Result.Value.Distance:0.00} yaw={run.Result.Value.YawDegrees:0}");
                else if (run.Triggers > 0) hits.Add("");
            }
            TestContext.Progress.WriteLine($"[stats] relocate random rests on felt: {hits.Count}/{Runs} relocations ({closeHolds} holds reached 2 s and were rejected as too close)");
            Assert.IsEmpty(hits, string.Join("\n", hits));
        }

        [Test, Category("Stats")]
        public void Resting_on_the_lap_or_armrests_never_relocates()
        {
            const int Runs = 500;
            var r = new Random(5151);
            var hits = new List<string>();
            for (int i = 0; i < Runs; i++)
            {
                var off = new Vector3(U(r, -0.2f, 0.2f), U(r, -0.35f, -0.08f), U(r, -0.45f, 0.03f) - HandsArea.Z);
                var run = Feed(FlatAt(U(r, -45, 45), off, 3.0, half: U(r, 0.12f, 0.3f), noise: 0.002f, seed: 60000 + i).Frames, i % 2 == 0 ? SeatHead : (Vector3?)null);
                if (run.Triggers > 0) hits.Add($"run {i}: off=({off.X:0.00},{off.Y:0.00},{off.Z:0.00})");
            }
            TestContext.Progress.WriteLine($"[stats] relocate random lap/armrest rests: {hits.Count}/{Runs} relocations");
            Assert.IsEmpty(hits, string.Join("\n", hits.Take(10)));
        }

        [Test, Category("Stats")]
        public void Everyday_fidgeting_never_relocates()
        {
            const int Runs = 500;
            int bad = 0, frames = 0;
            foreach (var white in new[] { false, true })
                for (int i = 0; i < Runs; i++)
                {
                    var s = RobustnessStatsTests.Fidget(50000 + i, white);
                    frames += s.Frames.Count;
                    if (Feed(s.Frames, SeatHead).Triggers > 0) bad++;
                }
            TestContext.Progress.WriteLine($"[stats] relocate fidget (1 cm noise, smooth+white) {bad}/{Runs * 2} runs relocated ({frames / 72.0 / 60.0:0.0} min)");
            Assert.That(bad, Is.EqualTo(0));
        }

        [Test, Category("Stats")]
        public void Relocating_to_random_new_spots_is_detected()
        {
            const int Runs = 300;
            var r = new Random(777);
            int ok = 0;
            var misses = new List<string>();
            for (int i = 0; i < Runs; i++)
            {
                // 一半：換到別處（≥ 45 cm、任何方向）；一半：原地轉身 35–180°
                bool far = i % 2 == 0;
                float yaw = far ? U(r, -180, 180) : (r.Next(2) == 0 ? 1 : -1) * U(r, 35, 180);
                double ang = r.NextDouble() * Math.PI * 2;
                float d = far ? U(r, 0.45f, 3f) : 0f;
                var off = new Vector3((float)Math.Cos(ang) * d, far ? U(r, -0.15f, 0.15f) : 0f, (float)Math.Sin(ang) * d);
                if (far && Math.Abs(off.Y) < 0.05f && Math.Abs(off.X) < 0.5f && off.Z > -0.25f && off.Z < 0.6f) off.Y = -0.08f;   // 剛好落在目前絨布上同高度的不算「換地方」
                // 比桌面低又在身前（大腿／扶手的位置）刻意不算換地方：改成同高度
                if (far && off.Y < -0.04f && Math.Abs(off.X) <= 0.36f && HandsArea.Z + off.Z <= 0.06f) off.Y = 0f;
                var run = Feed(FlatAt(yaw, off, 3.0, half: U(r, 0.12f, 0.22f), noise: 0.002f, seed: 8000 + i).Frames, HeadAt(yaw, off));
                if (run.Triggers == 1) ok++;
                else if (misses.Count < 10) misses.Add($"run {i}: yaw={yaw:0} off=({off.X:0.00},{off.Y:0.00},{off.Z:0.00}) triggers={run.Triggers} {string.Join(" | ", run.Log)}");
            }
            TestContext.Progress.WriteLine($"[stats] relocate to new spots: {ok}/{Runs} detected");
            Assert.That(ok, Is.GreaterThanOrEqualTo(Runs * 95 / 100), string.Join("\n", misses));
        }
    }
}
