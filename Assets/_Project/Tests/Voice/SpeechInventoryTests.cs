using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;
using Poker.AI;
using Poker.Characters;
using Poker.Game;
using Poker.Voice;
using UnityEngine;

namespace Poker.Tests.Voice
{
    /// <summary>
    /// 「頭上有字就一定有聲音」的守門測試：
    /// 1. 程式裡每一句固定台詞（SpeechInventory）都匯出到 tools/voice-proxy/bake/lines.json，而且都有預錄音檔；
    /// 2. 有數字、名字的句子，所有變化都能用預錄片段＋數字字拼出來；
    /// 3. 掃原始碼：Say("…") 的字面字串都在清單裡、沒有繞過 SpeechChannel 直接顯示泡泡的程式。
    /// 改了台詞：SPEECH_EXPORT=1 跑這個測試更新 lines.json → tools/voice-proxy 跑 bake → 再跑一次。
    /// </summary>
    public class SpeechInventoryTests
    {
        static string Root => Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
        static string LinesPath => Path.Combine(Root, "tools/voice-proxy/bake/lines.json");

        [Test]
        public void LinesJson_IsUpToDateWithTheCode()
        {
            var json = SpeechInventory.ToJson();
            if (Environment.GetEnvironmentVariable("SPEECH_EXPORT") == "1")
            {
                Directory.CreateDirectory(Path.GetDirectoryName(LinesPath));
                File.WriteAllText(LinesPath, json);
                Debug.Log("[Speech] exported " + SpeechInventory.All().Count + " lines to " + LinesPath);
            }
            Assert.IsTrue(File.Exists(LinesPath), "沒有 lines.json：SPEECH_EXPORT=1 跑這個測試");
            Assert.AreEqual(json, File.ReadAllText(LinesPath), "台詞改了：SPEECH_EXPORT=1 重跑這個測試，再到 tools/voice-proxy 跑 bake");
        }

        [Test]
        public void Inventory_IdsAreUnique_AndEveryVoiceIsKnown()
        {
            var all = SpeechInventory.All();
            Assert.Greater(all.Count, 150);
            var dup = all.GroupBy(l => l.Id).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
            CollectionAssert.IsEmpty(dup);
            foreach (var l in all) Assert.IsTrue(SpeechVoices.IsKnown(l.Voice), l.ToString());
            foreach (var l in LineBook.All) Assert.IsTrue(all.Any(x => x.Voice == LineBook.VoiceOf(l) && x.Text == l.Text), l.Id);
        }

        [Test]
        public void EveryInventoryLine_HasABakedClip()
        {
            SpeechBank.Reload();
            var missing = new List<string>();
            // 寫實角色的聲音另外檢查（CastVoiceTests.EveryCastVoiceLine_HasABakedClip）；清單裡有、manifest 還沒有＝待錄（最後 Inconclusive）
            var pending = new SortedSet<string>();
            foreach (var l in SpeechInventory.All().Where(x => SpeechText.Voices.Contains(x.Voice)))
            {
                if (!SpeechBank.TryGetEntry(l.Voice, l.Text, out var e)) { pending.Add(l.Voice); continue; }
                var clip = Resources.Load<AudioClip>(e.file);
                if (clip == null) { missing.Add(l + " (no clip at Resources/" + e.file + ")"); continue; }
                if (clip.length < 0.1f || clip.channels != 1) missing.Add(l + $" (bad clip {clip.length:0.00}s ch={clip.channels})");
            }
            CollectionAssert.IsEmpty(missing, "缺音檔：到 tools/voice-proxy 跑 bake（node bake/bake.ts）");
            InconclusiveIfPending(pending);
        }

        /// <summary>
        /// 待錄（2026-11-02，ElevenLabs 新 key 之前）：句子（或拼它的每個片段）已在匯出清單 lines.json 裡、只是還沒 bake → 不算失敗，
        /// 但測試標 Inconclusive 列出聲音（不是通過；node bake/bake.ts --count 有句數）。清單裡根本沒有的句子仍然失敗（程式漏列）；
        /// manifest 有但音檔不見或壞掉也失敗。執行期沒有音檔：字幕照出、記 warning（SpeechChannel），語音對話改說罐頭句。
        /// </summary>
        static void InconclusiveIfPending(ICollection<string> pending)
        {
            if (pending.Count > 0) Assert.Inconclusive("lines exported but not baked yet for: " + string.Join(", ", pending) + " — run node bake/bake.ts with a working ElevenLabs key");
        }

