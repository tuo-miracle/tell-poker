using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Poker.AI;
using Poker.Characters;
using UnityEngine;
using UnityEngine.TestTools;

namespace Poker.Tests
{
    /// <summary>寫實角色預覽（TELL_REALISTIC_PREVIEW，開發用）：開關、骨頭擺姿勢、表情對應 blendshape、Hank 坐上 Rocco 的座位。</summary>
    public class RealisticHankTests
    {
        const float Dt = 1f / 60f;
        const string HankPath = "Assets/_Project/Art/Characters/Hank/Hank.glb";
        readonly List<GameObject> _spawned = new List<GameObject>();

        [TearDown]
        public void TearDown()
        {
            foreach (var go in _spawned) if (go != null) Object.DestroyImmediate(go);
            _spawned.Clear();
        }

        GameObject New(string name, Transform parent = null, Vector3 pos = default)
        {
            var go = new GameObject(name);
            if (parent != null) go.transform.SetParent(parent, false);
            go.transform.position = pos;
            if (parent == null) _spawned.Add(go);
            return go;
        }

        // ───────────── 開關 ─────────────

        [Test]
        public void Switch_IsOffUnlessExactlyOne()
        {
            Assert.AreEqual("TELL_REALISTIC_PREVIEW", RealisticPreview.EnvVar);
            Assert.IsFalse(RealisticPreview.IsOn(null));
            Assert.IsFalse(RealisticPreview.IsOn(""));
            Assert.IsFalse(RealisticPreview.IsOn("0"));
            Assert.IsFalse(RealisticPreview.IsOn("true"));
            Assert.IsTrue(RealisticPreview.IsOn("1"));
        }

        [Test]
        public void FitView_PutsTheCameraAtThePlayerSeat()
        {
            // 頭盔沒人戴、亂放在桌上時，把牌桌搬到「相機正好坐在玩家座位」的位置（實機截圖用）
            var cam = new Pose(new Vector3(0.4f, 0.2f, -1.1f), Quaternion.Euler(70, 140, 160));
            var eye = new Vector3(0, 0.45f, -0.25f);
            var look = new Vector3(0, 0.15f, 0.6f);
            var t = RealisticPreview.TableForView(cam, eye, look);
            var table = New("Table").transform;
            table.SetPositionAndRotation(t.position, t.rotation);
            Assert.Less(Vector3.Distance(table.InverseTransformPoint(cam.position), eye), 1e-4f, "camera at the seat");
            var fwd = table.InverseTransformDirection(cam.rotation * Vector3.forward);
            Assert.Less(Vector3.Angle(fwd, look - eye), 0.05f, "looking at the table");
            var up = table.InverseTransformDirection(cam.rotation * Vector3.up);
            Assert.Greater(up.y, 0.9f, "no roll relative to the table");
        }

        // ───────────── 骨頭擺姿勢 ─────────────

        [Test]
        public void Aim_PointsChildAlongDirection()
        {
            var bone = New("bone");
            bone.transform.rotation = Quaternion.Euler(20, 70, -10);
            var child = New("child", bone.transform);
            child.transform.localPosition = new Vector3(0, 0.3f, 0.05f);
            var dir = new Vector3(0.2f, -1f, 0.4f).normalized;
            BoneAim.Aim(bone.transform, child.transform.position, dir);
            var got = (child.transform.position - bone.transform.position).normalized;
            Assert.Less(Vector3.Angle(got, dir), 0.1f);
        }

        Transform[] Chain(float a, float b)
        {
            var s = New("shoulder");
            var e = New("elbow", s.transform);
            e.transform.localPosition = new Vector3(0, -a, 0);
            var h = New("hand", e.transform);
            h.transform.localPosition = new Vector3(0, -b, 0);
            return new[] { s.transform, e.transform, h.transform };
        }

        [Test]
        public void TwoBone_ReachesTarget_AndBendsTowardPole()
        {
            var c = Chain(0.3f, 0.26f);
            var target = new Vector3(0.05f, -0.35f, 0.3f);
            var pole = new Vector3(0.5f, -0.2f, -0.3f);
            BoneAim.TwoBone(c[0], c[1], c[2], target, pole);
            Assert.Less(Vector3.Distance(c[2].position, target), 0.001f, "hand on target");
            Assert.AreEqual(0.3f, Vector3.Distance(c[0].position, c[1].position), 1e-4f, "upper length kept");
            Assert.AreEqual(0.26f, Vector3.Distance(c[1].position, c[2].position), 1e-4f, "lower length kept");
            // 手肘偏向 pole 那一側
            var axis = (target - c[0].position).normalized;
            var mid = c[0].position + Vector3.Project(c[1].position - c[0].position, axis);
            Assert.Greater(Vector3.Dot(c[1].position - mid, Vector3.ProjectOnPlane(pole - c[0].position, axis)), 0, "elbow toward pole");
        }

        [Test]
        public void TwoBone_OutOfReach_StraightensTowardTarget()
        {
            var c = Chain(0.3f, 0.26f);
            var target = new Vector3(0, -0.2f, 1.5f);
            BoneAim.TwoBone(c[0], c[1], c[2], target, Vector3.right);
            var dir = (target - c[0].position).normalized;
            Assert.Less(Vector3.Angle(c[2].position - c[0].position, dir), 0.5f);
            Assert.AreEqual(0.56f, Vector3.Distance(c[0].position, c[2].position), 0.002f, "fully extended");
        }

        // ───────────── 表情 → ARKit blendshape ─────────────

        /// <summary>wrap 模型（Rocketbox 臉）的 52 組 ARKit 形狀鍵（同 wrap/verify.json）。</summary>
        static readonly string[] Arkit52 =
        {
            "browDownLeft", "browDownRight", "browInnerUp", "browOuterUpLeft", "browOuterUpRight", "cheekPuff", "cheekSquintLeft", "cheekSquintRight",
            "eyeBlinkLeft", "eyeBlinkRight", "eyeLookDownLeft", "eyeLookDownRight", "eyeLookInLeft", "eyeLookInRight", "eyeLookOutLeft", "eyeLookOutRight",
            "eyeLookUpLeft", "eyeLookUpRight", "eyeSquintLeft", "eyeSquintRight", "eyeWideLeft", "eyeWideRight", "jawForward", "jawLeft", "jawOpen", "jawRight",
            "mouthClose", "mouthDimpleLeft", "mouthDimpleRight", "mouthFrownLeft", "mouthFrownRight", "mouthFunnel", "mouthLeft", "mouthLowerDownLeft",
            "mouthLowerDownRight", "mouthPressLeft", "mouthPressRight", "mouthPucker", "mouthRight", "mouthRollLower", "mouthRollUpper", "mouthShrugLower",
            "mouthShrugUpper", "mouthSmileLeft", "mouthSmileRight", "mouthStretchLeft", "mouthStretchRight", "mouthUpperUpLeft", "mouthUpperUpRight",
            "noseSneerLeft", "noseSneerRight", "tongueOut",
        };

        static float W(float[] w, string name)
        {
            int i = System.Array.IndexOf(HankFace.Shapes, name);
            Assert.GreaterOrEqual(i, 0, "HankFace drives " + name);
            return w[i];
        }

        static float[] Map(FaceState f)
        {
            var w = new float[HankFace.Shapes.Length];
            HankFace.Map(f, w);
            return w;
        }

        [Test]
        public void Face_DrivesArkitShapes_IncludingJawBlinkEyesAndCheeks()
        {
            CollectionAssert.IsSubsetOf(HankFace.Shapes, Arkit52, "only real ARKit names");
            CollectionAssert.AllItemsAreUnique(HankFace.Shapes);
            CollectionAssert.IsSubsetOf(new[]
            {
                "jawOpen", "mouthFunnel", "mouthClose", "eyeBlinkLeft", "eyeBlinkRight", "eyeLookDownLeft", "eyeLookDownRight", "eyeLookUpLeft",
                "eyeLookInLeft", "eyeLookOutLeft", "eyeWideLeft", "cheekSquintLeft", "mouthDimpleLeft", "mouthPressLeft", "mouthRollLower",
                "noseSneerLeft", "browInnerUp", "mouthStretchLeft",
            }, HankFace.Shapes);
        }

