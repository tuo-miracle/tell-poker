using System;
using System.Reflection;
using Poker.Gestures;

namespace Poker.UX
{
    /// <summary>
    /// 手勢靈敏度：在 Gestures 外部建立「套用倍率後的 GestureTuning」。倍率 s &gt; 1 時主要觸發門檻除以 s（較容易觸發）。
    /// 只縮放觸發門檻（速度、距離），不動捏合強度、時間窗與角度，避免改變手勢的判斷結構。
    /// </summary>
    public static class TuningScaler
    {
        /// <summary>被縮放的欄位。成對的門檻（峰值／結束、最小／滿力、佔用／完成）一起縮放，維持大小順序。</summary>
        public static readonly string[] ScaledFields =
        {
            nameof(GestureTuning.KnockPeakSpeed), nameof(GestureTuning.KnockStrokeEndSpeed), nameof(GestureTuning.KnockMinDrop),
            nameof(GestureTuning.FoldPushMinDistance), nameof(GestureTuning.FoldPushClaimDistance),
            nameof(GestureTuning.AllInStartDistance),
            nameof(GestureTuning.ChipStepHeight), nameof(GestureTuning.ChipStepHysteresis),
        };

        static readonly FieldInfo[] AllFields = typeof(GestureTuning).GetFields(BindingFlags.Public | BindingFlags.Instance);

        /// <summary>回傳新實例；source 不變。</summary>
        public static GestureTuning Apply(GestureTuning source, float sensitivity)
        {
            var t = new GestureTuning();
            ApplyInto(source, sensitivity, t);
            return t;
        }

        /// <summary>把 source 套用倍率後寫進 target（辨識器已持有的實例，改了下一幀就生效）。</summary>
        public static void ApplyInto(GestureTuning source, float sensitivity, GestureTuning target)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            if (target == null) throw new ArgumentNullException(nameof(target));
            float s = new UxSettings { GestureSensitivity = sensitivity }.Sanitize().GestureSensitivity;
            foreach (var f in AllFields) f.SetValue(target, f.GetValue(source));
            foreach (var name in ScaledFields)
            {
                var f = typeof(GestureTuning).GetField(name);
                f.SetValue(target, (float)f.GetValue(source) / s);
            }
        }

        public static GestureTuning Clone(GestureTuning source) => Apply(source, 1f);
    }
}
