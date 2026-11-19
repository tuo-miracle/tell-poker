using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Poker.Characters;
using Poker.Game;
using UnityEngine;
using UnityEngine.TestTools;

namespace Poker.Tests.Game
{
    /// <summary>
    /// 字幕泡泡不擋臉（2026-11-02 真實尺寸長圓桌截圖：Reginald 坐約 30°，泡泡要留在玩家視線 ±33° 內，被推回自己的頭後面）：
    /// 從玩家眼睛看，每個人說話時的泡泡不和任何人的頭（自己、鄰座）重疊，仍在舒適視野內；兩個人同時說話時泡泡也不互相重疊。
    /// 4 人卡通桌、5 人寫實桌（預覽）都檢查。
    /// </summary>
    public class BubbleClearanceTests
    {
        GameTestRig _rig;
        readonly List<GameObject> _spawned = new List<GameObject>();
        /// <summary>頭的半徑（公尺，寫實頭約 0.10–0.12，卡通頭約 0.16）：從玩家看是個圓。</summary>
        const float RealisticHead = 0.12f, CartoonHead = 0.17f;
        /// <summary>泡泡與頭、泡泡與泡泡之間至少隔幾度。</summary>
        const float Gap = 0.5f;

        [TearDown]
        public void TearDown()
        {
            _rig?.Dispose();
            _rig = null;
            foreach (var go in _spawned) if (go != null) Object.DestroyImmediate(go);
            _spawned.Clear();
        }

        Vector3 Eye => _rig.Table.transform.TransformPoint(RealisticPreview.PlayerEye);

        Vector2 Angles(Vector3 world)
        {
            var d = _rig.Table.transform.InverseTransformPoint(world) - RealisticPreview.PlayerEye;
            return new Vector2(Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg, Mathf.Atan2(d.y, new Vector2(d.x, d.z).magnitude) * Mathf.Rad2Deg);
        }

        Rect BubbleRect(CharacterPerformer c)
        {
            var bg = c.Subtitle.Transform.Find("BubbleBg").GetComponent<MeshFilter>();
            var b = bg.sharedMesh.bounds;
            float x0 = 999, x1 = -999, y0 = 999, y1 = -999;
            foreach (var p in new[] { new Vector3(b.min.x, b.min.y, 0f), new Vector3(b.max.x, b.min.y, 0f), new Vector3(b.max.x, b.max.y, 0f), new Vector3(b.min.x, b.max.y, 0f) })
            {
                var a = Angles(bg.transform.TransformPoint(p));
                x0 = Mathf.Min(x0, a.x); x1 = Mathf.Max(x1, a.x); y0 = Mathf.Min(y0, a.y); y1 = Mathf.Max(y1, a.y);
            }
            return Rect.MinMaxRect(x0, y0, x1, y1);
        }

        (Vector2 center, float radius) HeadDisc(CharacterPerformer c, float r)
        {
            var head = c.Rig.Head.position;
            float dist = Vector3.Distance(head, Eye);
            return (Angles(head), Mathf.Asin(Mathf.Clamp01(r / dist)) * Mathf.Rad2Deg);
        }

        static bool Overlaps(Rect rect, (Vector2 center, float radius) disc, float gap)
        {
            float dx = Mathf.Max(rect.xMin - disc.center.x, 0f, disc.center.x - rect.xMax);
            float dy = Mathf.Max(rect.yMin - disc.center.y, 0f, disc.center.y - rect.yMax);
            return dx * dx + dy * dy < (disc.radius + gap) * (disc.radius + gap);
        }

        GameController NewTable(bool realistic)
        {
            var cfg = new GameConfig { Seed = 12, FirstButton = 0, MonteCarloTrials = 40 };
            if (realistic) cfg.Opponents = RealisticPreview.Opponents;
            _rig = new GameTestRig(cfg);
            _rig.Camera.transform.SetPositionAndRotation(Eye, Quaternion.LookRotation(_rig.Table.transform.TransformDirection(RealisticPreview.PlayerLook - RealisticPreview.PlayerEye)));
            return _rig.Game;
        }

