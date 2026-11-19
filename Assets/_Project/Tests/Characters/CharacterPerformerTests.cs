using System;
using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using Poker.AI;
using Poker.Characters;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.TestTools.Constraints;
using Is = NUnit.Framework.Is;
using AllocIs = UnityEngine.TestTools.Constraints.Is;

namespace Poker.Tests
{
    /// <summary>角色演出層的 PlayMode 測試。大多用 Tick(dt) 決定性推進，另有一條確認 Update 真的在跑。</summary>
    public class CharacterPerformerTests
    {
        const float Dt = 1f / 60f;
        readonly List<GameObject> _spawned = new List<GameObject>();

        [TearDown]
        public void TearDown()
        {
            Application.logMessageReceived -= Collect;
            foreach (var go in _spawned) if (go != null) UnityEngine.Object.Destroy(go);
            _spawned.Clear();
        }

        CharacterPerformer Spawn(CharacterKind kind, Vector3 pos, int seed = 1)
        {
            var p = CharacterFactory.Create(kind, null, pos, Quaternion.identity, seed);
            p.AutoUpdate = false;
            _spawned.Add(p.gameObject);
            return p;
        }

        Transform Viewer(Vector3 pos)
        {
            var go = new GameObject("ViewerHead");
            go.transform.position = pos;
            _spawned.Add(go);
            return go.transform;
        }

        /// <summary>這個角色真的會說、有預錄聲音的一句。</summary>
        static string RealLine(CharacterKind k) =>
            k == CharacterKind.Rookie ? "Wait, what beats what again?" : k == CharacterKind.Loudmouth ? "Eh, let's see where this goes."
            : k == CharacterKind.Cold ? "Interesting." : k == CharacterKind.Veteran ? "Hm. We'll see." : "Let's see them.";

        static void Run(CharacterPerformer p, float seconds)
        {
            for (float t = 0; t < seconds; t += Dt) p.Tick(Dt);
        }

        static float EyeError(Transform eye, Vector3 target) =>
            Vector3.Angle(eye.forward, target - eye.position);

        [Test]
        public void EveryMoodAndTell_Plays_WithoutException([Values] CharacterKind kind)
        {
            _problems.Clear();
            Application.logMessageReceived += Collect;
            var p = Spawn(kind, Vector3.zero);
            p.SetViewer(Viewer(new Vector3(0, 0.35f, 0.9f)));
            foreach (Mood mood in Enum.GetValues(typeof(Mood)))
            foreach (Tell tell in Enum.GetValues(typeof(Tell)))
            foreach (var gazed in new[] { false, true })
            {
                p.SetGazedAt(gazed);
                // 台詞要用真的有預錄聲音的句子（沒有聲音的字會記 clip=none 警告，見 SpeechChannelTests）
                p.Play(new PerformanceCue { Mood = mood, Tell = tell, Intensity = 1f, Look = LookTarget.OwnCards, Subtitle = RealLine(kind) });
                Run(p, 0.4f);
            }
            foreach (LookTarget look in Enum.GetValues(typeof(LookTarget)))
            {
                p.Play(new PerformanceCue { Look = look, LookSeat = 2 });
                Run(p, 0.2f);
            }
            // 以前用 LogAssert.NoUnexpectedReceived（任何 log 都算失敗）；現在每句台詞都會記 [Speech] log（實機驗證用），
            // 所以改成只檢查沒有警告、錯誤或例外
            Application.logMessageReceived -= Collect;
            CollectionAssert.IsEmpty(_problems);
        }

        readonly List<string> _problems = new List<string>();
        void Collect(string msg, string stack, LogType type) { if (type != LogType.Log) _problems.Add(type + ": " + msg); }

        [Test]
        public void GazedAt_LeanBack_IsBiggerThanUngazed()
        {
            var calm = Spawn(CharacterKind.Loudmouth, Vector3.zero);
            var watched = Spawn(CharacterKind.Loudmouth, new Vector3(2, 0, 0));
            var cue = new PerformanceCue { Mood = Mood.Confident, Tell = Tell.LeanBack, Intensity = 0.5f };
            calm.Play(cue);
            watched.Play(cue);
            watched.SetGazedAt(true);
            Run(calm, 3f);
            Run(watched, 3f);

            float a = calm.LeanDegrees, b = watched.LeanDegrees;
            Assert.Less(a, -1f, "LeanBack 應該往後靠（負角度）");
            Assert.GreaterOrEqual(Mathf.Abs(b) / Mathf.Abs(a), 1.4f, $"注視時傾角應放大：未注視 {a:F1}°、注視 {b:F1}°");
            Assert.Greater(watched.TellAmplitude, calm.TellAmplitude * 1.4f);
        }