        [Test]
        public void Face_Blink_ClosesTheEyesFully()
        {
            // 卡通眨眼曲線在 72 fps 時峰值約 0.98：寫實版要真的全閉
            foreach (var b in new[] { 0.85f, 1f })
            {
                var w = Map(new FaceState { Blink = b, Mouth = MouthShape.Flat });
                Assert.AreEqual(1f, W(w, "eyeBlinkLeft"), 1e-4f, "blink " + b);
                Assert.AreEqual(1f, W(w, "eyeBlinkRight"), 1e-4f, "blink " + b);
            }
            Assert.AreEqual(0f, W(Map(new FaceState { Mouth = MouthShape.Flat }), "eyeBlinkLeft"), 1e-5f);
        }

        [Test]
        public void Face_CartoonHeavyLid_DoesNotMakeHankLookDrowsy()
        {
            // 上一輪實機：卡通的眼皮（個性基準＋低頭）照抄成瞇眼 → 看起來像在打瞌睡。寫實版只吃情緒本身的眼皮
            var w = Map(new FaceState { Lid = 0.55f, MoodLid = 0f, Mood = Mood.Neutral, Mouth = MouthShape.Smirk });
            Assert.Less(W(w, "eyeSquintLeft"), 0.05f);
            Assert.Less(W(w, "eyeSquintRight"), 0.05f);
            Assert.AreEqual(0f, W(w, "eyeBlinkLeft"), 1e-5f);
        }

        [Test]
        public void Face_TalkOpensTheJaw_AndRoundsTheLips()
        {
            var closed = Map(new FaceState { Mood = Mood.Neutral, Mouth = MouthShape.Flat });
            Assert.AreEqual(0f, W(closed, "jawOpen"), 1e-5f);
            var open = Map(new FaceState { Mood = Mood.Neutral, Mouth = MouthShape.Flat, Jaw = 1f });
            Assert.Greater(W(open, "jawOpen"), 0.45f, "visible from 1 m");
            var round = Map(new FaceState { Mood = Mood.Neutral, Mouth = MouthShape.Flat, Jaw = 0.6f, Round = 1f });
            Assert.Greater(W(round, "mouthFunnel"), 0.25f);
            // 緊張抿嘴時講話：嘴唇還是要能張開
            var nervous = FaceState.FromMood(Mood.Nervous);
            nervous.Jaw = 1f;
            var wn = Map(nervous);
            Assert.Greater(W(wn, "jawOpen"), 0.45f);
            Assert.Less(W(wn, "mouthPressLeft"), 0.25f, "press relaxes while talking");
            Assert.Less(W(wn, "mouthRollLower"), 0.2f);
        }

        [Test]
        public void Face_Happy_BigSmile_CheeksUp_TeethShow()
        {
            var w = Map(FaceState.FromMood(Mood.Happy));
            Assert.Greater(W(w, "mouthSmileLeft"), 0.8f);
            Assert.Greater(W(w, "mouthSmileRight"), 0.8f);
            Assert.Greater(W(w, "cheekSquintLeft"), 0.4f, "cheeks lift (smile is visible under the moustache)");
            Assert.Greater(W(w, "jawOpen"), 0.1f, "teeth show");
        }

        [Test]
        public void Face_Confident_OneSidedSmirk_Dimple_RaisedBrow_Squint()
        {
            var w = Map(FaceState.FromMood(Mood.Confident));
            Assert.Greater(W(w, "mouthSmileRight"), 0.75f);
            Assert.Greater(W(w, "mouthSmileRight"), W(w, "mouthSmileLeft") + 0.5f, "one-sided");
            Assert.Greater(W(w, "mouthDimpleRight"), 0.3f);
            Assert.Greater(W(w, "browOuterUpRight"), W(w, "browOuterUpLeft") + 0.3f);
            Assert.Greater(W(w, "eyeSquintLeft"), 0.4f, "confident narrowed eyes");
            // 待機嘴型（Rocco 的 RestMouth 也是 Smirk）比真的冷笑淡很多
            var rest = Map(new FaceState { Mood = Mood.Neutral, Mouth = MouthShape.Smirk });
            Assert.Less(W(rest, "mouthSmileRight"), 0.45f);
        }

        [Test]
        public void Face_Annoyed_BrowsDown_Sneer_Frown_Press()
        {
            var w = Map(FaceState.FromMood(Mood.Annoyed));
            Assert.Greater(W(w, "browDownLeft"), 0.8f);
            Assert.Greater(W(w, "browDownRight"), 0.8f);
            Assert.Greater(W(w, "noseSneerLeft"), 0.5f);
            Assert.Greater(W(w, "mouthFrownLeft"), 0.6f);
            Assert.Greater(W(w, "mouthPressLeft"), 0.3f);
            Assert.AreEqual(0f, W(w, "browInnerUp"), 1e-5f);
        }

        [Test]
        public void Face_Nervous_InnerBrowsUp_WideEyes_PressedRolledLips()
        {
            var w = Map(FaceState.FromMood(Mood.Nervous));
            Assert.Greater(W(w, "browInnerUp"), 0.8f);
            Assert.Greater(W(w, "eyeWideLeft"), 0.3f);
            Assert.Greater(W(w, "mouthPressLeft"), 0.4f);
            Assert.Greater(W(w, "mouthRollLower"), 0.3f);
            Assert.Greater(W(w, "mouthStretchLeft"), 0.2f);
        }

        [Test]
        public void Face_Bluffing_ForcedGrin_WorriedBrows()
        {
            var w = Map(FaceState.FromMood(Mood.Bluffing));
            Assert.Greater(W(w, "mouthSmileLeft"), 0.5f);
            Assert.Greater(W(w, "browInnerUp"), 0.4f, "worried brows give the bluff away");
            Assert.Greater(W(w, "mouthStretchLeft"), 0.2f, "forced, stretched grin");
            var happy = Map(FaceState.FromMood(Mood.Happy));
            Assert.Less(W(w, "cheekSquintLeft"), W(happy, "cheekSquintLeft"), "fake smile does not reach the eyes");
        }

        [Test]
        public void Face_Startled_Dejected_Hesitant()
        {
            var s = Map(FaceState.FromMood(Mood.Startled));
            Assert.Greater(W(s, "eyeWideLeft"), 0.9f);
            Assert.Greater(W(s, "jawOpen"), 0.3f);
            Assert.Greater(W(s, "browOuterUpLeft"), 0.8f);
            var d = Map(FaceState.FromMood(Mood.Dejected));
            Assert.Greater(W(d, "mouthFrownLeft"), 0.6f);
            Assert.Greater(W(d, "browInnerUp"), 0.5f);
            Assert.AreEqual(0f, W(d, "noseSneerLeft"), 1e-5f);
            var h = Map(FaceState.FromMood(Mood.Hesitant));
            Assert.Greater(Mathf.Max(W(h, "mouthLeft"), W(h, "mouthRight")), 0.2f, "mouth pulled to one side");
            Assert.Greater(Mathf.Abs(W(h, "browOuterUpLeft") - W(h, "browOuterUpRight")), 0.3f);
        }

        [Test]
        public void Face_AllMoods_StayInRange()
        {
            foreach (Mood m in System.Enum.GetValues(typeof(Mood)))
            {
                var f = FaceState.FromMood(m);
                f.Blink = 1f; f.Jaw = 1f; f.Round = 1f; f.Puff = 1f; f.EyePitch = 40f; f.EyeYaw = -40f;
                var raw = new float[HankFace.Shapes.Length];
                HankFace.Map(f, raw, clamp: false);   // 不夾值：係數疊加不能爆掉後被默默夾住
                Assert.That(raw, Has.All.InRange(0f, 1.05f), m.ToString());
                Assert.That(Map(f), Has.All.InRange(0f, 1f), m.ToString());
            }
        }

