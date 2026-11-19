using NUnit.Framework;
using Poker.Voice;

namespace Poker.Tests.Voice
{
    /// <summary>串流回話什麼時候算講完（2026-10-13 實機：角色話沒講完就被切掉）。</summary>
    public class LivePlaybackEndTests
    {
        const float Dur = 2f;

        [Test] public void KeepsPlayingWhileAudioIsStillBufferedEvenPastTheOldWallClockLimit()
        {
            var e = new LivePlaybackEnd();
            // 起播晚、中途斷音：牆上時間已經超過長度 1 s，但還有樣本沒交給音訊執行緒
            Assert.AreEqual(LivePlaybackEnd.Result.Continue, e.Tick(complete: true, drained: false, played: 30000, now: 10 + Dur + 1.0, startedAt: 10, duration: Dur));
        }

        [Test] public void StopsOnlyAfterTheOutputTailOnceEverythingWasHandedToTheAudioThread()
        {
            var e = new LivePlaybackEnd();
            Assert.AreEqual(LivePlaybackEnd.Result.Continue, e.Tick(true, true, 44100, 13.0, 10, Dur), "just drained: last samples still in the output buffer");
            Assert.AreEqual(LivePlaybackEnd.Result.Continue, e.Tick(true, true, 44100, 13.0 + LivePlaybackEnd.OutputTailSeconds * 0.5, 10, Dur));
            Assert.AreEqual(LivePlaybackEnd.Result.Played, e.Tick(true, true, 44100, 13.0 + LivePlaybackEnd.OutputTailSeconds + 0.01, 10, Dur));
        }

        [Test] public void NotCompleteNeverStops()
        {
            var e = new LivePlaybackEnd();
            Assert.AreEqual(LivePlaybackEnd.Result.Continue, e.Tick(false, false, 1000, 10 + Dur + 10, 10, Dur));
        }

        [Test] public void WithoutAnAudioThreadReadingFallsBackToTheWallClock()
        {
            var e = new LivePlaybackEnd();
            Assert.AreEqual(LivePlaybackEnd.Result.Continue, e.Tick(true, false, 0, 10 + Dur + 0.5, 10, Dur));
            Assert.AreEqual(LivePlaybackEnd.Result.NoReader, e.Tick(true, false, 0, 10 + Dur + 0.85, 10, Dur));
        }

        [Test] public void SafetyTimeoutWhenTheAudioThreadStalls()
        {
            var e = new LivePlaybackEnd();
            Assert.AreEqual(LivePlaybackEnd.Result.Timeout, e.Tick(true, false, 500, 10 + Dur + LivePlaybackEnd.SafetySeconds + 0.01, 10, Dur));
        }
    }
}