        [Test]
        public void GazedAt_GlanceAtCards_HappensMoreOften()
        {
            var calm = Spawn(CharacterKind.Rookie, Vector3.zero);
            var watched = Spawn(CharacterKind.Rookie, new Vector3(2, 0, 0));
            var cue = new PerformanceCue { Mood = Mood.Nervous, Tell = Tell.GlanceAtCards, Intensity = 0.6f };
            calm.Play(cue);
            watched.Play(cue);
            watched.SetGazedAt(true);
            Run(calm, 20f);
            Run(watched, 20f);
            Assert.Greater(calm.TellActionCount, 0);
            Assert.Greater(watched.TellActionCount, calm.TellActionCount, $"未注視 {calm.TellActionCount} 次、注視 {watched.TellActionCount} 次");
        }

        [TestCase(0f, 0.35f, 0.9f)]
        [TestCase(-0.5f, 0.45f, 0.7f)]
        [TestCase(0.6f, 0.3f, 0.6f)]
        public void Eyes_PointAtViewerHead(float x, float y, float z)
        {
            var p = Spawn(CharacterKind.Dealer, Vector3.zero);
            var head = Viewer(new Vector3(x, y, z));
            p.SetViewer(head);
            p.Play(new PerformanceCue { Mood = Mood.Neutral, Look = LookTarget.Player });
            Run(p, 1.5f);
            Assert.Less(EyeError(p.Rig.EyeL, head.position), 6f, "左眼");
            Assert.Less(EyeError(p.Rig.EyeR, head.position), 6f, "右眼");
        }

        [Test]
        public void StareDown_HoldsGaze_AvoidEyeContact_LooksAway_WhenWatched()
        {
            var cold = Spawn(CharacterKind.Cold, Vector3.zero);
            var rookie = Spawn(CharacterKind.Rookie, new Vector3(2, 0, 0));
            var h1 = Viewer(new Vector3(0, 0.35f, 0.9f));
            var h2 = Viewer(new Vector3(2, 0.35f, 0.9f));
            cold.SetViewer(h1);
            rookie.SetViewer(h2);
            cold.SetGazedAt(true);
            rookie.SetGazedAt(true);
            cold.Play(new PerformanceCue { Mood = Mood.Confident, Tell = Tell.StareDown, Intensity = 0.8f });
            rookie.Play(new PerformanceCue { Mood = Mood.Nervous, Tell = Tell.AvoidEyeContact, Intensity = 0.8f });
            Run(cold, 2f);
            Run(rookie, 2f);
            Assert.Less(EyeError(cold.Rig.EyeL, h1.position), 6f);
            Assert.Greater(EyeError(rookie.Rig.EyeL, h2.position), 20f);
        }

        [Test]
        public void StareDown_NarrowsEyes_ButKeepsThemOpen([Values] CharacterKind kind)
        {
            var p = Spawn(kind, Vector3.zero);
            p.SetViewer(Viewer(new Vector3(0, 0.35f, 0.9f)));
            p.SetGazedAt(true);
            p.Play(new PerformanceCue { Mood = Mood.Confident, Tell = Tell.StareDown, Intensity = 1f });
            Run(p, 2f);
            while (p.IsBlinking) p.Tick(Dt);
            float cover = p.Rig.LidL.localScale.y;
            Assert.That(cover, Is.InRange(0.15f, 0.6f), $"{kind} 瞪人時眼皮蓋住 {cover:P0}，要瞇但不能像閉眼");
        }

        /// <summary>每幀位置的二階差分：抓得到快速抖動，抓不到慢慢搓手的動作。</summary>
        static float MaxJerk(CharacterPerformer p, Transform t, float seconds)
        {
            Vector3 a = t.localPosition, b = a;
            float max = 0;
            for (float s = 0; s < seconds; s += Dt)
            {
                p.Tick(Dt);
                var c = t.localPosition;
                max = Mathf.Max(max, (c - 2 * b + a).magnitude);
                a = b; b = c;
            }
            return max;
        }