        List<CharacterPerformer> People(GameController g)
        {
            var list = new List<CharacterPerformer>();
            for (int s = 1; s < g.Spots.SeatCount; s++) if (g.Character(s) != null) list.Add(g.Character(s));
            list.Add(g.Dealer);
            return list;
        }

        void Talk(CharacterPerformer c, string text)
        {
            c.Subtitle.Show(text, 10f);
            for (int i = 0; i < 30; i++) c.Tick(1f / 72f);
        }

        List<string> Check(List<CharacterPerformer> people, float headRadius, IEnumerable<CharacterPerformer> speaking)
        {
            var problems = new List<string>();
            var rects = speaking.Select(c => (c, r: BubbleRect(c))).ToList();
            foreach (var (c, r) in rects)
            {
                Debug.Log($"[Bubble] {c.name}: yaw {r.xMin:F1}..{r.xMax:F1} pitch {r.yMin:F1}..{r.yMax:F1}");
                // 泡泡要看得出是誰講的（2026-11-02 實機：Pip 座的泡泡落在 Hank 胸口，看起來是 Hank 在講）：
                // 泡泡中心離說話者的頭比離任何其他人的頭近；兩端座位可以超出 ±35°，到說話者本人的方位附近（看他本來就要轉頭）
                var own = HeadDisc(c, headRadius);
                var center = r.center;
                foreach (var o in people)
                {
                    if (o == c) continue;
                    if (Vector2.Distance(center, HeadDisc(o, headRadius).center) <= Vector2.Distance(center, own.center))
                        problems.Add($"{c.name}'s bubble ({center.x:F1}, {center.y:F1}) is closer to {o.name}'s head than to the speaker's ({own.center.x:F1}, {own.center.y:F1})");
                }
                float yawLimit = Mathf.Max(FovAuditTests.MaxYaw, Mathf.Abs(own.center.x) + 12f);
                if (Mathf.Max(-r.xMin, r.xMax) > yawLimit) problems.Add($"{c.name}: bubble yaw {r.xMin:F1}..{r.xMax:F1} outside ±{yawLimit:F0}");
                if (r.yMax > FovAuditTests.MaxPitch) problems.Add($"{c.name}: bubble top {r.yMax:F1}° too high");
                foreach (var o in people)
                {
                    var disc = HeadDisc(o, headRadius);
                    if (Overlaps(r, disc, Gap)) problems.Add($"{c.name}'s bubble ({r.xMin:F1}..{r.xMax:F1}, {r.yMin:F1}..{r.yMax:F1}) covers {o.name}'s head ({disc.center.x:F1}, {disc.center.y:F1} r {disc.radius:F1})");
                }
                // 也不能躲在說話者自己的上身後面（2026-11-03 分鏡圖：荷官的泡泡被她自己的背心擋住）
                // 上身＝肩膀（頭中心下 0.15 m）到桌面、寬 ±0.22 m 的一條（從玩家看的角度範圍；肩膀以上是頭，上面已經查過）
                {
                    var head = c.Rig.Head.position;
                    var root = c.transform.position;
                    if (Vector3.Distance(c.Subtitle.Transform.position, Eye) > Vector3.Distance(head, Eye) - 0.05f)
                    {
                        var top = Angles(head - Vector3.up * 0.15f); var bottom = Angles(new Vector3(head.x, root.y, head.z));
                        float half = Mathf.Atan2(0.22f, Vector3.Distance(head, Eye)) * Mathf.Rad2Deg;
                        var torso = Rect.MinMaxRect(top.x - half, bottom.y, top.x + half, top.y);
                        if (torso.Overlaps(r)) problems.Add($"{c.name}'s bubble is hidden behind their own body");
                    }
                }
                // 泡泡不能落在別人的上身上（前後都不行：在前面像是他在講，在後面被擋住）
                foreach (var o in people)
                {
                    if (o == c) continue;
                    var chest = o.Rig.Chest.position;
                    var body = (Angles(chest), Mathf.Asin(Mathf.Clamp01(0.2f / Vector3.Distance(chest, Eye))) * Mathf.Rad2Deg);
                    if (Overlaps(r, body, 0f)) problems.Add($"{c.name}'s bubble is on {o.name}'s body");
                }
            }
            for (int i = 0; i < rects.Count; i++)
                for (int j = i + 1; j < rects.Count; j++)
                {
                    var a = rects[i].r; var b = rects[j].r;
                    a.xMin -= Gap; a.xMax += Gap; a.yMin -= Gap; a.yMax += Gap;
                    if (a.Overlaps(b)) problems.Add($"{rects[i].c.name}'s and {rects[j].c.name}'s bubbles overlap");
                }
            return problems;
        }

