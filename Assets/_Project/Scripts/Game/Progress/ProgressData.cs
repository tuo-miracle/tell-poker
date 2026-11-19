using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Poker.Game
{
    /// <summary>對某個 AI 的累計對戰紀錄。</summary>
    [Serializable]
    public sealed class OpponentRecord
    {
        public string Name;
        public int GamesPlayed, HandsPlayed, PlayerWon, TheyWon, ReadsCorrect, ReadsTotal;
    }

    /// <summary>某個手勢成功的次數（JsonUtility 存得下的形式）。</summary>
    [Serializable]
    public sealed class GestureCount
    {
        public string Gesture;
        public int Count;
    }

    /// <summary>
    /// 玩家進度（spec「Reason to Come Back」）：教學是否完成、局數、勝局、讀心正確率、每個 AI 的對戰紀錄。
    /// 用 JsonUtility 存成可讀的 JSON。
    /// </summary>
    [Serializable]
    public sealed class ProgressData
    {
        public const int CurrentVersion = 1;
        public int Version = CurrentVersion;
        public bool TutorialDone;
        /// <summary>完成的是第幾版教學（0＝沒有欄位的舊存檔；見 CompletedTutorialVersion）。</summary>
        public int TutorialVersion;
        public int GamesPlayed, GamesWon, HandsPlayed, HandsWon, ReadsCorrect, ReadsTotal, BestPot;
        public List<OpponentRecord> Opponents = new List<OpponentRecord>();

        /// <summary>每個手勢成功做過的次數（牌局中的提示做過 GestureHints.SuppressAfter 次就不再出現）。舊存檔沒有這個欄位 → 空。</summary>
        public List<GestureCount> Gestures = new List<GestureCount>();

        /// <summary>手勢名稱（enum 的 ToString 每次都會配置字串；提示排程每幀都會查）。</summary>
        static readonly string[] GestureKeys = Enum.GetNames(typeof(GhostGesture));
        static string KeyOf(GhostGesture g) => (int)g >= 0 && (int)g < GestureKeys.Length ? GestureKeys[(int)g] : g.ToString();

        public int Successes(GhostGesture g)
        {
            if (Gestures == null) return 0;
            var key = KeyOf(g);
            for (int i = 0; i < Gestures.Count; i++)
            {
                var c = Gestures[i];
                if (c != null && c.Gesture == key) return c.Count;
            }
            return 0;
        }

        /// <summary>記一次成功；回傳新的次數。</summary>
        public int RecordSuccess(GhostGesture g)
        {
            if (Gestures == null) Gestures = new List<GestureCount>();
            var key = KeyOf(g);
            foreach (var c in Gestures)
                if (c != null && c.Gesture == key) return ++c.Count;
            Gestures.Add(new GestureCount { Gesture = key, Count = 1 });
            return 1;
        }

        /// <summary>完成過的教學版本：舊存檔只有 TutorialDone（舊的 4 步教學）就算 v1。</summary>
        public int CompletedTutorialVersion => TutorialVersion > 0 ? TutorialVersion : (TutorialDone ? 1 : 0);

        public float ReadAccuracy => ReadsTotal <= 0 ? 0f : ReadsCorrect / (float)ReadsTotal;

        /// <summary>找（或新增）某個 AI 的紀錄。</summary>
        public OpponentRecord Opponent(string name)
        {
            if (Opponents == null) Opponents = new List<OpponentRecord>();
            foreach (var o in Opponents) if (o != null && o.Name == name) return o;
            var r = new OpponentRecord { Name = name };
            Opponents.Add(r);
            return r;
        }

        public void RecordHand(HandResult hand, Func<int, string> nameOfSeat)
        {
            if (hand == null) return;
            HandsPlayed++;
            if (hand.PlayerWon) HandsWon++;
            BestPot = Math.Max(BestPot, hand.PotWon);
            foreach (var seat in hand.SeatsDealt)
            {
                var name = nameOfSeat?.Invoke(seat);
                if (seat == 0 || string.IsNullOrEmpty(name)) continue;
                var o = Opponent(name);
                o.HandsPlayed++;
                if (hand.PlayerWon) o.PlayerWon++;
                if (hand.Winners.Contains(seat)) o.TheyWon++;
            }
            foreach (var r in hand.Reads)
            {
                ReadsTotal++;
                if (r.Correct) ReadsCorrect++;
                var name = nameOfSeat?.Invoke(r.Seat);
                if (string.IsNullOrEmpty(name)) continue;
                var o = Opponent(name);
                o.ReadsTotal++;
                if (r.Correct) o.ReadsCorrect++;
            }
        }

        public void RecordGame(GameSummary summary, IEnumerable<string> opponents)
        {
            if (summary == null) return;
            GamesPlayed++;
            if (summary.Won) GamesWon++;
            if (opponents != null) foreach (var n in opponents) if (!string.IsNullOrEmpty(n)) Opponent(n).GamesPlayed++;
        }

        /// <summary>修正讀進來的怪值（負數、勝場大於場數、空的紀錄）。</summary>
        public void Sanitize()
        {
            GamesPlayed = Math.Max(0, GamesPlayed);
            GamesWon = Mathf.Clamp(GamesWon, 0, GamesPlayed);
            HandsPlayed = Math.Max(0, HandsPlayed);
            HandsWon = Mathf.Clamp(HandsWon, 0, HandsPlayed);
            ReadsTotal = Math.Max(0, ReadsTotal);
            ReadsCorrect = Mathf.Clamp(ReadsCorrect, 0, ReadsTotal);
            BestPot = Math.Max(0, BestPot);
            // 舊存檔沒有 TutorialVersion：TutorialDone 代表完成的是 v1（舊的 4 步教學）
            TutorialVersion = Math.Max(0, TutorialVersion);
            if (TutorialDone && TutorialVersion == 0) TutorialVersion = 1;
            if (Opponents == null) Opponents = new List<OpponentRecord>();
            Opponents.RemoveAll(o => o == null || string.IsNullOrEmpty(o.Name));
            if (Gestures == null) Gestures = new List<GestureCount>();
            Gestures.RemoveAll(g => g == null || string.IsNullOrEmpty(g.Gesture));
            foreach (var g in Gestures) g.Count = Math.Max(0, g.Count);
            foreach (var o in Opponents)
            {
                o.GamesPlayed = Math.Max(0, o.GamesPlayed);
                o.HandsPlayed = Math.Max(0, o.HandsPlayed);
                o.PlayerWon = Mathf.Clamp(o.PlayerWon, 0, o.HandsPlayed);
                o.TheyWon = Mathf.Clamp(o.TheyWon, 0, o.HandsPlayed);
                o.ReadsTotal = Math.Max(0, o.ReadsTotal);
                o.ReadsCorrect = Mathf.Clamp(o.ReadsCorrect, 0, o.ReadsTotal);
            }
        }
    }

    /// <summary>
    /// 存檔讀寫：預設在 Application.persistentDataPath/tell_progress.json。
    /// 先寫暫存檔再 File.Replace 換名（寫到一半當機不會毀掉舊檔；換名前當機就在下次讀取時採用暫存檔）；
    /// 讀到壞檔就改名成 .corrupt 保留、回傳新的進度，不丟例外。
    /// </summary>
    public sealed class ProgressStore
    {
        public const string FileName = "tell_progress.json";

        public ProgressStore(string path) { Path = path; }

        public static string DefaultPath => System.IO.Path.Combine(Application.persistentDataPath, FileName);

        public string Path { get; }
        /// <summary>上一次 Load 遇到壞檔、已改用新的進度。</summary>
        public bool LastLoadRecovered { get; private set; }

        public ProgressData Load()
        {
            LastLoadRecovered = false;
            try
            {
                var tmp = Path + ".tmp";
                // 上次寫完暫存檔、還沒換名就被關掉：暫存檔就是最新的完整內容
                if (!File.Exists(Path) && File.Exists(tmp)) File.Move(tmp, Path);
                if (!File.Exists(Path)) return new ProgressData();
                var json = File.ReadAllText(Path);
                var d = string.IsNullOrWhiteSpace(json) ? null : JsonUtility.FromJson<ProgressData>(json);
                if (d == null || d.Version <= 0) return Recover("empty or unknown");
                d.Sanitize();
                return d;
            }
            catch (Exception e)
            {
                return Recover(e.GetType().Name);
            }
        }

        ProgressData Recover(string why)
        {
            LastLoadRecovered = true;
            Debug.LogWarning("[Progress] 存檔損毀（" + why + "），改用新的進度：" + Path);
            try
            {
                var bad = Path + ".corrupt";
                if (File.Exists(bad)) File.Delete(bad);
                if (File.Exists(Path)) File.Move(Path, bad);
            }
            catch (Exception e) { Debug.LogWarning("[Progress] 無法備份壞檔：" + e.Message); }
            return new ProgressData();
        }

        public bool Save(ProgressData data)
        {
            if (data == null) return false;
            try
            {
                var dir = System.IO.Path.GetDirectoryName(Path);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                var tmp = Path + ".tmp";
                File.WriteAllText(tmp, JsonUtility.ToJson(data, true));
                if (File.Exists(Path)) File.Replace(tmp, Path, null);
                else File.Move(tmp, Path);
                return true;
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Progress] 存檔失敗：" + e.Message);
                return false;
            }
        }
    }
}
