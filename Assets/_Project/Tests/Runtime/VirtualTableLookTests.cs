using NUnit.Framework;
using Poker.Runtime;
using UnityEngine;

namespace Poker.Tests.Runtime
{
    /// <summary>
    /// 備案虛擬桌的外觀（牌室桌：綠絨布、皮革軟墊扶手＋米色飾條、深色桌邊）。
    /// 2026-11-02 起是真實尺寸的家用撲克桌：長圓形、外框 84×42 吋（2.13×1.07 m）、扶手寬 0.12 m、荷官缺口在遠端長邊中央。
    /// </summary>
    public class VirtualTableLookTests
    {
        GameObject _go;
        TableFrame _frame;

        [TearDown]
        public void TearDown()
        {
            if (_go != null) Object.DestroyImmediate(_go);
        }

        void Build(TablePlacementSource source)
        {
            _go = new GameObject("Table");
            _frame = _go.AddComponent<TableFrame>();
            _go.AddComponent<TableView>();
            _frame.Place(new Pose(new Vector3(0, 0.74f, 0), Quaternion.identity), source);
        }

        static float W => TableDimensions.Width;
        static float D => TableDimensions.Depth;
        static float B => TableDimensions.VirtualBorder;

        /// <summary>點（XZ）離長圓形絨布邊的距離（外正內負）。</summary>
        static float EdgeDistance(Vector3 p, Vector2 felt)
        {
            float r = felt.y * 0.5f, cx = Mathf.Max(0f, felt.x * 0.5f - r);
            float dx = Mathf.Max(0f, Mathf.Abs(p.x) - cx), dz = p.z - r;
            return Mathf.Sqrt(dx * dx + dz * dz) - r;
        }

        [Test]
        public void Rail_FootprintIsTheHomeTable_84x42Inches_AndItSitsSlightlyAboveTheFelt()
        {
            var mesh = TableRail.Build(new Vector2(W, D));
            var b = mesh.bounds;
            Assert.AreEqual(-(W * 0.5f + B), b.min.x, 1e-3f);
            Assert.AreEqual(W * 0.5f + B, b.max.x, 1e-3f);
            Assert.AreEqual(-B, b.min.z, 1e-3f);
            Assert.AreEqual(D + B, b.max.z, 1e-3f);
            Assert.AreEqual(2.13f, b.size.x, 1e-3f, "84 in");
            Assert.AreEqual(1.07f, b.size.z, 1e-3f, "42 in");
            Assert.AreEqual(0.12f, B, 1e-4f, "armrest about 4.7 in wide");
            Assert.AreEqual(-TableDimensions.VirtualTopThickness, b.min.y, 1e-3f, "sides go down the top's thickness");
            Assert.That(b.max.y, Is.InRange(0.012f, 0.035f), "padded rail a little above the felt");
            Object.DestroyImmediate(mesh);
        }

        [Test]
        public void Rail_FollowsTheStadiumFelt_AndNeverCoversThePlayingSurface()
        {
            foreach (var felt in new[] { new Vector2(W, D), new Vector2(1.2f, 0.6f) })
            {
                var mesh = TableRail.Build(felt);
                var b = mesh.bounds;
                Assert.AreEqual(felt.x + 2 * B, b.size.x, 1e-3f, felt.ToString());
                Assert.AreEqual(felt.y + 2 * B, b.size.z, 1e-3f, felt.ToString());
                foreach (var v in mesh.vertices)
                {
                    float e = EdgeDistance(v, felt);
                    Assert.GreaterOrEqual(e, -5e-4f, $"rail vertex {v.ToString("F4")} is over the felt {felt}");
                    Assert.LessOrEqual(e, B + 5e-4f, $"rail vertex {v.ToString("F4")} outside the round outline {felt}");
                }
                Object.DestroyImmediate(mesh);
            }
        }

