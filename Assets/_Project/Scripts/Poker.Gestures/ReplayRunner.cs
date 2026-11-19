using System;
using System.Collections.Generic;

namespace Poker.Gestures
{
    /// <summary>把錄製或合成的手部資料逐幀餵給新的 GestureRecognizer，收集全部事件（回歸測試與調參用）。</summary>
    public static class ReplayRunner
    {
        public static List<GestureEvent> Run(HandRecording recording, Func<HandsFrame, TableContext> context,
            TableLayout layout = null, GestureTuning tuning = null) =>
            Run(recording.Frames, context, layout, tuning);

        public static List<GestureEvent> Run(IEnumerable<HandsFrame> frames, Func<HandsFrame, TableContext> context,
            TableLayout layout = null, GestureTuning tuning = null)
        {
            var r = new GestureRecognizer(layout, tuning);
            var all = new List<GestureEvent>();
            foreach (var f in frames) all.AddRange(r.Update(f, context(f)));
            return all;
        }
    }
}
