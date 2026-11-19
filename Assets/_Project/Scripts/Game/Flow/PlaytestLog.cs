using System;
using System.Globalization;
using UnityEngine;

namespace Poker.Game
{
    /// <summary>
    /// 試玩分析用的 log（2026-10-14；評審只玩一次、約 3 分鐘）：一行一件事
    /// `[Playtest] t=&lt;開機後秒數&gt; event=&lt;名稱&gt; &lt;欄位=值…&gt;`，事件有
    /// tutorial_step（step=… state=start|done|hint1|hint2|finished|skipped）、first_bet（這次開機第一次下注／跟注）、hint（牌力提示）、
    /// read_reveal（「Rocco read you: …」揭曉卡）、fooled（騙到 AI）、stuck(&lt;步驟&gt;,&lt;秒&gt;)（教學卡住給提示時）、tip（第一次用到時的手勢提示）、
    /// idle_pause（reason=no_hands|unfocused where=tutorial|game：沒有手 ≥ 5 s 或失焦，教學暫停）、idle_resume（after=&lt;暫停秒數&gt;）、
    /// session_end（摘要：手數、第一次下注與第一次騙到的時間、卡住次數）。stuck 只在有手的時候記。
    /// 用 `adb logcat -s Unity | grep "\[Playtest\]"` 收，就能算每個測試者的 time-to-first-bet、time-to-fooled、卡在哪裡。
    /// </summary>
    public sealed class PlaytestLog
    {
        /// <summary>時鐘（秒）：預設＝開機後的真實時間（選單暫停也算，評審戴著頭盔的時間）；測試換成遊戲時間。</summary>
        public Func<double> Clock = () => Time.realtimeSinceStartupAsDouble;

        public double? FirstBetAt { get; private set; }
        public double? FooledAt { get; private set; }
        public int StuckCount { get; private set; }
        public int Hands { get; private set; }
        bool _ended;

        public static string Format(double t, string evt, string detail = null) =>
            "[Playtest] t=" + t.ToString("0.0", CultureInfo.InvariantCulture) + " event=" + evt + (string.IsNullOrEmpty(detail) ? "" : " " + detail);

        /// <summary>stuck 事件的名稱：stuck(Peek,10)。</summary>
        public static string Stuck(string step, float seconds) =>
            "stuck(" + step + "," + Mathf.RoundToInt(seconds).ToString(CultureInfo.InvariantCulture) + ")";

        double Now() { try { return Clock(); } catch { return 0; } }

        public string Event(string evt, string detail = null)
        {
            var line = Format(Now(), evt, detail);
            Debug.Log(line);
            return line;
        }

        /// <summary>第一次下注／跟注／加注／全下（這次開機只記一次；教學裡的也算，tutorial=1）。</summary>
        public void FirstBet(bool tutorial, string action)
        {
            if (FirstBetAt.HasValue) return;
            FirstBetAt = Now();
            Event("first_bet", "tutorial=" + (tutorial ? "1" : "0") + " action=" + action);
        }

        public void StuckAt(string step, float seconds)
        {
            StuckCount++;
            Event(Stuck(step, seconds));
        }

        public void Fooled(string who, string where, int hand)
        {
            if (!FooledAt.HasValue) FooledAt = Now();
            Event("fooled", "who=" + who + " where=" + where + " hand=" + hand.ToString(CultureInfo.InvariantCulture));
        }

        public void HandPlayed() => Hands++;

        /// <summary>結束（離開 App、遊戲物件被移除）：只記一次。</summary>
        public void SessionEnd()
        {
            if (_ended) return;
            _ended = true;
            string T(double? t) => t.HasValue ? t.Value.ToString("0.0", CultureInfo.InvariantCulture) : "-";
            Event("session_end", "hands=" + Hands.ToString(CultureInfo.InvariantCulture) + " first_bet=" + T(FirstBetAt) + " fooled=" + T(FooledAt) + " stuck=" + StuckCount.ToString(CultureInfo.InvariantCulture));
        }
    }
}
