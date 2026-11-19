using UnityEngine;
using NVector3 = System.Numerics.Vector3;
using NQuaternion = System.Numerics.Quaternion;

namespace Poker.Runtime
{
    /// <summary>Unity ↔ System.Numerics 轉換。兩邊分量一一對應（同為左手座標的數值，不做軸翻轉）。</summary>
    public static class NumericsExtensions
    {
        public static NVector3 ToNumerics(this Vector3 v) => new NVector3(v.x, v.y, v.z);
        public static Vector3 ToUnity(this NVector3 v) => new Vector3(v.X, v.Y, v.Z);
        public static NQuaternion ToNumerics(this Quaternion q) => new NQuaternion(q.x, q.y, q.z, q.w);
        public static Quaternion ToUnity(this NQuaternion q) => new Quaternion(q.X, q.Y, q.Z, q.W);
    }
}
