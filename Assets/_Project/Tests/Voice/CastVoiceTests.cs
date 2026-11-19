using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;
using Poker.AI;
using Poker.Characters;
using Poker.Voice;
using UnityEngine;
using UnityEngine.TestTools;

namespace Poker.Tests.Voice
{
    /// <summary>
    /// 寫實角色自己的聲音（2026-11-02，使用者回報「男角女聲、女荷官男聲」）：台詞依座位個性，聲音依角色。
    /// 預錄音檔用「聲音鍵＋原文」找；寫實角色的聲音鍵見 SpeechVoices（Hank＝rocco，直接用 Rocco 座位的現有音檔）。
    /// </summary>
    public class CastVoiceTests
    {
        static readonly CharacterKind[] Seats = { CharacterKind.Rookie, CharacterKind.Loudmouth, CharacterKind.Cold };

        [Test] public void EachRealisticCharacterHasItsOwnVoice_HankKeepsCallum()
        {
            Assert.AreEqual("marcus", SpeechVoices.ForCharacter("Marcus", CharacterKind.Rookie));
            Assert.AreEqual("jace", SpeechVoices.ForCharacter("jace", CharacterKind.Cold), "名字不分大小寫");
            Assert.AreEqual("silas", SpeechVoices.ForCharacter("Reginald", CharacterKind.Loudmouth), "Reginald＝Bill＝老手座位 Silas 的聲音（坐老手座位時不用重錄）");
            Assert.AreEqual("erik", SpeechVoices.ForCharacter("Erik", CharacterKind.Rookie));
            Assert.AreEqual("rocco", SpeechVoices.ForCharacter("Hank", CharacterKind.Loudmouth), "Hank 沿用 Callum＝rocco 的音檔");
            Assert.AreEqual("rocco", SpeechVoices.ForCharacter("Hank", CharacterKind.Rookie), "Hank 坐別的座位也是他自己的聲音");
        }

        [Test] public void UnknownOrMissingCharacter_KeepsTheSeatVoice()
        {
            Assert.AreEqual("vex", SpeechVoices.ForCharacter("Nobody", CharacterKind.Cold));
            Assert.AreEqual("pip", SpeechVoices.ForCharacter(null, CharacterKind.Rookie));
            Assert.AreEqual("dot", SpeechVoices.ForCharacter("DealerWoman", CharacterKind.Dealer));
            Assert.AreEqual("rocco", SpeechVoices.ForSeat(CharacterKind.Loudmouth));
            Assert.AreEqual("silas", SpeechVoices.ForSeat(CharacterKind.Veteran), "老手座位（卡通 Silas）");
            Assert.AreEqual("silas", SpeechVoices.ForCharacter(null, CharacterKind.Veteran));
            Assert.AreEqual("silas", SpeechVoices.ForCharacter("Reginald", CharacterKind.Veteran));
            Assert.AreEqual(SpeechVoices.ForSeat(CharacterKind.Rookie), SpeechChannel.VoiceFor(CharacterKind.Rookie));
        }

        [Test] public void CastVoices_AreDistinct_AndNotSeatVoices()
        {
            CollectionAssert.AllItemsAreUnique(SpeechVoices.Cast);
            foreach (var v in SpeechVoices.Cast)
            {
                CollectionAssert.DoesNotContain(SpeechText.Voices, v);
                Assert.IsTrue(SpeechVoices.IsKnown(v), v);
            }
            foreach (var v in SpeechText.Voices) Assert.IsTrue(SpeechVoices.IsKnown(v), v);
            Assert.IsFalse(SpeechVoices.IsKnown("hank"), "Hank 沒有自己的聲音鍵（用 rocco）");
            Assert.IsFalse(SpeechVoices.IsKnown("reginald"), "Reginald 沒有自己的聲音鍵（用 silas）");
            CollectionAssert.Contains(SpeechText.Voices, "silas", "老手座位的聲音（數字字也要錄）");
        }

