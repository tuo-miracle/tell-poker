using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text;
using Poker.Gestures;

namespace Poker.Tests
{
    /// <summary>
    /// 錄製檔的診斷摘要：每隻手的關鍵數值（高度、掌心角度、捏合、離籌碼堆與牌的距離、咪牌抬起量、往下速度），
    /// 加上回放的事件與辨識器 Trace（每次下敲的判定、推牌與 All-in 的狀態）。重錄時用來快速判斷「為什麼沒觸發」。
    /// 用法：tools/run-core-tests.sh --where "test =~ Tool_PrintRecordingDiagnostics"（可設環境變數 GESTURE_DIAG_FILTER 篩檔名）。
    /// </summary>
    public static class RecordingDiagnostics
    {
        public static string SummarizeFile(string path, TableLayout layout = null, GestureTuning tuning = null)
        {
            var rec = HandRecording.Load(path);
            var name = Path.GetFileName(path);
            var exp = RecordingExpectation.Parse(name, rec.Note);
            var check = RecordingCheck.Evaluate(path);
            return Summarize(rec, name, exp.Context, layout, tuning, check.Labelled ? (check.Pass ? "OK" : check.Message.Split('\n')[0]) : "（沒有標示預期）");
        }

        public static string Summarize(HandRecording rec, string name, TableContext ctx, TableLayout layout = null, GestureTuning tuning = null, string verdict = null)
        {
            layout = layout ?? new TableLayout();
            tuning = tuning ?? new GestureTuning();
            var inv = CultureInfo.InvariantCulture;
            var sb = new StringBuilder();
            var frames = rec.Frames;
            double dur = frames.Count > 1 ? frames[frames.Count - 1].Time - frames[0].Time : 0;
            sb.AppendLine(string.Format(inv, "== {0}  {1} 幀  {2:0.00} s{3}", name, frames.Count, dur, verdict == null ? "" : "  → " + verdict));

            // 回放：事件、咪牌最大量、Trace
            var r = new GestureRecognizer(layout, tuning);
            var trace = new List<string>();
            r.Trace = trace.Add;
            var events = new List<string>();
            var peekMax = new float[2];
            double t0 = frames.Count > 0 ? frames[0].Time : 0;
            foreach (var f in frames)
            {
                foreach (var e in r.Update(f, ctx))
                    if (e.Kind != GestureKind.PeekChanged) events.Add(string.Format(inv, "{0:0.00} {1} {2}{3}", e.Time - t0, e.Kind, e.Hand,
                        e.Kind == GestureKind.ChipsReleased ? string.Format(inv, " n={0} at ({1:0.00},{2:0.00}) inZone={3}", e.Count, e.Position.X, e.Position.Z, e.InBetZone) :
                        e.Count > 0 ? " n=" + e.Count : ""));
                for (int i = 0; i < 2; i++) peekMax[i] = Math.Max(peekMax[i], r.PeekAmount((Hand)i));
            }

            for (int i = 0; i < 2; i++) sb.AppendLine(HandLine((Hand)i, frames, layout, tuning, peekMax[i]));
            sb.AppendLine("  events: " + (events.Count == 0 ? "(none)" : string.Join(" | ", events)));
            foreach (var line in trace)
            {
                // Trace 的時間是絕對時間：改成相對錄製開頭
                int sp = line.IndexOf(' ');
                if (sp > 0 && double.TryParse(line.Substring(0, sp), NumberStyles.Float, inv, out double t))
                    sb.AppendLine(string.Format(inv, "  . {0:0.00}{1}", t - t0, line.Substring(sp)));
                else sb.AppendLine("  . " + line);
            }
            return sb.ToString();
        }

