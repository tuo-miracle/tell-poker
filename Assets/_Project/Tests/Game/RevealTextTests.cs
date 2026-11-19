using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Poker.AI;
using Poker.Game;
using Poker.Runtime;
using UnityEngine;

namespace Poker.Tests.Game
{
    /// <summary>
    /// 一手結束的揭曉（2026-11-03 使用者：「美術太難看了，而且文字很長，難懂」）：
    /// A 攤牌標題＝每個 AI 一行白話（不再有「Acted the opposite」那種第二行）；B 讀人揭曉卡＝「Rocco read you: weak — right」，拿掉推理鏈。
    /// 外觀同第 5 輪選單：黃銅細框、墨綠底、象牙色襯線字，不透明 Unlit，字從玩家座位看 ≥ 1.3°。
    /// </summary>
    public class RevealTextTests
    {
        const int HeadlineMax = 12, RevealMax = 32;   // 最長的一種是「Silas read you: strong — wrong!」31 字

        static IEnumerable<string> AllReveals()
        {
            foreach (var belief in new[] { "weak", "strong" })
                foreach (ReadOutcome o in System.Enum.GetValues(typeof(ReadOutcome)))
                    foreach (var who in new[] { "Pip", "Rocco", "Vex", "Silas" })
                        yield return GameController.RevealText(who, new LiveBelief { Belief = belief, Cues = new List<ReadCue> { ReadCue.LongThink, ReadCue.ReLook, ReadCue.ReachRetreat } }, o);
            yield return TutorialScript.RevealNothing;
            yield return FoolRocco.RevealFooled;
            yield return FoolRocco.RevealQuick;
            yield return FoolRocco.RevealDistrust;
        }

        [Test]
        public void Headlines_OneShortPlainLine_ForEveryIntent()
        {
            var all = System.Enum.GetValues(typeof(Intent)).Cast<Intent>().Select(HandReveal.HeadlineFor).ToList();
            foreach (var h in all)
            {
                Assert.LessOrEqual(h.Length, HeadlineMax, h);
                Assert.IsFalse(h.Contains("\n"), h);
            }
            Assert.AreEqual("Bluffing", HandReveal.HeadlineFor(Intent.Bluff));
            Assert.AreEqual("Strong hand", HandReveal.HeadlineFor(Intent.Value));
            Assert.AreEqual("Unsure", HandReveal.HeadlineFor(Intent.Marginal));
            Assert.AreEqual(all.Count, all.Distinct().Count(), "every intent reads differently");
        }

        [Test]
        public void ReadReveals_OneShortLine_NoReasoningChain_SayWhoReadYouAndWhetherItWasRight()
        {
            foreach (var t in AllReveals())
            {
                Assert.LessOrEqual(t.Length, RevealMax, t);
                Assert.IsFalse(t.Contains("\n") || t.Contains("→") || t.Contains(","), t + ": one line, no reasoning chain");
                StringAssert.Contains(" read you: ", t);
            }
            var weak = new LiveBelief { Belief = "weak", Cues = new List<ReadCue> { ReadCue.LongThink } };
            Assert.AreEqual("Rocco read you: weak — right", GameController.RevealText("Rocco", weak, ReadOutcome.Right));
            Assert.AreEqual("Rocco read you: weak — wrong!", GameController.RevealText("Rocco", weak, ReadOutcome.Fooled));
            Assert.AreEqual("Rocco read you: weak", GameController.RevealText("Rocco", weak, ReadOutcome.None));
            Assert.AreEqual("Rocco read you: weak — wrong!", FoolRocco.RevealFooled);
            Assert.AreEqual("Rocco read you: strong — right", FoolRocco.RevealQuick);
            Assert.AreEqual("Rocco read you: a trick — right", FoolRocco.RevealDistrust);
            Assert.AreEqual("Rocco read you: nothing", TutorialScript.RevealNothing);
        }

        /// <summary>
        /// 5 人桌一手結束：4 塊攤牌牌子＋讀人揭曉卡，從玩家座位看字至少 RevealMinDegrees（使用者：荷官兩側的牌子太小，1 m 外不用瞇眼）；
        /// 不蓋到任何人的臉、自己的底牌、公共牌，彼此也不重疊。
        /// </summary>
        public const float RevealMinDegrees = 1.6f;

