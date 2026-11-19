using UnityEngine;

namespace Poker.Runtime
{
    /// <summary>
    /// 牌桌外觀（執行期產生，放在 TableFrame 底下，所以本地座標就是牌桌座標）：
    /// 綠色絨布，以及 TableLayout 定義的區域標線（下注區與下注線、棄牌線、底池、籌碼堆、兩張底牌位）；放置來源是虛擬桌時多一個桌體與桌腳。
    /// 虛擬桌照牌室桌的樣子（docs/concept/2026-10-28/table-3d）：絨布改成受光＋細毛貼圖、皮革軟墊扶手＋米色飾條、深色桌邊（TableRail）。
    /// 2026-11-02 起是家用 84×42 吋的長圓桌（絨布長圓形、荷官缺口在遠端中央）。真實桌面上只畫絨布（同以前）：
    /// 虛擬扶手會從真實桌緣往玩家身上凸出 12 cm，所以不畫；比真實桌面大的部分絨布照樣畫出來（角色才有桌子可坐）。
    /// </summary>
    [RequireComponent(typeof(TableFrame))]
    public sealed class TableView : MonoBehaviour
    {
        static readonly Color Felt = new Color(0.05f, 0.33f, 0.17f);
        static readonly Color Marking = new Color(0.16f, 0.47f, 0.28f);   // 淡淡的標線
        static readonly Color BetLine = new Color(0.30f, 0.62f, 0.40f);
        static readonly Color Wood = new Color(0.24f, 0.16f, 0.11f);
        static readonly Color Leather = new Color(0.42f, 0.30f, 0.22f);
        static readonly Color Trim = new Color(0.85f, 0.78f, 0.62f);
        /// <summary>絨布貼圖一格的邊長（公尺）。</summary>
        const float FeltTile = 0.08f;

        TableFrame _frame;
        GameObject _virtualBody;
        MeshRenderer _felt;
        Material _feltFlat, _feltLit;

        void Awake()
        {
            _frame = GetComponent<TableFrame>();
            Build();
            _frame.Placed += OnPlaced;
            OnPlaced(_frame);
        }

        void OnDestroy()
        {
            if (_frame != null) _frame.Placed -= OnPlaced;
        }

        void OnPlaced(TableFrame frame)
        {
            if (_virtualBody == null) return;
            bool show = frame.Source == TablePlacementSource.Virtual;
            _virtualBody.SetActive(show);
            _felt.sharedMaterial = show ? FeltLit() : _feltFlat;
            if (show) FitLegs();
        }

