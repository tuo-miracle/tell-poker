using Poker.AI;

namespace Poker.Characters
{
    public enum MouthShape { SmallSmile, Smile, Grin, Flat, Frown, Wavy, Smirk, O }

    public enum HeadSymbol { None, Exclaim, Question, Anger, Sweat }

    /// <summary>
    /// 一個情緒的臉與身體參數。多個情緒依權重混合（連續值線性混合，嘴型與符號取權重最大者）。
    /// 角度單位是度；Lean 正值往前；HeadPitch 正值低頭。
    /// </summary>
    public struct Expression
    {
        public float BrowInner;  // + 內側上揚（擔心）、− 內側下壓（生氣）
        public float BrowRaise;  // 整條眉毛上下
        public float BrowAsym;   // 單邊挑眉
        public float Lid;        // 上眼皮蓋住比例
        public float EyeScale;   // 眼睛大小倍率增量（0 = 不變）
        public float PupilScale; // 瞳孔大小增量
        public float MouthOpen;
        public float Blush, Sweat, Tremor;
        public float Lean, HeadPitch, HeadRoll;
        public float Shrug;      // 縮肩（頭往下沉）
        public float EyeDart;    // 眼睛亂飄
        public float Bounce;     // 開心時的彈跳
        public float GazeUp;     // + 往上看（思考）、− 往下看（沮喪）
        public float GazeSide;   // 心虛地斜眼瞄旁邊（虛張）
        public float Breath;     // 呼吸頻率增量（Hz）
        public float HandsTogether; // 雙手靠攏搓手
        public MouthShape Mouth;
        public HeadSymbol Symbol;

        public void AddScaled(in Expression o, float w)
        {
            BrowInner += o.BrowInner * w; BrowRaise += o.BrowRaise * w; BrowAsym += o.BrowAsym * w;
            Lid += o.Lid * w; EyeScale += o.EyeScale * w; PupilScale += o.PupilScale * w; MouthOpen += o.MouthOpen * w;
            Blush += o.Blush * w; Sweat += o.Sweat * w; Tremor += o.Tremor * w;
            Lean += o.Lean * w; HeadPitch += o.HeadPitch * w; HeadRoll += o.HeadRoll * w;
            Shrug += o.Shrug * w; EyeDart += o.EyeDart * w; Bounce += o.Bounce * w; GazeUp += o.GazeUp * w; GazeSide += o.GazeSide * w;
            Breath += o.Breath * w; HandsTogether += o.HandsTogether * w;
        }

        public static Expression For(Mood m)
        {
            switch (m)
            {
                case Mood.Nervous:
                    return new Expression { BrowInner = 1f, BrowRaise = 0.35f, EyeScale = 0.14f, PupilScale = -0.25f, Mouth = MouthShape.Wavy,
                        Blush = 0.5f, Sweat = 0.85f, Tremor = 0.8f, Lean = -2f, HeadPitch = 5f, Shrug = 0.8f, EyeDart = 0.9f,
                        Breath = 0.35f, HandsTogether = 1f, Symbol = HeadSymbol.Sweat };
                case Mood.Confident:
                    return new Expression { BrowInner = -0.25f, BrowAsym = 0.8f, Lid = 0.4f, Mouth = MouthShape.Smirk,
                        Lean = -6f, HeadPitch = -7f, HeadRoll = 5f, Breath = -0.05f };
                case Mood.Bluffing:
                    return new Expression { BrowRaise = 0.55f, BrowInner = 0.7f, BrowAsym = 0.4f, EyeScale = 0.1f, PupilScale = -0.2f, Mouth = MouthShape.Grin, MouthOpen = 0.3f,
                        Sweat = 0.55f, Tremor = 0.3f, EyeDart = 0.2f, GazeSide = 1f, Blush = 0.35f, Lean = 2f, HeadRoll = -5f, Breath = 0.2f };
                case Mood.Hesitant:
                    return new Expression { BrowAsym = -0.9f, BrowInner = 0.45f, Mouth = MouthShape.Flat, HeadRoll = 12f, HeadPitch = -2f,
                        EyeDart = 0.15f, GazeUp = 1f, Symbol = HeadSymbol.Question };
                case Mood.Startled:
                    return new Expression { BrowRaise = 1.2f, BrowInner = 0.3f, EyeScale = 0.35f, PupilScale = -0.45f, Mouth = MouthShape.O, MouthOpen = 1f,
                        Tremor = 0.6f, Shrug = 1f, Breath = 0.5f, Symbol = HeadSymbol.Exclaim };
                case Mood.Annoyed:
                    return new Expression { BrowInner = -1.2f, BrowRaise = -0.35f, Lid = 0.3f, Mouth = MouthShape.Frown, HeadPitch = 6f, Lean = 3f,
                        Blush = 0.45f, Symbol = HeadSymbol.Anger };
                case Mood.Happy:
                    return new Expression { BrowRaise = 0.4f, BrowInner = 0.15f, Lid = 0.2f, Mouth = MouthShape.Grin, MouthOpen = 0.75f, Blush = 0.6f,
                        Bounce = 1f, HeadRoll = 7f, Lean = -2f, HeadPitch = -4f };
                case Mood.Dejected:
                    return new Expression { BrowInner = 0.9f, BrowRaise = -0.3f, Lid = 0.3f, Mouth = MouthShape.Frown, HeadPitch = 9f, Lean = 6f,
                        Shrug = -0.5f, GazeUp = -1f, Breath = -0.1f };
                default:
                    return new Expression { Mouth = MouthShape.SmallSmile };
            }
        }
    }
}
