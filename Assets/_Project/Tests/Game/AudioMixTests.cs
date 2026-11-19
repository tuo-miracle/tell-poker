using System.Collections.Generic;
using System.Linq;
using System.Text;
using NUnit.Framework;
using Poker.Characters;
using Poker.Core;
using Poker.Game;
using UnityEngine;

namespace Poker.Tests.Game
{
    /// <summary>
    /// 混音：有人講話時（整桌發言權 SpeechFloor 有人在講）音效與環境音壓低；環境音床能無縫循環；
    /// 每種音效的響度（加上 AudioMix 的修正後）都比台詞小；所有角色的聲音空間化設定一致。
    /// 響度用 ITU-R BS.1770（K 加權）：音效取最響的 momentary（400 ms），台詞與環境音取 integrated（閘門），台詞取所有音檔的中位數。
    /// </summary>
    public class AudioMixTests
    {
        // ───────────── 壓低（ducking） ─────────────

        [Test]
        public void Ducker_Silence_IsUnity()
        {
            var d = new Ducker(-8f);
            for (int i = 0; i < 100; i++) d.Tick(1f / 72f, false);
            Assert.AreEqual(0f, d.GainDb, 1e-4f);
            Assert.AreEqual(1f, d.Gain, 1e-4f);
        }

        [Test]
        public void Ducker_Speech_DucksWithinTheAttackTime()
        {
            var d = new Ducker(-8f);
            float t = 0f;
            while (t < Ducker.AttackSeconds * 0.5f) { d.Tick(1f / 72f, true); t += 1f / 72f; }
            Assert.Less(d.GainDb, -2f, "already moving down halfway through the attack");
            Assert.Greater(d.GainDb, -8f);
            for (int i = 0; i < 20; i++) d.Tick(1f / 72f, true);
            Assert.AreEqual(-8f, d.GainDb, 0.05f);
            Assert.AreEqual(Mathf.Pow(10f, -8f / 20f), d.Gain, 1e-3f);
        }

        [Test]
        public void Ducker_AfterSpeech_HoldsThenRecoversSmoothly()
        {
            var d = new Ducker(-8f);
            for (int i = 0; i < 30; i++) d.Tick(1f / 72f, true);
            float prev = d.GainDb, t = 0f;
            while (t < Ducker.HoldSeconds - 0.02f) { d.Tick(1f / 72f, false); t += 1f / 72f; }
            Assert.AreEqual(-8f, d.GainDb, 0.05f, "held through the hold time");
            float maxStep = 0f;
            while (t < Ducker.HoldSeconds + Ducker.ReleaseSeconds + 0.05f)
            {
                d.Tick(1f / 72f, false);
                t += 1f / 72f;
                Assert.GreaterOrEqual(d.GainDb, prev - 1e-5f, "monotonic release");
                maxStep = Mathf.Max(maxStep, d.GainDb - prev);
                prev = d.GainDb;
            }
            Assert.AreEqual(0f, d.GainDb, 0.05f, "fully back");
            Assert.Less(maxStep, 0.5f, "no jump (dB per frame)");
        }

        [Test]
        public void Ducker_GapBetweenSpeakers_DoesNotPump()
        {
            var d = new Ducker(-8f);
            for (int i = 0; i < 40; i++) d.Tick(1f / 72f, true);
            // SpeechFloor 換人前停 Gap 秒：這段時間不該回彈
            for (float t = 0; t < SpeechFloor.Gap; t += 1f / 72f) { d.Tick(1f / 72f, false); Assert.Less(d.GainDb, -7.5f); }
            d.Tick(1f / 72f, true);
            Assert.Less(d.GainDb, -7.5f);
        }

        [Test]
        public void AnyoneSpeaking_FollowsTheFloor()
        {
            var floor = new SpeechFloor();
            Assert.IsFalse(AudioMix.AnyoneSpeaking(floor, playerSpeaking: false));
            Assert.IsTrue(AudioMix.AnyoneSpeaking(floor, playerSpeaking: true), "the player's own voice ducks the bed too (less echo into the mic)");
            Assert.IsFalse(AudioMix.AnyoneSpeaking(null, false));
        }

        // ───────────── 環境音床 ─────────────