        [Test]
        public void FiveHanded_EveryPlaque_IsBigEnough_AndCoversNoFaceCardOrOtherPlaque()
        {
            using var rig = new GameTestRig(new GameConfig { Seed = 12, FirstButton = 0, MonteCarloTrials = 40, Opponents = GameConfig.FiveHanded });
            var g = rig.Game;
            var t = rig.Table.transform;
            var eye = t.TransformPoint(SeatingPlanner.DefaultHead);
            Vector2 Ang(Vector3 w)
            {
                var d = t.InverseTransformPoint(w) - SeatingPlanner.DefaultHead;
                return new Vector2(Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg, Mathf.Atan2(d.y, new Vector2(d.x, d.z).magnitude) * Mathf.Rad2Deg);
            }
            Rect RectOf(Transform plate)
            {
                float x0 = 999, x1 = -999, y0 = 999, y1 = -999;
                foreach (var c in new[] { new Vector3(-0.5f, -0.5f, 0), new Vector3(0.5f, -0.5f, 0), new Vector3(0.5f, 0.5f, 0), new Vector3(-0.5f, 0.5f, 0) })
                {
                    var a = Ang(plate.TransformPoint(c));
                    x0 = Mathf.Min(x0, a.x); x1 = Mathf.Max(x1, a.x); y0 = Mathf.Min(y0, a.y); y1 = Mathf.Max(y1, a.y);
                }
                return Rect.MinMaxRect(x0, y0, x1, y1);
            }
            var intents = new[] { Intent.SemiBluff, Intent.Value, Intent.Marginal, Intent.Bluff };
            for (int seat = 1; seat <= 4; seat++)
                g.ShowHeadline(seat, new HandReveal { Seat = seat, Headline = HandReveal.HeadlineFor(intents[seat - 1]), Moments = new RevealMoment[0] });
            g.ReadRevealLabel.Show("Silas read you: strong — wrong!", g.ReadRevealSpot);
            g.Props.FaceLabels(eye);
            g.ReadRevealLabel.Face(eye);
            var labels = new List<(string name, WorldLabel l)>();
            for (int seat = 1; seat <= 4; seat++) labels.Add(("seat " + seat, g.Props.SeatHeadline[seat]));
            labels.Add(("read reveal", g.ReadRevealLabel));
            var problems = new List<string>();
            var rects = new List<(string, Rect)>();
            foreach (var (name, l) in labels)
            {
                var text = l.Go.GetComponentInChildren<TextMesh>();
                float lineHeight = text.characterSize * text.fontSize * 0.1f * text.transform.lossyScale.y;
                float deg = Mathf.Atan2(lineHeight, Vector3.Distance(eye, l.Go.transform.position)) * Mathf.Rad2Deg;
                var r = RectOf(l.Go.transform.Find("Frame") ?? l.Plate.transform);
                TestContext.Out.WriteLine($"[plaque] {name} \"{l.Text}\" {deg:F2} deg rect {r}");
                if (deg < RevealMinDegrees) problems.Add($"{name}: text {deg:F2} deg < {RevealMinDegrees}");
                foreach (var c in g.AllCharacters())
                {
                    var hc = Ang(c.Rig.Head.position);
                    float hr = Mathf.Asin(Mathf.Clamp01(0.13f / Vector3.Distance(eye, c.Rig.Head.position))) * Mathf.Rad2Deg;
                    float dx = Mathf.Max(r.xMin - hc.x, 0f, hc.x - r.xMax), dy = Mathf.Max(r.yMin - hc.y, 0f, hc.y - r.yMax);
                    if (dx * dx + dy * dy < hr * hr) problems.Add($"{name} covers {c.name}'s face");
                }
                var cards = new List<Vector3> { g.Spots.PlayerCard(0), g.Spots.PlayerCard(1) };
                for (int b = 0; b < 5; b++) cards.Add(g.Spots.Board(b));
                foreach (var cp in cards)
                {
                    var cr = Rect.MinMaxRect(999, 999, -999, -999);
                    foreach (var o in new[] { new Vector3(-0.032f, 0, -0.044f), new Vector3(0.032f, 0, -0.044f), new Vector3(0.032f, 0, 0.044f), new Vector3(-0.032f, 0, 0.044f) })
                    {
                        var a = Ang(t.TransformPoint(cp + o));
                        cr.xMin = Mathf.Min(cr.xMin, a.x); cr.xMax = Mathf.Max(cr.xMax, a.x); cr.yMin = Mathf.Min(cr.yMin, a.y); cr.yMax = Mathf.Max(cr.yMax, a.y);
                    }
                    if (r.Overlaps(cr)) problems.Add($"{name} covers a card at {cp}");
                }
                foreach (var (other, or) in rects) if (r.Overlaps(or)) problems.Add($"{name} overlaps {other}");
                rects.Add((name, r));
            }
            Assert.IsEmpty(problems, string.Join("\n", problems));
        }

