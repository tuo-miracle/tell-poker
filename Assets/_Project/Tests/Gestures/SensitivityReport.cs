using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using Poker.Gestures;

namespace Poker.Tests
{
    /// <summary>
    /// 調參工具：一次印出「辨識率＋All-in／棄牌誤觸」的對照表（合成統計、日常雜動、全部真實錄製）。
    /// 改門檻前後各跑一次比較：tools/run-core-tests.sh --where "class == Poker.Tests.SensitivityReport"
    /// </summary>
    [Explicit("工具：調參前後對照表")]
    public class SensitivityReport
    {
        static readonly GestureKind[] AllInKinds = { GestureKind.AllInPushStarted, GestureKind.AllInConfirmed };
        static readonly GestureKind[] FoldKinds = { GestureKind.FoldPushed };

        static bool HasAny(List<GestureEvent> ev, GestureKind[] kinds) => ev.Any(e => kinds.Contains(e.Kind));

        static bool Picked(List<GestureEvent> ev) => ev.Any(e => e.Kind == GestureKind.ChipsPicked);
        static bool Lifted(List<GestureEvent> ev) => ev.Any(e => e.Kind == GestureKind.CardsLifted);
        static bool Bet(List<GestureEvent> ev) => ev.Any(e => e.Kind == GestureKind.BetCommitted);
        static bool InZone(List<GestureEvent> ev) => ev.Any(e => e.Kind == GestureKind.ChipsReleased && e.InBetZone);
        static float MaxPeek(List<GestureEvent> ev) => ev.Where(e => e.Kind == GestureKind.PeekChanged).Select(e => e.Amount).DefaultIfEmpty(0f).Max();

        /// <summary>環境變數 GESTURE_TUNING="欄位=值,欄位=值"（GestureTuning 的欄位名）覆寫門檻，不必改程式就能比較。</summary>
        static GestureTuning TuningFromEnv()
        {
            var spec = Environment.GetEnvironmentVariable("GESTURE_TUNING");
            if (string.IsNullOrWhiteSpace(spec)) return null;
            var t = new GestureTuning();
            foreach (var kv in spec.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var parts = kv.Split('=');
                var f = typeof(GestureTuning).GetField(parts[0].Trim()) ?? throw new ArgumentException("沒有這個門檻欄位：" + parts[0]);
                f.SetValue(t, float.Parse(parts[1], System.Globalization.CultureInfo.InvariantCulture));
            }
            TestContext.Progress.WriteLine("[sens] GESTURE_TUNING = " + spec);
            return t;
        }

        [Test]
        public void Print()
        {
            var tuning = TuningFromEnv();
            G.DefaultTuning = tuning;
            try { PrintAll(tuning); }
            finally { G.DefaultTuning = null; }
        }