        [Test]
        public void Ambience_LoopsSeamlessly_IsQuiet_AndDeterministic()
        {
            var a = AmbienceSynth.Render(AmbienceSynth.Seconds, AmbienceSynth.SampleRate, 7);
            var b = AmbienceSynth.Render(AmbienceSynth.Seconds, AmbienceSynth.SampleRate, 7);
            Assert.AreEqual(a.Length, AmbienceSynth.Seconds * AmbienceSynth.SampleRate);
            CollectionAssert.AreEqual(a, b, "same seed → same bed");
            float peak = a.Max(Mathf.Abs);
            Assert.LessOrEqual(peak, 0.5f);
            // 接縫：最後一個樣本到第一個樣本的跳動不比一般相鄰樣本大
            float typical = 0f;
            for (int i = 1; i < a.Length; i++) typical = Mathf.Max(typical, Mathf.Abs(a[i] - a[i - 1]));
            Assert.LessOrEqual(Mathf.Abs(a[0] - a[a.Length - 1]), typical);
            // 接縫前後 100 ms 的 RMS 差不到 3 dB
            int w = AmbienceSynth.SampleRate / 10;
            Assert.AreEqual(Db(Rms(a, a.Length - w, w)), Db(Rms(a, 0, w)), 3f);
            Assert.AreEqual(0f, a.Average(), 0.01f, "no DC");
            // 給人耳試聽：兩輪接在一起（聽得到接縫的話就是沒接好），音量照遊戲裡的 AmbienceDb
            var dir = System.IO.Path.Combine(Application.dataPath, "..", "Logs", "scratch");
            System.IO.Directory.CreateDirectory(dir);
            WriteWav(System.IO.Path.Combine(dir, "ambience_bed_x2.wav"), a.Concat(a).Select(v => v * AudioMix.Linear(AudioMix.AmbienceDb)).ToArray(), AmbienceSynth.SampleRate);
        }

        [Test]
        public void Ambience_HasNoLowHum()
        {
            // 2026-11-03 使用者聽到「嗡嗡聲」：150 Hz 以下的能量要遠小於整體（只留遠處交談與籌碼聲）
            var a = AmbienceSynth.Render(AmbienceSynth.Seconds, AmbienceSynth.SampleRate, 7);
            // 三級一階低通串接（18 dB/oct），單級太緩會把 330 Hz 的交談也算進去
            double k = 1 - System.Math.Exp(-2 * System.Math.PI * 150 / AmbienceSynth.SampleRate), l1 = 0, l2 = 0, l3 = 0, low = 0, all = 0;
            foreach (var v in a) { l1 += k * (v - l1); l2 += k * (l1 - l2); l3 += k * (l2 - l3); low += l3 * l3; all += v * v; }
            Assert.Less(10 * System.Math.Log10(low / all), -14.0, "low-frequency hum vs. whole bed (dB)");
        }

        static void WriteWav(string path, float[] s, int rate)
        {
            using (var w = new System.IO.BinaryWriter(System.IO.File.Create(path)))
            {
                w.Write(Encoding.ASCII.GetBytes("RIFF")); w.Write(36 + s.Length * 2); w.Write(Encoding.ASCII.GetBytes("WAVEfmt "));
                w.Write(16); w.Write((short)1); w.Write((short)1); w.Write(rate); w.Write(rate * 2); w.Write((short)2); w.Write((short)16);
                w.Write(Encoding.ASCII.GetBytes("data")); w.Write(s.Length * 2);
                foreach (var v in s) w.Write((short)Mathf.Clamp(Mathf.RoundToInt(v * 32767f), -32768, 32767));
            }
        }

        // ───────────── 響度表 ─────────────

