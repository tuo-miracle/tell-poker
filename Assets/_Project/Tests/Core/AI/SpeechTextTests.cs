using System.Linq;
using NUnit.Framework;
using Poker.AI;

namespace Poker.Tests.Core.AI
{
    /// <summary>
    /// 台詞文字的拆解（純邏輯）：有數字的句子拆成「預錄片段＋數字的英文字」，播放時把預錄音檔接起來，字幕照原句顯示。
    /// </summary>
    public class SpeechTextTests
    {
        [TestCase(0, "zero")]
        [TestCase(7, "seven")]
        [TestCase(13, "thirteen")]
        [TestCase(40, "forty")]
        [TestCase(75, "seventy five")]
        [TestCase(100, "one hundred")]
        [TestCase(340, "three hundred forty")]
        [TestCase(1000, "one thousand")]
        [TestCase(2015, "two thousand fifteen")]
        [TestCase(12500, "twelve thousand five hundred")]
        [TestCase(1000000, "one million")]
        public void NumberWords_AmericanStyle_WithoutAnd(int n, string expected)
        {
            Assert.AreEqual(expected, string.Join(" ", SpeechText.NumberWords(n)));
        }

        [Test]
        public void NumberWords_OnlyUseTheBakedWordList()
        {
            for (int n = 0; n <= 25000; n += 7)
                foreach (var w in SpeechText.NumberWords(n))
                    CollectionAssert.Contains(SpeechText.AllNumberWords, w, n.ToString());
            CollectionAssert.Contains(SpeechText.AllNumberWords, "percent");
        }

        [Test]
        public void Pieces_SplitPhrasesAndNumbers()
        {
            CollectionAssert.AreEqual(new[] { "The pot is", "three", "hundred", "forty" }, SpeechText.Pieces("The pot is 340.").ToArray());
            CollectionAssert.AreEqual(new[] { "Blinds are", "five", "and", "ten" }, SpeechText.Pieces("Blinds are 5 and 10.").ToArray());
            CollectionAssert.AreEqual(new[] { "Welcome back!", "two", "wins in", "four", ", tells read", "seventy", "five", "percent" },
                SpeechText.Pieces("Welcome back! 2 wins in 4, tells read 75%.").ToArray());
            CollectionAssert.AreEqual(new[] { "Nothing to call. You can check." }, SpeechText.Pieces("Nothing to call. You can check.").ToArray());
            CollectionAssert.AreEqual(new[] { "You have", "one", "thousand", "two", "hundred" }, SpeechText.Pieces("You have 1,200.").ToArray());
        }

        [Test]
        public void Key_IgnoresCaseSpacesAndPunctuation_ButKeepsApostrophes()
        {
            Assert.AreEqual("tells read", SpeechText.Key(", tells read"));
            Assert.AreEqual("welcome back", SpeechText.Key("Welcome back!"));
            Assert.AreEqual("it's my turn", SpeechText.Key("It's my turn."));
            Assert.AreEqual("", SpeechText.Key(" . "));
        }

        [Test]
        public void HasNumber()
        {
            Assert.IsTrue(SpeechText.HasNumber("The pot is 60."));
            Assert.IsFalse(SpeechText.HasNumber("Pot goes to Rocco!"));
        }

        [Test]
        public void LineBook_EveryLineHasAVoice()
        {
            foreach (var l in LineBook.All)
                CollectionAssert.Contains(SpeechText.Voices, LineBook.VoiceOf(l), l.Id);
            Assert.AreEqual("rocco", LineBook.VoiceOf(LineBook.All.First(l => l.Id.StartsWith("rocco."))));
        }
    }
}
