using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Poker.Characters
{
    /// <summary>
    /// 用基本幾何體與程式網格組出 Q 版半身角色。靜態零件依材質合併成一個多 submesh renderer，控制 renderer 數量。
    /// 尺寸是真實牌桌比例（公尺）：頭高約 0.29 m，坐在桌緣後方約 0.18 m。
    /// </summary>
    public static class CharacterBuilder
    {
        public static readonly Color Ink = SpecHex("231A26");
        static Color SpecHex(string h) => CharacterSpec.Hex(h);

        public static CharacterRig Build(GameObject root, CharacterSpec s)
        {
            float W = s.BodyWidth, H = s.BodyHeight;
            var rig = new CharacterRig { Root = root.transform, HeadRadii = s.HeadRadii };

            rig.Hips = Node("Hips", root.transform, new Vector3(0, -0.12f * H, -0.18f));
            rig.HipsRest = rig.Hips.localPosition;
            rig.Chest = Node("Chest", rig.Hips, new Vector3(0, 0.13f * H, 0));
            BuildBody(rig.Chest, s);
            rig.Neck = Node("Neck", rig.Hips, new Vector3(0, 0.30f * H, 0));
            rig.NeckRest = rig.Neck.localPosition;
            rig.Head = Node("Head", rig.Neck, new Vector3(0, s.HeadRadii.y - 0.01f, 0.012f));
            rig.HeadRest = rig.Head.localPosition;
            rig.HeadRenderer = MeshNode("Skull", rig.Head, MeshLibrary.Sphere, Vector3.zero, Quaternion.identity, s.HeadRadii * 2f, MaterialCache.Lit(s.Skin, 0.25f)).GetComponent<Renderer>();
            BuildHeadDeco(rig.Head, s);
            BuildFace(rig, s);

            float sx = 0.128f * W, sy = 0.255f * H;
            rig.ShoulderL = new Vector3(-sx, sy, 0.0f);
            rig.ShoulderR = new Vector3(sx, sy, 0.0f);
            rig.UpperArmLength = 0.14f;
            rig.ForeArmLength = 0.14f;
            rig.ArmThickness = 0.05f * Mathf.Sqrt(W);
            var sleeve = MaterialCache.Lit(s.Kind == CharacterKind.Dealer ? s.Shirt : s.Shirt, 0.2f);
            rig.UpperArmL = MeshNode("UpperArmL", root.transform, MeshLibrary.Capsule, Vector3.zero, Quaternion.identity, Vector3.one * 0.05f, sleeve).transform;
            rig.UpperArmR = MeshNode("UpperArmR", root.transform, MeshLibrary.Capsule, Vector3.zero, Quaternion.identity, Vector3.one * 0.05f, sleeve).transform;
            var fore = s.Kind == CharacterKind.Loudmouth ? MaterialCache.Lit(s.Skin, 0.25f) : sleeve; // 老手捲袖子
            rig.ForeArmL = MeshNode("ForeArmL", root.transform, MeshLibrary.Capsule, Vector3.zero, Quaternion.identity, Vector3.one * 0.05f, fore).transform;
            rig.ForeArmR = MeshNode("ForeArmR", root.transform, MeshLibrary.Capsule, Vector3.zero, Quaternion.identity, Vector3.one * 0.05f, fore).transform;
            rig.HandL = Hand("HandL", root.transform, s, +1);
            rig.HandR = Hand("HandR", root.transform, s, -1);

            BuildProps(rig, s);
            return rig;
        }

        // ───────────── 身體 ─────────────

        static void BuildBody(Transform chest, CharacterSpec s)
        {
            float W = s.BodyWidth, H = s.BodyHeight;
            float tw = 0.15f * W, th = 0.17f * H, td = 0.115f;
            float FrontZ(float x, float y) => td * Mathf.Sqrt(Mathf.Max(0.02f, 1 - (x / tw) * (x / tw) - (y / th) * (y / th)));
            Quaternion FrontRot(float x, float y)
            {
                var n = new Vector3(x / (tw * tw), y / (th * th), FrontZ(x, y) / (td * td)).normalized;
                return Quaternion.LookRotation(n, Vector3.up);
            }

            var c = new MeshLibrary.Composer();
            var torsoColor = s.Kind == CharacterKind.Dealer ? s.Accent : s.Shirt;
            c.Add(MeshLibrary.Sphere, Vector3.zero, Quaternion.identity, new Vector3(2 * tw, 2 * th, 2 * td), MaterialCache.Lit(torsoColor, 0.2f));

            switch (s.Kind)
            {
                case CharacterKind.Rookie:
                {
                    var dark = MaterialCache.Lit(s.Shirt * 0.82f, 0.2f);
                    // 帽兜、口袋、帽繩
                    c.Add(MeshLibrary.Sphere, new Vector3(0, th * 0.78f, -td * 0.62f), Quaternion.Euler(-20, 0, 0), new Vector3(0.24f * W, 0.11f, 0.13f), dark);
                    c.Add(MeshLibrary.Sphere, new Vector3(0, -th * 0.42f, FrontZ(0, -th * 0.42f) - 0.008f), FrontRot(0, -th * 0.42f), new Vector3(0.17f * W, 0.075f, 0.03f), dark);
                    var white = MaterialCache.Lit(SpecHex("F4F1EA"), 0.2f);
                    for (int i = -1; i <= 1; i += 2)
                    {
                        float x = i * 0.028f, y = th * 0.5f;
                        c.Add(MeshLibrary.Capsule, new Vector3(x, y, FrontZ(x, y) + 0.004f), Quaternion.Euler(-12, 0, 0), new Vector3(0.007f, 0.025f, 0.007f), white);
                    }
                    break;
                }
                case CharacterKind.Loudmouth:
                {
                    var accent = MaterialCache.Lit(s.Accent, 0.2f);
                    // 夏威夷風圓點
                    var dots = new[] { new Vector2(-0.07f, 0.02f), new Vector2(0.075f, 0.05f), new Vector2(-0.03f, -0.07f), new Vector2(0.05f, -0.04f), new Vector2(-0.1f, 0.08f), new Vector2(0.11f, -0.03f) };
                    foreach (var d in dots)
                    {
                        float x = d.x * W, y = d.y;
                        c.Add(MeshLibrary.Sphere, new Vector3(x, y, FrontZ(x, y) - 0.004f), FrontRot(x, y), new Vector3(0.034f, 0.03f, 0.014f), accent);
                    }
                    // 敞開的領口
                    float ny = th * 0.78f;
                    var collar = MaterialCache.Lit(s.Shirt * 1.15f, 0.2f);
                    for (int i = -1; i <= 1; i += 2)
                    {
                        float x = i * 0.05f;
                        c.Add(MeshLibrary.Cube, new Vector3(x, ny, FrontZ(x, ny) - 0.004f), FrontRot(x, ny) * Quaternion.Euler(0, 0, i * 35f), new Vector3(0.05f, 0.03f, 0.008f), collar);
                    }
                    // 金項鍊
                    var chain = new List<Vector3>();
                    for (int i = 0; i <= 12; i++)
                    {
                        float t = i / 12f, x = Mathf.Lerp(-0.055f, 0.055f, t) * 1.0f, y = th * 0.86f - 0.06f * Mathf.Sin(t * Mathf.PI);
                        chain.Add(new Vector3(x, y, FrontZ(x, y) + 0.003f));
                    }
                    c.Add(MeshLibrary.Tube(chain, 0.0045f), Matrix4x4.identity, MaterialCache.Lit(SpecHex("F2C230"), 0.85f));
                    break;
                }
                case CharacterKind.Cold:
                {
                    var dark = MaterialCache.Lit(s.Shirt * 0.7f, 0.35f);
                    // 立領、翻領、銀色胸針與釦子
                    c.Add(MeshLibrary.Cylinder, new Vector3(0, th * 0.93f, -0.01f), Quaternion.identity, new Vector3(0.17f, 0.03f, 0.15f), dark);
                    for (int i = -1; i <= 1; i += 2)
                    {
                        float x = i * 0.045f * W, y = th * 0.35f;
                        c.Add(MeshLibrary.Sphere, new Vector3(x, y, FrontZ(x, y) - 0.012f), FrontRot(x, y) * Quaternion.Euler(0, 0, -i * 20f), new Vector3(0.04f, 0.15f, 0.03f), dark);
                    }
                    var silver = MaterialCache.Lit(s.Accent, 0.85f);
                    c.Add(MeshLibrary.Sphere, new Vector3(0.07f * W, th * 0.42f, FrontZ(0.07f * W, th * 0.42f)), Quaternion.identity, Vector3.one * 0.016f, silver);
                    for (int i = 0; i < 2; i++)
                    {
                        float y = -0.01f - i * 0.05f;
                        c.Add(MeshLibrary.Sphere, new Vector3(0, y, FrontZ(0, y)), Quaternion.identity, Vector3.one * 0.011f, silver);
                    }
                    break;
                }
                case CharacterKind.Dealer:
                {
                    // 背心中間露出一條白襯衫門襟，領口一個橫向黑領結（避免拼成一張「臉」）
                    var white = MaterialCache.Lit(s.Shirt, 0.2f);
                    float py = th * 0.45f;
                    c.Add(MeshLibrary.Capsule, new Vector3(0, py, FrontZ(0, py) - 0.006f), FrontRot(0, py), new Vector3(0.045f, 0.075f, 0.02f), white);
                    var ink = MaterialCache.Lit(Ink, 0.4f);
                    float ty = th * 0.86f, tz = FrontZ(0, ty) + 0.002f;
                    c.Add(MeshLibrary.Capsule, new Vector3(0, ty, tz), FrontRot(0, ty) * Quaternion.Euler(0, 0, 90), new Vector3(0.026f, 0.034f, 0.012f), ink);
                    c.Add(MeshLibrary.Sphere, new Vector3(0, ty, tz + 0.005f), Quaternion.identity, new Vector3(0.016f, 0.016f, 0.012f), MaterialCache.Lit(s.Accent * 0.6f, 0.4f));
                    var gold = MaterialCache.Lit(SpecHex("E8B83A"), 0.85f);
                    for (int i = 0; i < 2; i++)
                    {
                        float y = -0.02f - i * 0.045f;
                        c.Add(MeshLibrary.Sphere, new Vector3(0, y, FrontZ(0, y)), Quaternion.identity, Vector3.one * 0.012f, gold);
                    }
                    break;
                }
            }
            CompositeNode("Body", chest, c);
        }

        // ───────────── 頭部裝飾（頭髮、帽子、耳朵、鼻子） ─────────────

        static void BuildHeadDeco(Transform head, CharacterSpec s)
        {
            Vector3 R = s.HeadRadii;
            float a = R.x, b = R.y, cz = R.z;
            var skin = MaterialCache.Lit(s.Skin, 0.25f);
            var hair = MaterialCache.Lit(s.Hair, 0.45f);
            var c = new MeshLibrary.Composer();

            // 鼻子
            var np = Surface(R, 0, -0.03f, out var nn);
            c.Add(MeshLibrary.Sphere, np + nn * 0.004f, Quaternion.LookRotation(nn), new Vector3(0.03f, 0.022f, 0.022f), MaterialCache.Lit(Color.Lerp(s.Skin, s.Blush, 0.25f), 0.3f));

            bool ears = s.Kind != CharacterKind.Cold;
            if (ears)
                for (int i = -1; i <= 1; i += 2)
                    c.Add(MeshLibrary.Sphere, new Vector3(i * a * 0.97f, -0.012f, -0.01f), Quaternion.Euler(0, i * 15f, 0), new Vector3(0.026f, 0.052f, 0.042f), skin);

            switch (s.Kind)
            {
                case CharacterKind.Rookie:
                {
                    var hat = MaterialCache.Lit(s.Accent, 0.15f);
                    var cuff = MaterialCache.Lit(s.Accent * 0.86f, 0.15f);
                    // 露出瀏海的小捲毛
                    for (int i = -1; i <= 1; i += 2)
                        c.Add(MeshLibrary.Sphere, new Vector3(i * a * 0.86f, b * 0.2f, 0.035f), Quaternion.Euler(0, 0, i * 35f), new Vector3(0.035f, 0.06f, 0.04f), hair);
                    c.Add(MeshLibrary.Sphere, new Vector3(0, b * 0.42f, -0.006f), Quaternion.Euler(-8, 0, 0), new Vector3(2 * a * 1.05f, 2 * b * 0.78f, 2 * cz * 1.05f), hat);
                    c.Add(MeshLibrary.Cylinder, new Vector3(0, b * 0.43f, 0.0f), Quaternion.Euler(-8, 0, 0), new Vector3(2 * a * 1.1f, 0.024f, 2 * cz * 1.1f), cuff);
                    c.Add(MeshLibrary.Sphere, new Vector3(0.0f, b * 1.25f, -0.03f), Quaternion.identity, Vector3.one * 0.075f, MaterialCache.Lit(SpecHex("FF6F59"), 0.1f));
                    // 雀斑
                    var freckle = MaterialCache.Unlit(SpecHex("D98A5F"));
                    foreach (var f in new[] { new Vector2(0.085f, -0.03f), new Vector2(0.1f, -0.045f), new Vector2(0.075f, -0.05f) })
                        for (int i = -1; i <= 1; i += 2)
                        {
                            var p = Surface(R, i * f.x, f.y, out var n);
                            c.Add(MeshLibrary.Sphere, p, Quaternion.LookRotation(n), new Vector3(0.008f, 0.008f, 0.004f), freckle);
                        }
                    break;
                }
                case CharacterKind.Loudmouth:
                {
                    c.Add(MeshLibrary.Sphere, new Vector3(0, b * 0.36f, -0.014f), Quaternion.identity, new Vector3(2 * a * 1.03f, 2 * b * 0.74f, 2 * cz * 1.03f), hair);
                    // 飛機頭
                    c.Add(MeshLibrary.Sphere, new Vector3(0, b * 0.9f, cz * 0.25f), Quaternion.Euler(-12, 0, 0), new Vector3(0.19f, 0.085f, 0.24f), hair);
                    c.Add(MeshLibrary.Sphere, new Vector3(0.012f, b * 0.98f, cz * 0.78f), Quaternion.Euler(-30, 0, 10), new Vector3(0.13f, 0.07f, 0.1f), hair);
                    // 鬢角
                    for (int i = -1; i <= 1; i += 2)
                        c.Add(MeshLibrary.Sphere, new Vector3(i * a * 0.9f, -0.005f, -0.004f), Quaternion.identity, new Vector3(0.035f, 0.08f, 0.06f), hair);
                    // 架在頭上的墨鏡
                    var lens = MaterialCache.Lit(SpecHex("FF4F7B"), 0.9f);
                    var gold = MaterialCache.Lit(SpecHex("F2C230"), 0.85f);
                    var gp = Surface(R, 0, b * 0.6f, out var gnrm);
                    var gq = Quaternion.LookRotation(gnrm) * Quaternion.Euler(-15, 0, 0);
                    var gc = gp + gnrm * 0.03f;
                    for (int i = -1; i <= 1; i += 2)
                        c.Add(MeshLibrary.Sphere, gc + gq * new Vector3(i * 0.034f, 0, 0), gq, new Vector3(0.052f, 0.038f, 0.014f), lens);
                    c.Add(MeshLibrary.Cube, gc + gq * new Vector3(0, 0.008f, 0), gq, new Vector3(0.025f, 0.005f, 0.006f), gold);
                    // 翹鬍子
                    for (int i = -1; i <= 1; i += 2)
                    {
                        var p = Surface(R, i * 0.027f, -0.052f, out var n);
                        c.Add(MeshLibrary.Sphere, p + n * 0.004f, Quaternion.LookRotation(n) * Quaternion.Euler(0, 0, -i * 14f), new Vector3(0.058f, 0.022f, 0.02f), hair);
                    }
                    break;
                }
                case CharacterKind.Cold:
                {
                    // 齊瀏海鮑伯頭（後方大球只露出臉）
                    c.Add(MeshLibrary.Sphere, new Vector3(0, b * 0.28f, -0.01f), Quaternion.identity, new Vector3(2 * a * 1.07f, 2 * b * 0.82f, 2 * cz * 1.06f), hair);
                    c.Add(MeshLibrary.Sphere, new Vector3(0, -0.01f, -0.05f), Quaternion.identity, new Vector3(2 * a * 1.1f, 2 * b * 0.95f, 2 * cz * 0.85f), hair);
                    for (int i = -1; i <= 1; i += 2)
                        c.Add(MeshLibrary.Sphere, new Vector3(i * a * 0.9f, -0.01f, -0.02f), Quaternion.Euler(0, 0, i * 3f), new Vector3(0.075f, 0.24f, 0.22f), hair);
                    // 貼著額頭的齊瀏海
                    for (int i = -3; i <= 3; i++)
                    {
                        var fp = Surface(R, i * 0.022f, b * 0.6f - Mathf.Abs(i) * 0.005f, out var fn);
                        c.Add(MeshLibrary.Sphere, fp + fn * 0.001f, Quaternion.LookRotation(fn), new Vector3(0.05f, 0.058f, 0.028f), hair);
                    }
                    // 一撮白色挑染＋銀髮夾
                    var sp = Surface(R, 0.06f, b * 0.6f, out var sn);
                    c.Add(MeshLibrary.Sphere, sp + sn * 0.008f, Quaternion.LookRotation(sn) * Quaternion.Euler(0, 0, 10f), new Vector3(0.024f, 0.07f, 0.02f), MaterialCache.Lit(SpecHex("F2F2F7"), 0.45f));
                    var cp2 = Surface(R, -0.075f, b * 0.5f, out var cn2);
                    c.Add(MeshLibrary.Capsule, cp2 + cn2 * 0.012f, Quaternion.LookRotation(cn2) * Quaternion.Euler(0, 0, 70f), new Vector3(0.008f, 0.02f, 0.008f), MaterialCache.Lit(s.Accent, 0.9f));
                    // 淚痣
                    var mp = Surface(R, -0.075f, -0.045f, out var mn);
                    c.Add(MeshLibrary.Sphere, mp, Quaternion.LookRotation(mn), new Vector3(0.008f, 0.008f, 0.004f), MaterialCache.Unlit(Ink));
                    break;
                }
                case CharacterKind.Dealer:
                {
                    c.Add(MeshLibrary.Sphere, new Vector3(0, b * 0.3f, -0.016f), Quaternion.identity, new Vector3(2 * a * 1.04f, 2 * b * 0.8f, 2 * cz * 1.04f), hair);
                    c.Add(MeshLibrary.Sphere, new Vector3(0, b * 0.78f, -cz * 0.82f), Quaternion.identity, Vector3.one * 0.095f, hair);
                    // 側分瀏海
                    var fp = Surface(R, -0.05f, b * 0.52f, out var fn);
                    c.Add(MeshLibrary.Sphere, fp + fn * 0.002f, Quaternion.LookRotation(fn) * Quaternion.Euler(0, 0, -20f), new Vector3(0.12f, 0.045f, 0.04f), hair);
                    // 荷官綠色遮陽帽簷
                    var visor = MaterialCache.Lit(SpecHex("3DBB7A"), 0.6f);
                    c.Add(MeshLibrary.Cylinder, new Vector3(0, b * 0.66f, -0.01f), Quaternion.Euler(-14, 0, 0), new Vector3(2 * a * 1.02f, 0.014f, 2 * cz * 1.02f), visor);
                    c.Add(MeshLibrary.Cylinder, new Vector3(0, b * 0.7f, cz * 0.92f), Quaternion.Euler(22, 0, 0), new Vector3(0.21f, 0.007f, 0.15f), visor);
                    break;
                }
            }
            CompositeNode("HeadDeco", head, c);
        }

        // ───────────── 臉 ─────────────

        static void BuildFace(CharacterRig rig, CharacterSpec s)
        {
            Vector3 R = s.HeadRadii;
            float E = s.EyeSize;
            float w = 0.05f * E, h = 0.062f * E, d = 0.022f;
            var white = MaterialCache.Unlit(SpecHex("FBFAF7"));
            var lidMat = MaterialCache.Lit(s.Kind == CharacterKind.Cold ? SpecHex("A58CC8") : Color.Lerp(s.Skin, s.Brow, 0.12f) * 0.95f, 0.15f);
            var lashMat = MaterialCache.Unlit(Ink);
            var iris = MaterialCache.Unlit(IrisColor(s.Kind));
            var pupil = MaterialCache.Unlit(SpecHex("150F18"));
            var shine = MaterialCache.Unlit(Color.white);

            for (int side = -1; side <= 1; side += 2)
            {
                var p = Surface(R, side * s.EyeSpacing, s.EyeHeight, out var n);
                var dir = Vector3.Slerp(Vector3.forward, n, 0.3f);
                var eyeRoot = Node(side < 0 ? "EyeRootL" : "EyeRootR", rig.Head, p - n * (d * 0.15f));
                eyeRoot.localRotation = Quaternion.LookRotation(dir, Vector3.up);
                MeshNode("White", eyeRoot, MeshLibrary.Sphere, Vector3.zero, Quaternion.identity, new Vector3(w, h, d), white);

                const float rp = 0.04f;
                var pivot = Node(side < 0 ? "EyeL" : "EyeR", eyeRoot, new Vector3(0, 0, d * 0.5f - rp));
                var pc = new MeshLibrary.Composer()
                    .Add(MeshLibrary.Sphere, Vector3.zero, Quaternion.identity, new Vector3(0.031f * E, 0.036f * E, 0.008f), iris)
                    .Add(MeshLibrary.Sphere, new Vector3(0, 0, 0.0025f), Quaternion.identity, new Vector3(0.019f * E, 0.023f * E, 0.006f), pupil)
                    .Add(MeshLibrary.Sphere, new Vector3(0.007f * E, 0.009f * E, 0.0055f), Quaternion.identity, new Vector3(0.011f * E, 0.011f * E, 0.003f), shine)
                    .Add(MeshLibrary.Sphere, new Vector3(-0.007f * E, -0.008f * E, 0.0055f), Quaternion.identity, new Vector3(0.005f * E, 0.005f * E, 0.002f), shine);
                var pupilT = CompositeNode("Pupil", pivot, pc);
                pupilT.localPosition = new Vector3(0, 0, rp + 0.001f);

                var lidPivot = Node("LidPivot", eyeRoot, new Vector3(0, h * 0.5f + 0.002f, 0));
                var lid = MeshNode("Lid", lidPivot, MeshLibrary.Sphere, new Vector3(0, -h * 0.58f, d * 0.25f), Quaternion.identity, new Vector3(w * 1.2f, h * 1.16f, d * 1.6f), lidMat);
                lidPivot.localScale = new Vector3(1, 0.001f, 1);
                // 睫毛線：貼著眼皮下緣，讓半閉眼（得意、冷淡）一眼看得出來
                var lash = MeshNode("Lash", eyeRoot, LashMesh(), new Vector3(0, h * 0.5f, d * 0.85f), Quaternion.identity, new Vector3(w * 1.14f, 0.001f, 0.08f), lashMat);
                lash.SetActive(false);

                // 眉毛
                var bp = Surface(R, side * (s.EyeSpacing + 0.004f), s.EyeHeight + h * 0.5f + 0.02f, out var bn);
                var brow = MeshNode(side < 0 ? "BrowL" : "BrowR", rig.Head, BrowMesh(s.BrowThickness), bp + bn * 0.004f, Quaternion.LookRotation(Vector3.Slerp(Vector3.forward, bn, 0.5f)), Vector3.one, MaterialCache.Unlit(s.Brow));

                // 臉頰紅暈
                var cp = Surface(R, side * (s.EyeSpacing + 0.038f), s.EyeHeight - 0.048f, out var cn);
                var cheek = MeshNode(side < 0 ? "CheekL" : "CheekR", rig.Head, MeshLibrary.Sphere, cp + cn * 0.001f, Quaternion.LookRotation(cn), new Vector3(0.042f, 0.025f, 0.008f), MaterialCache.Unlit(s.Blush));

                if (side < 0)
                {
                    rig.EyeRootL = eyeRoot; rig.EyeL = pivot; rig.PupilL = pupilT; rig.LidL = lidPivot; rig.LidRendererL = lid.GetComponent<Renderer>(); rig.LashL = lash.transform;
                    rig.BrowL = brow.transform; rig.CheekL = cheek.transform;
                }
                else
                {
                    rig.EyeRootR = eyeRoot; rig.EyeR = pivot; rig.PupilR = pupilT; rig.LidR = lidPivot; rig.LidRendererR = lid.GetComponent<Renderer>(); rig.LashR = lash.transform;
                    rig.BrowR = brow.transform; rig.CheekR = cheek.transform;
                }
            }
            rig.BrowRestL = rig.BrowL.localPosition; rig.BrowRestR = rig.BrowR.localPosition;
            rig.BrowRotL = rig.BrowL.localRotation; rig.BrowRotR = rig.BrowR.localRotation;
            rig.CheekScale = rig.CheekL.localScale;

            // 嘴
            var mp = Surface(R, 0, s.MouthHeight, out var mn);
            rig.Mouth = Node("Mouth", rig.Head, mp + mn * 0.002f);
            rig.Mouth.localRotation = Quaternion.LookRotation(Vector3.Slerp(Vector3.forward, mn, 0.5f));
            rig.Mouth.localScale = new Vector3(s.MouthWidth, 1, 1);
            var line = MeshNode("MouthLine", rig.Mouth, MouthMesh(s.RestMouth), Vector3.zero, Quaternion.identity, Vector3.one, MaterialCache.Unlit(s.Lips));
            rig.MouthLine = line.GetComponent<MeshFilter>();
            rig.MouthLineRenderer = line.GetComponent<Renderer>();
            rig.MouthOpen = Node("MouthOpen", rig.Mouth, new Vector3(0, 0, 0.001f));
            var mo = rig.MouthOpen.gameObject;
            rig.MouthOpenFilter = mo.AddComponent<MeshFilter>();
            var mor = mo.AddComponent<MeshRenderer>();
            Setup(mor);
            rig.MouthOpenFilter.sharedMesh = OpenMouthMesh(true, out var mats);
            mor.sharedMaterials = mats;
            rig.MouthOpenRenderer = mor;
            mor.enabled = false;

            // 汗滴
            var sweat = MaterialCache.Lit(SpecHex("8FD8FF"), 0.92f);
            rig.SweatDrops = new Transform[3];
            for (int i = 0; i < 3; i++)
            {
                rig.SweatDrops[i] = MeshNode("Sweat" + i, rig.Head, MeshLibrary.Teardrop(), Vector3.zero, Quaternion.identity, Vector3.one * 0.013f, sweat).transform;
                rig.SweatDrops[i].gameObject.SetActive(false);
            }

            // 頭頂符號
            rig.SymbolAnchor = Node("SymbolAnchor", rig.Head, new Vector3(R.x * 0.95f, R.y * 1.45f, 0.02f));
            var sym = MeshNode("Symbol", rig.SymbolAnchor, SymbolMesh(HeadSymbol.Exclaim), Vector3.zero, Quaternion.identity, Vector3.one, SymbolMaterial(HeadSymbol.Exclaim));
            rig.Symbol = sym.transform;
            rig.SymbolFilter = sym.GetComponent<MeshFilter>();
            rig.SymbolRenderer = sym.GetComponent<Renderer>();
            sym.SetActive(false);
        }

        static Color IrisColor(CharacterKind k)
        {
            switch (k)
            {
                case CharacterKind.Rookie: return SpecHex("3E8F5A");
                case CharacterKind.Loudmouth: return SpecHex("5A3A22");
                case CharacterKind.Cold: return SpecHex("7A4FC2");
                default: return SpecHex("6B4A2A");
            }
        }

        // ───────────── 手與道具 ─────────────

        static Transform Hand(string name, Transform parent, CharacterSpec s, int thumbSide)
        {
            var skin = MaterialCache.Lit(s.Skin, 0.25f);
            var c = new MeshLibrary.Composer()
                .Add(MeshLibrary.Sphere, new Vector3(0, 0, 0.012f), Quaternion.identity, new Vector3(0.058f, 0.034f, 0.068f), skin)
                .Add(MeshLibrary.Sphere, new Vector3(thumbSide * 0.027f, 0.004f, 0.012f), Quaternion.Euler(0, thumbSide * 30f, 0), new Vector3(0.022f, 0.02f, 0.04f), skin);
            // 袖口
            var cuffColor = s.Kind == CharacterKind.Loudmouth ? s.Skin : s.Shirt * 0.9f;
            c.Add(MeshLibrary.Cylinder, new Vector3(0, 0, -0.03f), Quaternion.Euler(90, 0, 0), new Vector3(0.05f, 0.008f, 0.05f), MaterialCache.Lit(cuffColor, 0.2f));
            return CompositeNode(name, parent, c);
        }

        static void BuildProps(CharacterRig rig, CharacterSpec s)
        {
            rig.Props = Node("Props", rig.Root, Vector3.zero);
            var white = MaterialCache.Lit(SpecHex("F7F5EF"), 0.4f);
            var back = MaterialCache.Lit(s.CardBack, 0.4f);
            for (int i = -1; i <= 1; i += 2)
            {
                var pivot = Node(i < 0 ? "CardPivotL" : "CardPivotR", rig.Props, new Vector3(i * 0.038f - 0.03f, 0.0012f, 0.2f));
                pivot.localRotation = Quaternion.Euler(0, i * 7f, 0);
                var c = new MeshLibrary.Composer()
                    .Add(MeshLibrary.Cube, new Vector3(0, 0, -0.044f), Quaternion.identity, new Vector3(0.063f, 0.0016f, 0.088f), white)
                    .Add(MeshLibrary.Cube, new Vector3(0, 0.0003f, -0.044f), Quaternion.identity, new Vector3(0.053f, 0.0016f, 0.078f), back);
                CompositeNode("Card", pivot, c);
                if (i < 0) rig.CardPivotL = pivot; else rig.CardPivotR = pivot;
            }

            var chipColors = new[] { SpecHex("D63A3A"), SpecHex("F4F1EA"), SpecHex("2F6FD6"), SpecHex("D63A3A"), SpecHex("1F1F1F"), SpecHex("2F6FD6") };
            var stack = new MeshLibrary.Composer();
            const float chipH = 0.0034f;
            for (int i = 0; i < chipColors.Length; i++)
                stack.Add(MeshLibrary.Cylinder, new Vector3(0, chipH * (i + 0.5f), 0), Quaternion.Euler(0, i * 17f, 0), new Vector3(0.039f, chipH * 0.5f, 0.039f), MaterialCache.Lit(chipColors[i], 0.5f));
            rig.ChipStack = CompositeNode("ChipStack", rig.Props, stack);
            rig.ChipStack.localPosition = new Vector3(0.13f * s.BodyWidth, 0, 0.12f);
            rig.ChipTop = rig.ChipStack.localPosition + new Vector3(0, chipH * chipColors.Length, 0);
            rig.FidgetChip = MeshNode("FidgetChip", rig.Props, MeshLibrary.Cylinder, rig.ChipTop + new Vector3(0, chipH * 0.5f, 0), Quaternion.identity, new Vector3(0.039f, chipH * 0.5f, 0.039f), MaterialCache.Lit(SpecHex("F2C230"), 0.5f)).transform;
        }

        // ───────────── 網格 ─────────────

        /// <summary>眼皮下緣的弧線（單位寬 1、往下 0.5），由眼皮蓋住比例縮放高度。</summary>
        static Mesh LashMesh() => MeshLibrary.Cached("Lash", () =>
        {
            var pts = new List<Vector3>();
            for (int i = 0; i <= 12; i++)
            {
                float x = Mathf.Lerp(-0.47f, 0.47f, i / 12f);
                pts.Add(new Vector3(x, -0.5f * Mathf.Sqrt(Mathf.Max(0, 1 - 4 * x * x)), 0));
            }
            return MeshLibrary.Tube(pts, 0.045f);
        });

        static Mesh BrowMesh(float thick) => MeshLibrary.Cached("Brow" + thick.ToString("F2"), () =>
        {
            var pts = new List<Vector3>();
            var radii = new List<float>();
            const float L = 0.05f;
            for (int i = 0; i <= 8; i++)
            {
                float t = i / 8f, x = (t - 0.5f) * L;
                pts.Add(new Vector3(x, 0.006f * (1 - 4 * (t - 0.5f) * (t - 0.5f)), 0));
                radii.Add(0.0052f * thick * (1.0f - 0.35f * Mathf.Abs(t - 0.5f) * 2));
            }
            return MeshLibrary.Tube(pts, 0, radii.ToArray());
        });

        // 每幀都會查嘴型，用陣列快取避免字串與閉包配置
        static readonly Mesh[] MouthMeshes = new Mesh[Enum.GetValues(typeof(MouthShape)).Length];
        static readonly Mesh[] OpenMeshes = new Mesh[2];
        static readonly Material[][] OpenMats = new Material[2][];

        public static Mesh MouthMesh(MouthShape shape)
        {
            int i = (int)shape;
            return MouthMeshes[i] != null ? MouthMeshes[i] : (MouthMeshes[i] = BuildMouthMesh(shape));
        }

        static Mesh BuildMouthMesh(MouthShape shape)
        {
            const float hw = 0.026f;
            float width = shape == MouthShape.SmallSmile ? 0.7f : shape == MouthShape.Flat ? 0.75f : shape == MouthShape.Smirk ? 0.9f : 1f;
            var pts = new List<Vector3>();
            for (int i = 0; i <= 16; i++)
            {
                float u = i / 16f * 2 - 1; // -1..1
                float x = u * hw * width, y;
                switch (shape)
                {
                    case MouthShape.SmallSmile: y = 0.007f * u * u - 0.002f; break;
                    case MouthShape.Smile: y = 0.013f * u * u - 0.004f; break;
                    case MouthShape.Frown: y = -0.012f * u * u + 0.004f; break;
                    case MouthShape.Wavy: y = 0.0045f * Mathf.Sin(u * Mathf.PI * 2.5f); break;
                    case MouthShape.Smirk: y = 0.014f * Mathf.Pow((u + 1) * 0.5f, 2.4f) - 0.003f; break;
                    default: y = 0; break;
                }
                pts.Add(new Vector3(x, y, -x * x / 0.3f));
            }
            var m = MeshLibrary.Tube(pts, 0.0042f);
            m.name = "Mouth" + shape;
            return m;
        }

        /// <summary>張開的嘴：D 形（笑、說話，含牙齒與舌頭）或 O 形（驚訝）。單位寬高 1，前方是 +Z。</summary>
        public static Mesh OpenMouthMesh(bool dShape, out Material[] mats)
        {
            int i = dShape ? 0 : 1;
            if (OpenMeshes[i] == null) OpenMeshes[i] = BuildOpenMouthMesh(dShape, out OpenMats[i]);
            mats = OpenMats[i];
            return OpenMeshes[i];
        }

        static Mesh BuildOpenMouthMesh(bool dShape, out Material[] mats)
        {
            var interior = MaterialCache.Unlit(SpecHex("5A1A26"));
            var teeth = MaterialCache.Unlit(SpecHex("FFFDF6"));
            var tongue = MaterialCache.Unlit(SpecHex("F07A8A"));
            var key = dShape ? "MouthOpenD" : "MouthOpenO";
            var flip = Quaternion.Euler(0, 180, 0);
            var mesh = MeshLibrary.Cached(key, () =>
            {
                var outline = new List<Vector2>();
                if (dShape)
                {
                    for (int i = 0; i <= 8; i++) outline.Add(new Vector2(Mathf.Lerp(0.5f, -0.5f, i / 8f), 0.5f - 0.06f * Mathf.Pow(Mathf.Lerp(1, -1, i / 8f), 2)));
                    for (int i = 1; i < 16; i++)
                    {
                        float th = Mathf.PI + i / 16f * Mathf.PI;
                        outline.Add(new Vector2(0.5f * Mathf.Cos(th), 0.44f + 0.94f * Mathf.Sin(th)));
                    }
                }
                else
                    for (int i = 0; i < 20; i++) { float th = i / 20f * Mathf.PI * 2; outline.Add(new Vector2(0.5f * Mathf.Cos(th), 0.5f * Mathf.Sin(th))); }
                var c = new MeshLibrary.Composer();
                c.Add(MeshLibrary.Slab(outline, 0.004f), Matrix4x4.Rotate(flip), interior);
                if (dShape)
                {
                    var t = new List<Vector2> { new Vector2(-0.42f, 0.47f), new Vector2(0.42f, 0.47f), new Vector2(0.38f, 0.24f), new Vector2(-0.38f, 0.24f) };
                    t.Reverse();
                    c.Add(MeshLibrary.Slab(t, 0.002f), Matrix4x4.TRS(new Vector3(0, 0, 0.0012f), flip, Vector3.one), teeth);
                }
                c.Add(MeshLibrary.Sphere, new Vector3(0, dShape ? -0.28f : -0.22f, 0.0f), Quaternion.identity, new Vector3(0.55f, 0.35f, 0.004f), tongue);
                return c.Build(out _);
            });
            mats = dShape ? new[] { interior, teeth, tongue } : new[] { interior, tongue };
            return mesh;
        }

        public static Mesh SymbolMesh(HeadSymbol s) => MeshLibrary.Cached("Symbol" + s, () =>
        {
            var c = new MeshLibrary.Composer();
            var dummy = MaterialCache.Unlit(Color.white);
            switch (s)
            {
                case HeadSymbol.Question:
                {
                    var pts = new List<Vector3>();
                    for (int i = 0; i <= 14; i++)
                    {
                        float a = Mathf.Lerp(160f, -70f, i / 14f) * Mathf.Deg2Rad;
                        pts.Add(new Vector3(Mathf.Cos(a) * 0.026f, 0.09f + Mathf.Sin(a) * 0.026f, 0));
                    }
                    pts.Add(new Vector3(0.0f, 0.052f, 0));
                    pts.Add(new Vector3(0.0f, 0.04f, 0));
                    c.Add(MeshLibrary.Tube(pts, 0.009f), Matrix4x4.identity, dummy);
                    c.Add(MeshLibrary.Sphere, new Vector3(0, 0.015f, 0), Quaternion.identity, Vector3.one * 0.02f, dummy);
                    break;
                }
                case HeadSymbol.Anger:
                {
                    for (int q = 0; q < 4; q++)
                    {
                        float sx = q % 2 == 0 ? 1 : -1, sy = q < 2 ? 1 : -1;
                        var corner = new Vector3(sx * 0.034f, 0.06f + sy * 0.034f, 0);
                        var pts = new List<Vector3>();
                        float start = Mathf.Atan2(-sy, -sx) - 0.75f;
                        for (int i = 0; i <= 8; i++)
                        {
                            float a = start + i / 8f * 1.5f;
                            pts.Add(corner + new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0) * 0.026f);
                        }
                        c.Add(MeshLibrary.Tube(pts, 0.0075f), Matrix4x4.identity, dummy);
                    }
                    break;
                }
                case HeadSymbol.Sweat:
                    c.Add(MeshLibrary.Teardrop(), Matrix4x4.TRS(new Vector3(0, 0.05f, 0), Quaternion.identity, Vector3.one * 0.03f), dummy);
                    break;
                default:
                {
                    var pts = new List<Vector3> { new Vector3(0, 0.11f, 0), new Vector3(0, 0.08f, 0), new Vector3(0, 0.05f, 0), new Vector3(0, 0.038f, 0) };
                    c.Add(MeshLibrary.Tube(pts, 0, new[] { 0.016f, 0.0135f, 0.011f, 0.0095f }), Matrix4x4.identity, dummy);
                    c.Add(MeshLibrary.Sphere, new Vector3(0, 0.012f, 0), Quaternion.identity, Vector3.one * 0.022f, dummy);
                    break;
                }
            }
            var m = c.Build(out _);
            // 單一材質：把所有 submesh 合成一個
            var tris = new List<int>();
            for (int i = 0; i < m.subMeshCount; i++) tris.AddRange(m.GetTriangles(i));
            m.subMeshCount = 1;
            m.SetTriangles(tris, 0);
            return m;
        });

        public static Material SymbolMaterial(HeadSymbol s)
        {
            switch (s)
            {
                case HeadSymbol.Question: return MaterialCache.Unlit(SpecHex("3FA9F5"));
                case HeadSymbol.Anger: return MaterialCache.Unlit(SpecHex("E53935"));
                case HeadSymbol.Sweat: return MaterialCache.Unlit(SpecHex("7FCBFF"));
                default: return MaterialCache.Unlit(SpecHex("FF5A2E"));
            }
        }

        // ───────────── 工具 ─────────────

        /// <summary>頭（橢球）表面上 (x, y) 的前方點與法線。</summary>
        public static Vector3 Surface(Vector3 r, float x, float y, out Vector3 normal)
        {
            float k = 1 - (x / r.x) * (x / r.x) - (y / r.y) * (y / r.y);
            float z = r.z * Mathf.Sqrt(Mathf.Max(0.01f, k));
            normal = new Vector3(x / (r.x * r.x), y / (r.y * r.y), z / (r.z * r.z)).normalized;
            return new Vector3(x, y, z);
        }

        public static Transform Node(string name, Transform parent, Vector3 localPos)
        {
            var t = new GameObject(name).transform;
            t.SetParent(parent, false);
            t.localPosition = localPos;
            return t;
        }

        public static GameObject MeshNode(string name, Transform parent, Mesh mesh, Vector3 pos, Quaternion rot, Vector3 scale, Material mat)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localRotation = rot;
            go.transform.localScale = scale;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            Setup(r);
            return go;
        }

        static Transform CompositeNode(string name, Transform parent, MeshLibrary.Composer c)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = c.Build(out var mats);
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterials = mats;
            Setup(r);
            return go.transform;
        }

        static void Setup(Renderer r)
        {
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            r.lightProbeUsage = LightProbeUsage.Off;
            r.reflectionProbeUsage = ReflectionProbeUsage.Off;
            r.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
        }

        public static void SetupRenderer(Renderer r) => Setup(r);
    }
}