        [Test]
        public void Mix_SpeechStaysOnTop_AndPrintsTheTable()
        {
            float speech = SpeechLoudnessDb(out int clips, out float speechPeak);
            Assert.Greater(clips, 50, "measured the baked speech");
            var sb = new StringBuilder();
            sb.AppendLine($"[Mix] speech: {clips} clips, median integrated {speech:0.0} LUFS, peak {speechPeak:0.0} dBFS (volume 1, 3D at ~1.1 m ≈ −1.2 dB)");
            sb.AppendLine("[Mix] sound            call-vol  trim dB  max momentary LUFS  vs speech  ducked (speech on)");
            var bad = new List<string>();
            foreach (SfxKind k in System.Enum.GetValues(typeof(SfxKind)))
            {
                float vol = AudioMix.LoudestCallVolume(k);
                float m = Momentary(PokerSfx.Render(k), PokerSfx.SampleRate) + Db(vol) + AudioMix.TrimDb(k);
                float ducked = m + AudioMix.SfxDuckDb;
                sb.AppendLine($"[Mix] {k,-16} {vol,8:0.00} {AudioMix.TrimDb(k),8:0.0} {m,18:0.0} {m - speech,10:0.0} {ducked,10:0.0}");
                if (m > speech - AudioMix.SfxHeadroomDb) bad.Add($"{k}: {m:0.0} LUFS is not {AudioMix.SfxHeadroomDb} dB under speech");
                if (ducked > speech - 10f) bad.Add($"{k}: {ducked:0.0} LUFS while someone speaks");
            }
            var bed = AmbienceSynth.Render(AmbienceSynth.Seconds, AmbienceSynth.SampleRate, 7);
            float amb = Integrated(bed, AmbienceSynth.SampleRate) + AudioMix.AmbienceDb;
            sb.AppendLine($"[Mix] ambience bed     loop     {AudioMix.AmbienceDb,8:0.0} {amb,18:0.0} {amb - speech,10:0.0} {amb + AudioMix.AmbienceDuckDb,10:0.0}   (integrated)");
            Debug.Log(sb.ToString());
            Assert.IsEmpty(bad, string.Join("\n", bad));
            // 2026-11-03 使用者：環境音「一直有聲音」太明顯 → 壓到幾乎聽不出來（錄 demo 時不像雜音）
            Assert.LessOrEqual(amb, speech - 28f, "the bed is barely there");
            Assert.GreaterOrEqual(amb, speech - 38f, "…but not gone");
        }

        [Test]
        public void Voices_AreSpatializedTheSameWay()
        {
            var rig = new GameTestRig(new GameConfig { Seed = 3, MonteCarloTrials = 40 });
            try
            {
                var srcs = rig.Game.GazeTargets().Select(t => t.Who.Speech.Source).ToList();
                Assert.GreaterOrEqual(srcs.Count, 4, "3 AIs + the dealer");
                foreach (var s in srcs)
                {
                    Assert.AreEqual(1f, s.spatialBlend, s.name + ": full 3D");
                    Assert.AreEqual(srcs[0].minDistance, s.minDistance, s.name);
                    Assert.AreEqual(srcs[0].maxDistance, s.maxDistance, s.name);
                    Assert.AreEqual(srcs[0].rolloffMode, s.rolloffMode, s.name);
                    Assert.AreEqual(0f, s.dopplerLevel, s.name);
                    Assert.AreEqual(srcs[0].volume, s.volume, s.name);
                    Assert.AreEqual(srcs[0].spread, s.spread, s.name);
                    Assert.IsTrue(s.transform.parent.name.ToLower().Contains("head"), s.name + " sits on the head: " + s.transform.parent.name);
                }
                // 環境音床：2D、循環
                var bed = rig.Game.Ambience;
                Assert.IsNotNull(bed);
                Assert.AreEqual(0f, bed.spatialBlend);
                Assert.IsTrue(bed.loop);
            }
            finally { rig.Dispose(); }
        }

        [Test]
        public void Game_DucksSfxAndBed_WhileSomeoneSpeaks()
        {
            var rig = new GameTestRig(new GameConfig { Seed = 3, MonteCarloTrials = 40 });
            try
            {
                var g = rig.Game;
                rig.Idle(0.2);
                foreach (var t in g.GazeTargets()) t.Who.Speech.StopAll();
                rig.Idle(1.5);
                Assert.AreEqual(1f, g.Mix.SfxGain, 1e-3f, "nobody speaking");
                float bedQuiet = g.Ambience.volume;
                g.Dealer.Speech.Say("Your turn.", Poker.AI.SpeechPriority.Info);
                rig.Idle(0.2);
                Assert.IsTrue(g.Dealer.Speech.IsSpeaking);
                Assert.AreEqual(AudioMix.Linear(AudioMix.SfxDuckDb), g.Mix.SfxGain, 0.01f);
                Assert.Less(g.Ambience.volume, bedQuiet * 0.5f);
                Assert.AreEqual(g.Mix.SfxGain, g.Audio.Gain, 1e-5f, "the table SFX voices follow");
            }
            finally { rig.Dispose(); }
        }

