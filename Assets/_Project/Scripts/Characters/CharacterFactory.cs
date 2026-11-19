using UnityEngine;

namespace Poker.Characters
{
    /// <summary>建立角色：組骨架、掛 CharacterPerformer。</summary>
    public static class CharacterFactory
    {
        public static CharacterPerformer Create(CharacterKind kind, Transform parent, Vector3 localPos, Quaternion localRot, int seed = 0)
        {
            var spec = CharacterSpec.For(kind);
            var go = new GameObject(spec.DisplayName + " (" + kind + ")");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localRotation = localRot;
            var rig = CharacterBuilder.Build(go, spec);
            var p = go.AddComponent<CharacterPerformer>();
            p.Init(spec, rig, seed == 0 ? (int)kind * 7919 + 17 : seed);
            return p;
        }

        /// <summary>依 TableSeats 的預設座位建立（parent 是牌桌座標的根節點）。</summary>
        public static CharacterPerformer CreateAtSeat(CharacterKind kind, Transform tableRoot, int seed = 0)
        {
            var pose = TableSeats.For(kind);
            var p = Create(kind, tableRoot, pose.position, pose.rotation, seed);
            p.TableCenter = tableRoot != null ? tableRoot.TransformPoint(TableSeats.Center) : TableSeats.Center;
            return p;
        }
    }

    /// <summary>
    /// 預設座位（牌桌座標：原點在玩家側桌緣中央，+Y 上，+Z 指向牌桌中央，公尺）。真實的家用撲克桌（2026-11-02，
    /// docs/research/2026-11-02-real-poker-table-seating.md）：絨布是長圓形（兩端是半徑＝半個桌深的半圓），外面一圈 RailWidth 的扶手。
    /// 玩家在近端長邊中央、荷官在遠端長邊中央（扶手缺口）面對面；荷官左右（玩家看過去）是 Rocco 座與老手座，兩端半圓是 Pip 座與 Vex 座，左右對稱。
    /// 角色根節點在「角色面前的絨布邊」，臉朝牌桌：遠端直邊的人直接面向玩家側，半圓上的人面向自己那個半圓的圓心。
    /// 身體在根節點後方 BodyBehind（扶手外，荷官在缺口裡是 DealerBodyBehind）；相鄰兩人的身體至少隔一個真實座位寬（0.7 m），兩端的人在玩家視線約 ±55°。
    /// </summary>
    public static class TableSeats
    {
        /// <summary>絨布寬（X）與深（Z），公尺。預設＝家用 84×42 吋桌的絨布（TableLayout.FeltSize；TableSpots 依實際絨布設定）。</summary>
        public static Vector2 TableSize = new Vector2(1.89f, 0.83f);

        public static Vector3 Center => new Vector3(0, 0, TableSize.y * 0.5f);

        /// <summary>扶手寬（同 Poker.Runtime.TableDimensions.VirtualBorder）。</summary>
        public const float RailWidth = 0.12f;
        /// <summary>身體中心在根節點後方多遠：扶手外再 0.14 m（真人胸口貼著扶手外緣坐）。荷官坐在缺口裡，前面沒有扶手。</summary>
        public const float BodyBehind = 0.26f, DealerBodyBehind = 0.18f;
        /// <summary>
        /// 半圓上的座位角度（度，從遠端 +Z 量起往外轉；90° 是桌子最外端）：
        /// 兩端（Pip、Vex）80°、荷官兩側（Rocco、老手）16°。這樣荷官–荷官兩側–兩端的身體都隔 ≥ 0.7 m，兩端的身體在玩家視線 ±56° 內。
        /// </summary>
        public const float EndSeatDegrees = 80f, DealerSideDegrees = 16f;

        public static Pose For(CharacterKind kind)
        {
            switch (kind)
            {
                case CharacterKind.Rookie: return OnArc(-1, EndSeatDegrees);
                case CharacterKind.Loudmouth: return OnArc(-1, DealerSideDegrees);
                case CharacterKind.Veteran: return OnArc(1, DealerSideDegrees);
                case CharacterKind.Cold: return OnArc(1, EndSeatDegrees);
                default: return new Pose(new Vector3(0, 0, TableSize.y), Quaternion.LookRotation(Vector3.back, Vector3.up));
            }
        }

        /// <summary>身體中心（俯視，牌桌座標）。</summary>
        public static Vector3 BodyOf(CharacterKind kind) => BodyOf(kind, For(kind));
        public static Vector3 BodyOf(CharacterKind kind, Pose seat) => seat.position - seat.rotation * Vector3.forward * (kind == CharacterKind.Dealer ? DealerBodyBehind : BodyBehind);

        /// <summary>半圓上的座位：side −1 左端、+1 右端；面向那個半圓的圓心。</summary>
        static Pose OnArc(int side, float degrees)
        {
            float r = TableSize.y * 0.5f, cx = Mathf.Max(0f, TableSize.x * 0.5f - r), t = degrees * Mathf.Deg2Rad;
            var dir = new Vector3(side * Mathf.Sin(t), 0, Mathf.Cos(t));
            return new Pose(new Vector3(side * cx, 0, r) + dir * r, Quaternion.LookRotation(-dir, Vector3.up));
        }
    }
}
