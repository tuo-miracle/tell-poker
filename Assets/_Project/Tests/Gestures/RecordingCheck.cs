using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Poker.Gestures;

namespace Poker.Tests
{
    /// <summary>
    /// 錄製檔的「預期事件」。檔名慣例：[no_]&lt;手勢&gt;[_&lt;說明&gt;]_&lt;編號&gt;.hands，例如
    /// knock_01（預期 KnockCheck）、no_knock_single_03（預期沒有 KnockCheck）、idle_desk_01（不該有任何行動事件）。
    /// 手勢：peek、chips、bet、retract、knock、foldpush（或 push）、allin、flip、idle。
    /// slam（舊的摔牌標籤，2026-10-13 摔牌手勢移除）當反例：和 idle 一樣不該有任何行動事件（拍牌不能棄牌、過牌）。
    /// Note（每行 key: value）可覆寫或補充：expect、expect_not、turn、can_check、phase、chips、denom、min_peek。
    /// 咪牌錄製另外要求牌角最大翹起量 ≥ MinPeekAmount（預設 0.6）：只「有發出 PeekChanged」不代表掀得起來。
    /// </summary>
    public sealed class RecordingExpectation
    {
        public readonly List<GestureKind> Expect = new List<GestureKind>();
        public readonly List<GestureKind> ExpectNot = new List<GestureKind>();
        public bool NoActions;
        /// <summary>咪牌錄製：PeekChanged 最大 amount 的下限（0 = 不檢查）。</summary>
        public float MinPeekAmount;
        public TableContext Context = TableContext.Default;
        public bool IsLabelled => Expect.Count > 0 || ExpectNot.Count > 0 || NoActions;

        static readonly Dictionary<string, GestureKind> Words = new Dictionary<string, GestureKind>
        {
            ["peek"] = GestureKind.PeekChanged,
            ["chips"] = GestureKind.ChipsPicked,
            ["bet"] = GestureKind.BetCommitted,
            ["retract"] = GestureKind.BetRetracted,
            ["knock"] = GestureKind.KnockCheck,
            ["foldpush"] = GestureKind.FoldPushed,
            ["push"] = GestureKind.FoldPushed,
            ["allin"] = GestureKind.AllInConfirmed,
            ["flip"] = GestureKind.ShowdownFlip,
        };

        public static RecordingExpectation Parse(string fileName, string note)
        {
            var e = new RecordingExpectation();
            var parts = Path.GetFileNameWithoutExtension(fileName).ToLowerInvariant().Split('_');
            bool negative = parts.Length > 0 && parts[0] == "no";
            var words = negative ? parts.Skip(1).ToArray() : parts;
            if (words.Length > 0)
            {
                if (words[0] == "idle" || words[0] == "slam") e.NoActions = true;
                else if (Words.TryGetValue(words[0], out var kind))
                {
                    (negative ? e.ExpectNot : e.Expect).Add(kind);
                    if (kind == GestureKind.ShowdownFlip) e.Context.Phase = TablePhase.Showdown;
                    if (kind == GestureKind.PeekChanged && !negative) e.MinPeekAmount = 0.6f;
                }
            }

            foreach (var raw in (note ?? "").Split('\n'))
            {
                int colon = raw.IndexOf(':');
                if (colon < 0) continue;
                string key = raw.Substring(0, colon).Trim().ToLowerInvariant(), val = raw.Substring(colon + 1).Trim();
                switch (key)
                {
                    case "expect":
                        e.Expect.Clear();
                        e.Expect.AddRange(Kinds(val));
                        break;
                    case "expect_not":
                        e.ExpectNot.Clear();
                        e.ExpectNot.AddRange(Kinds(val));
                        break;
                    case "turn": e.Context.IsPlayerTurn = bool.Parse(val); break;
                    case "can_check": e.Context.CanCheck = bool.Parse(val); break;
                    case "phase": e.Context.Phase = (TablePhase)Enum.Parse(typeof(TablePhase), val, true); break;
                    case "chips": e.Context.PlayerChips = int.Parse(val); break;
                    case "denom": e.Context.ChipDenomination = int.Parse(val); break;
                    case "min_peek": e.MinPeekAmount = float.Parse(val, System.Globalization.CultureInfo.InvariantCulture); break;
                }
            }
            return e;
        }

        static IEnumerable<GestureKind> Kinds(string csv) =>
            csv.Split(new[] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries).Select(s => (GestureKind)Enum.Parse(typeof(GestureKind), s, true));
    }

    public static class RecordingCheck
    {
        public struct Result
        {
            public bool Pass, Labelled;
            public string Message;
        }

        public static Result Evaluate(string path, GestureTuning tuning = null)
        {
            var rec = HandRecording.Load(path);
            var exp = RecordingExpectation.Parse(Path.GetFileName(path), rec.Note);
            var name = Path.GetFileName(path);
            if (!exp.IsLabelled)
                return new Result { Labelled = false, Message = $"{name}: 檔名與 Note 都沒有標示預期事件" };
            var ctx = exp.Context;
            var ev = ReplayRunner.Run(rec, _ => ctx, null, tuning);
            var problems = new List<string>();
            foreach (var k in exp.Expect)
                if (!ev.Any(e => e.Kind == k)) problems.Add("缺少 " + k);
            foreach (var k in exp.ExpectNot)
                if (ev.Any(e => e.Kind == k)) problems.Add("不該出現 " + k);
            if (exp.NoActions && ev.Any(e => e.IsAction)) problems.Add("不該有行動事件");
            if (exp.MinPeekAmount > 0)
            {
                var peeks = ev.Where(e => e.Kind == GestureKind.PeekChanged).ToList();
                float max = peeks.Count == 0 ? 0f : peeks.Max(e => e.Amount);
                if (max < exp.MinPeekAmount) problems.Add($"咪牌最大翹起量 {max:0.00} 低於 {exp.MinPeekAmount:0.00}");
            }
            string events = string.Join("\n", ev.Where(e => e.Kind != GestureKind.PeekChanged));
            return new Result
            {
                Labelled = true,
                Pass = problems.Count == 0,
                Message = $"{name} ({rec.Frames.Count} 幀): " + (problems.Count == 0 ? "OK" : string.Join("；", problems)) + "\n" + events,
            };
        }

        /// <summary>從目前目錄與測試組件位置往上找 Assets/_Project/Tests/Gestures/Recordings。</summary>
        public static string FindRecordingsDir()
        {
            var rel = Path.Combine("Assets", "_Project", "Tests", "Gestures", "Recordings");
            foreach (var start in new[] { Environment.CurrentDirectory, AppDomain.CurrentDomain.BaseDirectory })
            {
                for (var d = new DirectoryInfo(start); d != null; d = d.Parent)
                {
                    var candidate = Path.Combine(d.FullName, rel);
                    if (Directory.Exists(candidate)) return candidate;
                }
            }
            return null;
        }
    }
}