        static string HandLine(Hand h, List<HandsFrame> frames, TableLayout layout, GestureTuning k, float peekMax)
        {
            var inv = CultureInfo.InvariantCulture;
            int tracked = 0, lowConf = 0, pinchFrames = 0, flagFrames = 0;
            var palmY = new List<float>();
            float angMin = 180, angMax = 0, pinchMax = 0, gapMin = float.MaxValue, stackMin = float.MaxValue, stackMinY = 0;
            float palmCards = float.MaxValue, tipCards = float.MaxValue, liftMax = 0, zMin = float.MaxValue, zMax = float.MinValue;
            float downMax = 0, tipDownMax = 0;
            var cards = layout.CardsRect;
            for (int n = 0; n < frames.Count; n++)
            {
                var p = frames[n].Get(h);
                if (!p.Tracked) continue;
                tracked++;
                if (p.Confidence < k.MinConfidence) { lowConf++; continue; }
                var palm = p.Position(HandJoint.Palm);
                var it = p.Position(HandJoint.IndexTip);
                var mt = p.Position(HandJoint.MiddleTip);
                var pp = (p.Position(HandJoint.ThumbTip) + it) * 0.5f;
                palmY.Add(palm.Y - layout.TableY);
                var nrm = p.PalmNormal;
                float ang = (float)(Math.Acos(Math.Max(-1f, Math.Min(1f, nrm.Y / Math.Max(1e-6f, nrm.Length())))) * 180 / Math.PI);
                angMin = Math.Min(angMin, ang); angMax = Math.Max(angMax, ang);
                pinchMax = Math.Max(pinchMax, p.IndexPinch);
                if (p.IndexPinch >= k.PinchStart) pinchFrames++;
                if (p.IndexPinching) flagFrames++;
                gapMin = Math.Min(gapMin, Vector3.Distance(p.Position(HandJoint.ThumbTip), it));
                float ds = Horiz(pp, layout.ChipStackCenter);
                if (ds < stackMin) { stackMin = ds; stackMinY = pp.Y - layout.TableY; }
                float dp = RectDist(cards, palm), dt = RectDist(cards, mt);
                palmCards = Math.Min(palmCards, dp);
                tipCards = Math.Min(tipCards, dt);
                if (Math.Min(dp, dt) <= k.PeekHoverMargin)
                    liftMax = Math.Max(liftMax, p.Position(HandJoint.Wrist).Y - Math.Min(it.Y, mt.Y));
                zMin = Math.Min(zMin, palm.Z); zMax = Math.Max(zMax, palm.Z);
                // 往下速度：約 50 ms 的差分（與辨識器的速度時間窗同量級）
                int m = n - 4;
                if (m >= 0 && frames[m].Get(h).Tracked)
                {
                    var q = frames[m].Get(h);
                    float dtm = (float)(frames[n].Time - frames[m].Time);
                    if (dtm > 1e-4f)
                    {
                        downMax = Math.Max(downMax, (q.Position(HandJoint.Palm).Y - palm.Y) / dtm);
                        float tipNow = Math.Max(it.Y, mt.Y), tipThen = Math.Max(q.Position(HandJoint.IndexTip).Y, q.Position(HandJoint.MiddleTip).Y);
                        tipDownMax = Math.Max(tipDownMax, (tipThen - tipNow) / dtm);
                    }
                }
            }
            if (tracked == 0) return string.Format(inv, "  {0}: 未追蹤", h);
            palmY.Sort();
            return string.Format(inv,
                "  {0}: tracked {1}/{2} lowConf {3} | palm y min/med/max {4:0.000}/{5:0.000}/{6:0.000} z {7:0.00}..{8:0.00} | palm angle {9:0}..{10:0}° | " +
                "pinch max {11:0.00} (≥start {12} 幀, flag {13} 幀) gap min {14:0.000} | pinch→stack min {15:0.000} (y {16:0.000}) | " +
                "cards dist palm {17:0.000} tip {18:0.000} | peek lift max {19:0.000} peek max {20:0.00} | down max palm {21:0.00} tip {22:0.00} m/s",
                h, tracked, frames.Count, lowConf, palmY[0], palmY[palmY.Count / 2], palmY[palmY.Count - 1], zMin, zMax, angMin, angMax,
                pinchMax, pinchFrames, flagFrames, gapMin, stackMin, stackMinY, palmCards, tipCards, liftMax, peekMax, downMax, tipDownMax);
        }

        static float Horiz(Vector3 a, Vector3 b) => (float)Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Z - b.Z) * (a.Z - b.Z));

        static float RectDist(TableRect r, Vector3 p)
        {
            float dx = Math.Max(0, Math.Max(r.MinX - p.X, p.X - r.MaxX));
            float dz = Math.Max(0, Math.Max(r.MinZ - p.Z, p.Z - r.MaxZ));
            return Math.Max(dx, dz);
        }
    }
}
