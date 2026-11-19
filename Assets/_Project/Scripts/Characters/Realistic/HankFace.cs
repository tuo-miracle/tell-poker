using Poker.AI;
using UnityEngine;

namespace Poker.Characters
{
    /// <summary>
    /// 把演出層的臉（FaceState）對應到 Hank（wrap 版，Rocketbox 臉）的 ARKit blendshape（0–1）。
    /// 幅度刻意做大：1 m 外要看得出來，而且八字鬍會蓋住嘴角、帽簷會擋住眉毛，所以表情主要靠臉頰、眼睛、下巴。
    /// 對應表（情緒 → 主要形狀鍵）寫在回報與各段註解；眼皮只吃情緒本身的 MoodLid，不吃卡通的個性眼皮與低頭。
    /// </summary>
    public static class HankFace
    {
        public static readonly string[] Shapes =
        {
            "browDownLeft", "browDownRight", "browOuterUpLeft", "browOuterUpRight", "browInnerUp",
            "eyeBlinkLeft", "eyeBlinkRight", "eyeSquintLeft", "eyeSquintRight", "eyeWideLeft", "eyeWideRight",
            "eyeLookDownLeft", "eyeLookDownRight", "eyeLookUpLeft", "eyeLookUpRight",
            "eyeLookInLeft", "eyeLookInRight", "eyeLookOutLeft", "eyeLookOutRight",
            "cheekSquintLeft", "cheekSquintRight", "cheekPuff", "noseSneerLeft", "noseSneerRight",
            "jawOpen", "mouthClose", "mouthFunnel", "mouthPucker", "mouthLowerDownLeft", "mouthLowerDownRight",
            "mouthSmileLeft", "mouthSmileRight", "mouthFrownLeft", "mouthFrownRight", "mouthDimpleLeft", "mouthDimpleRight",
            "mouthPressLeft", "mouthPressRight", "mouthRollLower", "mouthShrugLower", "mouthStretchLeft", "mouthStretchRight",
            "mouthUpperUpLeft", "mouthUpperUpRight", "mouthLeft", "mouthRight",
        };

        const int BrowDownL = 0, BrowDownR = 1, BrowOuterUpL = 2, BrowOuterUpR = 3, BrowInnerUp = 4,
            BlinkL = 5, BlinkR = 6, SquintL = 7, SquintR = 8, WideL = 9, WideR = 10,
            LookDownL = 11, LookDownR = 12, LookUpL = 13, LookUpR = 14, LookInL = 15, LookInR = 16, LookOutL = 17, LookOutR = 18,
            CheekSqL = 19, CheekSqR = 20, Puff = 21, SneerL = 22, SneerR = 23,
            Jaw = 24, Close = 25, Funnel = 26, Pucker = 27, LowerDownL = 28, LowerDownR = 29,
            SmileL = 30, SmileR = 31, FrownL = 32, FrownR = 33, DimpleL = 34, DimpleR = 35,
            PressL = 36, PressR = 37, RollLower = 38, ShrugLower = 39, StretchL = 40, StretchR = 41,
            UpperUpL = 42, UpperUpR = 43, MouthL = 44, MouthR = 45;