        /// <summary>中繼（tools/voice-proxy/src/elevenlabs.ts）認得每個寫實角色的聲音鍵（bake 與即時回話都靠它選 ElevenLabs 聲音）。</summary>
        [Test] public void ProxyKnowsEveryCastVoice()
        {
            var src = System.IO.File.ReadAllText(System.IO.Path.Combine(Application.dataPath, "../tools/voice-proxy/src/elevenlabs.ts"));
            var block = Regex.Match(src, @"CAST_VOICES = \{(.*?)\}", RegexOptions.Singleline).Groups[1].Value;
            var keys = Regex.Matches(block, @"^\s*(\w+):", RegexOptions.Multiline).Cast<Match>().Select(m => m.Groups[1].Value).ToList();
            CollectionAssert.AreEquivalent(SpeechVoices.Cast, keys);
        }

        [Test] public void CastLines_ReuseTheSeatLinesWithTheCharacterVoice()
        {
            var seat = new List<SpeechLine>
            {
                new SpeechLine("pip.value.01", "pip", "Oh! Um, I'll... put some in."),
                new SpeechLine("pip.num.three", "pip", "three"),
                new SpeechLine("rocco.value.01", "rocco", "Pay up."),
                new SpeechLine("vex.fold.01", "vex", "Fold."),
                new SpeechLine("dot.yourturn.01", "dot", "Action is on you."),
            };
            var cast = SpeechInventory.CastLines(new[] { "Marcus", "Hank", "Jace" }, Seats, seat).ToList();
            CollectionAssert.AreEqual(new[] { "marcus:pip.value.01", "marcus:pip.num.three", "jace:vex.fold.01" }, cast.Select(l => l.Id).ToList());
            Assert.AreEqual("marcus", cast[0].Voice);
            Assert.AreEqual("Oh! Um, I'll... put some in.", cast[0].Text);
            Assert.IsFalse(cast.Any(l => l.Voice == "rocco"), "Hank 坐 Rocco 座：現有音檔就是他的聲音，不用重錄");
        }

        [Test] public void CastLines_CartoonSeatsAndUnknownNamesAddNothing()
        {
            var seat = new List<SpeechLine> { new SpeechLine("pip.value.01", "pip", "Hi.") };
            CollectionAssert.IsEmpty(SpeechInventory.CastLines(new string[] { null, "-", "Nobody" }, Seats, seat).ToList());
        }

        [Test] public void Inventory_IncludesTheDefaultLineup()
        {
            var all = SpeechInventory.All();
            var names = RealisticPreview.DefaultLineup.Split(',');
            for (int i = 0; i < names.Length && i < RealisticPreview.LineupSeats.Length; i++)
            {
                var voice = SpeechVoices.ForCharacter(names[i].Trim(), RealisticPreview.LineupSeats[i]);
                var seatVoice = SpeechVoices.ForSeat(RealisticPreview.LineupSeats[i]);
                var seatTexts = all.Where(l => l.Voice == seatVoice).Select(l => l.Text).ToList();
                var castTexts = new HashSet<string>(all.Where(l => l.Voice == voice).Select(l => l.Text));
                foreach (var t in seatTexts) Assert.IsTrue(castTexts.Contains(t), names[i] + " (" + voice + ") cannot say the " + seatVoice + " line \"" + t + "\"");
            }
            Assert.IsFalse(all.Any(l => l.Voice == "hank"));
        }

        // ───────────── 執行期：SpeechChannel 換聲音、缺音檔時的後備 ─────────────

        readonly List<Object> _spawned = new List<Object>();

        [TearDown] public void TearDown()
        {
            foreach (var o in _spawned) if (o != null) Object.Destroy(o);
            _spawned.Clear();
        }

        SpeechChannel Channel(string who)
        {
            var go = new GameObject("cast_voice_test");
            _spawned.Add(go);
            var ch = go.AddComponent<SpeechChannel>();
            ch.Setup(who, null, null);
            return ch;
        }

        [Test] public void HasVoice_TellsWhetherAVoiceWasEverBaked()
        {
            SpeechBank.Reload();
            Assert.IsTrue(SpeechBank.HasVoice("pip"));
            Assert.IsFalse(SpeechBank.HasVoice("nobody"));
            Assert.IsFalse(SpeechBank.HasVoice(null));
        }

        [Test] public void Channel_VoiceDefaultsToTheSeat_UseVoiceSwitchesIt()
        {
            SpeechBank.Reload();
            var ch = Channel("rocco");
            Assert.AreEqual("rocco", ch.Voice);
            ch.UseVoice("rocco");
            var rocco = SpeechInventory.All().First(l => l.Voice == "rocco" && l.Id.StartsWith("rocco.") && !l.Id.Contains(".num."));
            var u = ch.Say(rocco.Text);
            Assert.AreEqual(rocco.Id, u.ClipId, "Hank 坐 Rocco 座：用現有的 rocco 音檔");
        }