        static HashSet<string> _inventory, _inventoryKeyed;

        /// <summary>這句話（整句，或有數字時拆出的每個片段）在匯出清單裡嗎（bake 之後就說得出來）。</summary>
        static bool InInventory(string voice, string text)
        {
            if (_inventory == null)
            {
                _inventory = new HashSet<string>();
                _inventoryKeyed = new HashSet<string>();
                foreach (var l in SpeechInventory.All()) { _inventory.Add(l.Voice + "\n" + l.Text); _inventoryKeyed.Add(l.Voice + "\n" + SpeechText.Key(l.Text)); }
            }
            if (string.IsNullOrEmpty(text)) return false;
            if (_inventory.Contains(voice + "\n" + text)) return true;
            var pieces = SpeechText.Pieces(text);
            if (pieces.Count == 0 || (pieces.Count == 1 && !SpeechText.HasNumber(text))) return false;
            return pieces.All(p => _inventory.Contains(voice + "\n" + p) || _inventoryKeyed.Contains(voice + "\n" + SpeechText.Key(p)));
        }

        /// <summary>遊戲會說的每一種句子（含數字、名字的所有變化）都必須找得到聲音。</summary>
        [Test]
        public void EveryLineTheGameCanSay_HasAudio()
        {
            SpeechBank.Reload();
            var bad = new List<string>();
            var pending = new SortedSet<string>();
            void Check(string voice, string text)
            {
                if (SpeechBank.CanSay(voice, text, out var why)) return;
                if (InInventory(voice, text)) pending.Add(voice);
                else bad.Add(voice + ": \"" + text + "\" " + why);
            }

            foreach (var l in LineBook.All) Check(LineBook.VoiceOf(l), l.Text);
            foreach (var t in DealerLines.Fixed()) Check("dot", t);
            // 教學 v4：每一步的提示、讚美輪替、卡住時的具體建議（每種原因）、結尾
            foreach (var (_, text) in Tutorial.Lines()) Check("dot", text);
            // 2026-10-14：牌力提示第一次的小聲提醒、正式牌局第一次用到某個手勢時的提示
            foreach (HandHint h in Enum.GetValues(typeof(HandHint))) Check("dot", DealerLines.HandHintWhisper(h));
            foreach (JitTip t in Enum.GetValues(typeof(JitTip))) Check("dot", JustInTimeTips.Line(t));
            foreach (TutorialStep s in Enum.GetValues(typeof(TutorialStep)))
            {
                if (s == TutorialStep.Done) continue;
                Check("dot", Tutorial.Prompt(s));
                foreach (var r in new[] { null, "palm too high", "fingertips too high", "palm not down", "palm turned too far", "hand beside the cards", "wrist not above fingertips",
                                          "pinch began away from the chips", Tutorial.ReasonOutsideZone, "fold: lifted", "fold: too slow", "pinch below start" })
                    Check("dot", Tutorial.Tip(s, r));
            }
            for (int i = 0; i < 12; i++) Check("dot", Tutorial.Praise(i));
            // Fool Rocco 原型（2026-10-14）：Rocco 的讀牌台詞（每種訊號、每個看牌次數）與 Dot 的教學、結尾
            foreach (var l in FoolRocco.Lines()) Check(l.Voice, l.Text);
            foreach (FoolCallout c in Enum.GetValues(typeof(FoolCallout)))
                for (int n = 0; n < 12; n++) { var l = FoolRocco.CalloutLine(c, n); Check(l.Voice, l.Text); }
            foreach (var l in new[] { FoolRocco.RoccoCall, FoolRocco.RoccoFold, FoolRocco.RoccoPlayed, FoolRocco.RoccoDistrust,
                                      FoolRocco.DotNextHand, FoolRocco.DotIntro, FoolRocco.DotCoach, FoolRocco.DotFooled, FoolRocco.DotRetry, FoolRocco.DotLearns })
                Check(l.Voice, l.Text);
            // 即時牌桌對話（2026-10-14 live talk）：每個角色每個觸發的每一句
            foreach (var l in LiveLines.All()) Check(l.Voice, l.Text);
            foreach (InvalidReason r in Enum.GetValues(typeof(InvalidReason))) Check("dot", DealerLines.Invalid(r));
            foreach (var who in DealerLines.WinnerNames)
                foreach (var side in new[] { false, true }) Check("dot", DealerLines.PotTo(who, side));
            foreach (var p in SampleProgress()) Check("dot", DealerLines.Greeting(p));

            foreach (VoiceCharacter c in new[] { VoiceCharacter.Pip, VoiceCharacter.Rocco, VoiceCharacter.Vex, VoiceCharacter.Dot, VoiceCharacter.Silas })
            {
                string v = VoiceCast.Id(c);
                Check(v, VoiceLines.Filler(c));
                for (int n = 0; n < 4; n++) Check(v, VoiceFallback.CannedLine(c, n));
                foreach (var st in SampleStates())
                    foreach (LocalIntent i in Enum.GetValues(typeof(LocalIntent)))
                        foreach (var q in new[] { "how much do I have", "how many chips does pip have", "rocco has how much", "vex chips", "silas chips", "what about my stack" })
                        {
                            var a = VoiceFallback.Answer(i, st, c, q);
                            if (a != null) Check(v, a);
                        }
            }
            CollectionAssert.IsEmpty(bad);
            InconclusiveIfPending(pending);
        }

