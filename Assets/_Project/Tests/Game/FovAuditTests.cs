using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using NUnit.Framework;
using Poker.Characters;
using Poker.Core;
using Poker.Game;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace Poker.Tests.Game
{
    /// <summary>
    /// 視野／可讀性檢查（比賽的 Experience Design：評審坐著玩一次）：從玩家座位的眼睛（桌面上 45 cm、桌緣後 30 cm）
    /// 把畫面上的字（角色字幕泡泡、下注金額標籤、攤牌標題、讀人揭曉卡、音量提示、結算看板、示範手字幕）投影到相機上，
    /// 檢查：在畫面內、在舒適視野內（水平 ±35°、不高過 +15°、不低於 −55°）、不被角色擋住、同時出現的彼此不重疊、
    /// 字高 ≥ 1.2°。相機：4:3、垂直 80°（比 Quest 3 窄，保守），看向牌桌中央偏上（自然坐姿的視線）。
    /// 座位的標籤（各 AI 的下注金額、攤牌標題）跟著座位走：真實尺寸的長圓桌（2026-11-02）兩端的人在 ±55°，
    /// 使用者接受看兩端時稍微轉頭，所以這些標籤用 ±SeatLabelMaxYaw，「在畫面內」用頭往那邊轉最多 SeatHeadTurn 的相機判斷。
    /// 結果表印在 log（[FoV]）。
    /// </summary>
    public class FovAuditTests
    {
        public const float MaxYaw = 35f, MaxPitch = 15f, MinPitch = -55f, MinCharDegrees = 1.2f;
        /// <summary>座位標籤的水平範圍（同 RealTableSeatingTests：兩端的人的身體在 ±57° 內）與看它時頭最多轉幾度。</summary>
        public const float SeatLabelMaxYaw = 58f, SeatHeadTurn = 25f;

        /// <summary>
        /// 跟著座位的標籤。字幕泡泡也算（2026-11-02 指揮官裁決：兩端座位的泡泡要貼著說話者本人才看得出是誰講的，
        /// 可以超出 ±35° 到他本人的方位附近；BubblePlacement.OwnerYawMargin）。
        /// </summary>
        static bool SeatLabel(string name) =>
            name.StartsWith("bet seat") || name.StartsWith("bet detail seat") || name.StartsWith("RevealHeadline") || name.StartsWith("RevealDetail") || name.StartsWith("bubble ");
        /// <summary>檢查用相機的垂直視角（Quest 3 單眼約 96°；用 80° 保守一點：不轉眼睛也看得到）。</summary>
        const float CameraFov = 80f;
        static readonly Vector3 Eye = new Vector3(0f, 0.45f, -0.30f);
        static readonly Vector3 Gaze = new Vector3(0f, 0.12f, 0.45f);

        GameTestRig _rig;
        readonly List<string> _violations = new List<string>();
        readonly StringBuilder _report = new StringBuilder();
        readonly List<Collider> _colliders = new List<Collider>();

        sealed class Item
        {
            public string Name;
            public Vector3[] Corners;
            public Vector3 Center;
            public float LineHeight;
            public Transform Self;
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var c in _colliders) if (c != null) Object.DestroyImmediate(c);
            _colliders.Clear();
            _rig?.Dispose();
            _rig = null;
        }

        [UnityTest]
        public IEnumerator KeyLabels_AreOnScreen_InTheComfortCone_Legible_AndNotOccluded()
        {
            _rig = new GameTestRig(new GameConfig { Seed = 12, FirstButton = 0, MonteCarloTrials = 60 }, g =>
            {
                g.AiOverride = st => Engine.GetLegalActions(st, st.ToAct).CanCheck ? (PlayerCommand)new Check(st.ToAct) : new Call(st.ToAct);
                g.AutoPlayer = st => Engine.GetLegalActions(st, 0).CanCheck ? (PlayerCommand)new Check(0) : new Call(0);
                g.AiThinkScale = 0.3f;
            });
            yield return null;
            var game = _rig.Game;
            var cam = _rig.Camera;
            cam.fieldOfView = CameraFov;
            cam.aspect = 4f / 3f;
            cam.transform.position = Eye;
            cam.transform.rotation = Quaternion.LookRotation(Gaze - Eye);
            _rig.Idle(0.2);   // 跑幾幀：角色拿到牌桌中心、看向玩家
            cam.transform.SetPositionAndRotation(Eye, Quaternion.LookRotation(Gaze - Eye));
            AddCharacterColliders(game);

            // ---- 角色字幕泡泡（一次一個，兩行字）----
            var speakers = new List<(string name, CharacterPerformer c)>();
            for (int s = 1; s < game.Spots.SeatCount; s++) if (game.Character(s) != null) speakers.Add((game.Character(s).name, game.Character(s)));
            if (game.Dealer != null) speakers.Add(("Dealer", game.Dealer));
            foreach (var (name, c) in speakers)
            {
                foreach (var (_, o) in speakers) o.Subtitle.Hide();
                c.Subtitle.Show("Hi, I'm Dot. Let's play one together.", 10f);
                for (int i = 0; i < 30; i++) c.Tick(1f / 72f);   // 角色的 Tick：泡泡夾進視野、面向玩家
                Check(Bubble("bubble " + name, c));
            }
            foreach (var (_, o) in speakers) o.Subtitle.Hide();

            // ---- 下注金額標籤（每個座位的下注堆與底池）----
            for (int s = 1; s <= game.Spots.SeatCount; s++)
            {
                bool pot = s == game.Spots.SeatCount;
                var top = (pot ? game.Spots.PotPile : game.Spots.BetOf(s)) + Vector3.up * 0.03f;
                game.BetLabel.Show(pot ? BetLabel.PotTarget : s, pot ? "Pot 240" : "Rocco bet 120", "To call: 115", top);
                game.BetLabel.Face(Eye);
                var both = new[] { Label("bet " + (pot ? "pot" : "seat" + s), game.BetLabel.Main), Label("bet detail " + (pot ? "pot" : "seat" + s), game.BetLabel.Detail) };
                foreach (var it in both) Check(it);
                NoOverlap(both);
            }
            game.BetLabel.Hide();

            // ---- 音量提示 ----
            game.VolumeNotice.Show(MediaVolumeGuard.Notice, GameController.VolumeNoticeSpot);
            game.VolumeNotice.Face(Eye);
            Check(Label("volume notice", game.VolumeNotice));
            game.VolumeNotice.Hide();

            // ---- 示範手字幕（牌局中的提示用同一個 GhostHands）----
            foreach (var g in GhostCatalog.All)
            {
                game.Ghost.Hide();
                game.Ghost.Play(g);
                game.Ghost.Tick(0.3f, Eye);
                Check(Label("ghost " + g, game.Ghost.Caption));
            }
            game.Ghost.Hide();

            // ---- 攤牌：每個 AI 的標題＋讀人揭曉卡＋說收尾台詞的那個角色的泡泡，同時出現不能重疊 ----
            Assert.IsTrue(_rig.IdleUntil(() => game.Flow == GameFlow.HandOver, 120), "showdown");
            _rig.Idle(0.3);
            cam.transform.SetPositionAndRotation(Eye, Quaternion.LookRotation(Gaze - Eye));
            AddChipColliders(game);   // 攤牌標題也不能插進籌碼堆（2026-10-14 截圖：側邊座位的標題穿過 Pip 的籌碼）
            game.ReadRevealLabel.Show("Rocco read you: strong — wrong!", game.ReadRevealSpot);   // 最長的一種（RevealTextTests）
            game.ReadRevealLabel.Face(Eye);
            var showdown = ActiveLabels(game.Props.Root).ToList();
            Assert.GreaterOrEqual(showdown.Count, 3, "headlines + reveal on screen");
            foreach (var it in showdown) Check(it);
            NoOverlap(showdown);
            // 標籤的底板不能插進籌碼堆（立體視覺看得出來）
            foreach (var it in showdown)
            {
                var plate = new Bounds(it.Corners[0], Vector3.zero);
                foreach (var c in it.Corners) plate.Encapsulate(c);
                foreach (var mc in _colliders.Where(c => c is MeshCollider && !IsCharacter(c.transform)))
                    if (mc.bounds.Intersects(plate)) _violations.Add($"{it.Name} cuts through {mc.transform.parent?.name}/{mc.name}");
            }
            foreach (var (name, c) in speakers)
            {
                if (c == game.Dealer) continue;
                foreach (var (_, o) in speakers) o.Subtitle.Hide();
                c.Subtitle.Show("Ha! Knew it.", 10f);
                for (int i = 0; i < 30; i++) c.Tick(1f / 72f);   // 角色的 Tick：泡泡夾進視野、面向玩家
                var b = Bubble("bubble " + name + " at showdown", c);
                NoOverlap(showdown.Concat(new[] { b }).ToList());
            }
            foreach (var (_, o) in speakers) o.Subtitle.Hide();
            game.ReadRevealLabel.Hide();

            // ---- 結算看板 ----
            game.Props.ShowSummary("You finished +1,250!", new[] { "Hands won: 7 of 20", "Best hand: Full house" });
            game.Props.FaceLabels(Eye);
            var summary = ActiveLabels(game.Props.Root).ToList();
            foreach (var it in summary) Check(it);
            NoOverlap(summary);

            Debug.Log("[FoV] report (eye " + Eye + ", gaze " + Gaze + ")\n" + _report);
            Assert.IsEmpty(_violations, string.Join("\n", _violations));
        }

        // ───────────── 量測 ─────────────

        void AddCharacterColliders(GameController game)
        {
            var roots = new List<Transform>();
            for (int s = 1; s < game.Spots.SeatCount; s++) if (game.Character(s) != null) roots.Add(game.Character(s).transform);
            if (game.Dealer != null) roots.Add(game.Dealer.transform);
            foreach (var r in roots)
                foreach (var mf in r.GetComponentsInChildren<MeshFilter>(true))
                {
                    // 只算看得到的部分（張嘴、符號等隱藏的網格不算）
                    if (mf.sharedMesh == null || IsUnder(mf.transform, "Bubble") || !mf.gameObject.activeInHierarchy) continue;
                    if (!mf.TryGetComponent<MeshRenderer>(out var mr) || !mr.enabled) continue;
                    var mc = mf.gameObject.AddComponent<MeshCollider>();
                    mc.sharedMesh = mf.sharedMesh;
                    _colliders.Add(mc);
                }
            Physics.SyncTransforms();
        }

        void AddChipColliders(GameController game)
        {
            foreach (Transform t in game.Props.Root)
            {
                if (!t.gameObject.activeInHierarchy || !(t.name.StartsWith("Stack") || t.name.StartsWith("Bet") || t.name == "Pot")) continue;
                foreach (var mf in t.GetComponentsInChildren<MeshFilter>())
                {
                    if (mf.sharedMesh == null || mf.sharedMesh.vertexCount == 0) continue;
                    var mc = mf.gameObject.AddComponent<MeshCollider>();
                    mc.sharedMesh = mf.sharedMesh;
                    _colliders.Add(mc);
                }
            }
            Physics.SyncTransforms();
        }

        bool IsCharacter(Transform t)
        {
            var g = _rig.Game;
            for (; t != null; t = t.parent)
                if (t.GetComponent<CharacterPerformer>() != null) return true;
            return false;
        }

        static bool IsUnder(Transform t, string name)
        {
            for (; t != null; t = t.parent) if (t.name == name) return true;
            return false;
        }

        static IEnumerable<Item> ActiveLabels(Transform root)
        {
            foreach (Transform t in root)
            {
                if (!t.gameObject.activeInHierarchy) continue;
                var plate = t.Find("Plate");
                var text = t.GetComponentInChildren<TextMesh>();
                if (plate == null || text == null) continue;
                yield return Label(t.name, t, plate, text);
            }
        }

        static Item Label(string name, WorldLabel l) => Label(name, l.Go.transform, l.Plate.transform, l.Go.GetComponentInChildren<TextMesh>());

        static Item Label(string name, Transform t, Transform plate, TextMesh text)
        {
            var corners = new[]
            {
                plate.TransformPoint(new Vector3(-0.5f, -0.5f, 0f)), plate.TransformPoint(new Vector3(0.5f, -0.5f, 0f)),
                plate.TransformPoint(new Vector3(0.5f, 0.5f, 0f)), plate.TransformPoint(new Vector3(-0.5f, 0.5f, 0f)),
            };
            return new Item
            {
                Name = name, Corners = corners, Center = plate.position, Self = t,
                LineHeight = text.characterSize * text.fontSize * 0.1f * text.transform.lossyScale.y,
            };
        }

        static Item Bubble(string name, CharacterPerformer c)
        {
            var root = c.Subtitle.Transform;
            var bg = root.Find("BubbleBg").GetComponent<MeshFilter>();
            var b = bg.sharedMesh.bounds;
            var corners = new[]
            {
                bg.transform.TransformPoint(new Vector3(b.min.x, b.min.y, 0f)), bg.transform.TransformPoint(new Vector3(b.max.x, b.min.y, 0f)),
                bg.transform.TransformPoint(new Vector3(b.max.x, b.max.y, 0f)), bg.transform.TransformPoint(new Vector3(b.min.x, b.max.y, 0f)),
            };
            return new Item
            {
                Name = name, Corners = corners, Center = bg.transform.TransformPoint(b.center), Self = root,
                LineHeight = SubtitleBubble.LineHeight * root.Find("BubbleText").lossyScale.y,
            };
        }

        void Check(Item it)
        {
            var cam = _rig.Camera;
            float minYaw = 999, maxYaw = -999, minPitch = 999, maxPitch = -999;
            foreach (var p in it.Corners)
            {
                var a = GhostCaptionPlacement.Angles(Eye, p);
                minYaw = Mathf.Min(minYaw, a.x); maxYaw = Mathf.Max(maxYaw, a.x);
                minPitch = Mathf.Min(minPitch, a.y); maxPitch = Mathf.Max(maxPitch, a.y);
            }
            bool seat = SeatLabel(it.Name);
            // 泡泡貼著說話者：可以到他頭的方位再往外 OwnerYawMargin
            float yawLimit = !seat ? MaxYaw : it.Name.StartsWith("bubble ") ? SeatLabelMaxYaw + BubblePlacement.OwnerYawMargin : SeatLabelMaxYaw;
            // 座位標籤：頭往標籤那邊轉（最多 SeatHeadTurn）再看在不在畫面內
            var rest = cam.transform.rotation;
            if (seat) cam.transform.rotation = Quaternion.AngleAxis(Mathf.Clamp((minYaw + maxYaw) * 0.5f, -SeatHeadTurn, SeatHeadTurn), Vector3.up) * rest;
            bool onScreen = true;
            foreach (var p in it.Corners)
            {
                var v = cam.WorldToViewportPoint(p);
                if (v.z <= 0f || v.x < 0f || v.x > 1f || v.y < 0f || v.y > 1f) onScreen = false;
            }
            cam.transform.rotation = rest;
            float dist = (it.Center - Eye).magnitude;
            float charDeg = Mathf.Atan2(it.LineHeight, dist) * Mathf.Rad2Deg;
            string occluder = Occluder(it);
            _report.AppendLine($"{it.Name,-34} yaw[{minYaw,6:0.0},{maxYaw,6:0.0}] pitch[{minPitch,6:0.0},{maxPitch,6:0.0}] dist {dist:0.00} m char {charDeg:0.00}° " +
                               (onScreen ? "on-screen" : "OFF-SCREEN") + (occluder != null ? " OCCLUDED by " + occluder : ""));
            if (!onScreen) _violations.Add(it.Name + ": off screen");
            if (Mathf.Max(-minYaw, maxYaw) > yawLimit) _violations.Add($"{it.Name}: yaw [{minYaw:0.0},{maxYaw:0.0}] outside ±{yawLimit}°");
            if (maxPitch > MaxPitch) _violations.Add($"{it.Name}: top at {maxPitch:0.0}° (look up)");
            if (minPitch < MinPitch) _violations.Add($"{it.Name}: bottom at {minPitch:0.0}° (too low)");
            if (charDeg < MinCharDegrees) _violations.Add($"{it.Name}: text {charDeg:0.00}° < {MinCharDegrees}°");
            if (occluder != null) _violations.Add($"{it.Name}: occluded by {occluder}");
        }

        /// <summary>從眼睛看向字上的 15 個點，先碰到角色的身體（或籌碼堆）就是被擋住。</summary>
        string Occluder(Item it)
        {
            // 5×3 格點（內縮 10%）
            var pts = new List<Vector3>();
            for (int i = 0; i < 5; i++)
                for (int j = 0; j < 3; j++)
                {
                    float u = 0.1f + 0.8f * i / 4f, v = 0.1f + 0.8f * j / 2f;
                    var bottom = Vector3.Lerp(it.Corners[0], it.Corners[1], u);
                    var top = Vector3.Lerp(it.Corners[3], it.Corners[2], u);
                    pts.Add(Vector3.Lerp(bottom, top, v));
                }
            foreach (var p in pts)
            {
                var d = p - Eye;
                if (Physics.Raycast(Eye, d.normalized, out var hit, d.magnitude - 0.005f)) return hit.collider.name;
            }
            return null;
        }

        void NoOverlap(IList<Item> items)
        {
            var cam = _rig.Camera;
            var rects = items.Select(it =>
            {
                var r = AngleRect.Empty;
                foreach (var p in it.Corners) r.Add(GhostCaptionPlacement.Angles(Eye, p));
                return r;
            }).ToList();
            for (int i = 0; i < items.Count; i++)
                for (int j = i + 1; j < items.Count; j++)
                {
                    // 同一個標籤的兩行（標題與補充、下注兩行）緊貼是設計，只要不疊上去
                    if (rects[i].Overlaps(rects[j], -0.05f))
                        _violations.Add($"{items[i].Name} overlaps {items[j].Name} ({rects[i]} / {rects[j]})");
                }
        }
    }
}