        [Test]
        public void Performer_ExposesFaceState_ForAnnoyedAndBlinks()
        {
            var p = CharacterFactory.Create(CharacterKind.Loudmouth, null, Vector3.zero, Quaternion.identity, 3);
            p.AutoUpdate = false;
            _spawned.Add(p.gameObject);
            p.Play(new PerformanceCue { Mood = Mood.Annoyed, Intensity = 1f, Duration = 10f });
            float maxBlink = 0;
            for (float t = 0; t < 6f; t += Dt) { p.Tick(Dt); maxBlink = Mathf.Max(maxBlink, p.Face.Blink); }
            Assert.Less(p.Face.BrowInner, -0.5f);
            Assert.AreEqual(MouthShape.Frown, p.Face.Mouth);
            Assert.AreEqual(Mood.Annoyed, p.Face.Mood);
            Assert.Greater(p.Face.MoodW, 0.6f);
            Assert.Greater(p.Face.MoodLid, 0.1f, "annoyed narrows the eyes");
            Assert.Greater(maxBlink, 0.5f, "auto blink reaches the face state");
        }

        // ───────────── 講話的嘴 ─────────────

        static float Run(MouthDriver m, float secs, bool talking, float? level, string text = null, float t0 = 0, float t1 = 0)
        {
            int n = Mathf.RoundToInt(secs / Dt);
            for (int i = 0; i < n; i++) m.Step(Dt, talking, level, text, Mathf.Lerp(t0, t1, (i + 1f) / n));
            return m.Jaw;
        }

        [Test]
        public void Mouth_FollowsTheVoiceVolume()
        {
            var m = new MouthDriver();
            Assert.Greater(Run(m, 0.1f, true, 1f), 0.7f, "loud: open");
            Assert.Less(Run(m, 0.15f, true, 0f), 0.1f, "silence between words: closed");
            Assert.Greater(Run(m, 0.1f, true, 0.5f), 0.25f, "medium");
        }

        [Test]
        public void Mouth_ClosesWhenNotTalking()
        {
            var m = new MouthDriver();
            Run(m, 0.2f, true, 1f);
            Assert.Less(Run(m, 0.25f, false, 1f), 0.02f);
        }

        [Test]
        public void Mouth_WithoutAudio_OpensAndClosesToTheSubtitle()
        {
            var m = new MouthDriver();
            const string text = "Eh, let's see where this goes.";
            int n = Mathf.RoundToInt(2f / Dt), cycles = 0;
            float max = 0, maxRound = 0;
            bool open = false;
            for (int i = 0; i < n; i++)
            {
                m.Step(Dt, true, null, text, (i + 1f) / n);
                max = Mathf.Max(max, m.Jaw);
                maxRound = Mathf.Max(maxRound, m.Round);
                if (!open && m.Jaw > 0.35f) { open = true; cycles++; }
                else if (open && m.Jaw < 0.15f) open = false;
            }
            Assert.Greater(max, 0.5f);
            Assert.GreaterOrEqual(cycles, 4, "rhythmic open/close, not a held-open mouth");
            Assert.Greater(maxRound, 0.2f, "'o' in 'goes' rounds the lips");
            Assert.Less(Run(m, 0.3f, true, null, text, 1f, 1f), 0.15f, "after the last word: closed");
        }

        static float[] Burst(int rate, float silence1, float loud, float silence2, float amp = 0.5f)
        {
            int a = Mathf.RoundToInt(silence1 * rate), b = Mathf.RoundToInt(loud * rate), c = Mathf.RoundToInt(silence2 * rate);
            var s = new float[a + b + c];
            for (int i = 0; i < b; i++) s[a + i] = amp * Mathf.Sin(2 * Mathf.PI * 180f * i / rate);
            return s;
        }

        [Test]
        public void Envelope_IsLoudOnlyWhereTheVoiceIs()
        {
            var env = ClipEnvelope.Build(Burst(8000, 0.5f, 0.5f, 0.5f), 1, 8000);
            Assert.Less(ClipEnvelope.Sample(env, 0.25f), 0.05f);
            Assert.Greater(ClipEnvelope.Sample(env, 0.75f), 0.8f);
            Assert.Less(ClipEnvelope.Sample(env, 1.25f), 0.05f);
            Assert.AreEqual(0f, ClipEnvelope.Sample(env, 5f), 1e-5f, "past the end");
            Assert.AreEqual(0f, ClipEnvelope.Sample(env, -1f), 1e-5f);
        }

        [Test]
        public void Envelope_FromAudioClip_AndNullSafe()
        {
            var clip = AudioClip.Create("burst", 12000, 1, 8000, false);
            clip.SetData(Burst(8000, 0.5f, 0.5f, 0.5f), 0);
            var env = ClipEnvelope.For(clip);
            Assert.IsNotNull(env);
            Assert.Greater(ClipEnvelope.Sample(env, 0.75f), 0.8f);
            Assert.AreSame(env, ClipEnvelope.For(clip), "cached");
            Assert.IsNull(ClipEnvelope.For(null));
            var stream = AudioClip.Create("stream", 8000, 1, 8000, true, d => System.Array.Clear(d, 0, d.Length));
            LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("streamed samples"));
            Assert.IsNull(ClipEnvelope.For(stream), "streamed clip: no samples to read");
            Assert.IsNull(ClipEnvelope.For(stream), "second call (remembered failure)");
            Object.DestroyImmediate(stream);
            Object.DestroyImmediate(clip);
        }

        // ───────────── 眼睛 ─────────────

        static Vector3 Dir(float yawRight, float pitchDown) => Quaternion.Euler(pitchDown, yawRight, 0) * Vector3.forward;

        [Test]
        public void EyeAim_FollowsTheTarget_WithinLidSafeLimits()
        {
            Assert.Less(EyeAim.Angles(Vector3.forward).magnitude, 0.01f);
            var a = EyeAim.Angles(Dir(10, 0));
            Assert.AreEqual(10f, a.x, 0.1f, "right");
            Assert.AreEqual(0f, a.y, 0.1f);
            Assert.AreEqual(-12f, EyeAim.Angles(Dir(-12, 0)).x, 0.1f, "left");
            Assert.AreEqual(EyeAim.MaxYaw, EyeAim.Angles(Dir(60, 0)).x, 0.01f, "clamped right");
            Assert.AreEqual(-EyeAim.MaxYaw, EyeAim.Angles(Dir(-80, 0)).x, 0.01f, "clamped left");
            Assert.AreEqual(15f, EyeAim.Angles(Dir(0, 15)).y, 0.1f, "down");
            Assert.AreEqual(EyeAim.MaxDown, EyeAim.Angles(Dir(0, 50)).y, 0.01f, "clamped down");
            Assert.AreEqual(-EyeAim.MaxUp, EyeAim.Angles(Dir(0, -40)).y, 0.01f, "clamped up");
            Assert.AreEqual(EyeAim.MaxYaw, EyeAim.Angles(-Vector3.forward + Vector3.right * 0.1f).x, 0.01f, "behind: clamp, no flip");
            Assert.That(EyeAim.MaxYaw, Is.InRange(18f, 30f));
            // v3 模型（2026-10-30）：從玩家的位置看，眼睛往上超過約 8° 瞳孔就躲進上眼皮（eyeLookUp 只抬眼皮約 1.5 mm）
            Assert.That(EyeAim.MaxUp, Is.InRange(4f, 7f));
            Assert.That(EyeAim.MaxDown, Is.InRange(20f, 32f));
        }

        // ───────────── 開發用：把 Hank 固定在某個表情（debug.tell.hankface） ─────────────

