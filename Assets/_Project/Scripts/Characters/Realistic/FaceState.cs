using Poker.AI;

namespace Poker.Characters
{
    /// <summary>演出層這一幀算好的臉（眉、眼皮、眨眼、嘴型）；寫實角色拿它對應到 blendshape。</summary>
    public struct FaceState
    {
        public float BrowInner, BrowRaise, BrowAsym;
        /// <summary>卡通的上眼皮蓋住比例（含個性基準與低頭，不含眨眼）；寫實角色不用（會像在打瞌睡），改用 MoodLid。</summary>
        public float Lid;
        /// <summary>眨眼 0–1。</summary>
        public float Blink;
        public float MouthOpen, Puff;
        public MouthShape Mouth;

        /// <summary>主導情緒與它的權重（0–1）：寫實角色依此決定嘴型要做多滿（待機嘴型比真的表情淡）。</summary>
        public Mood Mood;
        public float MoodW;
        /// <summary>情緒本身的眼皮（瞇眼／瞪人），不含個性基準與低頭：寫實角色預設眼睛張開。</summary>
        public float MoodLid;
        /// <summary>睜大眼睛（Expression.EyeScale，0 = 不變）。</summary>
        public float EyeWide;
        /// <summary>情緒本身的張嘴（大笑、驚訝），不含講話。</summary>
        public float ExprOpen;

        // 以下由寫實角色自己填（演出層不管）
        /// <summary>講話的開口 0–1、圓唇 0–1、雙唇閉合（m/b/p）0–1。</summary>
        public float Jaw, Round, Close;
        /// <summary>眼睛在頭裡的角度（度）：x = 往右、y = 往下（見 EyeAim）。</summary>
        public float EyeYaw, EyePitch;

        /// <summary>某個情緒全開時的臉（固定表情、截圖用）。</summary>
        public static FaceState FromMood(Mood m)
        {
            var e = Expression.For(m);
            bool open = e.Mouth == MouthShape.Grin || e.Mouth == MouthShape.O;
            return new FaceState
            {
                BrowInner = e.BrowInner, BrowRaise = e.BrowRaise, BrowAsym = e.BrowAsym, Lid = e.Lid, MoodLid = e.Lid, EyeWide = e.EyeScale,
                Mouth = e.Mouth, ExprOpen = open ? e.MouthOpen : 0, MouthOpen = open ? e.MouthOpen : 0, Mood = m, MoodW = 1f,
            };
        }
    }
}
