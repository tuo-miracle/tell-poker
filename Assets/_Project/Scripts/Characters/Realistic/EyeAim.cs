using UnityEngine;

namespace Poker.Characters
{
    /// <summary>眼睛在頭裡能轉多少（寫實角色的眼骨頭）：超過就停在上限，眼球不會轉進眼皮裡。</summary>
    public static class EyeAim
    {
        /// <summary>往上只到 6°：v3 模型從玩家的位置看，超過約 8° 瞳孔就躲進上眼皮（再高由頭往上抬，見 RealisticBody）。</summary>
        public const float MaxYaw = 25f, MaxUp = 6f, MaxDown = 26f;

        /// <summary>目標方向（臉的座標：+Z 前、+X 角色的右、+Y 上）→ x = 往右幾度、y = 往下幾度（夾在上限內）。</summary>
        public static Vector2 Angles(Vector3 dirInFace) => Angles(dirInFace, MaxUp);

        /// <summary>同上，往上的上限依角色（見 RealisticSpec.EyeMaxUp）。</summary>
        public static Vector2 Angles(Vector3 dirInFace, float maxUp, float maxDown = MaxDown)
        {
            if (dirInFace.sqrMagnitude < 1e-10f) return Vector2.zero;
            var d = dirInFace.normalized;
            float yaw = Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg;
            float pitch = -Mathf.Atan2(d.y, new Vector2(d.x, d.z).magnitude) * Mathf.Rad2Deg;
            return new Vector2(Mathf.Clamp(yaw, -MaxYaw, MaxYaw), Mathf.Clamp(pitch, -maxUp, maxDown));
        }
    }
}