        [UnityTest]
        public IEnumerator Cartoon_EachSpeaker_BubbleClearsEveryHead([Values(false, true)] bool fiveHanded)
        {
            var cfg = new GameConfig { Seed = 12, FirstButton = 0, MonteCarloTrials = 40 };
            if (fiveHanded) cfg.Opponents = GameConfig.FiveHanded;
            _rig = new GameTestRig(cfg);
            _rig.Camera.transform.SetPositionAndRotation(Eye, Quaternion.LookRotation(_rig.Table.transform.TransformDirection(RealisticPreview.PlayerLook - RealisticPreview.PlayerEye)));
            _rig.Idle(0.3);   // 遊戲每幀給角色牌桌中心（泡泡夾進視野用）
            yield return null;
            var people = People(_rig.Game);
            foreach (var c in people) c.SetViewer(_rig.Camera.transform);
            var problems = new List<string>();
            foreach (var c in people)
            {
                foreach (var o in people) o.Subtitle.Hide();
                Talk(c, "Hi, I'm Dot. Let's play one together.");
                problems.AddRange(Check(people, CartoonHead, new[] { c }));
            }
            Assert.IsEmpty(problems, string.Join("\n", problems));
        }

#if UNITY_EDITOR
        [UnityTest]
        public IEnumerator Realistic_FiveHanded_EachSpeaker_AndTwoAtOnce_BubblesClearEveryHead()
        {
            var g = NewTable(realistic: true);
            var preview = new GameObject("RealisticPreview").AddComponent<RealisticPreview>();
            _spawned.Add(preview.gameObject);
            string Glb(string n) => $"Assets/_Project/Art/Characters/{n}/{n}.glb";
            preview.Cast = new[] { "Hank", "Marcus", "Jace", "Reginald", "Erik" }.Select(n => UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(Glb(n))).ToArray();
            preview.DealerModel = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(Glb("Dealer"));
            yield return new WaitForSecondsRealtime(1.2f);
            _rig.Idle(0.3);   // 遊戲每幀給角色牌桌中心（泡泡夾進視野用）
            Assert.AreEqual(5, Object.FindObjectsByType<RealisticBody>(FindObjectsSortMode.None).Length);
            var people = People(g);
            foreach (var c in people) { c.AutoUpdate = false; c.SetViewer(_rig.Camera.transform); }
            var problems = new List<string>();
            foreach (var c in people)
            {
                foreach (var o in people) o.Subtitle.Hide();
                Talk(c, "Hi, I'm Dot. Let's play one together.");
                problems.AddRange(Check(people, RealisticHead, new[] { c }));
            }
            // 兩個人同時說話：由左到右相鄰的每一對（Pip–Rocco、Rocco–荷官、荷官–老手、老手–Vex）
            var order = new List<CharacterPerformer> { g.Character(1), g.Character(2), g.Dealer, g.Character(3), g.Character(4) };
            for (int i = 0; i + 1 < order.Count; i++)
            {
                var pair = new[] { order[i], order[i + 1] };
                foreach (var o in people) o.Subtitle.Hide();
                foreach (var c in pair) c.Subtitle.Show("Action is on you.", 10f);
                for (int k = 0; k < 30; k++) foreach (var c in pair) c.Tick(1f / 72f);
                problems.AddRange(Check(people, RealisticHead, pair));
            }
            Assert.IsEmpty(problems, string.Join("\n", problems));
        }
#endif
    }
}
