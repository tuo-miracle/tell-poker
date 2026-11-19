using UnityEngine;

namespace Poker.Characters
{
    /// <summary>用世界方向擺骨頭（Tripo 骨架的本地軸不規則，不寫死歐拉角）。</summary>
    public static class BoneAim
    {
        /// <summary>轉動 bone，讓「bone → 子骨頭」指向 dir（最小旋轉）。</summary>
        public static void Aim(Transform bone, Vector3 childWorld, Vector3 dir)
        {
            var cur = childWorld - bone.position;
            if (cur.sqrMagnitude < 1e-10f || dir.sqrMagnitude < 1e-10f) return;
            bone.rotation = Quaternion.FromToRotation(cur, dir) * bone.rotation;
        }

        /// <summary>兩段式 IK：upper→lower→end 讓 end 到 target，手肘彎向 pole；搆不到就伸直指向 target。</summary>
        public static void TwoBone(Transform upper, Transform lower, Transform end, Vector3 target, Vector3 pole)
        {
            var s = upper.position;
            float a = Vector3.Distance(s, lower.position), b = Vector3.Distance(lower.position, end.position);
            var d = target - s;
            float dist = Mathf.Clamp(d.magnitude, 1e-4f, a + b - 1e-5f);
            var dir = d.sqrMagnitude > 1e-10f ? d.normalized : Vector3.down;
            float cosA = Mathf.Clamp((a * a + dist * dist - b * b) / (2f * a * dist), -1f, 1f);
            float sinA = Mathf.Sqrt(1f - cosA * cosA);
            var bend = Vector3.ProjectOnPlane(pole - s, dir);
            if (bend.sqrMagnitude < 1e-8f) bend = Vector3.ProjectOnPlane(Vector3.down, dir);
            if (bend.sqrMagnitude < 1e-8f) bend = Vector3.ProjectOnPlane(Vector3.forward, dir);
            bend.Normalize();
            var elbow = s + dir * (cosA * a) + bend * (sinA * a);
            Aim(upper, lower.position, elbow - s);
            Aim(lower, end.position, s + dir * dist - lower.position);
        }
    }
}