        [Test]
        public void NumbersAreSpokenByJoiningBakedPieces()
        {
            SpeechBank.Reload();
            var a = SpeechBank.Resolve("dot", "The pot is 340.");
            Assert.IsNotNull(a, "可以拼出來");
            Assert.IsNotNull(a.Clip);
            StringAssert.Contains("+", a.Id, "拼接的 id 列出用到的片段");
            Assert.Greater(a.Clip.length, 0.8f, "「The pot is」＋three hundred forty");
            Assert.IsTrue(a.OwnsClip, "拼出來的音檔播完要釋放");
            var exact = SpeechBank.Resolve("dot", "Action is on you.");
            Assert.AreEqual("dot.yourturn.01", exact.Id);
            Assert.IsFalse(exact.OwnsClip);
            Assert.IsNull(SpeechBank.Resolve("dot", "Totally unknown sentence."));
        }

        static readonly Regex SayLiteral = new Regex(@"\.Say\(\s*(\$?)""((?:[^""\\]|\\.)*)""", RegexOptions.CultureInvariant);
        static readonly Regex SubtitleLiteral = new Regex(@"Subtitle\s*=\s*""((?:[^""\\]|\\.)*)""", RegexOptions.CultureInvariant);
        static readonly Regex DirectShow = new Regex(@"(Subtitle|Bubble)\??\.Show\(|(?<!GhostCatalog)\.Caption\(", RegexOptions.CultureInvariant);