        // ───────────── 量測 ─────────────

        static float Rms(float[] s, int from, int n)
        {
            double sum = 0;
            for (int i = from; i < from + n && i < s.Length; i++) sum += s[i] * s[i];
            return (float)System.Math.Sqrt(sum / Mathf.Max(1, n));
        }

        static float Db(float lin) => 20f * Mathf.Log10(Mathf.Max(lin, 1e-9f));

        /// <summary>ITU-R BS.1770 K 加權（高架濾波＋RLB 高通；係數依取樣率換算，同 pyloudnorm）。</summary>
        static float[] KWeight(float[] x, int fs)
        {
            double K = System.Math.Tan(System.Math.PI * 1681.974450955533 / fs), Q = 0.7071752369554196;
            double Vh = System.Math.Pow(10, 3.999843853973347 / 20), Vb = System.Math.Pow(Vh, 0.4996667741545416);
            double a0 = 1 + K / Q + K * K;
            var y = Biquad(x, (Vh + Vb * K / Q + K * K) / a0, 2 * (K * K - Vh) / a0, (Vh - Vb * K / Q + K * K) / a0, 2 * (K * K - 1) / a0, (1 - K / Q + K * K) / a0);
            K = System.Math.Tan(System.Math.PI * 38.13547087602444 / fs); Q = 0.5003270373238773;
            a0 = 1 + K / Q + K * K;
            return Biquad(y, 1, -2, 1, 2 * (K * K - 1) / a0, (1 - K / Q + K * K) / a0);
        }

        static float[] Biquad(float[] x, double b0, double b1, double b2, double a1, double a2)
        {
            var y = new float[x.Length];
            double x1 = 0, x2 = 0, y1 = 0, y2 = 0;
            for (int i = 0; i < x.Length; i++)
            {
                double v = b0 * x[i] + b1 * x1 + b2 * x2 - a1 * y1 - a2 * y2;
                x2 = x1; x1 = x[i]; y2 = y1; y1 = v;
                y[i] = (float)v;
            }
            return y;
        }

        /// <summary>400 ms 區塊（75% 重疊）的 K 加權響度（LUFS）；短於 400 ms 的聲音補靜音。</summary>
        static List<float> Blocks(float[] s, int fs)
        {
            int w = (int)(0.4f * fs), hop = w / 4;
            var k = KWeight(s.Length >= w ? s : s.Concat(new float[w - s.Length]).ToArray(), fs);
            var r = new List<float>();
            for (int i = 0; i + w <= k.Length; i += hop)
            {
                double sum = 0;
                for (int j = i; j < i + w; j++) sum += k[j] * k[j];
                r.Add((float)(-0.691 + 10 * System.Math.Log10(System.Math.Max(sum / w, 1e-12))));
            }
            return r;
        }

        /// <summary>最響的 momentary 響度（LUFS）。</summary>
        static float Momentary(float[] s, int fs) => Blocks(s, fs).Max();

        /// <summary>整段響度（LUFS，BS.1770 閘門：絕對 −70、相對 −10 LU）。</summary>
        static float Integrated(float[] s, int fs)
        {
            var b = Blocks(s, fs).Where(v => v > -70f).ToList();
            if (b.Count == 0) return -70f;
            float rel = Mean(b) - 10f;
            var g = b.Where(v => v > rel).ToList();
            return g.Count > 0 ? Mean(g) : -70f;
        }

        static float Mean(List<float> lufs) => (float)(10 * System.Math.Log10(lufs.Average(v => System.Math.Pow(10, (v + 0.691) / 10))) - 0.691);

        static float SpeechLoudnessDb(out int clips, out float peakDb)
        {
            var levels = new List<float>();
            float peak = 0f;
            foreach (var clip in Resources.LoadAll<AudioClip>("Speech"))
            {
                if (clip.length < 0.8f) continue;   // 拼句用的數字、片段不算
                var data = new float[clip.samples * clip.channels];
                if (!clip.GetData(data, 0)) continue;
                foreach (var v in data) peak = Mathf.Max(peak, Mathf.Abs(v));
                levels.Add(Integrated(data, clip.frequency));
            }
            clips = levels.Count;
            peakDb = Db(peak);
            levels.Sort();
            return levels.Count > 0 ? levels[levels.Count / 2] : -99f;
        }
    }
}