        static void PrintAll(GestureTuning tuning)
        {
            TestContext.Progress.WriteLine("[sens] gesture       rate     AllIn   Fold  Knock  Picked  Peek>=0.3  Lifted  Bet  InZone   (trials of 200 containing that event)");
            foreach (var g in RobustnessStatsTests.CaseNames)
            {
                int ai = 0, fold = 0, kn = 0, pk = 0, pe = 0, li = 0, bt = 0, iz = 0;
                G.RunObserver = ev =>
                {
                    if (HasAny(ev, AllInKinds)) ai++;
                    if (HasAny(ev, FoldKinds)) fold++;
                    if (ev.Any(e => e.Kind == GestureKind.KnockCheck)) kn++;
                    if (Picked(ev)) pk++;
                    if (MaxPeek(ev) >= 0.3f) pe++;
                    if (Lifted(ev)) li++;
                    if (Bet(ev)) bt++;
                    if (InZone(ev)) iz++;
                };
                double rate;
                try { rate = RobustnessStatsTests.Rate(g); }
                finally { G.RunObserver = null; }
                TestContext.Progress.WriteLine($"[sens] {g,-13} {rate,6:P1} {ai,6} {fold,6} {kn,6} {pk,7} {pe,10} {li,7} {bt,4} {iz,7}");
            }

            // 單手推（籌碼堆旁）：不能變成 All-in
            {
                int ai = 0, n = 0;
                for (int i = 0; i < 200; i++)
                {
                    var h = i % 2 == 0 ? Hand.Left : Hand.Right;
                    var ev = G.Run(GestureScripts.OneHandPush(new SyntheticHands(seed: 7000 + i).WithNoise(0.01f).WithPinchCap(), h, 0.18f + 0.1f * (i % 10) / 10f));
                    n++;
                    if (HasAny(ev, AllInKinds)) ai++;
                }
                TestContext.Progress.WriteLine($"[sens] onehand push → AllIn: {ai}/{n}");
            }

            // All-in 延遲：從腳本開始推到 AllInConfirmed（中位數），以及反例的 All-in 事件數（各 200 次、1 cm 雜訊）
            {
                var rng = new Random(4242);
                float U(float a, float b) => a + (float)rng.NextDouble() * (b - a);
                string Latency(string name, Func<int, (SyntheticHands s, double start)> make)
                {
                    var lat = new List<double>();
                    int n = 200;
                    for (int i = 0; i < n; i++)
                    {
                        var (sh, start) = make(8000 + i);
                        var ev = G.Run(sh);
                        var c = ev.FirstOrDefault(e => e.Kind == GestureKind.AllInConfirmed);
                        if (c.Kind == GestureKind.AllInConfirmed) lat.Add(c.Time - start);
                    }
                    lat.Sort();
                    string med = lat.Count == 0 ? "   -  " : $"{lat[lat.Count / 2],6:0.000}";
                    return $"[sens] latency {name,-11} confirmed {lat.Count,3}/{n}  median push→confirmed {med} s";
                }
                SyntheticHands N(int seed) => new SyntheticHands(seed: seed).WithNoise(0.01f).WithPinchCap();
                TestContext.Progress.WriteLine(Latency("allin", seed => (GestureScripts.AllIn(N(seed), U(0.14f, 0.24f), U(0.24f, 0.32f), U(0.4f, 0.8f)), 1.05)));
                TestContext.Progress.WriteLine(Latency("allin_soft", seed =>
                {
                    float dist = U(0.085f, 0.10f), endTip = U(0.31f, 0.34f);
                    var center = new System.Numerics.Vector3(U(-0.05f, 0.20f), 0f, endTip - 0.10f - dist + 0.01f);
                    return (GestureScripts.AllIn(N(seed), U(0.14f, 0.24f), dist, U(0.4f, 0.8f), center: center), 1.05);
                }));
                TestContext.Progress.WriteLine(Latency("allin_edge", seed =>
                    (GestureScripts.AllInFromEdge(N(seed), U(0.02f, 0.045f), U(0.12f, 0.15f), U(0.3f, 0.7f), U(0.22f, 0.34f), U(-0.04f, 0.06f), desync: U(-0.03f, 0.03f)), GestureScripts.EdgePushStart)));
                TestContext.Progress.WriteLine(Latency("allin_dev", seed =>
                    (GestureScripts.AllInFromEdge(N(seed), U(0.01f, 0.19f), U(0.18f, 0.28f), U(0.3f, 0.7f), U(0.22f, 0.34f), U(-0.04f, 0.06f), desync: U(-0.03f, 0.03f)), GestureScripts.EdgePushStart)));

                int driftS = 0, driftC = 0, slideS = 0, slideC = 0;
                for (int i = 0; i < 200; i++)
                {
                    var ev = G.Run(GestureScripts.RestingDrift(N(8500 + i), U(0.10f, 0.12f), U(1.6f, 2.4f), U(0.02f, 0.10f), U(0.22f, 0.34f), ease: i % 2 == 0 ? Ease.Smooth : Ease.Linear));
                    if (ev.Any(e => e.Kind == GestureKind.AllInPushStarted)) driftS++;
                    if (ev.Any(e => e.Kind == GestureKind.AllInConfirmed)) driftC++;
                    double lag = U(0.35f, 1.0f) * (i % 3 == 0 ? -1 : 1);
                    var ev2 = G.Run(GestureScripts.SlideCardsAndChips(N(8700 + i), lag, U(0.10f, 0.15f), U(0.10f, 0.16f), U(0.4f, 0.8f)));
                    if (ev2.Any(e => e.Kind == GestureKind.AllInPushStarted)) slideS++;
                    if (ev2.Any(e => e.Kind == GestureKind.AllInConfirmed)) slideC++;
                }
                TestContext.Progress.WriteLine($"[sens] resting drift 10–12 cm/1.6–2.4 s → AllInPushStarted {driftS}/200, AllInConfirmed {driftC}/200");
                TestContext.Progress.WriteLine($"[sens] slide cards+chips separately → AllInPushStarted {slideS}/200, AllInConfirmed {slideC}/200");
            }

            // 日常雜動
            {
                int ai = 0, fold = 0, any = 0, kn = 0, pk = 0, pe = 0, pe0 = 0, pe5 = 0, li = 0, bt = 0, iz = 0;
                G.RunObserver = ev =>
                {
                    if (HasAny(ev, AllInKinds)) ai++;
                    if (HasAny(ev, FoldKinds)) fold++;
                    if (ev.Any(e => e.IsAction)) any++;
                    if (ev.Any(e => e.Kind == GestureKind.KnockCheck)) kn++;
                    if (Picked(ev)) pk++;
                    float m = MaxPeek(ev);
                    if (m >= 0.3f) pe++;
                    if (m >= 0.5f) pe5++;
                    if (m > 0f) pe0++;
                    if (Lifted(ev)) li++;
                    if (Bet(ev)) bt++;
                    if (InZone(ev)) iz++;
                };
                try { RobustnessStatsTests.FidgetFalseTriggers(500); }
                finally { G.RunObserver = null; }
                TestContext.Progress.WriteLine($"[sens] fidget 500 runs: any action {any}, AllIn {ai}, Fold {fold}, Knock {kn}, Picked {pk}, Peek>=0.3 {pe}, Peek>=0.5 {pe5}, Peek>0 {pe0}, CardsLifted {li}, Bet {bt}, InZone {iz}");
            }

            // 還沒輪到但還在這一手（CanActEarly）：日常雜動的提前棄牌與「太早了」
            {
                var (bad, _, samples) = RobustnessStatsTests.FidgetFalseTriggers(500, early: true);
                TestContext.Progress.WriteLine($"[sens] fidget early 500 runs: early fold or too-early {bad}");
                foreach (var x in samples.Take(4)) TestContext.Progress.WriteLine("[sens]   " + x);
            }

            // 全部真實錄製（含 v1 位置不符與已知問題，全部列入）
            var dir = RecordingCheck.FindRecordingsDir();
            var files = Directory.GetFiles(dir, "*.hands").OrderBy(f => f, StringComparer.Ordinal).ToList();
            TestContext.Progress.WriteLine("[sens] recordings group   files  pass  AllInStarted  AllInConfirmed  FoldPushed  Knock  Picked  Peek>=0.3  Peek>=0.6  Lifted  Bet  InZone");
            foreach (var grp in files.GroupBy(f =>
                     {
                         var p = Path.GetFileNameWithoutExtension(f).Split('_');
                         return p[0] + (p.Length > 1 && p[1] == "v2" ? "_v2" : "");
                     }).OrderBy(x => x.Key, StringComparer.Ordinal))
            {
                int n = 0, pass = 0, st = 0, cf = 0, fp = 0, kn = 0, pk = 0, pe = 0, pe3 = 0, li = 0, bt = 0, iz = 0;
                foreach (var f in grp)
                {
                    var rec = HandRecording.Load(f);
                    var exp = RecordingExpectation.Parse(Path.GetFileName(f), rec.Note);
                    var ctx = exp.Context;
                    var ev = ReplayRunner.Run(rec, _ => ctx, null, tuning);
                    n++;
                    if (RecordingCheck.Evaluate(f, tuning).Pass) pass++;
                    if (ev.Any(e => e.Kind == GestureKind.AllInPushStarted)) st++;
                    if (ev.Any(e => e.Kind == GestureKind.AllInConfirmed)) cf++;
                    if (ev.Any(e => e.Kind == GestureKind.FoldPushed)) fp++;
                    if (ev.Any(e => e.Kind == GestureKind.KnockCheck)) kn++;
                    if (Picked(ev)) pk++;
                    if (MaxPeek(ev) >= 0.6f) pe++;
                    if (MaxPeek(ev) >= 0.3f) pe3++;
                    if (Lifted(ev)) li++;
                    if (Bet(ev)) bt++;
                    if (InZone(ev)) iz++;
                }
                TestContext.Progress.WriteLine($"[sens] rec {grp.Key,-12} {n,5} {pass,5} {st,13} {cf,15} {fp,11} {kn,6} {pk,7} {pe3,10} {pe,10} {li,7} {bt,4} {iz,7}");
            }
            // 同一批錄製改成「還沒輪到、還在這一手」重播：提前棄牌、太早了（籌碼丟進下注區或敲桌）各幾段
            TestContext.Progress.WriteLine("[sens] recordings early   files  FoldPushed  TooEarly(chips)  TooEarly(knock/all-in)  Picked  any action");
            foreach (var grp in files.GroupBy(f =>
                     {
                         var p = Path.GetFileNameWithoutExtension(f).Split('_');
                         return p[0] + (p.Length > 1 && p[1] == "v2" ? "_v2" : "");
                     }).OrderBy(x => x.Key, StringComparer.Ordinal))
            {
                int n = 0, fp = 0, tc = 0, tk = 0, pk = 0, any = 0;
                foreach (var f in grp)
                {
                    var rec = HandRecording.Load(f);
                    var ctx = RecordingExpectation.Parse(Path.GetFileName(f), rec.Note).Context;
                    ctx.IsPlayerTurn = false;
                    ctx.CanActEarly = ctx.Phase != TablePhase.Showdown;
                    var ev = ReplayRunner.Run(rec, _ => ctx, null, tuning);
                    n++;
                    if (ev.Any(e => e.Kind == GestureKind.FoldPushed)) fp++;
                    if (ev.Any(e => e.Kind == GestureKind.ActionTooEarly && e.Count > 0)) tc++;
                    if (ev.Any(e => e.Kind == GestureKind.ActionTooEarly && e.Count == 0)) tk++;
                    if (Picked(ev)) pk++;
                    if (ev.Any(e => e.IsAction)) any++;
                }
                TestContext.Progress.WriteLine($"[sens] early {grp.Key,-12} {n,5} {fp,11} {tc,16} {tk,16} {pk,7} {any,11}");
            }
            // 失敗的錄製逐一列出（比較門檻時看是哪幾段變了）
            foreach (var f in files)
            {
                var r = RecordingCheck.Evaluate(f, tuning);
                if (r.Labelled && !r.Pass) TestContext.Progress.WriteLine("[sens] fail " + r.Message.Split('\n')[0]);
            }
            Assert.Pass();
        }
    }
}