        /// <summary>掃原始碼：每個說話的呼叫點都用有聲音的句子；沒有程式繞過 SpeechChannel 直接把字放到頭上。</summary>
        [Test]
        public void SourceScan_EverySpeechCallSite_UsesALineWithAudio()
        {
            SpeechBank.Reload();
            var scripts = Path.Combine(Application.dataPath, "_Project/Scripts");
            var files = Directory.GetFiles(scripts, "*.cs", SearchOption.AllDirectories);
            Assert.Greater(files.Length, 50);
            var bad = new List<string>();
            int callSites = 0;
            foreach (var f in files)
            {
                var rel = f.Substring(scripts.Length + 1);
                var src = File.ReadAllText(f);
                callSites += Regex.Matches(src, @"\.Say(Stream)?\(").Count;
                foreach (Match m in SayLiteral.Matches(src))
                {
                    if (m.Groups[1].Value == "$") { bad.Add(rel + ": interpolated Say($\"…\") — use a DealerLines/VoiceLines template"); continue; }
                    var text = Regex.Unescape(m.Groups[2].Value);
                    if (!SpeechText.Voices.Any(v => SpeechBank.CanSay(v, text, out _))) bad.Add(rel + ": Say(\"" + text + "\") has no baked audio");
                }
                foreach (Match m in SubtitleLiteral.Matches(src))
                {
                    var text = Regex.Unescape(m.Groups[1].Value);
                    if (!SpeechText.Voices.Any(v => SpeechBank.CanSay(v, text, out _))) bad.Add(rel + ": Subtitle = \"" + text + "\" has no baked audio");
                }
                if (!rel.EndsWith("SpeechChannel.cs") && !rel.EndsWith("SubtitleBubble.cs"))
                    foreach (Match m in DirectShow.Matches(src))
                        bad.Add(rel + ": " + m.Value + " bypasses SpeechChannel (text without audio)");
            }
            CollectionAssert.IsEmpty(bad);
            Assert.Greater(callSites, 20, "掃描器有找到說話的呼叫點");
        }

        /// <summary>DealerLines／VoiceLines 的每個字串常數都有聲音（新增常數忘了列進清單時抓得到）。</summary>
        [Test]
        public void EveryLineConstant_HasAudio()
        {
            SpeechBank.Reload();
            var bad = new List<string>();
            var pending = new SortedSet<string>();
            foreach (var (type, voices) in new[] { (typeof(DealerLines), new[] { "dot" }), (typeof(VoiceLines), VoiceTalkers) })
                foreach (var f in type.GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static))
                {
                    if (!f.IsLiteral || f.FieldType != typeof(string) || f.Name == "Voice") continue;
                    var text = (string)f.GetRawConstantValue();
                    foreach (var v in voices)
                    {
                        if (type == typeof(VoiceLines) && v == "dot" && text == VoiceLines.MyTurn) continue;   // 荷官不會輪到
                        if (SpeechBank.CanSay(v, text, out var why)) continue;
                        if (InInventory(v, text)) pending.Add(v);
                        else bad.Add(type.Name + "." + f.Name + " (" + v + "): " + why);
                    }
                }
            CollectionAssert.IsEmpty(bad);
            InconclusiveIfPending(pending);
        }

        /// <summary>語音對話的角色（VoiceLines 為他們錄；2026-11-02 起含老手 silas）。</summary>
        static readonly string[] VoiceTalkers = { "pip", "rocco", "vex", "dot", "silas" };

        static IEnumerable<ProgressData> SampleProgress()
        {
            yield return new ProgressData();
            yield return new ProgressData { TutorialDone = true };                                    // 舊版教學完成
            yield return new ProgressData { TutorialDone = true, TutorialVersion = Tutorial.CurrentVersion };
            foreach (var (games, wins, reads, correct) in new[] { (1, 1, 0, 0), (1, 0, 0, 0), (5, 3, 0, 0), (4, 2, 4, 3), (12, 1, 9, 9), (250, 117, 1234, 1000) })
                yield return new ProgressData
                {
                    TutorialDone = true, TutorialVersion = Tutorial.CurrentVersion,
                    GamesPlayed = games, GamesWon = wins, ReadsTotal = reads, ReadsCorrect = correct,
                };
        }

        static IEnumerable<PublicTableState> SampleStates()
        {
            foreach (var (pot, call, sb, bb, stack) in new[] { (0, 0, 5, 10, 1000), (60, 20, 5, 10, 950), (1340, 405, 25, 50, 0), (12000, 3000, 100, 200, 24315) })
                foreach (var toAct in new[] { "you", "pip", "rocco", "vex", "silas", null })
                {
                    var s = States.Flop();
                    s.Pot = pot; s.YouToCall = call; s.SmallBlind = sb; s.BigBlind = bb; s.ToAct = toAct;
                    s.Seats.Add(new Poker.Voice.PublicSeat { Name = "silas", Status = "active" });   // 老手座位（寫實預覽的 5 人桌）
                    foreach (var seat in s.Seats) seat.Stack = stack;
                    yield return s;
                }
        }
    }
}
