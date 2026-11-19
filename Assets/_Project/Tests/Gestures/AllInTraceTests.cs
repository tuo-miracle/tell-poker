using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using NUnit.Framework;
using Poker.Gestures;

namespace Poker.Tests
{
    /// <summary>實機上 All-in「根本沒進入準備」時，診斷輸出要說出卡在哪個條件（使用者回報 All-in 很不靈敏）。</summary>
    public class AllInTraceTests
    {
        [Test]
        public void Explains_why_both_low_hands_did_not_arm_all_in()
        {
            var s = new SyntheticHands();
            // 雙手貼近桌面，但左手已經在下注線外（2026-10-12 起不要求夾住籌碼堆，原本「左手離籌碼堆太遠」的姿勢
            // 現在符合準備條件；改用仍不符合的姿勢，檢查的仍是「說出卡在哪隻手、哪個條件」）
            s.Set(Hand.Left, new Vector3(-0.10f, 0.03f, 0.34f));
            s.Set(Hand.Right, new Vector3(0.20f, 0.03f, 0.10f));
            s.Wait(0.6);
            var r = new GestureRecognizer(G.Layout);
            var trace = new List<string>();
            r.Trace = trace.Add;
            foreach (var f in s.Frames) r.Update(f, G.Ctx());
            var line = trace.FirstOrDefault(t => t.Contains("all-in not armed"));
            Assert.That(line, Is.Not.Null, string.Join("\n", trace));
            Assert.That(line, Does.Contain("left"));
        }

        [Test]
        public void Does_not_spam_the_same_reason_every_frame()
        {
            var s = new SyntheticHands();
            s.Set(Hand.Left, new Vector3(-0.10f, 0.03f, 0.34f));
            s.Set(Hand.Right, new Vector3(0.20f, 0.03f, 0.10f));
            s.Wait(2.0);
            var r = new GestureRecognizer(G.Layout);
            var trace = new List<string>();
            r.Trace = trace.Add;
            foreach (var f in s.Frames) r.Update(f, G.Ctx());
            int n = trace.Count(t => t.Contains("all-in not armed"));
            Assert.That(n, Is.GreaterThanOrEqualTo(1), "姿勢要真的不符合，這個測試才有意義");
            Assert.That(n, Is.LessThanOrEqualTo(2));
        }
    }
}