        [Test]
        public void Pin_ParsesTheDevProperty()
        {
            foreach (var off in new[] { null, "", "0", "off", "nonsense", "-1", "+2" }) Assert.IsFalse(HankPin.Parse(off).Active, off ?? "null");
            var b = HankPin.Parse("blink");
            Assert.IsTrue(b.Active && b.Blink && !b.Talk && b.Mood == null && b.Look == HankLook.None);
            var t = HankPin.Parse(" talk , happy,player ");
            Assert.IsTrue(t.Active && t.Talk && !t.Blink);
            Assert.AreEqual(Mood.Happy, t.Mood);
            Assert.AreEqual(HankLook.Player, t.Look);
            Assert.AreEqual(HankLook.Left, HankPin.Parse("left").Look);
            Assert.AreEqual(HankLook.Right, HankPin.Parse("right").Look);
            Assert.AreEqual(HankLook.Table, HankPin.Parse("table").Look);
            var n = HankPin.Parse("neutral");
            Assert.AreEqual(Mood.Neutral, n.Mood);
            Assert.AreEqual(HankLook.Player, n.Look, "neutral = calm face looking at the player");
            Assert.AreEqual(Mood.Confident, HankPin.Parse("confident").Mood);
            Assert.AreEqual(Mood.Bluffing, HankPin.Parse("bluffing").Mood);
            Assert.AreEqual("debug.tell.hankface", RealisticPreview.PinProp);
        }

        // ───────────── Hank 坐上 Rocco 的座位 ─────────────

#if UNITY_EDITOR
        static GameObject HankAsset()
        {
            var go = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(HankPath);
            Assert.IsNotNull(go, HankPath);
            return go;
        }

        (CharacterPerformer p, RealisticBody body, Transform table) SeatHank()
        {
            var table = New("Table").transform;
            table.position = new Vector3(0.3f, 0.74f, -0.2f);
            table.rotation = Quaternion.Euler(0, 25, 0);
            var p = CharacterFactory.CreateAtSeat(CharacterKind.Loudmouth, table, 5);
            p.AutoUpdate = false;
            var body = RealisticBody.Attach(p, HankAsset());
            p.Tick(Dt);
            body.Sync(Dt);
            return (p, body, table);
        }

        [Test]
        public void Hank_ImportsWrapModel_FaceWith52Arkit_EyeBones_AndUrpMaterials()
        {
            var smrs = HankAsset().GetComponentsInChildren<SkinnedMeshRenderer>(true);
            Assert.AreEqual(2, smrs.Length, "face + body");
            var face = smrs.Single(r => r.sharedMesh.blendShapeCount > 0);
            var body = smrs.Single(r => r != face);
            Assert.AreEqual(52, face.sharedMesh.blendShapeCount);
            for (int i = 0; i < 52; i++) CollectionAssert.Contains(Arkit52, face.sharedMesh.GetBlendShapeName(i));
            Assert.AreEqual(67, face.bones.Length);
            Assert.AreEqual(67, body.bones.Length);
            foreach (var eye in new[] { "mixamorig:LeftEye", "mixamorig:RightEye" })
            {
                var t = face.bones.SingleOrDefault(x => x.name == eye);
                Assert.IsNotNull(t, eye);
                Assert.AreEqual("mixamorig:Head", t.parent.name, eye + " parent");
            }
            Assert.AreEqual(3, face.sharedMaterials.Length, "skin, mouth inside, eyes");
            foreach (var m in face.sharedMaterials.Concat(body.sharedMaterials))
            {
                Assert.IsNotNull(m);
                Assert.IsTrue(m.shader.isSupported, m.shader.name);
                StringAssert.DoesNotContain("InternalError", m.shader.name);
                Assert.IsNotNull(m.mainTexture ?? m.GetTexture("baseColorTexture"), m.name + " base color texture");
            }
        }

        [Test]
        public void Hank_SitsAtRoccosSeat_FacingTheTable_HandsOnTheEdge()
        {
            var (p, body, table) = SeatHank();
            var root = p.transform;
            // 真人尺寸：頭頂在桌面上方 0.75–0.85 m
            // 用骨頭獨立量（不用實作算好的值）：HeadTop_End 在帽頂下方約 0.036 m
            float top = table.InverseTransformPoint(body.Bone("HeadTop_End").position).y;
            Assert.That(top, Is.InRange(0.70f, 0.80f), "head top above table");
            Assert.That(table.InverseTransformPoint(body.HeadTopWorld).y, Is.InRange(0.75f, 0.85f), "hat top above table");
            // 原始骨架 Hips→Head 0.359 單位 × 1.8
            Assert.AreEqual(0.646f, Vector3.Distance(body.Bone("Hips").position, body.Bone("Head").position), 0.02f, "scaled to 1.80 m");
            Assert.AreEqual(1.8f, body.StandingHeight, 0.02f);
            // 骨盆在桌緣後方、低於桌面
            var hips = root.InverseTransformPoint(body.Bone("Hips").position);
            Assert.Less(hips.z, -0.1f, "hips behind the edge");
            Assert.Less(hips.y, 0f, "sitting below the table top");
            // 面向牌桌中央：角色的右手邊是根節點的 +X
            var right = body.Bone("RightArm").position - body.Bone("LeftArm").position;
            Assert.Greater(Vector3.Dot(right.normalized, root.right), 0.9f, "faces table center");
            // 大腿往前、小腿往下
            var thigh = root.InverseTransformDirection(body.Bone("LeftLeg").position - body.Bone("LeftUpLeg").position).normalized;
            var shin = root.InverseTransformDirection(body.Bone("LeftFoot").position - body.Bone("LeftLeg").position).normalized;
            Assert.Greater(thigh.z, 0.8f, "thigh forward");
            Assert.Less(shin.y, -0.8f, "shin down");
            // 雙手在桌緣前、桌面上
            foreach (var side in new[] { "LeftHand", "RightHand" })
            {
                var h = root.InverseTransformPoint(body.Bone(side).position);
                Assert.That(h.y, Is.InRange(0f, 0.09f), side + " height above table");
                Assert.That(h.z, Is.InRange(-0.1f, 0.05f), side + " wrist at the table edge (cards and chips start further in)");
                var tip = root.InverseTransformPoint(body.Bone(side + "Middle3").position);
                Assert.GreaterOrEqual(tip.y, 0f, side + " fingers on top of the table, not through it");
                var fore = root.InverseTransformPoint(body.Bone(side.Replace("Hand", "ForeArm")).position);
                // 帽頂 0.75–0.85 m 時肩膀約在桌面上方 0.43 m、上臂 0.29 m，手肘最低也有 ~0.14 m：前臂是斜放到桌緣，不是平貼
                Assert.That(fore.y, Is.InRange(0f, 0.18f), side + " elbow near the table");
                Assert.Less(fore.y, root.InverseTransformPoint(body.Bone(side.Replace("Hand", "Arm")).position).y - 0.2f, side + " elbow well below the shoulder");
            }
        }

        [Test]
        public void Hank_HidesCartoonBody_KeepsBubble_AndMovesGazeTargetToHisHead()
        {
            var (p, body, _) = SeatHank();
            var hankRenderers = new HashSet<Renderer>(body.Model.GetComponentsInChildren<Renderer>(true));
            Assert.IsNotEmpty(hankRenderers);
            var bubble = new HashSet<Renderer>(p.Subtitle.Transform.GetComponentsInChildren<Renderer>(true));
            for (int i = 0; i < 30; i++) p.Tick(Dt);
            foreach (var r in p.GetComponentsInChildren<Renderer>(true))
            {
                if (hankRenderers.Contains(r)) Assert.IsFalse(r.forceRenderingOff, r.name);
                else if (bubble.Contains(r)) Assert.IsFalse(r.forceRenderingOff, "bubble stays: " + r.name);
                else Assert.IsTrue(r.forceRenderingOff, "cartoon hidden: " + r.name);
            }
            Assert.Less(Vector3.Distance(p.Rig.Head.position, body.HeadCenterWorld), 0.08f, "gaze/voice target on Hank's head");
            // 字幕泡泡不擋住臉：泡泡錨點在頭頂高度附近、偏離頭的中心
            var anchor = p.transform.TransformPoint(p.BubbleAnchor);
            Assert.Greater(Vector3.Distance(anchor, body.HeadCenterWorld), 0.2f);
        }