        void Build()
        {
            var l = TableDimensions.Layout;
            float w = TableDimensions.Width, d = TableDimensions.Depth, lift = TableDimensions.FeltLift;
            var markLift = lift + 0.0006f;
            var zone = l.BetZone;

            _felt = Flat("Felt", FlatMeshes.Stadium(new Vector2(w, d)), new Vector3(0, lift, d * 0.5f), Felt).GetComponent<MeshRenderer>();
            _feltFlat = _felt.sharedMaterial;
            // 下注區：淡淡的外框＋一條較亮的下注線（越過就算下注）
            Flat("BetZone", FlatMeshes.RectOutline(new Vector2(zone.MaxX - zone.MinX, zone.MaxZ - zone.MinZ), 0.003f),
                new Vector3((zone.MinX + zone.MaxX) * 0.5f, markLift, (zone.MinZ + zone.MaxZ) * 0.5f), Marking);
            Flat("BetLine", FlatMeshes.Rect(new Vector2(zone.MaxX - zone.MinX, 0.006f)), new Vector3((zone.MinX + zone.MaxX) * 0.5f, markLift, l.BetLineZ), BetLine);
            // 棄牌線：只畫在牌前方一小段
            var cards = l.CardsRect;
            Flat("FoldLine", FlatMeshes.Rect(new Vector2(cards.MaxX - cards.MinX + 0.06f, 0.003f)), new Vector3(l.CardsCenter.X, markLift, l.FoldLineZ), Marking);
            Flat("PotArea", FlatMeshes.Ring(0.07f, 0.004f, 48), new Vector3(l.PotCenter.X, markLift, l.PotCenter.Z), Marking);
            Flat("ChipSpot", FlatMeshes.Ring(0.05f, 0.003f, 40), new Vector3(l.ChipStackCenter.X, markLift, l.ChipStackCenter.Z), Marking);
            var card = new Vector2(l.CardSize.X + 0.008f, l.CardSize.Y + 0.008f);
            Flat("CardSlot_L", FlatMeshes.RectOutline(card, 0.002f), new Vector3(l.CardLeftCenter.X, markLift, l.CardLeftCenter.Z), Marking);
            Flat("CardSlot_R", FlatMeshes.RectOutline(card, 0.002f), new Vector3(l.CardRightCenter.X, markLift, l.CardRightCenter.Z), Marking);

            _virtualBody = new GameObject("VirtualTableBody");
            _virtualBody.transform.SetParent(transform, false);
            var wood = RuntimeMaterials.Lit(Wood, 0.25f);
            // 桌面底面（絨布底下，朝下的長圓形）＋一圈桌緣（扶手、飾條、桌邊）；外框是絨布往外 VirtualBorder
            var top = new GameObject("Top");
            top.transform.SetParent(_virtualBody.transform, false);
            top.transform.SetLocalPositionAndRotation(new Vector3(0, -TableDimensions.VirtualTopThickness, d * 0.5f), Quaternion.Euler(0, 0, 180f));
            top.AddComponent<MeshFilter>().sharedMesh = FlatMeshes.Stadium(new Vector2(w, d));
            var topR = top.AddComponent<MeshRenderer>();
            topR.sharedMaterial = wood;
            topR.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            var rail = new GameObject("Rail");
            rail.transform.SetParent(_virtualBody.transform, false);
            rail.AddComponent<MeshFilter>().sharedMesh = TableRail.Build(new Vector2(w, d));
            var railR = rail.AddComponent<MeshRenderer>();
            var mats = new Material[TableRail.SubmeshCount];
            mats[TableRail.Leather] = RuntimeMaterials.Lit(Leather, 0.35f);
            mats[TableRail.Trim] = RuntimeMaterials.Lit(Trim, 0.3f);
            mats[TableRail.Sides] = wood;
            railR.sharedMaterials = mats;
            railR.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            for (int i = 0; i < 4; i++)
            {
                var leg = Box("Leg" + i, Vector3.zero, Vector3.one, wood);
                leg.transform.SetParent(_virtualBody.transform, false);
            }
            _virtualBody.SetActive(false);
        }

        /// <summary>桌腳從桌面下方伸到地板（世界 y = 0，FloorLevel 追蹤原點），至少 10 cm；放在兩端半圓的圓心前後（長圓桌底下，不在方桌的角）。</summary>
        void FitLegs()
        {
            float topBottom = -TableDimensions.VirtualTopThickness;
            float height = Mathf.Max(0.1f, transform.position.y + topBottom);
            float s = TableDimensions.VirtualLegSize;
            float r = TableDimensions.Depth * 0.5f;
            float x = Mathf.Max(0f, TableDimensions.Width * 0.5f - r), z0 = r * 0.4f, z1 = r * 1.6f;
            var spots = new[] { new Vector3(-x, 0, z0), new Vector3(x, 0, z0), new Vector3(-x, 0, z1), new Vector3(x, 0, z1) };
            for (int i = 0; i < 4; i++)
            {
                var leg = _virtualBody.transform.Find("Leg" + i);
                leg.localPosition = spots[i] + new Vector3(0, topBottom - height * 0.5f, 0);
                leg.localScale = new Vector3(s, height, s);
            }
        }

        Material FeltLit()
        {
            if (_feltLit == null)
                _feltLit = RuntimeMaterials.Lit(Felt, 0f, FeltTexture.Create(64, 7),
                    new Vector2(TableDimensions.Width / FeltTile, TableDimensions.Depth / FeltTile));
            return _feltLit;
        }

        GameObject Flat(string name, Mesh mesh, Vector3 localPos, Color color)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            go.transform.localPosition = localPos;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = RuntimeMaterials.Unlit(color);
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return go;
        }

        static GameObject Box(string name, Vector3 localPos, Vector3 size, Material material)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            if (go.TryGetComponent<Collider>(out var col)) Destroy(col);
            go.transform.localPosition = localPos;
            go.transform.localScale = size;
            var r = go.GetComponent<MeshRenderer>();
            r.sharedMaterial = material;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return go;
        }
    }
}