        [Test] public void Channel_MissingCastClip_FallsBackToTheSeatVoiceWithAWarning()
        {
            SpeechBank.Reload();
            var ch = Channel("pip");
            ch.UseVoice("rocco");   // 例：Hank 坐 Pip 座（這個組合沒有預錄）
            var pip = SpeechInventory.All().First(l => l.Id == "pip.value.01");
            LogAssert.Expect(LogType.Warning, new Regex(@"\[Speech\] pip: voice rocco has no clip for .*using the seat voice pip"));
            var u = ch.Say(pip.Text);
            Assert.AreEqual("pip.value.01", u.ClipId, "不能無聲：退回座位原聲音");
            Assert.IsNotNull(u.Clip);
        }

        /// <summary>
        /// 預設陣容的寫實角色台詞都有預錄音檔。還沒 bake 的句子（ElevenLabs 新 key 之前）標 Inconclusive 並列出句數（規則同 SpeechInventoryTests 的待錄）；
        /// manifest 有但音檔不見才算失敗（執行期會退回座位聲音並警告，見上一個測試）。
        /// </summary>
        [Test] public void EveryCastVoiceLine_HasABakedClip()
        {
            SpeechBank.Reload();
            var cast = SpeechInventory.All().Where(l => SpeechVoices.Cast.Contains(l.Voice)).ToList();
            var missing = new List<string>();
            var unbaked = new List<string>();
            foreach (var g in cast.GroupBy(l => l.Voice))
            {
                int notBaked = g.Count(l => !SpeechBank.TryGetEntry(l.Voice, l.Text, out _));
                if (notBaked > 0) unbaked.Add(g.Key + " (" + notBaked + " lines)");
                foreach (var l in g)
                    if (SpeechBank.TryGetEntry(l.Voice, l.Text, out var e) && Resources.Load<AudioClip>(e.file) == null) missing.Add(l + " (no clip at Resources/" + e.file + ")");
            }
            CollectionAssert.IsEmpty(missing, "缺音檔：到 tools/voice-proxy 跑 bake（node bake/bake.ts）");
            if (unbaked.Count > 0) Assert.Inconclusive("not baked yet: " + string.Join(", ", unbaked) + " — run node bake/bake.ts with a working ElevenLabs key");
        }

        // ───────────── 即時回話：告訴中繼每個座位現在是誰的聲音 ─────────────

        [Test] public void VoicesJson_ListsOnlySeatsWhoseVoiceChanged()
        {
            var json = VoiceCast.VoicesJson(new[]
            {
                (VoiceCharacter.Pip, "marcus"), (VoiceCharacter.Rocco, "rocco"), (VoiceCharacter.Vex, "jace"), (VoiceCharacter.Dot, "dot"), (VoiceCharacter.None, "erik"),
            });
            Assert.AreEqual("{\"pip\":\"marcus\",\"vex\":\"jace\"}", json);
            Assert.AreEqual("{}", VoiceCast.VoicesJson(new (VoiceCharacter, string)[0]));
        }

        [Test] public void ProtocolCarriesVoicesOnlyWhenThereAreAny()
        {
            var live = VoiceProtocol.BuildLiveStart("pip", "{}", "{}", 16000, "{\"pip\":\"marcus\"}");
            StringAssert.Contains("\"voices\":{\"pip\":\"marcus\"}", live);
            StringAssert.DoesNotContain("voices", VoiceProtocol.BuildLiveStart("pip", "{}", "{}", 16000));
            StringAssert.DoesNotContain("voices", VoiceProtocol.BuildLiveStart("pip", "{}", "{}", 16000, "{}"));
            var talk = VoiceProtocol.BuildRequest(new byte[] { 1 }, "vex", "{}", "{}", "{\"vex\":\"jace\"}");
            StringAssert.Contains("\"voices\":{\"vex\":\"jace\"}", talk);
            StringAssert.DoesNotContain("voices", VoiceProtocol.BuildRequest(new byte[] { 1 }, "vex", "{}", "{}"));
        }
    }
}