        [Test]
        public void Labels_LookLikeTheCardRoomMenu_IvorySerifOnFeltWithABrassRim_AndAreLegible()
        {
            using var rig = new GameTestRig(new GameConfig { Seed = 12, FirstButton = 0, MonteCarloTrials = 40 });
            var g = rig.Game;
            var eye = rig.Table.transform.TransformPoint(SeatingPlanner.DefaultHead);
            var rev = new HandReveal { Seat = 1, Headline = HandReveal.HeadlineFor(Intent.Bluff), Detail = "Acted the opposite", Moments = new RevealMoment[0] };
            g.ShowHeadline(1, rev);
            g.ReadRevealLabel.Show(FoolRocco.RevealQuick, g.ReadRevealSpot);
            g.Props.FaceLabels(eye);
            g.ReadRevealLabel.Face(eye);
            foreach (Transform t in g.Props.Root) Assert.IsFalse(t.gameObject.activeSelf && t.name.StartsWith("RevealDetail"), "no second line under the headline");
            foreach (var l in new[] { g.Props.SeatHeadline[1], g.ReadRevealLabel })
            {
                var name = l.Go.name;
                var frame = l.Go.transform.Find("Frame");
                Assert.IsNotNull(frame, name + " brass rim");
                var fc = frame.GetComponent<Renderer>().sharedMaterial.color;
                Assert.Greater(fc.r, fc.b + 0.25f, name + " brass/gold rim");
                var pc = l.Plate.GetComponent<Renderer>().sharedMaterial.color;
                Assert.Less(pc.grayscale, 0.3f, name + " dark felt plate");
                Assert.Greater(pc.g, pc.r, name + " green felt");
                Assert.Greater(frame.localScale.x, l.Plate.transform.localScale.x, name + " rim shows around the plate");
                var text = l.Go.GetComponentInChildren<TextMesh>();
                StringAssert.Contains("Baskerville", text.font.name, name + " serif");
                var tc = text.GetComponent<Renderer>().sharedMaterial.GetColor("_BaseColor");
                Assert.Greater(tc.grayscale, 0.75f, name + " ivory text");
                foreach (var r in new[] { frame.GetComponent<Renderer>(), l.Plate.GetComponent<Renderer>() })
                {
                    Assert.Less(r.sharedMaterial.renderQueue, 3000, name + " opaque plates");
                    Assert.AreEqual(UnityEngine.Rendering.ShadowCastingMode.Off, r.shadowCastingMode);
                }
                float lineHeight = text.characterSize * text.fontSize * 0.1f * text.transform.lossyScale.y;
                float deg = Mathf.Atan2(lineHeight, Vector3.Distance(eye, l.Go.transform.position)) * Mathf.Rad2Deg;
                Assert.GreaterOrEqual(deg, 1.3f, name + $" text {deg:F2} deg from the seat");
                // 底板包得住字（字比較寬的襯線字）
                var tb = text.GetComponent<MeshRenderer>().bounds;
                var pb = l.Plate.GetComponent<Renderer>().bounds;
                Assert.IsTrue(pb.Contains(new Vector3(tb.min.x, tb.center.y, pb.center.z)) || pb.size.x >= tb.size.x * 0.98f, name + " plate wraps the text");
            }
        }
    }
}
