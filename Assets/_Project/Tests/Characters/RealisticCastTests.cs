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
    /// <summary>
    /// 新的寫實角色（Marcus、Jace、Reginald、Erik）：每位自己的 RealisticSpec，同一套檢查（比照 Hank／荷官）；
    /// 預覽陣容（debug.tell.lineup）讓 3 個對手位都放寫實角色。
    /// </summary>
    public class RealisticCastTests
    {
        const float Dt = 1f / 60f;
        static readonly string[] Names = { "Marcus", "Jace", "Reginald", "Erik" };
        static readonly Dictionary<string, float> Heights = new Dictionary<string, float> { { "Marcus", 1.75f }, { "Jace", 1.83f }, { "Reginald", 1.78f }, { "Erik", 1.83f } };
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

        // ───────────── 陣容（純邏輯） ─────────────

        static readonly string[] Cast = { "Hank", "Marcus", "Jace", "Reginald", "Erik" };

        [Test]
        public void Lineup_ParsesNamesBySeat_CaseInsensitive_DashKeepsTheCartoon()
        {
            // 2026-11-02 真實尺寸的桌子：4 個對手位（Pip、Rocco、荷官右邊的老手、Vex 的座位）
            CollectionAssert.AreEqual(new[] { CharacterKind.Rookie, CharacterKind.Loudmouth, CharacterKind.Veteran, CharacterKind.Cold }, RealisticPreview.LineupSeats, "seat order: Pip, Rocco, new seat, Vex");
            CollectionAssert.AreEqual(new[] { "Reginald", "Hank", "Erik", "Jace" }, RealisticPreview.ParseLineup(" reginald, HANK ,Erik,jace", Cast));
            CollectionAssert.AreEqual(new[] { "Marcus", null, "Jace", null }, RealisticPreview.ParseLineup("Marcus,-,Jace", Cast));
            CollectionAssert.AreEqual(new[] { "Marcus", null, null, null }, RealisticPreview.ParseLineup("Marcus", Cast), "missing seats stay cartoon");
            CollectionAssert.AreEqual(new[] { null, "Jace", null, null }, RealisticPreview.ParseLineup("Nobody,Jace", Cast), "unknown name stays cartoon");
            CollectionAssert.AreEqual(new[] { "Marcus", "Hank", "Jace", "Erik" }, RealisticPreview.ParseLineup("Marcus,Hank,Jace,Erik,Reginald", Cast), "only 4 opponent seats");
            Assert.IsNull(RealisticPreview.ParseLineup("", Cast), "empty = use the default");
            Assert.IsNull(RealisticPreview.ParseLineup("0", Cast));
            Assert.IsNull(RealisticPreview.ParseLineup(null, Cast));
            Assert.AreEqual("Marcus,Hank,Reginald,Jace", RealisticPreview.DefaultLineup);
            var def = RealisticPreview.ParseLineup(RealisticPreview.DefaultLineup, Cast);
            Assert.IsTrue(def.All(n => n != null), "default lineup is all realistic");
        }

        [Test]
        public void Lineup_TheNewSeatHasNoCartoon_SoItAlwaysGetsARealisticCharacter()
        {
            // 老手座沒有卡通角色：「-」、沒給、名字不認得都用預設陣容的那一位（Reginald），其他座位照舊維持卡通
            CollectionAssert.AreEqual(new[] { "Marcus", "Hank", "Reginald", "Jace" }, RealisticPreview.ResolveLineup("Marcus,Hank,-,Jace", Cast));
            CollectionAssert.AreEqual(new[] { "Marcus", null, "Reginald", null }, RealisticPreview.ResolveLineup("Marcus", Cast));
            CollectionAssert.AreEqual(new[] { null, null, "Erik", null }, RealisticPreview.ResolveLineup("-,-,erik,-", Cast));
            CollectionAssert.AreEqual(RealisticPreview.ParseLineup(RealisticPreview.DefaultLineup, Cast), RealisticPreview.ResolveLineup("", Cast), "empty = the default");
            CollectionAssert.AreEqual(new[] { CharacterKind.Veteran }, RealisticPreview.SeatsWithoutCartoon);
        }

        [Test]
        public void DevSay_ParsesSeatsAndPicksALineFromThatSeatsBook()
        {
            // 實機驗證（頭盔沒人戴、沒有手）：adb shell setprop debug.tell.say "rookie+loudmouth:3" 讓指定座位講一句有預錄的台詞（兩個人同時講）
            CollectionAssert.AreEqual(new[] { CharacterKind.Rookie, CharacterKind.Loudmouth }, RealisticPreview.ParseSay("rookie+loudmouth:3"));
            CollectionAssert.AreEqual(new[] { CharacterKind.Veteran }, RealisticPreview.ParseSay(" Veteran "));
            CollectionAssert.AreEqual(new[] { CharacterKind.Dealer, CharacterKind.Cold }, RealisticPreview.ParseSay("dealer+cold:7"));
            CollectionAssert.IsEmpty(RealisticPreview.ParseSay(""));
            CollectionAssert.IsEmpty(RealisticPreview.ParseSay("0"));
            CollectionAssert.IsEmpty(RealisticPreview.ParseSay("nobody:2"));
            Assert.AreEqual("Bet. Don't take it personally.", RealisticPreview.SayLine(CharacterKind.Veteran, 0), "first line in his book");
            StringAssert.StartsWith("pip.", RealisticPreview.SayLineId(CharacterKind.Rookie, 5));
            StringAssert.StartsWith("dot.", RealisticPreview.SayLineId(CharacterKind.Dealer, 1));
            Assert.AreNotEqual(RealisticPreview.SayLine(CharacterKind.Cold, 0), RealisticPreview.SayLine(CharacterKind.Cold, 1), "the number picks different lines");
        }

        [Test]
        public void Preview_OpensTheFifthSeat_OpponentsInSeatOrder()
        {
            // MainSceneBuilder 在寫實預覽建置時把 GameConfig.Opponents 換成這個（正式版沒有寫實模型，維持 3 位卡通對手）
            CollectionAssert.AreEqual(new[] { Archetype.Rookie, Archetype.Loudmouth, Archetype.Veteran, Archetype.Cold }, RealisticPreview.Opponents);
        }

        [Test]
        public void Spec_EachCharacterHasTheirOwn()
        {
            foreach (var n in Names)
            {
                var s = RealisticSpec.ByName(n);
                Assert.IsTrue(s.HasValue, n);
                Assert.AreEqual(Heights[n], s.Value.StandingHeight, 1e-4f, n + " height");
                Assert.That(s.Value.EyeMaxUp, Is.InRange(3f, 15f), n);
                Assert.That(s.Value.EyeMaxDown, Is.InRange(14f, 30f), n);
                Assert.That(s.Value.LookDownGain, Is.InRange(0.2f, 1f), n);
                Assert.That(s.Value.HeadDownMax, Is.InRange(0f, 10f), n + ": a big dip hides the face");
            }
            Assert.AreEqual(RealisticSpec.Hank.EyeMaxDown, RealisticSpec.ByName("Hank").Value.EyeMaxDown);
            Assert.AreEqual(RealisticSpec.Dealer.LookDownGain, RealisticSpec.ByName("Dealer").Value.LookDownGain);
            Assert.IsFalse(RealisticSpec.ByName("Nobody").HasValue);
            // 不是照抄：四位的眼睛設定不會全部相同
            Assert.Greater(Names.Select(n => RealisticSpec.ByName(n).Value).Select(s => (s.EyeMaxUp, s.EyeMaxDown, s.LookDownGain)).Distinct().Count(), 1);
        }

#if UNITY_EDITOR
        static string PathOf(string name) => $"Assets/_Project/Art/Characters/{name}/{name}.glb";

        static GameObject Asset(string name)
        {
            var go = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(PathOf(name));
            Assert.IsNotNull(go, PathOf(name));
            return go;
        }

        (CharacterPerformer p, RealisticBody body, Transform table) Seat(string name, CharacterKind seat = CharacterKind.Loudmouth)
        {
            var table = New("Table").transform;
            table.position = new Vector3(0.3f, 0.74f, -0.2f);
            table.rotation = Quaternion.Euler(0, 25, 0);
            var p = CharacterFactory.CreateAtSeat(seat, table, 2);
            p.AutoUpdate = false;
            var body = RealisticBody.Attach(p, Asset(name), RealisticSpec.ByName(name).Value);
            p.Tick(Dt);
            body.Sync(Dt);
            return (p, body, table);
        }

        static void Settle(CharacterPerformer p, RealisticBody body, float secs)
        {
            for (float t = 0; t < secs; t += Dt) { p.Tick(Dt); body.Sync(Dt); }
        }

        static Vector2 YawPitch(Transform root, Vector3 worldDir)
        {
            var d = root.InverseTransformDirection(worldDir).normalized;
            return new Vector2(Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg, -Mathf.Asin(Mathf.Clamp(d.y, -1, 1)) * Mathf.Rad2Deg);
        }

        static Vector3 EyeGaze(RealisticBody body, string eye) => body.Bone(eye).up;

        /// <summary>這個鍵全開時上眼皮最多往下幾公尺、眼球半徑（真實尺寸）；子網格用材質名稱認。</summary>
        static (float lidTravel, float eyeRadius) LidAndEye(RealisticBody body, string key)
        {
            var face = body.FaceSkin;
            var m = face.sharedMesh;
            float scale = body.StandingHeight / body.RawHeight;
            int eyeSub = System.Array.FindIndex(face.sharedMaterials, x => x.name.StartsWith("rb_eye"));
            int skinSub = System.Array.FindIndex(face.sharedMaterials, x => !x.name.StartsWith("rb_"));
            var v = m.vertices;
            var eyes = m.GetTriangles(eyeSub).Distinct().Select(i => v[i]).ToList();
            float eyeY = eyes.Average(q => q.y);
            float radius = (eyes.Max(q => q.y) - eyes.Min(q => q.y)) * 0.5f;
            int i0 = m.GetBlendShapeIndex(key);
            var d = new Vector3[m.vertexCount];
            m.GetBlendShapeFrameVertices(i0, m.GetBlendShapeFrameCount(i0) - 1, d, null, null);
            float travel = m.GetTriangles(skinSub).Distinct().Where(i => v[i].y > eyeY).Max(i => -d[i].y);
            return (travel * scale, radius * scale);
        }

        // ───────────── 每位都要過的檢查 ─────────────

        [TestCaseSource(nameof(Names))]
        public void Imports_FaceWith52Arkit_EyeBonesUnderTheHead(string name)
        {
            var smrs = Asset(name).GetComponentsInChildren<SkinnedMeshRenderer>(true);
            var face = smrs.Single(r => r.sharedMesh.blendShapeCount > 0);
            Assert.AreEqual(52, face.sharedMesh.blendShapeCount);
            foreach (var eye in new[] { "mixamorig:LeftEye", "mixamorig:RightEye" })
                Assert.AreEqual("mixamorig:Head", face.bones.Single(x => x.name == eye).parent.name, eye);
            CollectionAssert.AreEquivalent(new[] { name.ToLowerInvariant() + "_face", "rb_inner", "rb_eye" }, face.sharedMaterials.Select(x => x.name));
            if (name == "Marcus") Assert.IsTrue(smrs.Any(r => r.name == "Glasses"), "Marcus wears glasses");
        }

        [TestCaseSource(nameof(Names))]
        public void Sits_AtRealHeight_HandsAtTheEdge_NoRealtimeShadows(string name)
        {
            var (p, body, table) = Seat(name);
            var root = p.transform;
            var spec = RealisticSpec.ByName(name).Value;
            Assert.AreEqual(Heights[name], body.StandingHeight, 0.02f, "scaled to a real height");
            // 獨立量：烘出蒙皮後的頂點，最高點（頭髮頂）在桌面上方多高
            float meshTop = float.MinValue;
            var baked = new Mesh();
            foreach (var r in body.Model.GetComponentsInChildren<SkinnedMeshRenderer>())
            {
                r.BakeMesh(baked, true);
                var t = r.transform;
                var parent = t.parent != null ? t.parent.lossyScale : Vector3.one;
                foreach (var v in baked.vertices) meshTop = Mathf.Max(meshTop, table.InverseTransformPoint(t.position + t.rotation * Vector3.Scale(parent, v)).y);
            }
            Object.DestroyImmediate(baked);
            Assert.AreEqual(spec.HeadTop, meshTop, 0.03f, "head top above the table");
            var hips = root.InverseTransformPoint(body.Bone("Hips").position);
            Assert.Less(hips.z, -0.1f, "hips behind the edge");
            Assert.Less(hips.y, 0f, "sitting");
            foreach (var side in new[] { "LeftHand", "RightHand" })
            {
                var h = root.InverseTransformPoint(body.Bone(side).position);
                Assert.That(h.y, Is.InRange(0f, 0.09f), side + " on the table");
                Assert.That(h.z, Is.InRange(-0.1f, 0.05f), side + " wrist at the table edge");
                Assert.GreaterOrEqual(root.InverseTransformPoint(body.Bone(side + "Middle3").position).y, 0f, side + " fingers not through the table");
            }
            foreach (var r in body.Model.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                // Android 的品質等級（Medium）只用 2 根骨頭蒙皮，Editor 用不限：頭盔上領口、眼皮的變形和 Editor 不同（2026-11-02 Reginald 領口）
                Assert.AreEqual(SkinQuality.Bone4, r.quality, r.name + " skins with 4 bones on every platform");
                Assert.AreEqual(UnityEngine.Rendering.ShadowCastingMode.Off, r.shadowCastingMode, r.name + " casts no shadows (glasses: broken lines on the cheeks)");
                Assert.IsFalse(r.receiveShadows, r.name);
            }
        }

        [TestCaseSource(nameof(Names))]
        public void LooksAtThePlayer(string name)
        {
            var (p, body, _) = Seat(name);
            var viewer = New("Viewer").transform;
            p.SetViewer(viewer);
            p.SetGazedAt(true);
            viewer.position = p.transform.TransformPoint(new Vector3(0.3f, 0.45f, 1.0f));
            p.Play(new PerformanceCue { Mood = Mood.Neutral, Look = LookTarget.Player, Duration = 30f });
            Settle(p, body, 2.5f);
            Assert.Less(Vector3.Angle(EyeGaze(body, "LeftEye"), viewer.position - body.Bone("LeftEye").position), 8f, "eyes on the player");
        }

        /// <summary>
        /// 看桌面時「上眼皮下移／虹膜上緣下移」可接受的範圍：靜止時眼皮離虹膜多遠每位不同，所以範圍各自定（2026-11-02 玩家視角放大截圖目視）：
        /// Marcus 靜止時上眼皮就壓在瞳孔上緣，跟滿（0.9 權重）看桌面時眼睛幾乎閉上；Reginald 半垂眼。
        /// 修補版（2026-11-02）Jace／Erik 上眼皮降低：跟滿就蓋到瞳孔，上限改 0.95。
        /// </summary>
        static readonly Dictionary<string, (float lo, float hi)> LidBand = new Dictionary<string, (float, float)>
        {
            { "Marcus", (0.45f, 0.85f) }, { "Jace", (0.65f, 0.95f) }, { "Reginald", (0.4f, 1.0f) }, { "Erik", (0.6f, 0.95f) },
        };

        [TestCaseSource(nameof(Names))]
        public void LooksAtTheTable_UpperLidFollowsTheIris_FaceStaysVisible(string name)
        {
            var (p, body, _) = Seat(name);
            var root = p.transform;
            body.Pin = HankPin.Parse("neutral,table");
            Settle(p, body, 2.5f);
            var (tl, r) = LidAndEye(body, "eyeLookDownLeft");
            var (tr, _) = LidAndEye(body, "eyeLookDownRight");
            float iris = r * Mathf.Sin(body.EyeAngles.y * Mathf.Deg2Rad);
            float l = tl * body.Weight("eyeLookDownLeft") / 100f / iris, rr = tr * body.Weight("eyeLookDownRight") / 100f / iris;
            float head = YawPitch(root, body.HeadForwardWorld).y;
            Debug.Log($"[LookDown] {name} eyes {body.EyeAngles.ToString("F1")}, iris drop {iris * 1000:F1} mm, lid/iris L {l:F2} R {rr:F2}, head pitch {head:F1}°");
            var band = LidBand[name];
            Assert.That(Mathf.Min(l, rr), Is.GreaterThan(band.lo), "upper lid comes down with the iris");
            Assert.That(Mathf.Max(l, rr), Is.LessThan(band.hi), "but not hooded");
            Assert.Less(head, 16f, "face stays visible from across the table");
            Assert.Less(Vector3.Angle(EyeGaze(body, "LeftEye"), p.LookPointFor(LookTarget.Board) - body.Bone("LeftEye").position), 12f, "looking at the table");
        }

        [TestCaseSource(nameof(Names))]
        public void Blinks_Closed_AndTalks_WithTheVoice(string name)
        {
            var (p, body, _) = Seat(name);
            body.Pin = HankPin.Parse("blink,neutral,table");
            Settle(p, body, 0.5f);
            Assert.AreEqual(100f, body.Weight("eyeBlinkLeft"), 0.5f, "pinned blink closes the lids");
            Assert.AreEqual(0f, body.Weight("eyeLookDownLeft"), 0.5f, "look-down fades out while closed (keys add)");
            body.Pin = default;
            const int rate = 44100;
            var s = new float[(int)(rate * 1.6f)];
            for (int i = (int)(rate * 0.4f); i < (int)(rate * 1.2f); i++) s[i] = 0.6f * Mathf.Sin(i * 0.05f);
            var clip = AudioClip.Create("burst", s.Length, 1, rate, false);
            clip.SetData(s, 0);
            p.Speech.Say("Your move.", clip, "test.burst", SpeechPriority.Chatter);
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

        /// <summary>從玩家眼睛看過去，一個點的水平角／仰角（度；牌桌座標，+Z 朝桌心）。</summary>
        static Vector2 Angles(Transform table, Vector3 world)
        {
            var d = table.InverseTransformPoint(world) - RealisticPreview.PlayerEye;
            return new Vector2(Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg, Mathf.Atan2(d.y, new Vector2(d.x, d.z).magnitude) * Mathf.Rad2Deg);
        }

        /// <summary>頭（半徑 0.12 m 的球）從玩家眼睛看的角度範圍：x＝水平、y＝仰角。</summary>
        static Rect HeadBox(Transform table, RealisticBody b)
        {
            var c = Angles(table, b.HeadCenterWorld);
            float dist = (table.InverseTransformPoint(b.HeadCenterWorld) - RealisticPreview.PlayerEye).magnitude;
            float r = Mathf.Asin(Mathf.Clamp01(0.12f / dist)) * Mathf.Rad2Deg;
            return new Rect(c.x - r, c.y - r, 2 * r, 2 * r);
        }

        static IEnumerable<string> Lineups() => new[] { "Marcus,Hank,Reginald,Jace", "Reginald,Hank,Erik,Marcus", "Jace,Erik,Marcus,Hank" };

        /// <summary>
        /// 真實尺寸的長圓桌（2026-11-02 使用者回報：小桌擠在一起、荷官和 Rocco 重疊）：4 位寫實對手＋荷官。
        /// 從玩家眼睛看：兩端的頭中心在 ±56° 內、整顆頭在 ±61° 內（Quest 3 水平約 ±55°，使用者接受兩端稍微轉頭；舊的小桌是 ±50°）；
        /// 頭不重疊、相鄰兩人的肩膀不重疊（荷官兩側的人不壓到荷官）；手放在自己面前的桌緣；骨盆在扶手外（身體不穿過扶手）。
        /// </summary>
        [TestCaseSource(nameof(Lineups))]
        public void FullRealisticTable_EveryoneVisibleFromThePlayer_NoOverlap_HandsAtTheirEdge(string lineup)
        {
            var table = New("Table").transform;
            var names = RealisticPreview.ResolveLineup(lineup, Cast);
            var bodies = new List<(string who, RealisticBody body, CharacterPerformer p)>();
            for (int i = 0; i < RealisticPreview.LineupSeats.Length; i++)
            {
                var kind = RealisticPreview.LineupSeats[i];
                var p = CharacterFactory.CreateAtSeat(kind, table, i + 1);
                p.AutoUpdate = false;
                var spec = RealisticPreview.SeatSpec(RealisticSpec.ByName(names[i]).Value, kind);
                bodies.Add((names[i], RealisticBody.Attach(p, Asset(names[i]), spec), p));
            }
            var dot = CharacterFactory.CreateAtSeat(CharacterKind.Dealer, table, 4242);
            dot.AutoUpdate = false;
            bodies.Insert(2, ("Dealer", RealisticBody.Attach(dot, Asset("Dealer"), RealisticPreview.SeatSpec(RealisticSpec.Dealer, CharacterKind.Dealer)), dot));
            // 由左到右：Pip 座、Rocco 座、荷官、老手座、Vex 座
            var boxes = bodies.Select(b => (b.who, box: HeadBox(table, b.body), center: Angles(table, b.body.HeadCenterWorld).x)).ToList();
            Debug.Log($"[Seats] {lineup}: " + string.Join("; ", boxes.Select(b => $"{b.who} yaw {b.box.xMin:F0}..{b.box.xMax:F0} pitch {b.box.yMin:F0}..{b.box.yMax:F0}")));
            foreach (var (who, box, center) in boxes)
            {
                Assert.LessOrEqual(Mathf.Abs(center), 56f, who + ": head center within a slight head turn");
                Assert.LessOrEqual(Mathf.Max(Mathf.Abs(box.xMin), Mathf.Abs(box.xMax)), 61f, who + ": whole head within a slight head turn");
            }
            for (int i = 0; i < boxes.Count; i++)
                for (int j = i + 1; j < boxes.Count; j++)
                {
                    var a = boxes[i].box; var b = boxes[j].box;
                    a.xMin -= 1.5f; a.xMax += 1.5f; b.xMin -= 1.5f; b.xMax += 1.5f;   // 至少隔 3°
                    Assert.IsFalse(a.Overlaps(b), $"{boxes[i].who} and {boxes[j].who} heads overlap or nearly touch");
                }
            (float lo, float hi) Torso(RealisticBody b)
            {
                float l = Angles(table, b.Bone("LeftArm").position).x, r = Angles(table, b.Bone("RightArm").position).x;
                return (Mathf.Min(l, r), Mathf.Max(l, r));
            }
            var torsos = bodies.Select(b => (b.who, t: Torso(b.body))).ToList();
            Debug.Log($"[Seats] torsos: " + string.Join("; ", torsos.Select(t => $"{t.who} {t.t.lo:F0}..{t.t.hi:F0}")));
            for (int i = 1; i < torsos.Count; i++)
                Assert.Less(torsos[i - 1].t.hi, torsos[i].t.lo, $"{torsos[i - 1].who}'s shoulders overlap {torsos[i].who}'s ({torsos[i - 1].t.lo:F0}..{torsos[i - 1].t.hi:F0} vs {torsos[i].t.lo:F0}..{torsos[i].t.hi:F0})");
            foreach (var (who, body, p) in bodies)
            {
                foreach (var side in new[] { "LeftHand", "RightHand" })
                {
                    var h = p.transform.InverseTransformPoint(body.Bone(side).position);
                    Assert.That(h.z, Is.InRange(-0.1f, 0.05f), $"{who} {side} wrist at the table edge in front of them");
                }
                if (p.Kind == CharacterKind.Dealer) continue;   // 荷官坐在缺口裡，前面沒有扶手
                var hips = p.transform.InverseTransformPoint(body.Bone("Hips").position);
                Assert.Less(hips.z, -(TableSeats.RailWidth + 0.1f), $"{who}: hips behind the armrest, not through it");
            }
        }

        [Test]
        public void Reginald_LooksAtHisNeighbours_HeadTurnsLessOnTheChest_SoTheCollarHolds()
        {
            // 頭盔 2026-11-02：看左時下巴下方的領口撕出暗缺口（頸部蒙皮在頭相對胸口轉 35° 時撐不住；換臉端已停損）。
            // Editor 遊戲燈光掃描：頭相對胸口 ≤ 22° 缺口消失 → 頭少轉、胸口多分擔，臉的總朝向不變。
            // 2026-11-02 起他預設坐荷官右邊（老手座）：右手邊是荷官（最近的鄰座，約轉 70°）、左手邊是 Vex，最遠的 Pip 在右邊。
            var spec = RealisticSpec.Reginald;
            Assert.That(spec.HeadYawLimit, Is.InRange(15f, 23f), "head limit on the chest (default 35)");
            Assert.That(spec.ChestYawLimit, Is.InRange(20f, 40f), "chest takes more (default 15)");
            var table = New("Table").transform;
            var pip = CharacterFactory.CreateAtSeat(CharacterKind.Rookie, table, 1);
            var vex = CharacterFactory.CreateAtSeat(CharacterKind.Cold, table, 3);
            var dealer = CharacterFactory.CreateAtSeat(CharacterKind.Dealer, table, 4242);
            var p = CharacterFactory.CreateAtSeat(CharacterKind.Veteran, table, 2);
            foreach (var o in new[] { pip, vex, dealer, p }) o.AutoUpdate = false;
            var body = RealisticBody.Attach(p, Asset("Reginald"), RealisticPreview.SeatSpec(spec, CharacterKind.Veteran));
            foreach (var (look, who) in new[] { ("left", pip), ("right", vex) })
            {
                body.Pin = HankPin.Parse(look);
                Settle(p, body, 2.5f);
                float face = YawPitch(p.transform, body.HeadForwardWorld).x;
                float onChest = face - body.ChestYaw;
                var to = who.Rig.Head.position - body.Bone("LeftEye").position;
                float want = YawPitch(p.transform, to).x;
                Debug.Log($"[Reginald] {look}: target {want:F1}, chest {body.ChestYaw:F1}, head on chest {onChest:F1}, face {face:F1}, eyes {body.EyeAngles.x:F1}");
                Assert.LessOrEqual(Mathf.Abs(onChest), spec.HeadYawLimit + 1.5f, look + ": head turns little on the chest");
                Assert.AreEqual(Mathf.Sign(want), Mathf.Sign(face), look + ": still faces toward them");
                // 舊座位（Rocco 座）的鄰座約 50°，當時要求臉轉 > 35°；老手座兩邊的人都約 60°，眼睛到上限（25°）、臉轉 34–38°，剩下的由眼睛補到 12° 內（下一條）
                Assert.GreaterOrEqual(Mathf.Abs(face) + EyeAim.MaxYaw, Mathf.Abs(want) - 5f, look + ": face plus the eyes' reach covers the target (chest takes the rest of the head's share)");
                Assert.LessOrEqual(Mathf.Abs(body.EyeAngles.x), EyeAim.MaxYaw + 0.01f, look + ": eyes within their limit");
                Assert.Less(Vector3.Angle(EyeGaze(body, "LeftEye"), to), 12f, look + ": eyes land on " + who.Spec.DisplayName);
            }
            // 看旁邊的荷官（遊戲裡 LookTarget.OtherSeat 沒有指定別人時看荷官）
            p.LookPointResolver = (t, other) => t == LookTarget.OtherSeat ? dealer.Rig.Head.position : (Vector3?)null;
            p.Play(new PerformanceCue { Mood = Mood.Neutral, Look = LookTarget.OtherSeat, LookSeat = -1, Duration = 30f });
            body.Pin = default;
            Settle(p, body, 2.5f);
            {
                float face = YawPitch(p.transform, body.HeadForwardWorld).x;
                float onChest = face - body.ChestYaw;
                var to = dealer.Rig.Head.position - body.Bone("LeftEye").position;
                Debug.Log($"[Reginald] dealer: target {YawPitch(p.transform, to).x:F1}, chest {body.ChestYaw:F1}, head on chest {onChest:F1}, face {face:F1}, eyes {body.EyeAngles.x:F1}");
                Assert.LessOrEqual(Mathf.Abs(onChest), spec.HeadYawLimit + 1.5f, "dealer: head turns little on the chest");
                Assert.Less(Vector3.Angle(EyeGaze(body, "LeftEye"), to), 12f, "dealer: eyes land on her");
            }
        }

        [UnityTest]
        public IEnumerator Preview_LineupPutsEachNamedCharacterAtItsSeat()
        {
            var table = New("Table").transform;
            var pip = CharacterFactory.CreateAtSeat(CharacterKind.Rookie, table, 1);
            var rocco = CharacterFactory.CreateAtSeat(CharacterKind.Loudmouth, table, 2);
            var vet = CharacterFactory.CreateAtSeat(CharacterKind.Veteran, table, 3);
            var vex = CharacterFactory.CreateAtSeat(CharacterKind.Cold, table, 4);
            var dot = CharacterFactory.CreateAtSeat(CharacterKind.Dealer, table, 4242);
            var preview = New("RealisticPreview").AddComponent<RealisticPreview>();
            preview.Cast = Cast.Select(Asset).ToArray();
            preview.DealerModel = Asset("Dealer");
            preview.Lineup = "Erik,-,-,Jace";
            yield return new WaitForSecondsRealtime(1.2f);
            Assert.IsTrue(pip.TryGetComponent<RealisticBody>(out var a));
            Assert.IsFalse(rocco.TryGetComponent<RealisticBody>(out _), "'-' keeps Rocco's cartoon");
            Assert.IsTrue(vet.TryGetComponent<RealisticBody>(out var b), "the new seat has no cartoon: '-' still gets the default (Reginald)");
            Assert.IsTrue(vex.TryGetComponent<RealisticBody>(out var c));
            Assert.IsTrue(dot.TryGetComponent<RealisticBody>(out var d));
            StringAssert.StartsWith("Erik", a.Model.name);
            StringAssert.StartsWith("Reginald", b.Model.name);
            StringAssert.StartsWith("Jace", c.Model.name);
            Assert.AreEqual(1.83f, a.StandingHeight, 0.02f, "Erik's own spec");
            Assert.AreEqual(1.78f, b.StandingHeight, 0.02f, "Reginald's own spec");
            Assert.AreEqual(1.65f, d.StandingHeight, 0.02f, "dealer");
            Assert.AreEqual(4, Object.FindObjectsByType<RealisticBody>(FindObjectsSortMode.None).Length, "attached once each");
        }
#endif
    }
}