        /// <param name="w">長度 = Shapes.Length，寫入 0–1。</param>
        /// <param name="clamp">最後夾到 0–1（測試用 false 檢查係數有沒有疊爆）。</param>
        public static void Map(in FaceState f, float[] w, bool clamp = true, float maxUp = EyeAim.MaxUp, float maxDown = EyeAim.MaxDown, float lookDownGain = 0.55f)
        {
            System.Array.Clear(w, 0, w.Length);

            // ── 眉（帽簷會擋一部分，所以給滿一點）：左右分配同卡通，左 = raise − asym/2、右 = raise + asym/2
            float inner = f.BrowInner;
            float raiseL = f.BrowRaise - f.BrowAsym * 0.5f, raiseR = f.BrowRaise + f.BrowAsym * 0.5f;
            w[BrowInnerUp] = Mathf.Clamp01(inner * 0.9f + Mathf.Max(0, f.BrowRaise) * 0.4f);
            w[BrowDownL] = Mathf.Clamp01(-inner * 0.75f + Mathf.Max(0, -raiseL) * 0.8f);
            w[BrowDownR] = Mathf.Clamp01(-inner * 0.75f + Mathf.Max(0, -raiseR) * 0.8f);
            w[BrowOuterUpL] = Mathf.Clamp01(raiseL * 1.3f);
            w[BrowOuterUpR] = Mathf.Clamp01(raiseR * 1.3f);

            // ── 眼：眨眼要全閉（卡通曲線峰值不一定到 1）；瞇眼只看情緒；睜大給緊張、驚訝
            float blink = Mathf.Clamp01(f.Blink * 1.25f);
            float squint = Mathf.Clamp01(f.MoodLid * 1.6f + Mathf.Max(0, -inner) * 0.15f);
            float wide = Mathf.Clamp01(f.EyeWide * 3f) * (1f - squint) * (1f - blink);
            w[BlinkL] = w[BlinkR] = blink;
            w[SquintL] = w[SquintR] = squint;
            w[WideL] = w[WideR] = wide;
            // 眼皮跟著眼球走（眼球是骨頭轉；這些形狀鍵只帶眼皮，往下看時上眼皮跟著下來，不會露出眼白）
            float down = Mathf.Clamp01(f.EyePitch / maxDown), up = Mathf.Clamp01(-f.EyePitch / maxUp);
            float right = Mathf.Clamp01(f.EyeYaw / EyeAim.MaxYaw), left = Mathf.Clamp01(-f.EyeYaw / EyeAim.MaxYaw);
            // 形狀鍵相加：眨眼時往下看的量要退掉，不然眼皮會折過全閉
            w[LookDownL] = w[LookDownR] = down * lookDownGain * (1f - blink);
            w[LookUpL] = w[LookUpR] = up * 0.4f;
            // 往角色自己的右邊看：左眼往鼻子（In）、右眼往外（Out）
            w[LookInL] = w[LookOutR] = right * 0.35f;
            w[LookOutL] = w[LookInR] = left * 0.35f;

            // ── 皺鼻：生氣（內側眉下壓）
            w[SneerL] = w[SneerR] = Mathf.Clamp01(-inner * 0.6f);

            // ── 嘴：真的情緒做滿，待機嘴型（例如 Rocco 平常的 Smirk）只做 40%
            float k = f.Mood == Mood.Neutral ? 0.4f : Mathf.Lerp(0.4f, 1f, Mathf.Clamp01((f.MoodW - 0.3f) / 0.5f));
            float jawExpr = 0;
            switch (f.Mouth)
            {
                case MouthShape.SmallSmile:
                    w[SmileL] = w[SmileR] = 0.3f * k;
                    w[DimpleL] = w[DimpleR] = 0.1f * k;
                    break;
                case MouthShape.Smile:
                    w[SmileL] = w[SmileR] = 0.65f * k;
                    w[CheekSqL] = w[CheekSqR] = 0.35f * k;
                    w[DimpleL] = w[DimpleR] = 0.25f * k;
                    break;
                case MouthShape.Grin:
                {
                    // 開心＝笑到眼睛（臉頰上推）；虛張＝擔心的眉＋拉開的假笑，臉頰不太動
                    float forced = Mathf.Clamp01((inner - 0.2f) / 0.5f);
                    w[SmileL] = w[SmileR] = 1f * k * (1f - 0.3f * forced);
                    w[CheekSqL] = w[CheekSqR] = 0.9f * k * (1f - 0.7f * forced);
                    w[DimpleL] = w[DimpleR] = 0.4f * k;
                    w[UpperUpL] = w[UpperUpR] = 0.25f * k;
                    w[StretchL] = w[StretchR] = 0.45f * k * forced;
                    jawExpr = f.ExprOpen * 0.45f;   // 八字鬍會蓋住嘴角：靠露牙讓 1 m 外看得出在笑
                    break;
                }
                case MouthShape.Smirk:
                    // 自信冷笑：單邊（右）嘴角＋酒窩＋臉頰，另一邊抿著
                    w[SmileR] = 0.95f * k;
                    w[SmileL] = 0.05f * k;
                    w[DimpleR] = 0.6f * k;
                    w[CheekSqR] = 0.45f * k;
                    w[MouthR] = 0.25f * k;
                    w[PressL] = 0.25f * k;
                    break;
                case MouthShape.Frown:
                    w[FrownL] = w[FrownR] = 0.9f * k;
                    w[PressL] = w[PressR] = 0.45f * k;
                    w[ShrugLower] = 0.4f * k;
                    break;
                case MouthShape.Wavy:
                    // 緊張：抿嘴、下唇內捲、嘴角往兩邊拉
                    w[PressL] = w[PressR] = 0.65f * k;
                    w[RollLower] = 0.5f * k;
                    w[StretchL] = w[StretchR] = 0.35f * k;
                    w[FrownL] = w[FrownR] = 0.2f * k;
                    break;
                case MouthShape.Flat:
                    // 猶豫：抿一點、嘴歪向挑眉的另一邊
                    w[PressL] = w[PressR] = 0.25f * k;
                    if (f.BrowAsym < 0) w[MouthL] = 0.35f * k; else if (f.BrowAsym > 0) w[MouthR] = 0.35f * k;
                    break;
                case MouthShape.O:
                    w[Funnel] = 0.55f * k;
                    jawExpr = f.ExprOpen * 0.5f;
                    break;
            }
            w[Puff] = Mathf.Clamp01(f.Puff);

            // ── 講話：下巴開合＋下唇往下，圓唇用 funnel／pucker，m/b/p 閉唇；講話時抿嘴放鬆，嘴唇才張得開
            float j = Mathf.Clamp01(f.Jaw);
            float relax = 1f - 0.85f * j;
            w[PressL] *= relax; w[PressR] *= relax; w[RollLower] *= relax;
            w[Jaw] = Mathf.Max(jawExpr, j * 0.55f);
            w[LowerDownL] = w[LowerDownR] = j * 0.3f;
            w[Funnel] = Mathf.Max(w[Funnel], Mathf.Clamp01(f.Round) * 0.45f);
            w[Pucker] = Mathf.Clamp01(f.Round) * 0.15f;
            w[Close] = Mathf.Clamp01(f.Close) * 0.5f;

            if (clamp) for (int i = 0; i < w.Length; i++) w[i] = Mathf.Clamp01(w[i]);
        }
    }
}
