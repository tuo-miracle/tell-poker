using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using Poker.Characters;
using Poker.Game;
using Poker.Runtime;
using UnityEngine;
using UnityEngine.TestTools;

namespace Poker.Tests.Game
{
    /// <summary>寫實角色在牌桌上：放著的手不蓋牌和籌碼；燈跟著牌桌轉（世界座標的燈會讓臉在某些方向出現硬邊暗塊）；沒有環境反射（嘴裡偏藍）。</summary>
    public class RealisticTableFitTests
    {
        const float Dt = 1f / 60f;
        readonly List<GameObject> _spawned = new List<GameObject>();

        [TearDown]
        public void TearDown()
        {
            foreach (var go in _spawned) if (go != null) Object.DestroyImmediate(go);
            _spawned.Clear();
        }

#if UNITY_EDITOR
        static GameObject Asset(string path) => UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(path);
        const string HankPath = "Assets/_Project/Art/Characters/Hank/Hank.glb", DealerPath = "Assets/_Project/Art/Characters/Dealer/Dealer.glb";

        /// <summary>點到線段（手腕→中指尖）的水平距離。</summary>
        static float DistXZ(Vector3 p, Vector3 a, Vector3 b)
        {
            var A = new Vector2(a.x, a.z); var B = new Vector2(b.x, b.z); var P = new Vector2(p.x, p.z);
            var ab = B - A;
            float t = ab.sqrMagnitude > 1e-8f ? Mathf.Clamp01(Vector2.Dot(P - A, ab) / ab.sqrMagnitude) : 0f;
            return Vector2.Distance(P, A + ab * t);
        }

        void AssertHandsClear(RealisticBody body, Transform table, IEnumerable<(string name, Vector3 at, float clear)> zones, string who)
        {
            foreach (var side in new[] { "Left", "Right" })
            {
                var wrist = table.InverseTransformPoint(body.Bone(side + "Hand").position);
                var tip = table.InverseTransformPoint(body.Bone(side + "HandMiddle3").position);
                Debug.Log($"[Hands] {who} {side}: wrist {wrist.ToString("F3")} tip {tip.ToString("F3")}");
                foreach (var (name, at, clear) in zones)
                {
                    float d = DistXZ(at, wrist, tip);
                    Assert.GreaterOrEqual(d, clear, $"{who} {side} hand is {d:F3} m from {name} (at {at.ToString("F3")})");
                }
            }
        }

        static List<(string, Vector3, float)> Zones(TableSpots s)
        {
            var z = new List<(string, Vector3, float)>();
            const float card = 0.05f, chips = 0.06f;
            for (int seat = 1; seat < s.SeatCount; seat++)
            {
                z.Add(($"seat {seat} card 0", s.CardOf(seat, 0), card));
                z.Add(($"seat {seat} card 1", s.CardOf(seat, 1), card));
                z.Add(($"seat {seat} stack", s.StackOf(seat), chips));
                z.Add(($"seat {seat} bet", s.BetOf(seat), card));
            }
            for (int i = 0; i < 5; i++) z.Add(($"board {i}", s.Board(i), card));
            z.Add(("pot", s.PotPile, chips));
            z.Add(("deck", s.Deck, card));
            return z;
        }

        static readonly CharacterKind[] FiveSeat = { CharacterKind.Rookie, CharacterKind.Rookie, CharacterKind.Loudmouth, CharacterKind.Veteran, CharacterKind.Cold };

        CharacterPerformer Seat(CharacterKind kind, Transform table, Pose pose)
        {
            var p = CharacterFactory.Create(kind, table, pose.position, pose.rotation, 7);
            p.AutoUpdate = false;
            return p;
        }

        [Test]
        public void RestingHands_StayOffTheCardsAndChips_AtTheMultiwayTable()
        {
            var table = new GameObject("Table").transform;
            _spawned.Add(table.gameObject);
            var spots = new TableSpots(TableDimensions.Layout, FiveSeat);
            var hank = RealisticBody.Attach(Seat(CharacterKind.Loudmouth, table, spots.SeatPose(2)), Asset(HankPath),
                RealisticPreview.SeatSpec(RealisticSpec.Hank, CharacterKind.Loudmouth));
            var dealer = RealisticBody.Attach(Seat(CharacterKind.Dealer, table, spots.DealerSeat), Asset(DealerPath), RealisticSpec.Dealer);
            var zones = Zones(spots);
            AssertHandsClear(hank, table, zones, "Hank");
            AssertHandsClear(dealer, table, zones, "Dealer");
        }

