using UnityEngine;

namespace Poker.Characters
{
    /// <summary>
    /// 每個寫實角色的設定（同一套 RealisticBody）：站姿身高、坐下後頭頂在桌面上方多高、坐姿、眼睛往上的上限。
    /// 眼睛往上的上限要看模型的眼皮：從玩家的位置看，超過就瞳孔躲進上眼皮（再高由頭往上抬）。
    /// </summary>
    public struct RealisticSpec
    {
        /// <summary>站姿身高（公尺，含帽子）。</summary>
        public float StandingHeight;
        /// <summary>坐下時頭頂（帽頂）在桌面上方幾公尺。</summary>
        public float HeadTop;
        /// <summary>骨盆在桌緣後方幾公尺。</summary>
        public float HipsBehind;
        /// <summary>上身前傾幾度。</summary>
        public float SpineLean;
        /// <summary>眼睛最多往上轉幾度（臉的座標）。</summary>
        public float EyeMaxUp;
        /// <summary>眼睛最多往下轉幾度（臉的座標）。</summary>
        public float EyeMaxDown;
        /// <summary>眼睛到往下上限時 eyeLookDown 開多少（這個鍵帶動眼皮的行程依模型而不同）。</summary>
        public float LookDownGain;
        /// <summary>往下看超過眼睛上限時，頭最多再低幾度補足（0 ＝不補）。</summary>
        public float HeadDownMax;
        /// <summary>頭（相對胸口）最多轉幾度、胸口最多轉幾度；0 ＝用 RealisticBody 的預設（35°、15°）。看鄰座時頭轉少，胸口多分擔。</summary>
        public float HeadYawLimit, ChestYawLimit;

        /// <summary>Hank：eyeLookDown 全開帶上眼皮 8.1 mm，眼睛往下到 21°、權重 0.8 正好跟上虹膜（6.5 對 5.9 mm）；頭最多再低 4°，低更多帽簷會把眼睛遮住。</summary>
        public static RealisticSpec Hank => new RealisticSpec { StandingHeight = 1.80f, HeadTop = 0.78f, HipsBehind = 0.18f, SpineLean = 12f, EyeMaxUp = EyeAim.MaxUp, EyeMaxDown = 21f, LookDownGain = 0.8f, HeadDownMax = 4f };
        /// <summary>荷官：1.65 m、沒帽子；從玩家位置目視，往上 10° 瞳孔約露出三分之二，12° 只剩約一半。
        /// eyeLookDown 全開約 7 mm；她靜止時眼皮就貼著虹膜上緣，0.8 就蓋住瞳孔一半（目視），0.65 剛好；頭最多再低 8°（低更多臉就看不到了）。</summary>
        public static RealisticSpec Dealer => new RealisticSpec { StandingHeight = 1.65f, HeadTop = 0.70f, HipsBehind = 0.18f, SpineLean = 12f, EyeMaxUp = 10f, EyeMaxDown = 26f, LookDownGain = 0.65f, HeadDownMax = 8f };

        // 新角色（2026-11-02）：身高照設定；頭頂高度 ≈ 0.52 × 身高 − 0.158（荷官 1.65 m → 0.70 m）；眼睛各自從玩家位置放大截圖調：
        // Marcus：靜止時上眼皮就壓在瞳孔上緣，往上 3.6° 瞳孔就碰到眼皮 → 上限 4°；往下權重 0.9 幾乎閉眼 → 0.55
        // Jace：往上到 8.8° 瞳孔都清楚；修補版（2026-11-02）上眼皮降低，往下 22° 用 0.85 會蓋到瞳孔中間 → 0.7
        // Reginald：半垂眼；往下 0.6 就跟上（再多就像閉眼）
        //   看鄰座：頭相對胸口超過約 22° 領口就撕出暗缺口（頸部蒙皮）→ 頭 22°、胸口 25°，臉的總朝向不變
        // Erik：往上到 9° 清楚；往下 0.8 在 21° 蓋住半個虹膜 → 0.7；修補版上眼皮降低，0.7 仍蓋到瞳孔上緣 → 0.6
        public static RealisticSpec Marcus => new RealisticSpec { StandingHeight = 1.75f, HeadTop = 0.752f, HipsBehind = 0.18f, SpineLean = 12f, EyeMaxUp = 4f, EyeMaxDown = 24f, LookDownGain = 0.55f, HeadDownMax = 6f };
        public static RealisticSpec Jace => new RealisticSpec { StandingHeight = 1.83f, HeadTop = 0.794f, HipsBehind = 0.18f, SpineLean = 12f, EyeMaxUp = 8f, EyeMaxDown = 22f, LookDownGain = 0.7f, HeadDownMax = 6f };
        public static RealisticSpec Reginald => new RealisticSpec { StandingHeight = 1.78f, HeadTop = 0.768f, HipsBehind = 0.18f, SpineLean = 12f, EyeMaxUp = 6f, EyeMaxDown = 20f, LookDownGain = 0.6f, HeadDownMax = 6f, HeadYawLimit = 22f, ChestYawLimit = 25f };
        public static RealisticSpec Erik => new RealisticSpec { StandingHeight = 1.83f, HeadTop = 0.794f, HipsBehind = 0.18f, SpineLean = 12f, EyeMaxUp = 8f, EyeMaxDown = 21f, LookDownGain = 0.6f, HeadDownMax = 6f };

        public static RealisticSpec For(CharacterKind kind) => kind == CharacterKind.Dealer ? Dealer : Hank;

        /// <summary>依模型名稱（GLB 檔名）找設定；不認得回 null。</summary>
        public static RealisticSpec? ByName(string name)
        {
            switch ((name ?? "").Trim().ToLowerInvariant())
            {
                case "hank": return Hank;
                case "dealer": return Dealer;
                case "marcus": return Marcus;
                case "jace": return Jace;
                case "reginald": return Reginald;
                case "erik": return Erik;
                default: return null;
            }
        }
    }
}