        [Test]
        public void Hank_ExpressionsAndBlinks_DriveBlendshapes()
        {
            var (p, body, _) = SeatHank();
            p.Play(new PerformanceCue { Mood = Mood.Annoyed, Intensity = 1f, Duration = 30f });
            float maxBlink = 0;
            for (float t = 0; t < 6f; t += Dt)
            {
                p.Tick(Dt);
                body.Sync(Dt);
                maxBlink = Mathf.Max(maxBlink, body.Weight("eyeBlinkLeft"));
            }
            Assert.Greater(body.Weight("browDownLeft"), 40f, "annoyed brows (0–100)");
            Assert.Greater(body.Weight("mouthFrownRight"), 30f);
            Assert.Greater(maxBlink, 50f, "auto blink");

            p.Play(new PerformanceCue { Mood = Mood.Nervous, Intensity = 1f, Duration = 30f });
            for (float t = 0; t < 2f; t += Dt) { p.Tick(Dt); body.Sync(Dt); }
            Assert.Greater(body.Weight("browInnerUp"), 40f, "nervous inner brows");
            Assert.Less(body.Weight("browDownLeft"), 10f);
        }

        [Test]
        public void Hank_FullExpressions_DoNotExplodeTheMesh()
        {
            // blendshape 的影格權重依匯入器而定（glTFast 是 1，不是 100）：要正規化，不然 60% 會變 60 倍
            var (p, body, _) = SeatHank();
            var mesh = body.FaceSkin.sharedMesh;
            for (int i = 0; i < mesh.blendShapeCount; i++) body.FaceSkin.SetBlendShapeWeight(i, 0);
            var rest = new Mesh();
            body.FaceSkin.BakeMesh(rest, true);
            foreach (var mood in new[] { Mood.Happy, Mood.Annoyed, Mood.Bluffing })
            {
                p.Play(new PerformanceCue { Mood = mood, Intensity = 1f, Duration = 30f });
                for (float t = 0; t < 2f; t += Dt) p.Tick(Dt);
                body.Sync(Dt);
                var posed = new Mesh();
                body.FaceSkin.BakeMesh(posed, true);
                var a = rest.vertices; var b = posed.vertices;
                float max = 0;
                for (int i = 0; i < a.Length; i++) max = Mathf.Max(max, (a[i] - b[i]).magnitude);
                Assert.Less(max, 0.06f, mood + ": largest vertex move (model units, head turn included)");
                Object.DestroyImmediate(posed);
            }
            Object.DestroyImmediate(rest);
        }

        [Test]
        public void Hank_HeadFollowsThePerformersGaze()
        {
            var (p, body, table) = SeatHank();
            var head = body.Bone("Head");
            var viewer = New("Viewer").transform;
            p.SetViewer(viewer);
            // 看向自己的右手邊（世界座標用根節點換算）
            viewer.position = p.transform.TransformPoint(new Vector3(1.2f, 0.4f, 0.6f));
            p.Play(new PerformanceCue { Mood = Mood.Neutral, Look = LookTarget.Player, Duration = 30f });
            var before = p.transform.InverseTransformDirection(body.HeadForwardWorld);
            for (float t = 0; t < 2f; t += Dt) { p.Tick(Dt); body.Sync(Dt); }
            var after = p.transform.InverseTransformDirection(body.HeadForwardWorld);
            Assert.Greater(after.x, before.x + 0.2f, "head turned toward the viewer");
            Assert.IsNotNull(head);
        }

        // ───────────── v2：眼骨頭、講話、眨眼、固定表情 ─────────────

        /// <summary>水平角（+ 往角色的右邊）與俯角（+ 往下），在根節點座標量。</summary>
        static Vector2 YawPitch(Transform root, Vector3 worldDir)
        {
            var d = root.InverseTransformDirection(worldDir).normalized;
            return new Vector2(Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg, -Mathf.Asin(Mathf.Clamp(d.y, -1, 1)) * Mathf.Rad2Deg);
        }

        /// <summary>眼睛的視線：眼骨頭的 local +Y（wrap/verify：眼骨頭朝前）。</summary>
        static Vector3 EyeGaze(RealisticBody body, string eye) => body.Bone(eye).up;

        void Settle(CharacterPerformer p, RealisticBody body, float secs)
        {
            for (float t = 0; t < secs; t += Dt) { p.Tick(Dt); body.Sync(Dt); }
        }

        [Test]
        public void Hank_EyeBones_PointForwardAtRest_AndTurnAboutLocalX()
        {
            var (p, body, _) = SeatHank();
            var rest = body.Bone("LeftEye").localRotation;
            // 靜止（看正前方的玩家）時視線大致朝前
            Assert.Less(Vector3.Angle(body.HeadForwardWorld, EyeGaze(body, "LeftEye")), 20f, "eye bone +Y = forward");
            var viewer = New("Viewer").transform;
            p.SetViewer(viewer);
            // 玩家在右前方、和 Hank 的眼睛差不多高：眼睛在頭裡只做水平轉
            viewer.position = body.Bone("LeftEye").position + p.transform.TransformDirection(new Vector3(0.9f, 0f, 0.9f));
            p.SetGazedAt(true);
            p.Play(new PerformanceCue { Mood = Mood.Neutral, Look = LookTarget.Player, Duration = 30f });
            Settle(p, body, 2f);
            (Quaternion.Inverse(rest) * body.Bone("LeftEye").localRotation).ToAngleAxis(out float ang, out var axis);
            Assert.Greater(ang, 5f, "eye turned");
            Assert.Greater(Mathf.Abs(axis.x), 0.85f, "left/right look = rotation about the eye bone's local X");
        }

        [Test]
        public void Hank_EyesAimAtTheViewer_TurningMoreThanTheHead_WithinLimits()
        {
            var (p, body, _) = SeatHank();
            var root = p.transform;
            var viewer = New("Viewer").transform;
            p.SetViewer(viewer);
            viewer.position = root.TransformPoint(new Vector3(1.0f, 0.45f, 0.8f));
            p.SetGazedAt(true);   // 玩家盯著他：不會有待機時隨機瞄別處
            p.Play(new PerformanceCue { Mood = Mood.Neutral, Look = LookTarget.Player, Duration = 30f });
            Settle(p, body, 2.5f);
            float head = YawPitch(root, body.HeadForwardWorld).x;
            foreach (var eye in new[] { "LeftEye", "RightEye" })
            {
                float gaze = YawPitch(root, EyeGaze(body, eye)).x;
                float want = YawPitch(root, viewer.position - body.Bone(eye).position).x;
                Assert.Greater(head, 3f, "head turns a bit");
                Assert.Greater(gaze - head, head * 0.5f, eye + ": eyes turn further than the head");
                Assert.Less(Mathf.Abs(gaze - want), 8f, eye + ": looking at the viewer (saccades allowed)");
            }
            Assert.That(Mathf.Abs(body.EyeAngles.x), Is.LessThanOrEqualTo(EyeAim.MaxYaw + 0.01f));

            // 在身後的目標：眼睛停在上限，不轉進眼皮裡
            viewer.position = root.TransformPoint(new Vector3(1.5f, 0.45f, -0.6f));
            Settle(p, body, 2f);
            Assert.AreEqual(EyeAim.MaxYaw, body.EyeAngles.x, 0.5f, "clamped");
        }

        [Test]
        public void Hank_LookingAtTheTable_UsesTheEyes_HeadDipsOnlyALittle_LidsStayOpen()
        {
            var (p, body, _) = SeatHank();
            var root = p.transform;
            p.Play(new PerformanceCue { Mood = Mood.Neutral, Look = LookTarget.Board, Duration = 30f });
            float maxSquint = 0;
            for (float t = 0; t < 3f; t += Dt)
            {
                p.Tick(Dt);
                body.Sync(Dt);
                if (t > 1f) maxSquint = Mathf.Max(maxSquint, body.Weight("eyeSquintLeft"));
            }
            float headDown = YawPitch(root, body.HeadForwardWorld).y;
            float eyeDown = YawPitch(root, EyeGaze(body, "LeftEye")).y;
            Assert.Less(headDown, 12f, "head only dips a little (was the drowsy look)");
            Assert.Greater(eyeDown, headDown + 12f, "the eyes look down at the felt");
            Assert.Greater(body.Weight("eyeLookDownLeft"), 15f, "lids follow the eyes down");
            Assert.Less(body.Weight("eyeLookDownLeft"), 80f, "but do not close them");
            Assert.Less(maxSquint, 5f, "no drowsy squint");
            Assert.Less(body.Weight("eyeBlinkLeft") * (p.IsBlinking ? 0 : 1), 1f);
        }

