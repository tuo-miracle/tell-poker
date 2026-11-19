using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using Poker.Gestures;

namespace Poker.Tests
{
    /// <summary>新手教學的示範手（ghost hands）：從錄製挑最乾淨的一段、裁到手勢片段，烘焙成遊戲讀得到的 .bytes。</summary>
    public class GhostBakeTests
    {
        static string Scratch()
        {
            var dir = Path.Combine(Path.GetTempPath(), "poker-ghost-" + Guid.NewGuid().ToString("N").Substring(0, 8));
            Directory.CreateDirectory(dir);
            return dir;
        }

        static HandRecording ToRecording(SyntheticHands s)
        {
            var rec = new HandRecording();
            rec.Frames.AddRange(s.Frames);
            return rec;
        }

        static void Save(SyntheticHands s, string path)
        {
            using (var fs = File.Create(path))
            using (var w = new HandRecordingWriter(fs, ""))
                foreach (var f in s.Frames) w.Write(f);
        }

        [Test]
        public void Trim_rebases_time_to_zero_and_keeps_only_the_window()
        {
            var rec = ToRecording(GestureScripts.Knock(new SyntheticHands(), Hand.Right, GestureScripts.KnockSpot(Hand.Right)));
            double t0 = rec.Frames[0].Time;
            var clip = GhostBake.Trim(rec, t0 + 0.5, t0 + 1.5, "note");
            Assert.That(clip.Frames[0].Time, Is.EqualTo(0).Within(1e-9));
            Assert.That(clip.Frames[clip.Frames.Count - 1].Time, Is.LessThanOrEqualTo(1.0 + 1e-6));
            Assert.That(clip.Frames.Count, Is.InRange(70, 74), "72 Hz × 1 s");
            Assert.That(clip.Note, Is.EqualTo("note"));
            Assert.That(rec.Frames[0].Time, Is.EqualTo(t0), "source is not modified");
        }

        [Test]
        public void Trim_hides_a_hand_that_never_moves_and_keeps_both_for_two_hand_gestures()
        {
            var knock = GhostBake.Trim(ToRecording(GestureScripts.Knock(new SyntheticHands(), Hand.Right, GestureScripts.KnockSpot(Hand.Right))), 0, 10, "");
            Assert.That(knock.Frames.All(f => f.Right.Tracked), Is.True);
            Assert.That(knock.Frames.All(f => !f.Left.Tracked), Is.True, "the resting left hand is not drawn");

            var allIn = GhostBake.Trim(ToRecording(GestureScripts.AllIn(new SyntheticHands())), 0, 10, "");
            Assert.That(allIn.Frames.All(f => f.Left.Tracked && f.Right.Tracked), Is.True);
        }

        [Test]
        public void Choose_prefers_a_passing_smooth_take_over_noisy_or_failing_ones()
        {
            var dir = Scratch();
            var spot = GestureScripts.KnockSpot(Hand.Right);
            Save(GestureScripts.Knock(new SyntheticHands(seed: 3).WithWhiteNoise(0.003f, 2f, 0f), Hand.Right, spot), Path.Combine(dir, "knock_a.hands"));
            Save(GestureScripts.Knock(new SyntheticHands(), Hand.Right, spot), Path.Combine(dir, "knock_b.hands"));
            Save(GestureScripts.Knock(new SyntheticHands(), Hand.Right, spot, taps: 1), Path.Combine(dir, "knock_c.hands"));
            var spec = GhostBake.Specs.First(s => s.Name == "knock");
            var pick = GhostBake.Choose(spec, Directory.GetFiles(dir, "*.hands"));
            Assert.That(pick, Is.Not.Null);
            Assert.That(Path.GetFileName(pick.Source), Is.EqualTo("knock_b.hands"), pick.Report);
        }

        [Test]
        public void Bake_trims_around_the_key_event_and_the_clip_still_triggers_it()
        {
            var dir = Scratch();
            var src = GestureScripts.Knock(new SyntheticHands(), Hand.Right, GestureScripts.KnockSpot(Hand.Right));
            src.Wait(3.0);
            Save(src, Path.Combine(dir, "knock_long.hands"));
            var spec = GhostBake.Specs.First(s => s.Name == "knock");
            var pick = GhostBake.Choose(spec, Directory.GetFiles(dir, "*.hands"));
            Assert.That(pick.Clip.Frames.Last().Time, Is.LessThanOrEqualTo(spec.Lead + spec.Tail + 1e-6));
            Assert.That(pick.Clip.Frames.Last().Time, Is.LessThan(src.Frames.Last().Time - 1.0), "the 3 s of idle at the end is cut");
            Assert.That(GhostBake.Fires(spec, pick.Clip), Is.True);
            StringAssert.Contains("knock_long.hands", pick.Clip.Note);
            Assert.That(pick.Clip.Frames.All(f => !f.Left.Tracked && f.Right.Tracked), Is.True, "one-hand gesture: only the hand that knocked is drawn");
        }