        [Test]
        public void Rail_HasADealerCutout_InTheMiddleOfTheFarSide_WithAWoodEdge()
        {
            var mesh = TableRail.Build(new Vector2(W, D));
            var v = mesh.vertices;
            float half = TableRail.CutoutHalfWidth;
            Assert.That(half * 2f, Is.InRange(0.4f, 0.6f), "about a dealer's width");
            var leather = mesh.GetTriangles(TableRail.Leather);
            foreach (var i in leather)
                Assert.IsFalse(Mathf.Abs(v[i].x) < half - 1e-3f && v[i].z > D - 1e-3f, $"armrest vertex {v[i].ToString("F3")} in the dealer cutout");
            bool woodEdge = false;
            foreach (var i in mesh.GetTriangles(TableRail.Sides))
                if (Mathf.Abs(v[i].x) < 0.05f && Mathf.Abs(v[i].z - D) < 0.04f && v[i].y < 0f) woodEdge = true;
            Assert.IsTrue(woodEdge, "the felt doesn't end in a paper-thin edge at the cutout");
            bool padNearPlayer = false;
            foreach (var i in leather) if (Mathf.Abs(v[i].x) < 0.05f && v[i].z < 0f) padNearPlayer = true;
            Assert.IsTrue(padNearPlayer, "armrest in front of the player");
            Object.DestroyImmediate(mesh);
        }

        [Test]
        public void Felt_IsAStadium_NotARectangle()
        {
            Build(TablePlacementSource.Virtual);
            var mesh = _go.transform.Find("Felt").GetComponent<MeshFilter>().sharedMesh;
            var felt = new Vector2(W, D);
            var t = _go.transform.Find("Felt");
            foreach (var v in mesh.vertices) Assert.LessOrEqual(EdgeDistance(t.localPosition + v, felt), 5e-4f, v.ToString("F3"));
            float area = 0f;
            var tri = mesh.triangles; var vs = mesh.vertices;
            for (int i = 0; i < tri.Length; i += 3) area += Vector3.Cross(vs[tri[i + 1]] - vs[tri[i]], vs[tri[i + 2]] - vs[tri[i]]).magnitude * 0.5f;
            float r = D * 0.5f;
            Assert.AreEqual(W * D - (4f - Mathf.PI) * r * r, area, 0.01f * W * D, "stadium area");
            foreach (var n in mesh.normals) Assert.Greater(n.y, 0.99f, "faces up");
        }

        [Test]
        public void Legs_StandUnderTheRoundTable_NotAtSquareCorners()
        {
            Build(TablePlacementSource.Virtual);
            var body = _go.transform.Find("VirtualTableBody");
            var felt = new Vector2(W, D);
            int legs = 0;
            for (int i = 0; i < body.childCount; i++)
            {
                var c = body.GetChild(i);
                if (!c.name.StartsWith("Leg")) continue;
                legs++;
                float half = c.localScale.x * 0.5f;
                Assert.Less(EdgeDistance(c.localPosition, felt), -half - 0.02f, c.name + " under the felt");
            }
            Assert.AreEqual(4, legs);
        }

        [Test]
        public void Rail_HasLeatherTrimAndSides_AsThreeSubmeshes_WithOutwardFacingNormals()
        {
            var mesh = TableRail.Build(new Vector2(W, D));
            Assert.AreEqual(TableRail.SubmeshCount, mesh.subMeshCount);
            for (int i = 0; i < mesh.subMeshCount; i++) Assert.Greater(mesh.GetSubMesh(i).indexCount, 0, "submesh " + i);
            // 正反面：三角形的繞向和頂點法線一致；扶手頂朝上、最外側朝外（不會被背面剔除掉）
            var v = mesh.vertices;
            var n = mesh.normals;
            var t = mesh.triangles;
            for (int i = 0; i < t.Length; i += 3)
            {
                var face = Vector3.Cross(v[t[i + 1]] - v[t[i]], v[t[i + 2]] - v[t[i]]);
                if (face.sqrMagnitude < 1e-14f) continue;
                Assert.Greater(Vector3.Dot(face, n[t[i]] + n[t[i + 1]] + n[t[i + 2]]), 0f, $"winding at {v[t[i]].ToString("F3")}");
            }
            float top = mesh.bounds.max.y, right = mesh.bounds.max.x;
            bool outward = false;
            // 扶手頂（皮革）朝上；荷官缺口兩端的封口（深色桌邊）在同一高度有頂點，但朝向缺口
            foreach (var i in mesh.GetTriangles(TableRail.Leather))
                if (v[i].y > top - 1e-4f) Assert.Greater(n[i].y, 0.7f, "rail top faces up");
            for (int i = 0; i < v.Length; i++)
            {
                // 長圓桌右端的最外點：軟墊外緣朝外，同一點的軟墊下緣朝下（以前的方桌這裡沒有頂點），都不能朝內
                if (v[i].x > right - 1e-4f && Mathf.Abs(v[i].z - D * 0.5f) < 0.1f)
                {
                    Assert.GreaterOrEqual(n[i].x, -1e-3f, "outer side never faces in");
                    outward |= n[i].x > 0.9f;
                }
            }
            Assert.IsTrue(outward, "outer side faces out");
            Object.DestroyImmediate(mesh);
        }

