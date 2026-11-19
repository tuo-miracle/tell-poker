using System.Numerics;

namespace Poker.Gestures
{
    /// <summary>XZ 平面上的軸對齊矩形（牌桌座標，公尺）。</summary>
    public struct TableRect
    {
        public float MinX, MaxX, MinZ, MaxZ;

        public TableRect(float minX, float maxX, float minZ, float maxZ)
        {
            MinX = minX; MaxX = maxX; MinZ = minZ; MaxZ = maxZ;
        }

        public bool Contains(Vector3 p, float margin = 0f) =>
            p.X >= MinX - margin && p.X <= MaxX + margin && p.Z >= MinZ - margin && p.Z <= MaxZ + margin;

        public Vector3 Center => new Vector3((MinX + MaxX) * 0.5f, 0f, (MinZ + MaxZ) * 0.5f);
    }

    /// <summary>
    /// 玩家座位附近的牌桌配置（牌桌座標：原點在玩家側桌緣中央、+Y 上、+Z 指向牌桌中央、公尺）。
    /// 預設值以「坐著、所有互動在 60 cm 內」設計（spec §3），實機上由 Table 層依真實擺放覆寫。
    /// </summary>
    public sealed class TableLayout
    {
        /// <summary>
        /// 絨布（遊戲區）大小：寬 X、深 Z，範圍 X ∈ [−X/2, X/2]、Z ∈ [0, Y]，長圓形（兩端是半徑 Y/2 的半圓，見 InFelt）。
        /// 家用標準撲克桌：外框 84×42 吋（2.13×1.07 m）減掉一圈 0.12 m 扶手（docs/research/2026-11-02-real-poker-table-seating.md）。
        /// </summary>
        public Vector2 FeltSize = new Vector2(1.89f, 0.83f);

        /// <summary>桌面高度。牌桌座標定義桌面就是 y = 0，保留欄位給校準偏移。</summary>
        public float TableY = 0f;

        /// <summary>兩張底牌的中心（並排、左右各一）。距桌緣 15 cm，坐著低頭就看得到。</summary>
        public Vector3 CardLeftCenter = new Vector3(-0.036f, 0f, 0.15f);
        public Vector3 CardRightCenter = new Vector3(0.036f, 0f, 0.15f);
        /// <summary>單張牌的大小（寬 X、長 Z）：標準撲克牌 63 × 88 mm。</summary>
        public Vector2 CardSize = new Vector2(0.063f, 0.088f);

        /// <summary>玩家籌碼堆中心（慣用手側、牌的右邊；和牌隔開，雙手放籌碼堆兩側時左手不會落在牌上）。</summary>
        public Vector3 ChipStackCenter = new Vector3(0.24f, 0f, 0.12f);

        /// <summary>下注線：z 超過這條線才算進了下注區。</summary>
        public float BetLineZ = 0.30f;
        /// <summary>下注區（含底池一帶；丟進底池也算下注，沿用 PokerStars VR 的做法）。</summary>
        public TableRect BetZone = new TableRect(-0.35f, 0.35f, 0.30f, 0.70f);

        /// <summary>
        /// 玩家自己的區域（牌、籌碼、手）：下注線以前、左右到下注區外 10 cm。別的座位的牌、籌碼不能放進來。
        /// （舊的 0.9 m 小桌就是整條近端；真實尺寸的長圓桌兩端的人離這裡很遠，他們靠玩家那側的籌碼不算擠到玩家。）
        /// </summary>
        public TableRect PlayerZone => new TableRect(BetZone.MinX - 0.1f, BetZone.MaxX + 0.1f, -1f, BetLineZ);

        /// <summary>棄牌線：把牌推到 z 超過這條線就是棄牌（在下注線前面，避免與下注混淆）。</summary>
        public float FoldLineZ = 0.27f;

        /// <summary>棄牌堆中心（與遊戲 TableSpots.Muck 相同：下注線後 6 cm、右側）。把牌拿起來棄牌時要放到這附近。</summary>
        public Vector3 MuckCenter = new Vector3(0.25f, 0f, 0.36f);

        /// <summary>底池中心。</summary>
        public Vector3 PotCenter = new Vector3(0f, 0f, 0.55f);

        /// <summary>兩張底牌合起來的矩形。</summary>
        public TableRect CardsRect
        {
            get
            {
                float hx = CardSize.X * 0.5f, hz = CardSize.Y * 0.5f;
                return new TableRect(
                    System.Math.Min(CardLeftCenter.X, CardRightCenter.X) - hx,
                    System.Math.Max(CardLeftCenter.X, CardRightCenter.X) + hx,
                    System.Math.Min(CardLeftCenter.Z, CardRightCenter.Z) - hz,
                    System.Math.Max(CardLeftCenter.Z, CardRightCenter.Z) + hz);
            }
        }

        public Vector3 CardsCenter => (CardLeftCenter + CardRightCenter) * 0.5f;

        /// <summary>絨布兩端半圓的半徑（＝半個桌深）。</summary>
        public float FeltEndRadius => FeltSize.Y * 0.5f;

        /// <summary>點（只看 XZ）在長圓形絨布上、而且離邊緣至少 margin。</summary>
        public bool InFelt(Vector3 p, float margin = 0f)
        {
            float r = FeltEndRadius, cx = System.Math.Max(0f, FeltSize.X * 0.5f - r);
            float dx = System.Math.Max(0f, System.Math.Abs(p.X) - cx), dz = p.Z - r;
            return dx * dx + dz * dz <= (r - margin) * (r - margin) && r - margin >= 0f;
        }

        /// <summary>籌碼畫在桌上的位置：落點夾進下注區（內縮 3 cm，遠端再留 12 cm 給底池），避免掉到桌外或自己的牌上。
        /// 遊戲（TableSpots.ClampToBetZone）與辨識器撈回判斷共用這一個。</summary>
        public Vector3 BetDisplayPoint(Vector3 p)
        {
            var z = BetZone;
            float maxZ = System.Math.Min(z.MaxZ, FeltSize.Y) - 0.12f;
            return new Vector3(Clamp(p.X, z.MinX + 0.03f, z.MaxX - 0.03f), TableY, Clamp(p.Z, z.MinZ + 0.03f, maxZ));
        }

        /// <summary>攤牌時往前放開、推出去亮的牌（兩張的中心）畫在哪：放開處正下方，夾在玩家前方的桌面上
        /// （牌前緣前 5 cm 到下注線、左右 ±(玩家區半寬−15 cm)），不壓到自己的牌位、公共牌與對手的區域。</summary>
        public Vector3 RevealDisplayPoint(Vector3 p)
        {
            float hx = PlayerZone.MaxX - 0.15f;
            return new Vector3(Clamp(p.X, -hx, hx), TableY, Clamp(p.Z, CardsRect.MaxZ + 0.05f, BetLineZ));
        }

        static float Clamp(float v, float lo, float hi) => v < lo ? lo : v > hi ? hi : v;
    }
}