        [Test]
        public void Hank_LooksAtTheCharacterOnTheLeft_WhenPinned()
        {
            var table = New("Table").transform;
            var others = new[] { CharacterFactory.CreateAtSeat(CharacterKind.Rookie, table, 1), CharacterFactory.CreateAtSeat(CharacterKind.Cold, table, 3) };
            var p = CharacterFactory.CreateAtSeat(CharacterKind.Loudmouth, table, 2);
            p.AutoUpdate = false;
            foreach (var o in others) o.AutoUpdate = false;
            var body = RealisticBody.Attach(p, HankAsset());
            // 玩家視角的左邊（牌桌 −X）那位
            var left = others.OrderBy(o => table.InverseTransformPoint(o.transform.position).x).First();
            body.Pin = HankPin.Parse("left");
            Settle(p, body, 1.5f);
            var to = left.Rig.Head.position - body.Bone("LeftEye").position;
            Assert.Less(Vector3.Angle(EyeGaze(body, "LeftEye"), to), 12f, "eyes on " + left.Spec.DisplayName);
            Assert.Less(Vector3.Angle(body.HeadForwardWorld, to), Vector3.Angle(p.transform.forward, to), "head turns toward them too");
        }

        [Test]
        public void HeadYaw_SplitsTheTurn_HeadAboutFortyPercent_Capped_EyesTheRest()
        {
            Assert.AreEqual(0f, RealisticBody.HeadYawToward(0f), 1e-4f);
            Assert.AreEqual(4f, RealisticBody.HeadYawToward(10f), 0.01f, "small turns: head 40%");
            Assert.AreEqual(-4f, RealisticBody.HeadYawToward(-10f), 0.01f);
            Assert.That(RealisticBody.HeadYawMax, Is.InRange(32f, 35f));
            for (float t = -150f; t <= 150f; t += 5f)
            {
                float h = RealisticBody.HeadYawToward(t);
                Assert.LessOrEqual(Mathf.Abs(h), RealisticBody.HeadYawMax + 1e-4f, "capped at " + t);
                Assert.GreaterOrEqual(Mathf.Abs(h), Mathf.Min(Mathf.Abs(t) * 0.4f, RealisticBody.HeadYawMax) - 1e-4f, "at least 40% at " + t);
                Assert.IsTrue(t == 0 || Mathf.Sign(h) == Mathf.Sign(t), "same side at " + t);
                if (Mathf.Abs(t) <= RealisticBody.HeadYawMax + EyeAim.MaxYaw * 0.85f)
                    Assert.LessOrEqual(Mathf.Abs(t) - Mathf.Abs(h), EyeAim.MaxYaw, "eyes can reach the rest at " + t);
            }
        }

        [Test]
        public void Hank_LooksUp_HeadTiltsUp_EyesStayBelowTheLid_LidsFollow()
        {
            // 站著的玩家：目標在眼睛上方 15°、22°
            foreach (var deg in new[] { 15f, 22f })
            {
                var (p, body, _) = SeatHank();
                var root = p.transform;
                var viewer = New("Viewer").transform;
                p.SetViewer(viewer);
                p.SetGazedAt(true);
                viewer.position = body.HeadCenterWorld + root.TransformDirection(new Vector3(0, 0, 1.2f));
                p.Play(new PerformanceCue { Mood = Mood.Neutral, Look = LookTarget.Player, Duration = 30f });
                Settle(p, body, 2.5f);
                float levelHead = YawPitch(root, body.HeadForwardWorld).y;

                viewer.position = body.HeadCenterWorld + root.TransformDirection(Quaternion.Euler(-deg, 0, 0) * Vector3.forward) * 1.2f;
                p.Tick(Dt);
                body.Sync(Dt);
                Assert.Less(Mathf.Abs(YawPitch(root, body.HeadForwardWorld).y - levelHead), 8f, deg + "°: one frame later the head does not snap up");
                Settle(p, body, 2.5f);
                var to = viewer.position - body.Bone("LeftEye").position;
                Debug.Log($"[LookUp] {deg}°: eyes {body.EyeAngles.ToString("F1")}, head pitch {YawPitch(root, body.HeadForwardWorld).y:F1}° (level {levelHead:F1}°), miss {Vector3.Angle(EyeGaze(body, "LeftEye"), to):F1}°, eyeLookUp {body.Weight("eyeLookUpLeft"):F0}");
                Assert.LessOrEqual(-body.EyeAngles.y, EyeAim.MaxUp + 0.01f, deg + "°: eyes stay under the lid limit (≤ 6° up)");
                Assert.Less(YawPitch(root, body.HeadForwardWorld).y, levelHead - 5f, deg + "°: the head tilts up instead");
                Assert.Less(Vector3.Angle(EyeGaze(body, "LeftEye"), to), 6f, deg + "°: still looking at them");
                Assert.Greater(body.Weight("eyeLookUpLeft"), 20f, deg + "°: upper lids follow the eyes up");
                Object.DestroyImmediate(p.transform.parent.gameObject);
            }
        }

        [Test]
        public void ChestYaw_OnlyForTargetsBeyondHeadAndEyes_TwelveToFifteenDegrees()
        {
            Assert.That(RealisticBody.ChestYawMax, Is.InRange(12f, 15f));
            float reach = RealisticBody.HeadYawMax + EyeAim.MaxYaw * 0.85f;
            for (float t = -reach; t <= reach; t += 2f)
                Assert.AreEqual(0f, RealisticBody.ChestYawToward(t), 1e-4f, "head and eyes cover it, no chest twist at " + t);
            float prev = 0;
            for (float t = 0; t <= 180f; t += 5f)
            {
                float c = RealisticBody.ChestYawToward(t);
                Assert.GreaterOrEqual(c, prev - 1e-4f, "grows with the angle at " + t);
                Assert.LessOrEqual(c, RealisticBody.ChestYawMax + 1e-4f, "capped at " + t);
                Assert.AreEqual(-c, RealisticBody.ChestYawToward(-t), 1e-4f, "symmetric at " + t);
                prev = c;
            }
            Assert.That(RealisticBody.ChestYawToward(85f), Is.InRange(12f, RealisticBody.ChestYawMax + 1e-4f), "neighbour (~85°)");
            // 胸＋頭＋眼睛加起來搆得到旁邊的人
            float chest = RealisticBody.ChestYawToward(85f), head = RealisticBody.HeadYawToward(85f - chest);
            Assert.LessOrEqual(85f - chest - head - EyeAim.MaxYaw, 12f, "eyes land within 12° of a neighbour");
        }

        /// <summary>同一桌（Pip、Hank、Vex）依序固定眼神，記下眼睛角度、眼皮 blendshape、眼骨頭視線（Hank 根節點座標）。</summary>
        List<(string look, Vector2 eyes, float down, float up, float outL, float inL, Vector3 gaze)> EyeRun(GameObject model)
        {
            var table = New("Table").transform;
            var pip = CharacterFactory.CreateAtSeat(CharacterKind.Rookie, table, 1);
            var vex = CharacterFactory.CreateAtSeat(CharacterKind.Cold, table, 3);
            var p = CharacterFactory.CreateAtSeat(CharacterKind.Loudmouth, table, 2);
            foreach (var o in new[] { pip, vex, p }) o.AutoUpdate = false;
            var body = RealisticBody.Attach(p, model);
            var res = new List<(string, Vector2, float, float, float, float, Vector3)>();
            foreach (var look in new[] { "player", "table", "left", "right" })
            {
                body.Pin = HankPin.Parse("neutral," + look);
                Settle(p, body, 1.5f);
                res.Add((look, body.EyeAngles, body.Weight("eyeLookDownLeft"), body.Weight("eyeLookUpLeft"), body.Weight("eyeLookOutLeft"),
                    body.Weight("eyeLookInLeft"), p.transform.InverseTransformDirection(EyeGaze(body, "LeftEye"))));
            }
            Object.DestroyImmediate(table.gameObject);
            return res;
        }

