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
    /// <summary>寫實荷官（取代 Dot，開發用預覽）：同一套 RealisticBody，換模型與每個角色的設定（身高、坐姿、眼睛往上的上限）。</summary>
    public class RealisticDealerTests
    {
        const float Dt = 1f / 60f;
        const string DealerPath = "Assets/_Project/Art/Characters/Dealer/Dealer.glb";
        const string HankPath = "Assets/_Project/Art/Characters/Hank/Hank.glb";
        readonly List<GameObject> _spawned = new List<GameObject>();

        [TearDown]
        public void TearDown()
        {
            foreach (var go in _spawned) if (go != null) Object.DestroyImmediate(go);
            _spawned.Clear();
        }

        GameObject New(string name)
        {
            var go = new GameObject(name);
            _spawned.Add(go);
            return go;
        }

#if UNITY_EDITOR
        static GameObject Asset(string path)
        {
            var go = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(path);
            Assert.IsNotNull(go, path);
            return go;
        }

        (CharacterPerformer p, RealisticBody body, Transform table) SeatDealer(GameObject model = null)
        {
            var table = New("Table").transform;
            table.position = new Vector3(0.3f, 0.74f, -0.2f);
            table.rotation = Quaternion.Euler(0, 25, 0);
            var p = CharacterFactory.CreateAtSeat(CharacterKind.Dealer, table, 4242);
            p.AutoUpdate = false;
            var body = RealisticBody.Attach(p, model != null ? model : Asset(DealerPath), RealisticSpec.Dealer);
            p.Tick(Dt);
            body.Sync(Dt);
            return (p, body, table);
        }

        void Settle(CharacterPerformer p, RealisticBody body, float secs)
        {
            for (float t = 0; t < secs; t += Dt) { p.Tick(Dt); body.Sync(Dt); }
        }

        static Vector2 YawPitch(Transform root, Vector3 worldDir)
        {
            var d = root.InverseTransformDirection(worldDir).normalized;
            return new Vector2(Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg, -Mathf.Asin(Mathf.Clamp(d.y, -1, 1)) * Mathf.Rad2Deg);
        }

        static Vector3 EyeGaze(RealisticBody body, string eye) => body.Bone(eye).up;

        [Test]
        public void Specs_HankAndDealer_DifferInHeightAndEyeLimit()
        {
            Assert.AreEqual(1.80f, RealisticSpec.Hank.StandingHeight, 1e-4f);
            Assert.AreEqual(EyeAim.MaxUp, RealisticSpec.Hank.EyeMaxUp, 1e-4f, "Hank keeps his measured 6°");
            Assert.AreEqual(1.65f, RealisticSpec.Dealer.StandingHeight, 1e-4f);
            // 2026-10-30 量 v5 模型（玩家視角、窄視角拍眼睛）：目視往上 10° 瞳孔約露出三分之二，12° 只剩約一半，13° 以上幾乎被蓋住
            Assert.That(RealisticSpec.Dealer.EyeMaxUp, Is.InRange(9f, 11f), "dealer: measured lid clearance");
            Assert.Less(RealisticSpec.Dealer.HeadTop, RealisticSpec.Hank.HeadTop, "shorter, no hat");
            Assert.AreEqual(RealisticSpec.Hank.HeadTop, RealisticSpec.For(CharacterKind.Loudmouth).HeadTop);
            Assert.AreEqual(RealisticSpec.Dealer.HeadTop, RealisticSpec.For(CharacterKind.Dealer).HeadTop);
        }

        [Test]
        public void EyeAim_UpLimitIsPerCharacter()
        {
            var up40 = Quaternion.Euler(-40f, 0, 0) * Vector3.forward;
            Assert.AreEqual(-EyeAim.MaxUp, EyeAim.Angles(up40).y, 0.01f, "default stays Hank's");
            Assert.AreEqual(-10f, EyeAim.Angles(up40, 10f).y, 0.01f);
            // 眼皮跟著眼睛往上：到這個角色的上限就是全開
            var w = new float[HankFace.Shapes.Length];
            HankFace.Map(new FaceState { EyePitch = -10f }, w, maxUp: 10f);
            float atLimit = w[System.Array.IndexOf(HankFace.Shapes, "eyeLookUpLeft")];
            HankFace.Map(new FaceState { EyePitch = -EyeAim.MaxUp }, w);
            Assert.AreEqual(w[System.Array.IndexOf(HankFace.Shapes, "eyeLookUpLeft")], atLimit, 1e-4f);
        }

        [Test]
        public void Dealer_ImportsWrapModel_FaceWith52Arkit_EyeBones()
        {
            var smrs = Asset(DealerPath).GetComponentsInChildren<SkinnedMeshRenderer>(true);
            var face = smrs.Single(r => r.sharedMesh.blendShapeCount > 0);
            Assert.AreEqual(52, face.sharedMesh.blendShapeCount);
            Assert.AreEqual(54, face.bones.Length);
            foreach (var eye in new[] { "mixamorig:LeftEye", "mixamorig:RightEye" })
                Assert.AreEqual("mixamorig:Head", face.bones.Single(x => x.name == eye).parent.name, eye);
            Assert.IsFalse(face.bones.Any(b => b.name == "mixamorig:HeadTop_End"), "no head-top bone: placement must not need it");
        }

        [Test]
        public void Dealer_SitsAtDotsSeat_RealHeight_HandsOnTheEdge_NoHarshFaceShadows()
        {
            var (p, body, table) = SeatDealer();
            var root = p.transform;
            Assert.AreEqual(1.65f, body.StandingHeight, 0.02f, "scaled to a real height");
            Assert.AreEqual(RealisticSpec.Dealer.HeadTop, table.InverseTransformPoint(body.HeadTopWorld).y, 0.01f, "head top above the table");
            // 獨立量：烘出蒙皮後的頂點，最高點（頭髮頂）在桌面上方多高
            float meshTop = float.MinValue;
            var baked = new Mesh();
            foreach (var r in body.Model.GetComponentsInChildren<SkinnedMeshRenderer>())
            {
                r.BakeMesh(baked, true);   // 只含 renderer 自己的縮放：父節點（模型）的縮放自己補上
                var t = r.transform;
                var parent = t.parent != null ? t.parent.lossyScale : Vector3.one;
                foreach (var v in baked.vertices) meshTop = Mathf.Max(meshTop, table.InverseTransformPoint(t.position + t.rotation * Vector3.Scale(parent, v)).y);
            }
            Object.DestroyImmediate(baked);
            Assert.AreEqual(RealisticSpec.Dealer.HeadTop, meshTop, 0.02f, "measured mesh top agrees");
            var hips = root.InverseTransformPoint(body.Bone("Hips").position);
            Assert.Less(hips.z, -0.1f, "hips behind the edge");
            Assert.Less(hips.y, 0f, "sitting");
            var right = body.Bone("RightArm").position - body.Bone("LeftArm").position;
            Assert.Greater(Vector3.Dot(right.normalized, root.right), 0.9f, "faces the table");
            foreach (var side in new[] { "LeftHand", "RightHand" })
            {
                var h = root.InverseTransformPoint(body.Bone(side).position);
                Assert.That(h.y, Is.InRange(0f, 0.09f), side + " on the table");
                Assert.That(h.z, Is.InRange(-0.1f, 0.05f), side + " wrist at the table edge");
                Assert.GreaterOrEqual(root.InverseTransformPoint(body.Bone(side + "Middle3").position).y, 0f, side + " fingers not through the table");
            }
            // 低面數臉罩：即時陰影會在眼下、鼻下自我遮蔽（shadow terminator）→ 臉不投也不收即時陰影
            foreach (var r in body.Model.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                Assert.IsFalse(r.receiveShadows, r.name + " receives no realtime shadows");
                Assert.AreEqual(UnityEngine.Rendering.ShadowCastingMode.Off, r.shadowCastingMode, r.name);
            }
            Assert.Less(Vector3.Distance(p.Rig.Head.position, body.HeadCenterWorld), 0.08f, "gaze/voice target on her head");
        }

        [Test]
        public void Dealer_AutoBlink_ClosesTheEyesFully()
        {
            var (p, body, _) = SeatDealer();
            p.Play(new PerformanceCue { Mood = Mood.Neutral, Duration = 30f });
            float max = 0;
            for (float t = 0; t < 8f; t += 1f / 72f) { p.Tick(1f / 72f); body.Sync(1f / 72f); max = Mathf.Max(max, body.Weight("eyeBlinkLeft")); }
            Assert.Greater(max, 99f);
        }

        [Test]
        public void Dealer_MouthMovesWithTheVoiceClip()
        {
            var (p, body, _) = SeatDealer();
            const int rate = 44100;
            var s = new float[(int)(rate * 1.6f)];
            for (int i = (int)(rate * 0.4f); i < (int)(rate * 1.2f); i++) s[i] = 0.6f * Mathf.Sin(i * 0.05f);
            var clip = AudioClip.Create("burst", s.Length, 1, rate, false);
            clip.SetData(s, 0);
            p.Speech.Say("Cards in the air.", clip, "test.burst", SpeechPriority.Chatter);
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
            Assert.Greater(loud, 35f, "voice: jaw open");
            Object.DestroyImmediate(clip);
        }

        [Test]
        public void Dealer_LooksAtThePlayer_AndAtTheTable()
        {
            var (p, body, _) = SeatDealer();
            var root = p.transform;
            var viewer = New("Viewer").transform;
            p.SetViewer(viewer);
            p.SetGazedAt(true);
            viewer.position = root.TransformPoint(new Vector3(0.4f, 0.45f, 1.0f));
            p.Play(new PerformanceCue { Mood = Mood.Neutral, Look = LookTarget.Player, Duration = 30f });
            Settle(p, body, 2.5f);
            var to = viewer.position - body.Bone("LeftEye").position;
            Assert.Less(Vector3.Angle(EyeGaze(body, "LeftEye"), to), 8f, "eyes on the player");

            p.Play(new PerformanceCue { Mood = Mood.Neutral, Look = LookTarget.Board, Duration = 30f });
            Settle(p, body, 3f);
            float headDown = YawPitch(root, body.HeadForwardWorld).y;
            float eyeDown = YawPitch(root, EyeGaze(body, "LeftEye")).y;
            Assert.Greater(eyeDown, headDown + 10f, "the eyes look down at the felt");
            Assert.Greater(body.Weight("eyeLookDownLeft"), 15f, "lids follow the eyes down");
        }

        [Test]
        public void Dealer_LooksUp_EyesStayUnderHerLidLimit_HeadCoversTheRest()
        {
            var (p, body, _) = SeatDealer();
            var root = p.transform;
            var viewer = New("Viewer").transform;
            p.SetViewer(viewer);
            p.SetGazedAt(true);
            viewer.position = body.HeadCenterWorld + root.TransformDirection(Quaternion.Euler(-20f, 0, 0) * Vector3.forward) * 1.2f;
            p.Play(new PerformanceCue { Mood = Mood.Neutral, Look = LookTarget.Player, Duration = 30f });
            Settle(p, body, 2.5f);
            var to = viewer.position - body.Bone("LeftEye").position;
            Assert.LessOrEqual(-body.EyeAngles.y, RealisticSpec.Dealer.EyeMaxUp + 0.01f, "eyes ≤ her up limit");
            Assert.Less(Vector3.Angle(EyeGaze(body, "LeftEye"), to), 6f, "the head covers the rest");
        }

        [Test]
        public void Dealer_EyeRestTilt_DoesNotChangeHerEyes()
        {
            // 臉的方向從頭骨頭來：眼骨頭靜止時怎麼傾都一樣
            var (p0, b0, t0) = SeatDealer();
            b0.Pin = HankPin.Parse("neutral,table");
            Settle(p0, b0, 1.5f);
            var plain = (b0.EyeAngles, b0.Weight("eyeLookDownLeft"), p0.transform.InverseTransformDirection(EyeGaze(b0, "LeftEye")));
            Object.DestroyImmediate(t0.gameObject);

            var tilted = Object.Instantiate(Asset(DealerPath));
            _spawned.Add(tilted);
            foreach (var eye in tilted.GetComponentsInChildren<Transform>(true))
            {
                if (eye.name != "mixamorig:LeftEye" && eye.name != "mixamorig:RightEye") continue;
                var axis = Vector3.Cross(eye.up, Vector3.up).normalized;
                var q = Quaternion.AngleAxis(12f, axis);
                if ((q * eye.up).y > eye.up.y) q = Quaternion.Inverse(q);
                eye.rotation = q * eye.rotation;
            }
            var (p1, b1, _) = SeatDealer(tilted);
            b1.Pin = HankPin.Parse("neutral,table");
            Settle(p1, b1, 1.5f);
            Assert.Less((plain.Item1 - b1.EyeAngles).magnitude, 0.5f);
            Assert.AreEqual(plain.Item2, b1.Weight("eyeLookDownLeft"), 1f);
            Assert.Less(Vector3.Angle(plain.Item3, p1.transform.InverseTransformDirection(EyeGaze(b1, "LeftEye"))), 1f);
        }

        /// <summary>這個鍵全開時，上眼皮最多往下幾公尺（真實尺寸）；眼球半徑（公尺）。眼球、皮膚子網格用材質名稱認。</summary>
        static (float lidTravel, float eyeRadius) LidAndEye(RealisticBody body, string key)
        {
            var face = body.FaceSkin;
            var m = face.sharedMesh;
            float scale = body.StandingHeight / body.RawHeight;
            int eyeSub = System.Array.FindIndex(face.sharedMaterials, x => x.name.StartsWith("rb_eye"));
            int skinSub = System.Array.FindIndex(face.sharedMaterials, x => !x.name.StartsWith("rb_"));
            var v = m.vertices;
            var eyes = m.GetTriangles(eyeSub).Distinct().Select(i => v[i]).ToList();
            float eyeY = eyes.Average(p => p.y);
            float radius = (eyes.Max(p => p.y) - eyes.Min(p => p.y)) * 0.5f;
            int i0 = m.GetBlendShapeIndex(key);
            var d = new Vector3[m.vertexCount];
            m.GetBlendShapeFrameVertices(i0, m.GetBlendShapeFrameCount(i0) - 1, d, null, null);
            float travel = m.GetTriangles(skinSub).Distinct().Where(i => v[i].y > eyeY).Max(i => -d[i].y);
            return (travel * scale, radius * scale);
        }

        /// <summary>看桌面時兩眼的「上眼皮下移／虹膜上緣下移」（權重 × 這個鍵的行程）。</summary>
        static (float left, float right, float iris) LidFollow(RealisticBody body)
        {
            var (tl, r) = LidAndEye(body, "eyeLookDownLeft");
            var (tr, _) = LidAndEye(body, "eyeLookDownRight");
            float iris = r * Mathf.Sin(body.EyeAngles.y * Mathf.Deg2Rad);
            return (tl * body.Weight("eyeLookDownLeft") / 100f / iris, tr * body.Weight("eyeLookDownRight") / 100f / iris, iris);
        }

        [Test]
        public void Dealer_LookingAtTheTable_UpperLidFollowsTheIris_FaceStaysVisible()
        {
            // 實機 2026-10-30：看桌面像嚇到（眼皮跟不上）；上一版用頭低補足，頭低到臉看不到。新模型的鍵夠長，眼睛自己轉
            var (p, body, _) = SeatDealer();
            var root = p.transform;
            body.Pin = HankPin.Parse("neutral,table");
            Settle(p, body, 2.5f);
            var (l, r, iris) = LidFollow(body);
            float head = YawPitch(root, body.HeadForwardWorld).y;
            Debug.Log($"[LookDown] Dealer eyes {body.EyeAngles.ToString("F1")}, iris drop {iris * 1000:F1} mm, lid/iris L {l:F2} R {r:F2}, head pitch {head:F1}°");
            Assert.LessOrEqual(body.EyeAngles.y, RealisticSpec.Dealer.EyeMaxDown + 0.01f, "eyes stay within her limit");
            // 她靜止時眼皮就貼著虹膜上緣（Hank 離一段），所以比例比他低：跟滿（1.0）反而蓋住半個瞳孔
            Assert.That(Mathf.Min(l, r), Is.GreaterThan(0.65f), "upper lid comes down with the iris");
            Assert.That(Mathf.Max(l, r), Is.LessThan(0.95f), "but does not cover the pupil");
            Assert.Less(head, 16f, "face stays visible from across the table");
            var board = p.LookPointFor(LookTarget.Board);
            Assert.Less(Vector3.Angle(EyeGaze(body, "LeftEye"), board - body.Bone("LeftEye").position), 8f, "still looking at the table");
        }

        [Test]
        public void Hank_LookingAtTheTable_UpperLidFollowsTheIris()
        {
            // 他的 eyeLookDown 全開帶上眼皮約 8 mm：權重太高會變瞇眼，太低會露眼白；頭不能低太多，帽簷會把臉遮住
            var table = New("Table").transform;
            var p = CharacterFactory.CreateAtSeat(CharacterKind.Loudmouth, table, 2);
            p.AutoUpdate = false;
            var body = RealisticBody.Attach(p, Asset(HankPath), RealisticSpec.Hank);
            body.Pin = HankPin.Parse("neutral,table");
            Settle(p, body, 2.5f);
            var (l, r, iris) = LidFollow(body);
            Debug.Log($"[LookDown] Hank eyes {body.EyeAngles.ToString("F1")}, iris drop {iris * 1000:F1} mm, lid/iris L {l:F2} R {r:F2}");
            Assert.That(Mathf.Min(l, r), Is.GreaterThan(0.85f), "upper lid keeps up with the iris");
            Assert.That(Mathf.Max(l, r), Is.LessThan(1.25f), "but not hooded");
            Assert.Less(YawPitch(p.transform, body.HeadForwardWorld).y, 12f, "head stays up enough that the hat brim does not hide his eyes");
            var to = p.LookPointFor(LookTarget.Board) - body.Bone("LeftEye").position;
            Assert.Less(Vector3.Angle(EyeGaze(body, "LeftEye"), to), 12f, "still looking at the table");
        }

        [Test]
        public void LookDownLimit_IsPerCharacter()
        {
            Assert.That(RealisticSpec.Dealer.EyeMaxDown, Is.InRange(22f, 28f), "the new keys let her eyes go further");
            Assert.That(RealisticSpec.Dealer.HeadDownMax, Is.LessThanOrEqualTo(10f), "a bigger dip hid her face on the device");
            Assert.That(RealisticSpec.Hank.EyeMaxDown, Is.InRange(18f, 23f), "his key is sized for about 21°");
            var down40 = Quaternion.Euler(40f, 0, 0) * Vector3.forward;
            Assert.AreEqual(18f, EyeAim.Angles(down40, 10f, 18f).y, 0.01f);
            // 到這個角色的上限，就是她的 eyeLookDown 全量
            var w = new float[HankFace.Shapes.Length];
            HankFace.Map(new FaceState { EyePitch = 18f }, w, maxDown: 18f, lookDownGain: 0.9f);
            Assert.AreEqual(0.9f, w[System.Array.IndexOf(HankFace.Shapes, "eyeLookDownLeft")], 1e-4f);
        }

        [Test]
        public void LookDown_FadesOutAsTheBlinkCloses_KeysDoNotFoldTheLidPastClosure()
        {
            // 形狀鍵相加：眨眼＋往下看的眼皮行程會超過全閉，眼皮折過頭
            var w = new float[HankFace.Shapes.Length];
            int downL = System.Array.IndexOf(HankFace.Shapes, "eyeLookDownLeft"), downR = System.Array.IndexOf(HankFace.Shapes, "eyeLookDownRight");
            var f = new FaceState { EyePitch = 26f };
            HankFace.Map(f, w, maxDown: 26f, lookDownGain: 1f);
            Assert.AreEqual(1f, w[downL], 1e-4f, "eyes open: full look-down");
            f.Blink = 1f;
            HankFace.Map(f, w, maxDown: 26f, lookDownGain: 1f);
            Assert.AreEqual(0f, w[downL], 1e-4f, "closed: no look-down, left");
            Assert.AreEqual(0f, w[downR], 1e-4f, "closed: no look-down, right");
            // 中間：閉到一半也不會超過（眨眼行程 ≈ 全開往下看的行程，兩者相加不超過 1）
            for (float b = 0; b <= 1f; b += 0.05f)
            {
                f.Blink = b;
                HankFace.Map(f, w, maxDown: 26f, lookDownGain: 1f);
                float blinkW = w[System.Array.IndexOf(HankFace.Shapes, "eyeBlinkLeft")];
                Assert.LessOrEqual(blinkW + w[downL], 1f + 1e-4f, "sum at blink " + b);
            }
        }

        [TestCase(DealerPath), TestCase(HankPath)]
        public void EyeLookKeys_MoveOnlyTheLids_NotTheEyeball(string path)
        {
            // 眼球由骨頭轉：形狀鍵再動眼球就是轉兩次。眼球子網格用材質名稱認（順序不一定和 GLB 相同）
            var face = Asset(path).GetComponentsInChildren<SkinnedMeshRenderer>(true).Single(r => r.sharedMesh.blendShapeCount > 0);
            var m = face.sharedMesh;
            int sub = System.Array.FindIndex(face.sharedMaterials, x => x.name.StartsWith("rb_eye"));
            Assert.GreaterOrEqual(sub, 0);
            var eyeVerts = new HashSet<int>(m.GetTriangles(sub));
            var d = new Vector3[m.vertexCount];
            foreach (var key in new[] { "eyeLookUpLeft", "eyeLookDownLeft", "eyeLookInLeft", "eyeLookOutLeft", "eyeLookUpRight", "eyeLookDownRight", "eyeLookInRight", "eyeLookOutRight" })
            {
                int i = m.GetBlendShapeIndex(key);
                m.GetBlendShapeFrameVertices(i, m.GetBlendShapeFrameCount(i) - 1, d, null, null);
                float eye = 0, any = 0;
                for (int v = 0; v < d.Length; v++)
                {
                    any = Mathf.Max(any, d[v].magnitude);
                    if (eyeVerts.Contains(v)) eye = Mathf.Max(eye, d[v].magnitude);
                }
                Debug.Log($"[EyeKeys] {path} {key} eyeball {eye:F5} any {any:F5}");
                Assert.Less(eye, 1e-4f, key);
                Assert.Greater(any, 1e-4f, key + " still moves the lids");
            }
        }

        [Test]
        public void Preview_ListsWhatIsLockedToTheCamera()
        {
            var cam = New("Cam").transform;
            var locked = GameObject.CreatePrimitive(PrimitiveType.Quad);
            locked.name = "HeadLockedThing";
            locked.transform.SetParent(cam, false);
            var free = GameObject.CreatePrimitive(PrimitiveType.Quad);
            free.name = "WorldThing";
            _spawned.Add(free);
            var names = RealisticPreview.CameraLockedRenderers(cam).Select(r => r.name).ToList();
            CollectionAssert.Contains(names, "HeadLockedThing");
            CollectionAssert.DoesNotContain(names, "WorldThing");
        }

        sealed class GameLikeTicker : MonoBehaviour
        {
            public CharacterPerformer P;
            void Update() { if (P != null) P.Tick(Time.deltaTime); }
        }

        [UnityTest]
        public IEnumerator Dealer_FollowsThePerformer_WhenTheGameTicksIt()
        {
            var table = New("Table").transform;
            var p = CharacterFactory.CreateAtSeat(CharacterKind.Dealer, table, 4242);
            p.AutoUpdate = false;
            New("Game").AddComponent<GameLikeTicker>().P = p;
            var body = RealisticBody.Attach(p, Asset(DealerPath), RealisticSpec.Dealer);
            body.Pin = HankPin.Parse("blink,happy");
            for (int i = 0; i < 10; i++) yield return null;
            Assert.AreEqual(100f, body.Weight("eyeBlinkLeft"), 0.5f, "synced every frame");
            Assert.Greater(body.Weight("mouthSmileLeft"), 80f);
        }

        [UnityTest]
        public IEnumerator Preview_SwapsRoccoAndDot_EachWithItsOwnModelAndSpec()
        {
            var table = New("Table").transform;
            var rookie = CharacterFactory.CreateAtSeat(CharacterKind.Rookie, table, 1);
            var rocco = CharacterFactory.CreateAtSeat(CharacterKind.Loudmouth, table, 2);
            var dot = CharacterFactory.CreateAtSeat(CharacterKind.Dealer, table, 4242);
            var preview = New("RealisticPreview").AddComponent<RealisticPreview>();
            preview.Model = Asset(HankPath);
            preview.DealerModel = Asset(DealerPath);
            yield return new WaitForSecondsRealtime(1.2f);
            Assert.IsTrue(rocco.TryGetComponent<RealisticBody>(out var hank));
            Assert.IsTrue(dot.TryGetComponent<RealisticBody>(out var dealer));
            Assert.IsFalse(rookie.TryGetComponent<RealisticBody>(out _));
            Assert.AreEqual(2, Object.FindObjectsByType<RealisticBody>(FindObjectsSortMode.None).Length, "attached once each");
            Assert.AreEqual(1.80f, hank.StandingHeight, 0.02f);
            Assert.AreEqual(1.65f, dealer.StandingHeight, 0.02f);
            StringAssert.StartsWith("Dealer", dealer.Model.name);
            preview.SetPin("blink");
            Assert.IsTrue(dealer.Pin.Blink, "pin applies to the dealer too");
        }
#endif
    }
}
