using System.Collections;
using System.Collections.Generic;
using System.Text;
using NUnit.Framework;
using Poker.Characters;
using Poker.Game;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace Poker.Tests.Game
{
    /// <summary>
    /// 示範手字幕 × 角色（2026-10-14 Quest 3 實機截圖：頭戴裝置放在桌上、眼睛只比桌面高約 0.2 m，
    /// 教學咪牌那一步的「Cup your cards and lift your wrist to peek」浮在右上方、蓋住 Dot 和 Vex 的身體，離示範手很遠）。
    /// 之前的 FoV 檢查只從固定的坐姿眼睛看、只檢查字幕「被」角色擋住，沒檢查字幕「蓋住」角色。
    /// 這裡用多個眼睛姿勢（桌面上 0.15–0.55 m、桌緣後 0.22–0.40 m、轉頭 ±20°）投影：
    /// 從眼睛穿過字幕底板的視線不能碰到任何角色身體部位（每個看得到的網格加 MeshCollider）或字幕泡泡；字幕貼桌、離示範手 ≤ 10 cm。
    /// </summary>
    public class GhostCaptionViewTests
    {
        /// <summary>字幕中心離示範手範圍（GhostHands.Measure 的 region）最遠多少（公尺）。</summary>
        public const float MaxGhostDistance = 0.10f;
        /// <summary>字幕中心最高離桌面多少（公尺）：貼著桌面，不要浮起來。</summary>
        public const float MaxCenterHeight = 0.08f;
        static readonly float[] EyeHeights = { 0.15f, 0.20f, 0.25f, 0.35f, 0.45f, 0.55f };
        static readonly float[] EyeBack = { -0.22f, -0.30f, -0.40f };
        static readonly float[] Yaws = { -20f, 0f, 20f };
        /// <summary>轉頭的軸在眼睛後方（公尺）：轉頭時眼睛也會左右移一點。</summary>
        const float NeckBehind = 0.08f;

        GameTestRig _rig;

        [TearDown]
        public void TearDown()
        {
            foreach (var c in _targets) if (c != null) Object.DestroyImmediate(c);
            _targets.Clear();
            _rig?.Dispose();
            _rig = null;
        }

        /// <summary>重現實機：教學咪牌那一步、眼睛在桌面上 0.2 m（裝置放在桌上），字幕不能蓋住角色或泡泡。</summary>
        [UnityTest]
        public IEnumerator TutorialPeekCaption_FromTheDeviceLowEye_CoversNoCharacter()
        {
            _rig = new GameTestRig(new GameConfig { Seed = 31, MonteCarloTrials = 60 }, g => g.TutorialEnabled = true);
            yield return null;
            var game = _rig.Game;
            Assert.IsTrue(_rig.IdleUntil(() => game.GhostShown == GhostGesture.Peek, 10), "tutorial peek ghost");
            Assert.IsTrue(_rig.IdleUntil(() => game.Ghost.PlaybackTime > 1.0, 6));
            var eye = new Vector3(0f, 0.20f, -0.30f);
            SetPose(eye, 0f);
            SettleBubbles(game);
            game.Ghost.Tick(0f, _rig.Camera.transform.position);
            var hits = Overlaps(game, "device pose");
            Assert.IsEmpty(hits, string.Join("\n", hits));
        }

        /// <summary>每個示範手勢 × 每個眼睛姿勢：不蓋住角色身體與泡泡；字幕貼桌、靠近示範手。</summary>
        [UnityTest]
        public IEnumerator GhostCaptions_AllGestures_AllEyePoses_StayNearTheHand_AndCoverNoCharacter()
        {
            _rig = new GameTestRig(new GameConfig { Seed = 12, FirstButton = 0, MonteCarloTrials = 60 });
            yield return null;
            var game = _rig.Game;
            _rig.Idle(0.2);
            // 所有角色同時在講話（最壞情況：泡泡都在）
            foreach (var c in Speakers(game)) c.Subtitle.Show("Hi, I'm Dot. Let's play one together.", 60f);

            var problems = new List<string>();
            var report = new StringBuilder();
            var gestures = new List<GhostGesture>(GhostCatalog.All) { GhostGesture.Raise, GhostGesture.Gaze };
            foreach (var g in gestures)
            {
                game.Ghost.Hide();
                var sw = System.Diagnostics.Stopwatch.StartNew();
                game.Ghost.Play(g);
                sw.Stop();
                var cap = game.Ghost.Caption.Go.transform;
                var local = game.Table.transform.InverseTransformPoint(cap.position);
                var clip = GhostCatalog.Load(g);
                if (clip != null)
                {
                    var region = GhostHands.Measure(clip).region;
                    float dist = Mathf.Sqrt(region.SqrDistance(local));
                    report.AppendLine($"{g,-12} caption {local:F3}{(game.Ghost.CaptionFlat ? " flat" : "")} \"{game.Ghost.Caption.Text.Replace('\n', '|')}\" region {region.min:F2}-{region.max:F2} dist {dist:0.000} m place {sw.Elapsed.TotalMilliseconds:0} ms");
                    if (dist > MaxGhostDistance + 1e-3f) problems.Add($"{g}: caption {dist:0.000} m from the hand (> {MaxGhostDistance})");
                    if (local.y > MaxCenterHeight) problems.Add($"{g}: caption centre {local.y:0.000} m above the table (> {MaxCenterHeight})");
                }
                else report.AppendLine($"{g,-12} caption {local:F3} (no hand)");

                foreach (var h in EyeHeights)
                    foreach (var z in EyeBack)
                        foreach (var yaw in Yaws)
                        {
                            SetPose(new Vector3(0f, h, z), yaw);
                            SettleBubbles(game);
                            game.Ghost.Tick(0f, _rig.Camera.transform.position);
                            problems.AddRange(Overlaps(game, $"{g} eye h={h:0.00} z={z:0.00} yaw={yaw:0}"));
                        }
            }
            Debug.Log("[GhostCaptionView]\n" + report);
            // 每個手勢一行摘要（幾個姿勢有問題）＋前 40 筆細節
            var summary = new StringBuilder();
            foreach (var g in gestures)
            {
                int n = problems.FindAll(x => x.StartsWith(g + ":") || x.StartsWith(g + " ")).Count;
                if (n > 0) summary.AppendLine($"  {g}: {n}");
            }
            Assert.IsEmpty(problems, $"{problems.Count} problems\n{summary}" + string.Join("\n", problems.GetRange(0, Mathf.Min(40, problems.Count))));
        }

        // ───────────── 量測 ─────────────

        static IEnumerable<CharacterPerformer> Speakers(GameController game)
        {
            for (int s = 1; s < game.Spots.SeatCount; s++) if (game.Character(s) != null) yield return game.Character(s);
            if (game.Dealer != null) yield return game.Dealer;
        }

        /// <summary>眼睛在牌桌座標 eye、轉頭 yaw 度（繞眼睛後方 NeckBehind 的垂直軸），看向牌桌中段偏下。</summary>
        void SetPose(Vector3 eye, float yaw)
        {
            var rot = Quaternion.Euler(0f, yaw, 0f);
            var neck = eye + Vector3.back * NeckBehind;
            var pos = neck + rot * (Vector3.forward * NeckBehind);
            var target = new Vector3(0f, 0.04f, 0.35f);
            var look = rot * (target - eye);
            var cam = _rig.Camera;
            cam.fieldOfView = 90f;
            cam.aspect = 1f;
            var t = _rig.Table.transform;
            cam.transform.SetPositionAndRotation(t.TransformPoint(pos), t.rotation * Quaternion.LookRotation(look));
        }

        /// <summary>角色的 Tick：泡泡跟著新的視點夾進舒適視野、面向玩家。</summary>
        void SettleBubbles(GameController game)
        {
            foreach (var c in Speakers(game))
                for (int i = 0; i < 20; i++) c.Tick(1f / 72f);
        }

        readonly HashSet<Collider> _targets = new HashSet<Collider>();

        /// <summary>每個角色看得到的網格（身體、頭、手臂、字幕泡泡）加 MeshCollider（測試用，TearDown 跟著角色一起刪）。</summary>
        void AddCharacterColliders(GameController game)
        {
            foreach (var c in Speakers(game))
                foreach (var mf in c.GetComponentsInChildren<MeshFilter>(true))
                {
                    if (mf.sharedMesh == null || mf.sharedMesh.vertexCount == 0) continue;
                    if (!mf.TryGetComponent<MeshRenderer>(out var mr) || !mr.enabled) continue;
                    if (mf.TryGetComponent<MeshCollider>(out _)) continue;
                    var mc = mf.gameObject.AddComponent<MeshCollider>();
                    mc.sharedMesh = mf.sharedMesh;
                    _targets.Add(mc);
                }
        }

        /// <summary>
        /// 從眼睛看過去，字幕底板（四周多留 5 mm）上 9×5 個點：視線（一直延伸到桌面為止——桌面以下被牌桌的遮擋面擋住）
        /// 碰到角色或泡泡就是重疊（字幕蓋住角色，或角色擋住字幕）。
        /// </summary>
        List<string> Overlaps(GameController game, string at)
        {
            var hits = new List<string>();
            var cap = game.Ghost.Caption;
            if (!cap.Go.activeInHierarchy) return hits;
            if (_targets.Count == 0) AddCharacterColliders(game);
            Physics.SyncTransforms();
            var plate = cap.Plate.transform;
            var eye = _rig.Camera.transform.position;
            var table = _rig.Table.transform;
            var seen = new HashSet<string>();
            float mx = 0.005f / Mathf.Max(1e-4f, plate.lossyScale.x), my = 0.005f / Mathf.Max(1e-4f, plate.lossyScale.y);
            for (int i = 0; i < 9; i++)
                for (int j = 0; j < 5; j++)
                {
                    var local = new Vector3(Mathf.Lerp(-0.5f - mx, 0.5f + mx, i / 8f), Mathf.Lerp(-0.5f - my, 0.5f + my, j / 4f), 0f);
                    var p = plate.TransformPoint(local);
                    var dir = p - eye;
                    // 到桌面（牌桌座標 y = 0）為止
                    float dy = table.InverseTransformDirection(dir).y, ey = table.InverseTransformPoint(eye).y;
                    float maxT = dy < -1e-5f ? ey / -dy : 50f;
                    float len = dir.magnitude;
                    foreach (var h in Physics.RaycastAll(eye, dir / len, maxT * len))
                    {
                        if (!_targets.Contains(h.collider)) continue;
                        var who = h.collider.GetComponentInParent<CharacterPerformer>();
                        string what = (who != null ? who.name : "?") + "/" + h.collider.name;
                        if (seen.Add(what)) hits.Add($"{at}: caption {(h.distance > len ? "covers" : "is behind")} {what}");
                    }
                }
            return hits;
        }
    }
}
