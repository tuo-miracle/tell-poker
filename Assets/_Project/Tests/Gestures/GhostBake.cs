using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Numerics;
using Poker.Gestures;

namespace Poker.Tests
{
    /// <summary>一段示範手的來源與裁切方式。</summary>
    public sealed class GhostSpec
    {
        /// <summary>檔名（遊戲端 Resources/Ghosts/&lt;Name&gt;.bytes）。</summary>
        public string Name;
        /// <summary>錄製檔分組（檔名第一段，v2 加 "_v2"，同 ReplayTests 的分組）。</summary>
        public string[] Groups = new string[0];
        /// <summary>沒有可用錄製時的合成動作（測試套件的 GestureScripts）。</summary>
        public Func<SyntheticHands> Synthetic;
        /// <summary>這段要示範的關鍵事件（第一次成立的時間當裁切中心）。</summary>
        public Func<GestureEvent, bool> Key;
        /// <summary>關鍵事件前後保留的秒數。</summary>
        public double Lead, Tail;
        public TableContext Context = TableContext.Default;
        /// <summary>雙手手勢（All-in）：兩隻手都畫；否則只畫做出關鍵事件的那隻手。</summary>
        public bool BothHands;
        /// <summary>&gt; 0：片段從「手掌已經在關鍵事件位置水平這麼近（公尺）以內」才開始（裁掉伸手過來的那段）。</summary>
        public float ApproachRadius;
    }

    public sealed class GhostPick
    {
        public string Source;
        public HandRecording Clip;
        public double Score;
        /// <summary>來源在 spec.Groups 的順位（越小越優先）。</summary>
        public int Group;
        /// <summary>每個候選的分數或淘汰原因。</summary>
        public string Report;
    }

    /// <summary>
    /// 新手教學的示範手：從 Recordings 挑「回放通過、多餘動作最少、抖動最小」的一段，裁到手勢片段（關鍵事件前 Lead、後 Tail 秒），
    /// 從頭到尾幾乎沒動的那隻手標成未追蹤（不畫），烘焙成 v2 .hands 存到 Scripts/Game/Resources/Ghosts/&lt;名稱&gt;.bytes。
    /// 沒有乾淨錄製的手勢（拿起來看、翻腕咪牌）用 GestureScripts 合成。
    /// </summary>
    public static class GhostBake
    {
        public const string Extension = ".bytes";
        /// <summary>手掌移動不到這個距離的手當成沒參與（公尺）。</summary>
        public const float MinTravel = 0.04f;

        /// <summary>從 from 起、到 key 為止手掌都在 key 那一刻手掌水平 radius 以內的最早時間（裁掉伸手過來的那段）。</summary>
        public static double ApproachEnd(HandRecording rec, double from, double key, Hand hand, float radius)
        {
            HandPose at = null;
            foreach (var f in rec.Frames)
                if (f.Time >= key - 1e-9 && f.Get(hand).Tracked) { at = f.Get(hand); break; }
            if (at == null) return from;
            var target = at.Position(HandJoint.Palm);
            double start = from;
            // 往回找：最後一個離開半徑的幀之後就是開始
            foreach (var f in rec.Frames)
            {
                if (f.Time < from - 1e-9 || f.Time > key + 1e-9) continue;
                var p = f.Get(hand);
                if (!p.Tracked) continue;
                var d = p.Position(HandJoint.Palm) - target;
                if (new Vector2(d.X, d.Z).Length() > radius) start = f.Time + 1e-6;
            }
            if (start <= from) return from;
            // 對齊到下一幀的時間
            foreach (var f in rec.Frames) if (f.Time >= start - 1e-9) return Math.Max(from, f.Time);
            return from;
        }

        static readonly TableContext Showdown = new TableContext { IsPlayerTurn = true, CanCheck = true, Phase = TablePhase.Showdown, PlayerChips = 1000, ChipDenomination = 50 };

