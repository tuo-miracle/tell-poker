using System.Collections.Generic;
using Poker.Characters;
using Poker.Game;
using UnityEngine;

namespace Poker.UX
{
    /// <summary>
    /// 高對比模式（不改別人的程式）：桌面標線（下注區、下注線、棄牌線…）換成亮黃色、桌布壓暗；
    /// 籌碼堆加反向外殼外框、牌底下墊一層放大的亮色剪影當外框。關閉時還原原本顏色、隱藏外框。
    /// </summary>
    public static class HighContrast
    {
        public const string OutlineName = "HC_Outline";
        public static readonly Color MarkColor = new Color(1f, 0.84f, 0.08f);
        public static readonly Color ChipOutlineColor = new Color(1f, 0.84f, 0.08f);
        public static readonly Color CardOutlineColor = new Color(1f, 0.84f, 0.08f);
        static readonly string[] Marks = { "BetZone", "BetLine", "FoldLine", "PotArea", "ChipSpot", "CardSlot_L", "CardSlot_R" };
        const float FeltDim = 0.55f;
        public const float HullThickness = 0.0025f;

        static readonly Dictionary<Material, Color> Originals = new Dictionary<Material, Color>();
        static Material _hullMat;

        public static Color ColorOf(Material m) => m.HasProperty("_BaseColor") ? m.GetColor("_BaseColor") : m.color;

        static void SetColor(Material m, Color c)
        {
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
            if (m.HasProperty("_Color")) m.color = c;
        }

        static void Tint(Renderer r, System.Func<Color, Color> on, bool enable)
        {
            if (r == null || r.sharedMaterial == null) return;
            var m = r.sharedMaterial;
            if (!Originals.TryGetValue(m, out var orig)) Originals[m] = orig = ColorOf(m);
            SetColor(m, enable ? on(orig) : orig);
        }

        public static void Apply(GameController game, bool on)
        {
            if (game == null) return;
            PruneDestroyed();
            if (game.Table != null)
            {
                foreach (var name in Marks)
                {
                    var t = UxController.FindDeep(game.Table.transform, name);
                    if (t != null && t.TryGetComponent<Renderer>(out var r)) Tint(r, _ => MarkColor, on);
                }
                var felt = UxController.FindDeep(game.Table.transform, "Felt");
                if (felt != null && felt.TryGetComponent<Renderer>(out var fr)) Tint(fr, c => new Color(c.r * FeltDim, c.g * FeltDim, c.b * FeltDim, c.a), on);
            }

            var props = game.Props;
            if (props == null) return;
            if (game.BetLabel != null) game.BetLabel.HighContrast = on;
            foreach (var pile in Piles(props)) if (pile != null) Outline(pile.Go, on, hull: true);
            // 其他在執行期才建立的籌碼堆（例如 Flyer）：凡是用籌碼材質的 MeshRenderer 都加外框
            if (props.Root != null)
                foreach (var mr in props.Root.GetComponentsInChildren<MeshRenderer>(true))
                    if (mr.sharedMaterial == ChipStackMesh.Material) Outline(mr.gameObject, on, hull: true);
            foreach (var card in Cards(props))
            {
                if (card == null) continue;
                foreach (var mf in card.GetComponentsInChildren<MeshFilter>(true))
                    if (mf.name != OutlineName) Outline(mf.gameObject, on, hull: false);
            }
        }

        static IEnumerable<ChipPile> Piles(TableProps p)
        {
            foreach (var x in p.Stack) yield return x;
            foreach (var x in p.Bet) yield return x;
            yield return p.Pot;
            yield return p.Pending;
            foreach (var x in p.InHand) yield return x;
        }

        static IEnumerable<CardView> Cards(TableProps p)
        {
            foreach (var x in p.PlayerCards) yield return x;
            foreach (var x in p.AiCards) yield return x;
            foreach (var x in p.Board) yield return x;
        }

        static void Outline(GameObject target, bool on, bool hull)
        {
            var existing = target.transform.Find(OutlineName);
            if (!on) { if (existing != null) existing.gameObject.SetActive(false); return; }
            if (existing != null) { existing.gameObject.SetActive(true); return; }
            if (!target.TryGetComponent<MeshFilter>(out var src)) return;

            var go = new GameObject(OutlineName);
            go.transform.SetParent(target.transform, false);
            go.AddComponent<MeshFilter>();
            var r = go.AddComponent<MeshRenderer>();
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            if (hull)
            {
                // 反向外殼：頂點沿（平滑後的）法線外推、只畫背面 → 籌碼堆外圍一圈亮邊（一堆裡有好幾疊，不能用整體放大）
                r.sharedMaterial = HullMaterial();
            }
            else
            {
                // 牌：放大的剪影墊在牌底下 → 牌四周一圈亮框
                go.transform.localScale = new Vector3(1.14f, 1f, 1.1f);
                go.transform.localPosition = new Vector3(0f, -0.0008f, 0f);
                r.sharedMaterial = MaterialCache.Unlit(CardOutlineColor);
            }
            var shell = go.AddComponent<OutlineShell>();
            shell.Source = src;
            shell.Extrude = hull ? HullThickness : 0f;
        }