        [Test]
        public void Hank_EyeRestTilt_DoesNotChangeEyeLimitsLidsOrAim()
        {
            // 新的 GLB 會讓眼骨頭靜止時的 +Y 穿過瞳孔（往下傾、兩眼內聚）：臉的方向要從頭骨頭來，不能跟著眼骨頭傾
            var plain = EyeRun(HankAsset());
            var tilted = Object.Instantiate(HankAsset());
            _spawned.Add(tilted);
            foreach (var eye in tilted.GetComponentsInChildren<Transform>(true))
            {
                if (eye.name != "mixamorig:LeftEye" && eye.name != "mixamorig:RightEye") continue;
                var axis = Vector3.Cross(eye.up, Vector3.up).normalized;
                var q = Quaternion.AngleAxis(16f, axis);
                if ((q * eye.up).y > eye.up.y) q = Quaternion.Inverse(q);
                eye.rotation = q * eye.rotation;
            }
            var tilt = EyeRun(tilted);
            for (int i = 0; i < plain.Count; i++)
            {
                var a = plain[i];
                var b = tilt[i];
                Debug.Log($"[EyeTilt] {a.look}: eyes {a.eyes.ToString("F1")} vs {b.eyes.ToString("F1")}, down {a.down:F1}/{b.down:F1}, gaze diff {Vector3.Angle(a.gaze, b.gaze):F2}°");
                Assert.Less((a.eyes - b.eyes).magnitude, 0.5f, a.look + ": same eye angles (limits)");
                Assert.AreEqual(a.down, b.down, 1f, a.look + ": same lid look-down");
                Assert.AreEqual(a.up, b.up, 1f, a.look + ": same lid look-up");
                Assert.AreEqual(a.outL, b.outL, 1f, a.look + ": same lid look-out");
                Assert.AreEqual(a.inL, b.inL, 1f, a.look + ": same lid look-in");
                Assert.Less(Vector3.Angle(a.gaze, b.gaze), 1f, a.look + ": the pupil (bone +Y) aims at the same point");
            }
        }

        float ChestYaw(RealisticBody body, Transform root, Quaternion restSpine)
        {
            // Spine1 相對靜止姿勢繞上軸轉了幾度（根節點座標）
            var f = root.InverseTransformDirection(body.Bone("Spine1").rotation * Quaternion.Inverse(restSpine) * root.forward);
            return Mathf.Atan2(f.x, f.z) * Mathf.Rad2Deg;
        }

        [Test]
        public void Hank_LooksAtHisNeighbours_MostlyWithTheEyes_NotTurningHisBackOnThePlayer()
        {
            var table = New("Table").transform;
            var pip = CharacterFactory.CreateAtSeat(CharacterKind.Rookie, table, 1);
            var vex = CharacterFactory.CreateAtSeat(CharacterKind.Cold, table, 3);
            var p = CharacterFactory.CreateAtSeat(CharacterKind.Loudmouth, table, 2);
            foreach (var o in new[] { pip, vex, p }) o.AutoUpdate = false;
            var body = RealisticBody.Attach(p, HankAsset());
            body.Pin = HankPin.Parse("player");
            Settle(p, body, 2f);
            var restSpine = body.Bone("Spine1").rotation;
            foreach (var (look, who) in new[] { ("left", pip), ("right", vex) })
            {
                body.Pin = HankPin.Parse(look);
                Settle(p, body, 2f);
                float face = YawPitch(p.transform, body.HeadForwardWorld).x;
                float chest = body.ChestYaw, spine = ChestYaw(body, p.transform, restSpine);
                float want = YawPitch(p.transform, who.Rig.Head.position - body.Bone("LeftEye").position).x;
                float miss = Vector3.Angle(EyeGaze(body, "LeftEye"), who.Rig.Head.position - body.Bone("LeftEye").position);
                Debug.Log($"[HeadSplit] {look}: target {want:F1}°, chest {chest:F1}° (Spine1 {spine:F1}°), face {face:F1}°, eyes {body.EyeAngles.x:F1}°, miss {miss:F1}°");
                // 胸口只補頭（上限 35°）＋眼睛搆不到的部分：舊的小桌鄰座約 75–80°、胸口 12–15°；
                // 真實尺寸長圓桌（2026-11-02）Rocco 座看兩端的人約 70°，胸口約 6°
                Assert.That(Mathf.Abs(chest), Is.InRange(4f, RealisticBody.ChestYawMax + 1f), look + ": chest twists a little (what the head and eyes can't reach)");
                Assert.AreEqual(Mathf.Sign(want), Mathf.Sign(chest), look + ": chest toward them");
                Assert.AreEqual(Mathf.Sign(want), Mathf.Sign(spine), look + ": the Spine1 bone really turns");
                Assert.Greater(Mathf.Abs(spine), Mathf.Abs(chest) - 3f, look + ": the Spine1 bone really turns (plus the cartoon's small idle sway)");
                Assert.LessOrEqual(Mathf.Abs(face - chest), RealisticBody.HeadYawMax + 2f, look + ": head on the chest capped (was ~60°)");
                Assert.LessOrEqual(Mathf.Abs(face), RealisticBody.HeadYawMax + RealisticBody.ChestYawMax + 2f, look + ": face stays mostly toward the player");
                Assert.Greater(Mathf.Abs(body.EyeAngles.x), 15f, look + ": eyes do the rest");
                Assert.Less(miss, 12f, look + ": eyes land on " + who.Spec.DisplayName);
            }
            // 看玩家、看桌面：胸口不轉
            foreach (var look in new[] { "player", "table" })
            {
                body.Pin = HankPin.Parse(look);
                Settle(p, body, 2.5f);
                Assert.AreEqual(0f, body.ChestYaw, 0.1f, look + ": no chest twist");
                Assert.Less(Mathf.Abs(ChestYaw(body, p.transform, restSpine)), 2f, look + ": Spine1 back to rest (idle sway only)");
            }
        }

        [Test]
        public void Hank_ChestTwist_EasesInAndOut_LikeTheHead()
        {
            var table = New("Table").transform;
            var pip = CharacterFactory.CreateAtSeat(CharacterKind.Rookie, table, 1);
            pip.AutoUpdate = false;
            var p = CharacterFactory.CreateAtSeat(CharacterKind.Loudmouth, table, 2);
            p.AutoUpdate = false;
            var body = RealisticBody.Attach(p, HankAsset());
            body.Pin = HankPin.Parse("player");
            Settle(p, body, 1.5f);
            body.Pin = HankPin.Parse("left");
            p.Tick(Dt);
            body.Sync(Dt);
            Assert.Less(Mathf.Abs(body.ChestYaw), 3f, "one frame later: no snap in");
            Settle(p, body, 1.5f);
            // 真實尺寸長圓桌（2026-11-02）：Rocco 座看左端的 Pip 時胸口約轉 6°（舊的小桌 12–15°）
            Assert.Greater(Mathf.Abs(body.ChestYaw), 4f, "gets there");
            float held = body.ChestYaw;
            body.Pin = HankPin.Parse("player");
            p.Tick(Dt);
            body.Sync(Dt);
            Assert.Greater(Mathf.Abs(body.ChestYaw), Mathf.Abs(held) - 3f, "one frame later: no snap back");
            Settle(p, body, 1.5f);
            Assert.Less(Mathf.Abs(body.ChestYaw), 0.5f, "and settles back");
        }

        static AudioClip BurstClip(float silence1, float loud, float silence2)
        {
            const int rate = 44100;
            var s = Burst(rate, silence1, loud, silence2);
            var clip = AudioClip.Create("burst", s.Length, 1, rate, false);
            clip.SetData(s, 0);
            return clip;
        }

