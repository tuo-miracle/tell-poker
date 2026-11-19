using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;
using Poker.Voice;
using UnityEngine;

namespace Poker.Tests.Voice
{
    /// <summary>第 4 位對手「老手」Silas（2026-11-02）：語音對話也能叫他、問他的籌碼、他用自己的罐頭句與預錄片段回話。</summary>
    public class SilasVoiceTests
    {
        [Test] public void Silas_IsAVoiceCharacter()
        {
            Assert.AreEqual(VoiceCharacter.Silas, VoiceCast.Parse("Silas"));
            Assert.AreEqual("silas", VoiceCast.Id(VoiceCharacter.Silas));
            Assert.AreEqual("Silas", VoiceCast.DisplayName(VoiceCharacter.Silas));
            Assert.AreEqual(VoiceCharacter.Silas, VoiceCast.NameIn("Silas, are you bluffing?"));
            Assert.AreEqual(VoiceCharacter.Silas, VoiceCast.Resolve("silas", "hey", VoiceCharacter.Pip));
            Assert.AreEqual((int)VoiceCharacter.Dot + 1, (int)VoiceCharacter.Silas, "加在最後：舊的數值不變");
        }

        [Test] public void Silas_HasHisOwnFillerAndCannedLines()
        {
            var filler = VoiceLines.Filler(VoiceCharacter.Silas);
            Assert.IsNotEmpty(filler);
            foreach (var c in new[] { VoiceCharacter.Pip, VoiceCharacter.Rocco, VoiceCharacter.Vex, VoiceCharacter.Dot })
            {
                Assert.AreNotEqual(VoiceLines.Filler(c), filler, c.ToString());
                for (int n = 0; n < 2; n++) Assert.AreNotEqual(VoiceFallback.CannedLine(c, n), VoiceFallback.CannedLine(VoiceCharacter.Silas, n));
            }
            Assert.AreNotEqual(VoiceFallback.CannedLine(VoiceCharacter.Silas, 0), VoiceFallback.CannedLine(VoiceCharacter.Silas, 1));
        }

        [Test] public void Fallback_AnswersAboutSilas()
        {
            var s = States.Flop();
            s.Seats.Add(new PublicSeat { Name = "silas", Stack = 1200, Bet = 0, Status = "active" });
            Assert.AreEqual("Silas has 1200.", VoiceFallback.Answer(LocalIntent.Stack, s, VoiceCharacter.Dot, "how many chips does silas have"));
            Assert.AreEqual("I have 1200.", VoiceFallback.Answer(LocalIntent.Stack, s, VoiceCharacter.Silas, "how many chips do you have"));
            s.ToAct = "silas";
            Assert.AreEqual("It's Silas's turn.", VoiceFallback.Answer(LocalIntent.Turn, s, VoiceCharacter.Pip, "whose turn"));
            Assert.AreEqual("It's my turn.", VoiceFallback.Answer(LocalIntent.Turn, s, VoiceCharacter.Silas, "whose turn"));
        }

        [Test] public void SilasVoiceLines_AreInTheBakeInventory()
        {
            var all = Poker.Voice.SpeechInventory.All();
            foreach (var l in VoiceLines.Lines(VoiceCharacter.Silas))
                Assert.IsTrue(all.Any(x => x.Voice == "silas" && x.Text == l.Text), l.ToString());
            Assert.IsTrue(all.Any(x => x.Voice == "pip" && x.Text == "It's Silas's turn."), "其他角色也說得出 Silas 的名字");
            Assert.IsTrue(all.Any(x => x.Voice == "dot" && x.Text == "Silas has"));
        }

        /// <summary>中繼（tools/voice-proxy/src/addressee.ts）的角色名單和 Unity 的一致。</summary>
        [Test] public void ProxyCharacters_MatchVoiceCast()
        {
            var src = File.ReadAllText(Path.Combine(Application.dataPath, "../tools/voice-proxy/src/addressee.ts"));
            var list = Regex.Match(src, @"CHARACTERS = \[(.*?)\]").Groups[1].Value;
            var proxy = Regex.Matches(list, "\"(\\w+)\"").Cast<Match>().Select(m => m.Groups[1].Value).ToList();
            var unity = new[] { VoiceCharacter.Pip, VoiceCharacter.Rocco, VoiceCharacter.Vex, VoiceCharacter.Dot, VoiceCharacter.Silas }.Select(VoiceCast.Id).ToList();
            CollectionAssert.AreEquivalent(unity, proxy);
        }
    }
}