        [Test]
        public void Every_spec_has_a_unique_name_and_a_source()
        {
            Assert.That(GhostBake.Specs.Select(s => s.Name).Distinct().Count(), Is.EqualTo(GhostBake.Specs.Length));
            foreach (var s in GhostBake.Specs)
                Assert.That(s.Groups.Length > 0 || s.Synthetic != null, Is.True, s.Name);
            CollectionAssert.IsSubsetOf(new[] { "peek", "rollpeek", "look", "chips", "bet", "knock", "fold", "allin", "flip" }, GhostBake.Specs.Select(s => s.Name).ToArray());
        }

        // ── 已烘焙、commit 進 Resources 的片段：每段回放都要真的觸發它示範的手勢 ──
        static string[] SpecNames() => GhostBake.Specs.Select(s => s.Name).ToArray();

        [TestCaseSource(nameof(SpecNames))]
        public void Committed_ghost_clip_demonstrates_its_gesture(string name)
        {
            var dir = GhostBake.FindOutputDir();
            Assert.That(dir, Is.Not.Null, "Resources/Ghosts folder");
            var path = Path.Combine(dir, name + GhostBake.Extension);
            Assert.That(File.Exists(path), Is.True, path + "（跑 Tool_BakeGhostClips 產生）");
            var clip = HandRecording.Load(path);
            var spec = GhostBake.Specs.First(s => s.Name == name);
            Assert.That(clip.Frames.Count, Is.GreaterThan(30));
            Assert.That(clip.Frames[0].Time, Is.EqualTo(0).Within(1e-9));
            Assert.That(clip.Frames.Last().Time, Is.LessThanOrEqualTo(7.0), "short loop");
            Assert.That(GhostBake.Fires(spec, clip), Is.True, name + " replays without its key event: " + clip.Note);
            StringAssert.StartsWith("ghost " + name, clip.Note);
            int hands = (clip.Frames.Any(f => f.Left.Tracked) ? 1 : 0) + (clip.Frames.Any(f => f.Right.Tracked) ? 1 : 0);
            Assert.That(hands, Is.EqualTo(spec.BothHands ? 2 : 1), "hands drawn");
        }

        /// <summary>
        /// 咪牌示範一開始手就在牌上（2026-10-14 實機：錄製開頭手從左邊平放處掃過來、手指插向牌桌深處，看起來像從桌子裡冒出來）：
        /// 第一幀的手掌和「關鍵事件（咪牌）那一刻」的手掌水平距離 ≤ ApproachRadius（＋1 cm）。
        /// </summary>
        [Test]
        public void Committed_peek_clip_starts_with_the_hand_already_over_the_cards()
        {
            var spec = GhostBake.Specs.First(s => s.Name == "peek");
            Assert.That(spec.ApproachRadius, Is.GreaterThan(0f), "peek trims its approach");
            var clip = HandRecording.Load(Path.Combine(GhostBake.FindOutputDir(), "peek" + GhostBake.Extension));
            var key = ReplayRunner.Run(clip, _ => spec.Context).First(spec.Key);
            var at = clip.Frames.First(f => f.Time >= key.Time - 1e-9).Get(key.Hand).Position(HandJoint.Palm);
            var start = clip.Frames.First(f => f.Get(key.Hand).Tracked).Get(key.Hand).Position(HandJoint.Palm);
            float d = new System.Numerics.Vector2(start.X - at.X, start.Z - at.Z).Length();
            Assert.That(d, Is.LessThanOrEqualTo(spec.ApproachRadius + 0.01f), clip.Note);
            Assert.That(key.Time, Is.GreaterThanOrEqualTo(0.4), "still a short lead-in before the peek (fade-in)");
        }

        [Test]
        public void Approach_trim_starts_where_the_palm_stays_near_the_key_spot()
        {
            // 手從 30 cm 外滑過來（0–0.5 s），之後在原地
            var rec = new HandRecording();
            for (int i = 0; i <= 72; i++)
            {
                double t = i / 72.0;
                var f = new HandsFrame { Time = t };
                f.Right.Tracked = true;
                float x = t < 0.5 ? (float)(-0.3 + 0.6 * t) : 0f;
                for (int j = 0; j < HandPose.JointCount; j++) f.Right.Positions[j] = new System.Numerics.Vector3(x, 0.03f, 0.15f);
                rec.Frames.Add(f);
            }
            double start = GhostBake.ApproachEnd(rec, 0.0, 0.9, Hand.Right, 0.05f);
            // x = −0.3 + 0.6 t ≥ −0.05 → t ≥ 0.4167
            Assert.That(start, Is.EqualTo(0.4167).Within(1.0 / 72 + 1e-6));
            Assert.That(GhostBake.ApproachEnd(rec, 0.6, 0.9, Hand.Right, 0.05f), Is.EqualTo(0.6).Within(1e-9), "already there: no trim");
        }

        [Test, Explicit("工具：從 Recordings 挑最乾淨的一段、裁切後寫到 Scripts/Game/Resources/Ghosts（tools/run-core-tests.sh --test=Poker.Tests.GhostBakeTests.Tool_BakeGhostClips）")]
        public void Tool_BakeGhostClips()
        {
            var outDir = GhostBake.FindOutputDir(create: true);
            foreach (var line in GhostBake.BakeAll(RecordingCheck.FindRecordingsDir(), outDir)) TestContext.Progress.WriteLine(line);
        }
    }
}