        [Test]
        public void Nervous_HandsVisiblyTremble_CalmHandsDoNot()
        {
            var nervous = Spawn(CharacterKind.Rookie, Vector3.zero);
            var calm = Spawn(CharacterKind.Rookie, new Vector3(2, 0, 0));
            nervous.Play(new PerformanceCue { Mood = Mood.Nervous, Intensity = 0.8f });
            calm.Play(new PerformanceCue { Mood = Mood.Neutral });
            Run(nervous, 2f);
            Run(calm, 2f);
            float shaky = MaxJerk(nervous, nervous.Rig.HandR, 1f);
            float still = MaxJerk(calm, calm.Rig.HandR, 1f);
            Assert.Greater(shaky, 0.001f, $"緊張時手要抖得看得出來（{shaky * 1000:F2} mm）");
            Assert.Less(still, 0.0005f, $"平靜時手不抖（{still * 1000:F2} mm）");
        }

        [Test]
        public void Startled_MovesHeadNoticeably_ThenRecovers()
        {
            var p = Spawn(CharacterKind.Rookie, Vector3.zero);
            p.Play(new PerformanceCue { Mood = Mood.Neutral });
            Run(p, 1f);
            var rest = p.Rig.Head.position;
            p.Play(new PerformanceCue { Mood = Mood.Startled, Intensity = 1f, Duration = 1.5f });
            float maxMove = 0;
            for (float t = 0; t < 0.6f; t += Dt)
            {
                p.Tick(Dt);
                maxMove = Mathf.Max(maxMove, Vector3.Distance(p.Rig.Head.position, rest));
            }
            Assert.Greater(maxMove, 0.05f, "被嚇到時頭應該明顯移動");
            Assert.IsTrue(p.Rig.Symbol.gameObject.activeInHierarchy, "被嚇到時頭頂要有符號");
            Run(p, 4f);
            Assert.Less(Vector3.Distance(p.Rig.Head.position, rest), 0.03f, "之後要回到原位");
        }

        [Test]
        public void CueLine_IsSpokenThroughTheSpeechChannel_BubbleAndAudioTogether()
        {
            // 2026-10-13 改版：台詞不再「有字沒聲音」或「拿掉字」，而是交給角色自己的 SpeechChannel：
            // 預錄音檔開始播的同時顯示同一句字幕（取代原本的 Caption／VoiceActive 機制，見 SpeechChannelTests）
            var p = Spawn(CharacterKind.Loudmouth, Vector3.zero);
            string heardLine = null;
            p.LineStarted += (who, id) => heardLine = id;
            p.Play(new PerformanceCue { Mood = Mood.Happy, LineId = "rocco.bigbet.02", Subtitle = "Cute bet. Real cute.", Duration = 2f });
            p.Tick(Dt);
            Assert.AreEqual("rocco.bigbet.02", heardLine, "台詞事件照樣發出");
            Assert.IsTrue(p.Speech.IsSpeaking, "有預錄聲音");
            Assert.AreEqual("rocco.bigbet.02", p.Speech.Current.ClipId);
            Assert.IsTrue(p.Subtitle.IsVisible, "字幕和聲音一起出現");
            StringAssert.Contains("Cute bet.", p.Subtitle.Text.Replace("\n", " "));
            Assert.IsTrue(p.IsTalking, "嘴巴跟著動");
            Run(p, p.Speech.Current.Duration + SpeechChannel.ReadTail + 0.1f);
            Assert.IsFalse(p.Subtitle.IsVisible, "講完再過閱讀時間就收起來");
        }

        [Test]
        public void EveryCharacter_HasItsOwnVoice([Values] CharacterKind kind)
        {
            var p = Spawn(kind, Vector3.zero);
            var expect = kind == CharacterKind.Rookie ? "pip" : kind == CharacterKind.Loudmouth ? "rocco" : kind == CharacterKind.Cold ? "vex" : kind == CharacterKind.Veteran ? "silas" : "dot";
            Assert.AreEqual(expect, p.Speech.Who);
            Assert.IsTrue(p.Speech.Source.transform.IsChildOf(p.Rig.Head), "聲音從頭發出");
            Assert.Greater(p.Speech.Source.spatialBlend, 0.9f);
        }

        [Test]
        public void Talk_MovesMouthForTheGivenTime()
        {
            var p = Spawn(CharacterKind.Rookie, Vector3.zero);
            p.Talk(0.5f);
            p.Tick(Dt);
            Assert.IsTrue(p.IsTalking);
            Assert.IsFalse(p.Subtitle.IsVisible, "只動嘴不會自己冒出字（字只來自 SpeechChannel）");
            Run(p, 0.6f);
            Assert.IsFalse(p.IsTalking);
        }

