using Poker.Gestures;

namespace Poker.UX
{
    public static class HandednessResolver
    {
        /// <summary>慣用手：設定優先；自動時用系統的慣用手，查不到就當右手。</summary>
        public static Hand Dominant(Handedness setting, Hand? system)
            => setting == Handedness.Left ? Hand.Left
             : setting == Handedness.Right ? Hand.Right
             : system ?? Hand.Right;

        /// <summary>手腕按鈕放在非慣用手。</summary>
        public static Hand WristButtonHand(Handedness setting, Hand? system)
            => Dominant(setting, system) == Hand.Left ? Hand.Right : Hand.Left;
    }
}