        [Test]
        public void RestingHands_StayOffTheCardsAndChips_AtTheSideSeats_FullRealisticTable()
        {
            // 預覽陣容（2026-11-02）：4 個對手位都放寫實角色（真實尺寸的長圓桌）；真人尺寸的手也不能蓋到牌和籌碼
            var table = new GameObject("Table").transform;
            _spawned.Add(table.gameObject);
            var spots = new TableSpots(TableDimensions.Layout, FiveSeat);
            var zones = Zones(spots);
            for (int seat = 1; seat < FiveSeat.Length; seat++)
                foreach (var name in new[] { "Marcus", "Jace", "Reginald", "Erik" })
                {
                    var kind = FiveSeat[seat];
                    var p = Seat(kind, table, spots.SeatPose(seat));
                    var body = RealisticBody.Attach(p, Asset($"Assets/_Project/Art/Characters/{name}/{name}.glb"),
                        RealisticPreview.SeatSpec(RealisticSpec.ByName(name).Value, kind));
                    AssertHandsClear(body, table, zones, $"{name} at seat {seat}");
                    Object.DestroyImmediate(p.gameObject);
                }
        }

        [Test]
        public void RestingHands_StayOffTheCardsAndChips_HeadsUp()
        {
            var table = new GameObject("Table").transform;
            _spawned.Add(table.gameObject);
            var spots = new TableSpots(TableDimensions.Layout);
            var dealer = RealisticBody.Attach(Seat(CharacterKind.Dealer, table, spots.DealerSeat), Asset(DealerPath), RealisticSpec.Dealer);
            AssertHandsClear(dealer, table, Zones(spots), "Dealer heads-up");
        }

        [UnityTest]
        public IEnumerator Lights_FollowTheTable()
        {
            var table = new GameObject("Table");
            _spawned.Add(table);
            table.AddComponent<TableFrame>();
            var rig = MrLighting.CreateRig();
            _spawned.Add(rig);
            rig.GetComponent<MrLighting>().Table = table.transform;   // 全套測試時場上可能留著別的牌桌
            yield return null;
            var key = rig.transform.Find("Key Light");
            var fill = rig.transform.Find("Fill Light");
            Assert.Less(Quaternion.Angle(key.rotation, MrLighting.KeyRotation), 0.01f, "table at identity: the designed angle");
            var turned = Quaternion.Euler(0, 135, 0);
            table.transform.rotation = turned;
            yield return null;
            Assert.Less(Quaternion.Angle(key.rotation, turned * MrLighting.KeyRotation), 0.01f, "key turns with the table");
            Assert.Less(Quaternion.Angle(fill.rotation, turned * MrLighting.FillRotation), 0.01f, "fill turns with the table");
        }

        [UnityTest]
        public IEnumerator MainSceneLayout_KeyWithComponent_AlsoTurnsTheSeparateFillLight()
        {
            // 主場景：主光掛 MrLighting、補光是另一個物件
            var table = new GameObject("Table");
            _spawned.Add(table);
            var key = new GameObject("Directional Light").AddComponent<Light>();
            _spawned.Add(key.gameObject);
            MrLighting.ConfigureKey(key);
            var fill = new GameObject("Fill Light").AddComponent<Light>();
            _spawned.Add(fill.gameObject);
            MrLighting.ConfigureFill(fill);
            var c = key.gameObject.AddComponent<MrLighting>();
            c.Table = table.transform;
            yield return null;
            var turned = Quaternion.Euler(0, 90, 0);
            table.transform.rotation = turned;
            yield return null;
            Assert.Less(Quaternion.Angle(key.transform.rotation, turned * MrLighting.KeyRotation), 0.01f);
            Assert.Less(Quaternion.Angle(fill.transform.rotation, turned * MrLighting.FillRotation), 0.01f);
        }

        [Test]
        public void NoEnvironmentReflection_ItTintsDarkSurfacesBlue()
        {
            RenderSettings.reflectionIntensity = 1f;
            MrLighting.ApplyAmbient();
            Assert.AreEqual(0f, RenderSettings.reflectionIntensity, 1e-5f);
        }
#endif
    }
}