        public static readonly GhostSpec[] Specs =
        {
            new GhostSpec { Name = "peek", Groups = new[] { "peek_v2", "peek" }, Key = e => e.Kind == GestureKind.PeekChanged && e.Amount >= 0.6f, Lead = 1.6, Tail = 1.0, ApproachRadius = 0.05f },
            new GhostSpec { Name = "rollpeek", Synthetic = () => GestureScripts.RollPeek(new SyntheticHands(), Hand.Right), Key = e => e.Kind == GestureKind.PeekChanged && e.Amount >= 0.45f, Lead = 1.4, Tail = 1.2 },
            new GhostSpec { Name = "look", Synthetic = () => GestureScripts.LiftCards(new SyntheticHands(), Hand.Right, hold: 1.2), Key = e => e.Kind == GestureKind.CardsLifted, Lead = 1.2, Tail = 2.2 },
            new GhostSpec { Name = "chips", Groups = new[] { "bet_v2" }, Key = e => e.Kind == GestureKind.ChipCountChanged && e.Count >= 2, Lead = 1.6, Tail = 0.8 },
            new GhostSpec { Name = "bet", Groups = new[] { "bet_v2" }, Key = e => e.Kind == GestureKind.ChipsReleased && e.InBetZone, Lead = 2.6, Tail = 0.8 },
            new GhostSpec { Name = "knock", Groups = new[] { "knock", "knock_v2" }, Key = e => e.Kind == GestureKind.KnockCheck, Lead = 1.6, Tail = 0.6 },
            new GhostSpec { Name = "fold", Groups = new[] { "foldpush_v2" }, Key = e => e.Kind == GestureKind.FoldPushed, Lead = 1.6, Tail = 0.7 },
            new GhostSpec { Name = "allin", Groups = new[] { "allin_v2" }, Key = e => e.Kind == GestureKind.AllInConfirmed, Lead = 2.6, Tail = 0.5, BothHands = true },
            new GhostSpec { Name = "flip", Groups = new[] { "flip_v2", "flip" }, Key = e => e.Kind == GestureKind.ShowdownFlip, Lead = 1.4, Tail = 0.7, Context = Showdown },
        };

        /// <summary>同 ReplayTests 的分組：檔名第一段，v2 錄製加 "_v2"。</summary>
        public static string GroupOf(string path)
        {
            var parts = Path.GetFileNameWithoutExtension(path).ToLowerInvariant().Split('_');
            return parts[0] + (parts.Length > 1 && parts[1] == "v2" ? "_v2" : "");
        }

        /// <summary>取 [from, to]（錄製的絕對時間）內的幀，時間從 0 起算；only 有給就只留那隻手，否則整段沒動的手標成未追蹤（雙手都沒動就都留著）。不改來源。</summary>
        public static HandRecording Trim(HandRecording src, double from, double to, string note, Hand? only = null)
        {
            var clip = new HandRecording { Note = note ?? "" };
            foreach (var f in src.Frames)
            {
                if (f.Time < from - 1e-9 || f.Time > to + 1e-9) continue;
                var c = new HandsFrame { Time = f.Time, Left = f.Left.Clone(), Right = f.Right.Clone() };
                clip.Frames.Add(c);
            }
            if (clip.Frames.Count == 0) return clip;
            double t0 = clip.Frames[0].Time;
            foreach (var f in clip.Frames) f.Time -= t0;
            bool left = only.HasValue ? only == Hand.Left : Moves(clip, Hand.Left);
            bool right = only.HasValue ? only == Hand.Right : Moves(clip, Hand.Right);
            if (left || right)
                foreach (var f in clip.Frames)
                {
                    if (!left) f.Left.Tracked = false;
                    if (!right) f.Right.Tracked = false;
                }
            return clip;
        }