        [Test]
        public void VirtualTable_LooksLikeACardRoomTable_CheaplyAndWithoutLights()
        {
            Build(TablePlacementSource.Virtual);
            var body = _go.transform.Find("VirtualTableBody");
            Assert.IsTrue(body.gameObject.activeSelf);
            var rail = body.Find("Rail").GetComponent<MeshRenderer>();
            Assert.AreEqual(TableRail.SubmeshCount, rail.sharedMaterials.Length);
            foreach (var m in rail.sharedMaterials) StringAssert.Contains("Lit", m.shader.name);
            Assert.AreNotEqual("Universal Render Pipeline/Unlit", rail.sharedMaterials[0].shader.name, "leather is lit");
            int drawCalls = 0;
            foreach (var r in body.GetComponentsInChildren<Renderer>(true)) drawCalls += r.sharedMaterials.Length;
            Assert.LessOrEqual(drawCalls, 8, "few draw calls on Quest");
            Assert.IsEmpty(_go.GetComponentsInChildren<Light>(true), "no extra realtime lights");
            Assert.IsNull(rail.GetComponent<Collider>(), "rail: nothing for chips or hands to bump into");
        }

        [Test]
        public void Felt_StaysAtTheSameHeight_LitWithTextureOnTheVirtualTable_UnchangedOnARealDesk()
        {
            Build(TablePlacementSource.Virtual);
            var felt = _go.transform.Find("Felt");
            Assert.AreEqual(TableDimensions.FeltLift, felt.localPosition.y, 1e-6f);
            var mat = felt.GetComponent<MeshRenderer>().sharedMaterial;
            Assert.AreEqual("Universal Render Pipeline/Lit", mat.shader.name);
            Assert.IsNotNull(mat.GetTexture("_BaseMap"), "felt nap texture");
            Assert.Greater(mat.GetTextureScale("_BaseMap").x, 1f, "small tiling texture");

            _frame.Place(new Pose(new Vector3(0, 0.74f, 0), Quaternion.identity), TablePlacementSource.MrukTable);
            Assert.IsFalse(_go.transform.Find("VirtualTableBody").gameObject.activeSelf);
            Assert.AreEqual(TableDimensions.FeltLift, felt.localPosition.y, 1e-6f);
            Assert.AreEqual("Universal Render Pipeline/Unlit", felt.GetComponent<MeshRenderer>().sharedMaterial.shader.name, "real desk: same felt as before");
        }

        [Test]
        public void FeltNoise_IsASmallTilingTexture()
        {
            var tex = FeltTexture.Create(64, 1);
            Assert.AreEqual(64, tex.width);
            Assert.AreEqual(TextureWrapMode.Repeat, tex.wrapMode);
            float min = 1, max = 0;
            foreach (var c in tex.GetPixels()) { min = Mathf.Min(min, c.g); max = Mathf.Max(max, c.g); }
            Assert.Greater(max - min, 0.03f, "some nap variation");
            Assert.Less(max - min, 0.35f, "subtle, still reads as one green");
            // 左右、上下接得起來（可無縫平鋪）
            float seam = 0;
            for (int y = 0; y < 64; y++) seam = Mathf.Max(seam, Mathf.Abs(tex.GetPixel(0, y).g - tex.GetPixel(63, y).g));
            Assert.Less(seam, 0.2f);
            Object.DestroyImmediate(tex);
        }
    }
}
