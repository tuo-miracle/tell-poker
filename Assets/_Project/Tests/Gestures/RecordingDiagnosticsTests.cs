using System.IO;
using NUnit.Framework;
using Poker.Gestures;

namespace Poker.Tests
{
    public class RecordingDiagnosticsTests
    {
        static HandRecording Rec(SyntheticHands s)
        {
            var r = new HandRecording();
            r.Frames.AddRange(s.Frames);
            return r;
        }

        [Test]
        public void Summary_lists_knock_strokes_with_their_verdicts()
        {
            var rec = Rec(GestureScripts.Knock(new SyntheticHands(), Hand.Right, GestureScripts.KnockSpot(Hand.Right)));
            string text = RecordingDiagnostics.Summarize(rec, "knock_test", G.Ctx());
            StringAssert.Contains("knock_test", text);
            StringAssert.Contains("KnockCheck", text);
            StringAssert.Contains("knock-palm Right", text);
            StringAssert.Contains("PAIR", text);
        }

        [Test]
        public void Summary_reports_per_hand_key_numbers()
        {
            var rec = Rec(GestureScripts.PickChips(new SyntheticHands(), Hand.Right, 0.05f));
            string text = RecordingDiagnostics.Summarize(rec, "chips_test", G.Ctx());
            StringAssert.Contains("Right:", text);
            StringAssert.Contains("pinch max 0.70", text);
            StringAssert.Contains("pinch→stack min", text);
            StringAssert.Contains("peek max", text);
        }

        [Test]
        public void Summarize_file_uses_the_expectation_from_the_file_name()
        {
            var dir = Path.Combine(Path.GetTempPath(), "poker-diag-" + System.Guid.NewGuid().ToString("N").Substring(0, 8));
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, "flip_01.hands");
            using (var fs = File.Create(path))
            using (var w = new HandRecordingWriter(fs, ""))
                foreach (var f in GestureScripts.Flip(new SyntheticHands()).Frames) w.Write(f);
            string text = RecordingDiagnostics.SummarizeFile(path);
            StringAssert.Contains("ShowdownFlip", text, "攤牌情境才會翻牌：要用檔名推出的牌局狀態回放");
            StringAssert.Contains("OK", text);
        }
    }
}