        [Test]
        public void Hank_MouthMovesWithTheVoiceClip()
        {
            var (p, body, _) = SeatHank();
            var clip = BurstClip(0.4f, 0.8f, 0.4f);
            p.Speech.Say("Eh, let's see where this goes.", clip, "test.burst", SpeechPriority.Chatter);
            float quiet = 0, loud = 0;
            for (float t = 0; t < 1.6f; t += Dt)
            {
                p.Tick(Dt);
                body.Sync(Dt);
                float j = body.Weight("jawOpen");
                if (t > 0.05f && t < 0.3f) quiet = Mathf.Max(quiet, j);
                if (t > 0.55f && t < 1.1f) loud = Mathf.Max(loud, j);
            }
            Assert.Less(quiet, 5f, "leading silence: mouth shut");
            Assert.Greater(loud, 35f, "voice: jaw open (0–100)");
            Settle(p, body, 0.6f);
            Assert.Less(body.Weight("jawOpen"), 3f, "after the line: closed");
            Object.DestroyImmediate(clip);
        }

        [Test]
        public void Hank_MouthMovesToTheSubtitle_WhenThereIsNoAudio()
        {
            var (p, body, _) = SeatHank();
            p.Speech.Say("Testing a brand new line here, okay?");   // 沒有預錄的句子：沒有聲音（離線備案）
            Assert.IsNotNull(p.Speech.Current);
            Assert.IsNull(p.Speech.Current.Clip);
            int cycles = 0;
            float max = 0;
            bool open = false;
            for (float t = 0; t < 2.5f && p.Speech.Current != null; t += Dt)
            {
                p.Tick(Dt);
                body.Sync(Dt);
                float j = body.Weight("jawOpen");
                max = Mathf.Max(max, j);
                if (!open && j > 25f) { open = true; cycles++; }
                else if (open && j < 10f) open = false;
            }
            Assert.Greater(max, 30f);
            Assert.GreaterOrEqual(cycles, 3);
        }

        [Test]
        public void Hank_HeadTurnsSmoothly_WhenTheGazeTargetJumps()
        {
            // 注視點是瞬間跳的（待機瞄別處、固定眼神）：眼睛可以跳，頭要平滑追上
            var table = New("Table").transform;
            var pip = CharacterFactory.CreateAtSeat(CharacterKind.Rookie, table, 1);
            pip.AutoUpdate = false;
            var p = CharacterFactory.CreateAtSeat(CharacterKind.Loudmouth, table, 2);
            p.AutoUpdate = false;
            var body = RealisticBody.Attach(p, HankAsset());
            body.Pin = HankPin.Parse("player");
            Settle(p, body, 1.5f);
            float before = YawPitch(p.transform, body.HeadForwardWorld).x;
            body.Pin = HankPin.Parse("left");
            p.Tick(Dt);
            body.Sync(Dt);
            float after = YawPitch(p.transform, body.HeadForwardWorld).x;
            Assert.Less(Mathf.Abs(after - before), 10f, "one frame later (snapping was 48°; same speed as the cartoon head is ~6°)");
            Settle(p, body, 1.5f);
            Assert.Greater(Mathf.Abs(YawPitch(p.transform, body.HeadForwardWorld).x - before), 30f, "but it does get there");
        }

        [Test]
        public void Hank_AutoBlink_ClosesTheEyesFully()
        {
            var (p, body, _) = SeatHank();
            p.Play(new PerformanceCue { Mood = Mood.Neutral, Duration = 30f });
            float max = 0;
            for (float t = 0; t < 8f; t += 1f / 72f) { p.Tick(1f / 72f); body.Sync(1f / 72f); max = Mathf.Max(max, body.Weight("eyeBlinkRight")); }
            Assert.Greater(max, 99f);
        }

        [Test]
        public void Hank_Pins_HoldBlinkTalkAndMood()
        {
            var (p, body, _) = SeatHank();
            body.Pin = HankPin.Parse("blink");
            Settle(p, body, 0.3f);
            Assert.AreEqual(100f, body.Weight("eyeBlinkLeft"), 0.5f);
            body.Pin = HankPin.Parse("talk");
            Settle(p, body, 0.5f);
            Assert.Greater(body.Weight("jawOpen"), 40f);
            Assert.Less(body.Weight("eyeBlinkLeft"), 1f, "pinned: no random blink in the shot");
            body.Pin = HankPin.Parse("happy");
            Settle(p, body, 0.5f);
            Assert.Greater(body.Weight("mouthSmileLeft"), 80f);
            body.Pin = HankPin.Parse("table");
            Settle(p, body, 1f);
            Assert.Greater(YawPitch(p.transform, EyeGaze(body, "LeftEye")).y, 15f, "eyes down at the felt");
            body.Pin = default;
            Settle(p, body, 0.5f);
            Assert.Less(body.Weight("mouthSmileLeft"), 60f, "unpinned: back to the performer");
        }

        /// <summary>像 GameController：角色 AutoUpdate = false，由別人在 Update 裡 Tick。</summary>
        sealed class GameLikeTicker : MonoBehaviour
        {
            public CharacterPerformer P;
            void Update() { if (P != null) P.Tick(Time.deltaTime); }
        }

        [UnityTest]
        public IEnumerator Hank_FollowsThePerformer_WhenTheGameTicksIt()
        {
            // 實機回歸（2026-10-29）：GameController 把角色設成 AutoUpdate = false 自己 Tick，Hank 就從來沒同步過（臉、眼、頭都不動）
            var table = New("Table").transform;
            var p = CharacterFactory.CreateAtSeat(CharacterKind.Loudmouth, table, 2);
            p.AutoUpdate = false;
            New("Game").AddComponent<GameLikeTicker>().P = p;
            var body = RealisticBody.Attach(p, HankAsset());
            body.Pin = HankPin.Parse("blink,happy");
            for (int i = 0; i < 10; i++) yield return null;
            Assert.AreEqual(100f, body.Weight("eyeBlinkLeft"), 0.5f, "synced every frame");
            Assert.Greater(body.Weight("mouthSmileLeft"), 80f);
        }

        [UnityTest]
        public IEnumerator Preview_AppliesThePinToHank()
        {
            var table = New("Table").transform;
            var rocco = CharacterFactory.CreateAtSeat(CharacterKind.Loudmouth, table, 2);
            var preview = New("RealisticPreview").AddComponent<RealisticPreview>();
            preview.Model = HankAsset();
            yield return new WaitForSecondsRealtime(0.7f);
            Assert.IsTrue(rocco.TryGetComponent<RealisticBody>(out var body));
            preview.SetPin("blink,annoyed");
            Assert.IsTrue(body.Pin.Blink);
            Assert.AreEqual(Mood.Annoyed, body.Pin.Mood);
            preview.SetPin("0");
            Assert.IsFalse(body.Pin.Active);
        }

        [UnityTest]
        public IEnumerator Preview_SwapsOnlyRocco()
        {
            var table = New("Table").transform;
            var rookie = CharacterFactory.CreateAtSeat(CharacterKind.Rookie, table, 1);
            var rocco = CharacterFactory.CreateAtSeat(CharacterKind.Loudmouth, table, 2);
            var cold = CharacterFactory.CreateAtSeat(CharacterKind.Cold, table, 3);
            var preview = New("RealisticPreview").AddComponent<RealisticPreview>();
            preview.Model = HankAsset();
            // 找角色每 0.5 s 一次：等 1.2 s 讓它跑好幾輪，才驗得到「只掛一次」
            yield return new WaitForSecondsRealtime(1.2f);
            Assert.IsTrue(rocco.TryGetComponent<RealisticBody>(out _), "Rocco swapped");
            Assert.IsFalse(rookie.TryGetComponent<RealisticBody>(out _));
            Assert.IsFalse(cold.TryGetComponent<RealisticBody>(out _));
            Assert.AreEqual(1, Object.FindObjectsByType<RealisticBody>(FindObjectsSortMode.None).Length, "attached once");
            Assert.IsTrue(rookie.GetComponentsInChildren<Renderer>().All(r => !r.forceRenderingOff), "cartoons untouched");
        }
#endif
    }
}