        static readonly List<Material> Dead = new List<Material>();

        static void PruneDestroyed()
        {
            Dead.Clear();
            foreach (var m in Originals.Keys) if (m == null) Dead.Add(m);
            foreach (var m in Dead) Originals.Remove(m);
        }

        static Material HullMaterial()
        {
            if (_hullMat != null) return _hullMat;
            _hullMat = new Material(MaterialCache.Unlit(ChipOutlineColor)) { name = "HC_Hull" };
            if (_hullMat.HasProperty("_Cull")) _hullMat.SetFloat("_Cull", 1f);   // URP：0 Off、1 Front、2 Back
            return _hullMat;
        }
    }

    /// <summary>外框跟著來源換網格（籌碼數量變了網格就換）；Extrude &gt; 0 時用外推後的網格（依來源網格快取）。</summary>
    public sealed class OutlineShell : MonoBehaviour
    {
        public MeshFilter Source;
        public float Extrude;
        MeshFilter _mf;
        Mesh _from;
        static readonly Dictionary<(Mesh, float), Mesh> Hulls = new Dictionary<(Mesh, float), Mesh>();

        void LateUpdate()
        {
            if (Source == null) return;
            if (_mf == null && !TryGetComponent(out _mf)) return;
            var m = Source.sharedMesh;
            if (m == _from) return;
            _from = m;
            _mf.sharedMesh = m == null || Extrude <= 0f ? m : Hull(m, Extrude);
        }

        /// <summary>頂點沿法線外推。同位置的頂點（硬邊拆開的）共用平均法線，外殼才不會裂開。</summary>
        public static Mesh Hull(Mesh src, float d)
        {
            if (Hulls.TryGetValue((src, d), out var cached) && cached != null) return cached;
            var v = src.vertices;
            var n = src.normals;
            var sum = new Dictionary<Vector3Int, Vector3>();
            Vector3Int Key(Vector3 p) => Vector3Int.RoundToInt(p * 10000f);
            for (int i = 0; i < v.Length; i++)
            {
                var k = Key(v[i]);
                sum.TryGetValue(k, out var acc);
                sum[k] = acc + (i < n.Length ? n[i] : Vector3.zero);
            }
            var outV = new Vector3[v.Length];
            for (int i = 0; i < v.Length; i++) outV[i] = v[i] + sum[Key(v[i])].normalized * d;
            var hull = new Mesh { name = src.name + "_hull", indexFormat = src.indexFormat };
            hull.vertices = outV;
            hull.normals = n;
            hull.subMeshCount = src.subMeshCount;
            for (int s = 0; s < src.subMeshCount; s++) hull.SetTriangles(src.GetTriangles(s), s);
            hull.RecalculateBounds();
            Hulls[(src, d)] = hull;
            return hull;
        }
    }

    /// <summary>
    /// 文字大小：籌碼金額標籤、示範手的字幕，以及每個角色頭上的字幕泡泡（SpeechChannel：每一句話都有字幕，和聲音同步）。
    /// 泡泡根節點的縮放被 SubtitleBubble 的彈出動畫每幀覆寫，所以縮放子物件。
    /// </summary>
    public static class SubtitleScaler
    {
        public static void Apply(GameController game, float scale)
        {
            if (game == null) return;
            if (game.BetLabel != null) game.BetLabel.Scale = scale;   // 看籌碼時的金額標籤也跟著字幕大小
            if (game.Ghost != null) game.Ghost.CaptionScale = scale;  // 示範手的字幕
            foreach (var (who, _) in game.GazeTargets())
            {
                var bubble = who != null ? who.Subtitle : null;
                if (bubble == null || bubble.Transform == null) continue;
                foreach (var name in new[] { "BubbleBg", "BubbleText" })
                {
                    var c = bubble.Transform.Find(name);
                    if (c != null) c.localScale = Vector3.one * scale;
                }
            }
        }
    }
}