        /// <summary>這隻手的手掌離第一個追蹤到的位置最遠超過 MinTravel。</summary>
        static bool Moves(HandRecording rec, Hand h)
        {
            Vector3? first = null;
            foreach (var f in rec.Frames)
            {
                var p = f.Get(h);
                if (!p.Tracked) continue;
                var pos = p.Position(HandJoint.Palm);
                if (first == null) { first = pos; continue; }
                if (Vector3.Distance(first.Value, pos) >= MinTravel) return true;
            }
            return false;
        }

        public static bool Fires(GhostSpec spec, HandRecording clip) =>
            ReplayRunner.Run(clip, _ => spec.Context).Any(spec.Key);

        /// <summary>從錄製檔挑最好的一段：要通過 RecordingCheck（v1 位置不符、ReplayTests.KnownIssues 列出的不用）、裁切後回放仍觸發關鍵事件；
        /// 先比分組順位（spec.Groups 越前面越優先），同組再比分數 = 抖動（mm）＋多餘行動 × 5＋追蹤遺失比例 × 10，越低越好。</summary>
        public static GhostPick Choose(GhostSpec spec, IEnumerable<string> files)
        {
            var lines = new List<string>();
            GhostPick best = null;
            foreach (var path in files.OrderBy(f => Path.GetFileName(f), StringComparer.Ordinal))
            {
                var name = Path.GetFileName(path);
                if (ReplayTests.KnownPositionMismatch(path) != null) { lines.Add(name + ": skip (v1 position mismatch)"); continue; }
                // 已知判斷有疑慮的錄製（就算現在通過）不拿來當示範：例如 peek_v2_153110 是手側立，不像抬手腕咪牌
                if (ReplayTests.KnownIssues.TryGetValue(name, out var issue)) { lines.Add(name + ": skip (known issue: " + issue + ")"); continue; }
                var check = RecordingCheck.Evaluate(path);
                if (!check.Pass) { lines.Add(name + ": skip (replay fails: " + check.Message.Split('\n')[0] + ")"); continue; }
                var pick = Evaluate(spec, HandRecording.Load(path), name, lines);
                if (pick == null) continue;
                // 分組排在前面的優先（例如 v2：錄製時桌上畫了牌與籌碼堆，位置可信），同一組裡再比分數
                pick.Group = Math.Max(0, Array.IndexOf(spec.Groups, GroupOf(path)));
                if (best == null || pick.Group < best.Group || pick.Group == best.Group && pick.Score < best.Score) best = pick;
            }
            if (best != null) best.Report = string.Join("\n", lines);
            return best;
        }

        /// <summary>裁切一段並打分；觸發不了關鍵事件就回傳 null（原因寫進 lines）。</summary>
        public static GhostPick Evaluate(GhostSpec spec, HandRecording rec, string source, List<string> lines)
        {
            var inv = CultureInfo.InvariantCulture;
            if (rec.Frames.Count < 2) { lines.Add(source + ": empty"); return null; }
            var all = ReplayRunner.Run(rec, _ => spec.Context);
            var keyEvents = all.Where(spec.Key).ToList();
            if (keyEvents.Count == 0) { lines.Add(source + ": no key event"); return null; }
            double? key = keyEvents[0].Time;
            Hand? only = spec.BothHands ? (Hand?)null : keyEvents[0].Hand;
            double start = rec.Frames[0].Time, end = rec.Frames[rec.Frames.Count - 1].Time;
            double from = Math.Max(start, key.Value - spec.Lead), to = Math.Min(end, key.Value + spec.Tail);
            if (spec.ApproachRadius > 0f) from = ApproachEnd(rec, from, key.Value, keyEvents[0].Hand, spec.ApproachRadius);
            var note = string.Format(inv, "ghost {0} from {1} [{2:0.00}-{3:0.00} s]", spec.Name, source, from - start, to - start);
            var clip = Trim(rec, from, to, note, only);
            var ev = ReplayRunner.Run(clip, _ => spec.Context);
            if (!ev.Any(spec.Key)) { lines.Add(source + ": trimmed clip loses the key event"); return null; }
            int extra = ev.Count(e => e.IsAction && !spec.Key(e));
            double jitter = Jitter(clip, out double lost);
            double score = jitter + 5 * extra + 10 * lost;
            lines.Add(string.Format(inv, "{0}: score {1:0.00} (jitter {2:0.00} mm, extra actions {3}, lost {4:0.00})", source, score, jitter, extra, lost));
            return new GhostPick { Source = source, Clip = clip, Score = score };
        }

