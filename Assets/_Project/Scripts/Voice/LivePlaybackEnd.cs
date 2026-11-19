namespace Poker.Voice
{
    /// <summary>
    /// 串流回話的收尾判斷（純邏輯）。2026-10-13 實機：只看「起播後的牆上時間」會在 Android 起播延遲與中途斷音時切掉最後幾個字。
    /// 改成：全部樣本都交給音訊執行緒（Drained）後，再等輸出緩衝的延遲（OutputTailSeconds）才停；
    /// 音訊執行緒從沒讀過（沒有音訊裝置）時退回牆上時間；卡住時有安全上限。
    /// </summary>
    public sealed class LivePlaybackEnd
    {
        public enum Result { Continue, Played, NoReader, Timeout }
        /// <summary>最後一個樣本交給音訊執行緒到真正出聲的餘裕（Quest 輸出延遲約 0.1–0.3 s）。</summary>
        public const double OutputTailSeconds = 0.35;
        /// <summary>寫完後最多再等多久（音訊執行緒卡住）。</summary>
        public const double SafetySeconds = 4.0;
        const double NoReaderSeconds = 0.8;

        double _drainedAt = -1;

        public Result Tick(bool complete, bool drained, long played, double now, double startedAt, float duration)
        {
            if (!complete) return Result.Continue;
            double sinceStart = now - startedAt;
            if (drained)
            {
                if (_drainedAt < 0) _drainedAt = now;
                return now - _drainedAt >= OutputTailSeconds ? Result.Played : Result.Continue;
            }
            if (played == 0 && sinceStart >= duration + NoReaderSeconds) return Result.NoReader;
            if (sinceStart >= duration + SafetySeconds) return Result.Timeout;
            return Result.Continue;
        }
    }
}