        [Test]
        public void RendererBudget_AndNoShadows([Values] CharacterKind kind)
        {
            var p = Spawn(kind, Vector3.zero);
            var rs = p.GetComponentsInChildren<Renderer>(true);
            Assert.LessOrEqual(rs.Length, 40, $"{kind} renderer 數量 {rs.Length}");
            foreach (var r in rs)
            {
                Assert.AreEqual(UnityEngine.Rendering.ShadowCastingMode.Off, r.shadowCastingMode, r.name);
                Assert.IsFalse(r.receiveShadows, r.name);
            }
            Assert.AreEqual(0, p.GetComponentsInChildren<Collider>(true).Length, "不需要 collider");
        }

        [Test]
        public void HeadSize_IsChibiButTableScale([Values] CharacterKind kind)
        {
            var p = Spawn(kind, Vector3.zero);
            var b = p.Rig.HeadRenderer.bounds;
            Assert.That(b.size.y, Is.InRange(0.25f, 0.35f), $"頭高 {b.size.y:F3} m");
        }

        [Test]
        public void Idle_IsAlive_NotAStatue()
        {
            var p = Spawn(CharacterKind.Dealer, Vector3.zero);
            p.Play(new PerformanceCue());
            Run(p, 0.5f);
            var chest = p.Rig.Chest.localScale;
            float maxDiff = 0;
            for (float t = 0; t < 4f; t += Dt)
            {
                p.Tick(Dt);
                maxDiff = Mathf.Max(maxDiff, (p.Rig.Chest.localScale - chest).magnitude);
            }
            Assert.Greater(maxDiff, 0.005f, "待機要有呼吸");
            Assert.Greater(p.BlinkCount, 0, "待機要眨眼");
        }

        [TestCase(Mood.Nervous, Tell.FidgetChips)]
        [TestCase(Mood.Happy, Tell.TapFingers)]
        [TestCase(Mood.Bluffing, Tell.GlanceAtCards)]
        [TestCase(Mood.Startled, Tell.Swallow)]
        public void Tick_DoesNotAllocate_InSteadyState(Mood mood, Tell tell)
        {
            var p = Spawn(CharacterKind.Rookie, Vector3.zero);
            p.SetViewer(Viewer(new Vector3(0, 0.35f, 0.9f)));
            p.SetGazedAt(true);
            p.Play(new PerformanceCue { Mood = mood, Tell = tell, Intensity = 1f, Subtitle = RealLine(CharacterKind.Rookie), Duration = 30f });
            Run(p, 3f); // 暖身：所有網格與材質都建好
            // GC.GetAllocatedBytesForCurrentThread 在 Unity Mono 恆為 0，改用 profiler 的 GC.Alloc 記錄
            Assert.That(() => p.Tick(Dt), AllocIs.Not.AllocatingGCMemory());
            Run(p, 0.7f);
            Assert.That(() => p.Tick(Dt), AllocIs.Not.AllocatingGCMemory());
        }

        [Test]
        public void FourCharacters_TickIsCheap()
        {
            var list = new List<CharacterPerformer>();
            var head = Viewer(new Vector3(0, 0.45f, -0.25f));
            int i = 0;
            foreach (CharacterKind k in Enum.GetValues(typeof(CharacterKind)))
            {
                var p = Spawn(k, new Vector3(i++ * 0.5f, 0, 1f));
                p.SetViewer(head);
                p.Play(new PerformanceCue { Mood = Mood.Nervous, Tell = Tell.FidgetChips, Intensity = 0.8f });
                list.Add(p);
            }
            foreach (var p in list) Run(p, 1f);
            const int frames = 600;
            var sw = System.Diagnostics.Stopwatch.StartNew();
            for (int f = 0; f < frames; f++)
                foreach (var p in list) p.Tick(Dt);
            sw.Stop();
            double perCharMs = sw.Elapsed.TotalMilliseconds / frames / list.Count;
            Debug.Log($"[Perf] CharacterPerformer.Tick 平均 {perCharMs:F4} ms／角色／幀（Editor）");
            Assert.Less(perCharMs, 0.1, "Editor 上每個角色每幀應低於 0.1 ms");
        }

        [UnityTest]
        public IEnumerator Update_DrivesAnimation_InPlayMode()
        {
            var p = CharacterFactory.Create(CharacterKind.Cold, null, Vector3.zero, Quaternion.identity, 3);
            _spawned.Add(p.gameObject);
            p.Play(new PerformanceCue { Mood = Mood.Startled, Intensity = 1f });
            var start = p.Rig.Head.position;
            float moved = 0;
            for (int i = 0; i < 20; i++)
            {
                yield return null;
                moved = Mathf.Max(moved, Vector3.Distance(start, p.Rig.Head.position));
            }
            Assert.Greater(moved, 0.01f);
        }
    }
}