        /// <summary>有畫出來的手，手掌每幀二階差分的平均長度（mm）；lost = 這些手未追蹤幀的比例。</summary>
        static double Jitter(HandRecording clip, out double lost)
        {
            double sum = 0;
            int n = 0, missing = 0, total = 0;
            var drawn = new[] { clip.Frames.Any(f => f.Left.Tracked), clip.Frames.Any(f => f.Right.Tracked) };
            for (int h = 0; h < 2; h++)
            {
                if (!drawn[h]) continue;
                for (int i = 0; i < clip.Frames.Count; i++)
                {
                    total++;
                    if (!clip.Frames[i].Get((Hand)h).Tracked) { missing++; continue; }
                    if (i < 1 || i + 1 >= clip.Frames.Count) continue;
                    var a = clip.Frames[i - 1].Get((Hand)h);
                    var c = clip.Frames[i + 1].Get((Hand)h);
                    if (!a.Tracked || !c.Tracked) continue;
                    var d = a.Position(HandJoint.Palm) - 2 * clip.Frames[i].Get((Hand)h).Position(HandJoint.Palm) + c.Position(HandJoint.Palm);
                    sum += d.Length() * 1000.0;
                    n++;
                }
            }
            lost = total == 0 ? 1 : missing / (double)total;
            return n == 0 ? 0 : sum / n;
        }

        /// <summary>Assets/_Project/Scripts/Game/Resources/Ghosts（從錄製資料夾往上找）。</summary>
        public static string FindOutputDir(bool create = false)
        {
            var rec = RecordingCheck.FindRecordingsDir();
            if (rec == null) return null;
            var project = Directory.GetParent(rec).Parent.Parent.FullName; // Recordings → Gestures → Tests → _Project
            var dir = Path.Combine(project, "Scripts", "Game", "Resources", "Ghosts");
            if (create) Directory.CreateDirectory(dir);
            return Directory.Exists(dir) ? dir : null;
        }

        /// <summary>烘焙全部示範手：有錄製就挑錄製，否則用合成。回傳報表行。</summary>
        public static IEnumerable<string> BakeAll(string recordingsDir, string outDir)
        {
            var files = recordingsDir == null ? new string[0] : Directory.GetFiles(recordingsDir, "*.hands");
            foreach (var spec in Specs)
            {
                GhostPick pick = null;
                string report = "";
                if (spec.Groups.Length > 0)
                {
                    pick = Choose(spec, files.Where(f => spec.Groups.Contains(GroupOf(f))));
                    report = pick?.Report ?? "(no usable recording)";
                }
                if (pick == null && spec.Synthetic != null)
                {
                    var s = spec.Synthetic();
                    var rec = new HandRecording();
                    rec.Frames.AddRange(s.Frames);
                    var lines = new List<string>();
                    pick = Evaluate(spec, rec, "synthetic GestureScripts", lines);
                    report += string.Join("\n", lines);
                }
                if (pick == null) { yield return "== " + spec.Name + ": FAILED\n" + report; continue; }
                var path = Path.Combine(outDir, spec.Name + Extension);
                using (var fs = File.Create(path))
                using (var w = new HandRecordingWriter(fs, pick.Clip.Note))
                    foreach (var f in pick.Clip.Frames) w.Write(f);
                yield return "== " + pick.Clip.Note + string.Format(CultureInfo.InvariantCulture, " ({0} frames)\n", pick.Clip.Frames.Count) + report;
            }
        }
    }
}
