using System;
using UnityEngine;

namespace Poker.Runtime
{
    /// <summary>Restored：從上次存的空間錨點放回原位（TableFrame.RestoredFrom 記得當初的來源）。</summary>
    public enum TablePlacementSource { None, MrukTable, EnvironmentRaycast, Virtual, HandCalibrated, Restored }

    /// <summary>
    /// 牌桌座標系：這個 Transform 的位置是原點（玩家側桌緣中央），forward 是 +Z（指向牌桌中央），up 是 +Y（桌面法線）。
    /// 牌桌座標 = 這個 Transform 的本地座標（不含縮放；縮放一律為 1）。+X 是玩家的右手邊。
    /// </summary>
    public sealed class TableFrame : MonoBehaviour
    {
        /// <summary>每次（重新）放置後觸發。</summary>
        public event Action<TableFrame> Placed;

        public bool IsPlaced { get; private set; }
        public TablePlacementSource Source { get; private set; }

        public Pose WorldPose => new Pose(transform.position, transform.rotation);
        /// <summary>Source 是 Restored（從空間錨點放回）時，當初是怎麼放的（雙手校準、MRUK…）；其他情況是 None。</summary>
        public TablePlacementSource RestoredFrom { get; private set; }

        /// <summary>真實房間的障礙物（牌桌座標；TablePlacer 從 MRUK 建立，牌桌每次移動都會重算）。沒有房間資料時是 null。</summary>
        public RoomObstacles Room { get; private set; }
        /// <summary>Room 換了（房間載入、牌桌移動後重算）。</summary>
        public event Action<TableFrame> RoomChanged;

        public void SetRoom(RoomObstacles room)
        {
            Room = room;
            RoomChanged?.Invoke(this);
        }

        public void Place(Pose originWorld, TablePlacementSource source) => Place(originWorld, source, TablePlacementSource.None);

        public void Place(Pose originWorld, TablePlacementSource source, TablePlacementSource restoredFrom)
        {
            RestoredFrom = source == TablePlacementSource.Restored ? restoredFrom : TablePlacementSource.None;
            transform.SetPositionAndRotation(originWorld.position, originWorld.rotation);
            transform.localScale = Vector3.one;
            IsPlaced = true;
            Source = source;
            Placed?.Invoke(this);
        }

        public Vector3 WorldToTable(Vector3 worldPosition) => Quaternion.Inverse(transform.rotation) * (worldPosition - transform.position);
        public Vector3 TableToWorld(Vector3 tablePosition) => transform.position + transform.rotation * tablePosition;
        public Vector3 WorldToTableDirection(Vector3 worldDirection) => Quaternion.Inverse(transform.rotation) * worldDirection;
        public Vector3 TableToWorldDirection(Vector3 tableDirection) => transform.rotation * tableDirection;
        public Quaternion WorldToTable(Quaternion worldRotation) => Quaternion.Inverse(transform.rotation) * worldRotation;
        public Quaternion TableToWorld(Quaternion tableRotation) => transform.rotation * tableRotation;
        public Pose WorldToTable(Pose world) => new Pose(WorldToTable(world.position), WorldToTable(world.rotation));
        public Pose TableToWorld(Pose table) => new Pose(TableToWorld(table.position), TableToWorld(table.rotation));
    }
}
