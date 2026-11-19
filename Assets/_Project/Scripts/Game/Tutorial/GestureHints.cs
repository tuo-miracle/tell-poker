using System;
using System.Collections.Generic;

namespace Poker.Game
{
    /// <summary>每幀給提示排程的牌桌狀態。</summary>
    public struct HintContext
    {
        public bool PlayerTurn;
        /// <summary>這回合的合法動作（Core 的 LegalActions）。</summary>
        public bool CanCheck, CanCall, CanBet, CanFold, CanAllIn;
        /// <summary>手腕選單開著、正在對角色講話（收音或等回話）、教學那一手：都不顯示提示。</summary>
        public bool MenuOpen, VoiceBusy, TutorialActive;
    }

    /// <summary>
    /// 牌局中的手勢提示：輪到玩家、閒置 IdleSeconds 秒（手沒動、沒有手勢）→ 顯示目前合法動作的示範手
    /// （可過牌 → 敲桌；可跟／下注 → 捏籌碼丟進下注區；面對下注 → 推牌棄牌；All-in → 雙手推）。
    /// 玩家開始動手就收起來並重新計時；某個手勢成功做過 SuppressAfter 次就不再提示它（次數由進度存檔提供）。
    /// 選單開著、語音收音／等回話、教學那一手、或玩家在設定關掉提示時都不顯示，這段時間也不算閒置。
    /// </summary>
    public sealed class GestureHints
    {
        public const float IdleSeconds = 5f;
        public const int SuppressAfter = 3;

        public bool Enabled = true;
        /// <summary>某個手勢成功做過幾次（遊戲層接到進度存檔）。</summary>
        public Func<GhostGesture, int> Successes = _ => 0;

        readonly List<GhostGesture> _legal = new List<GhostGesture>(4);
        readonly List<GhostGesture> _show = new List<GhostGesture>(4);
        bool _wasTurn;

        /// <summary>現在要顯示提示。</summary>
        public bool Visible { get; private set; }
        /// <summary>要示範的手勢（依序輪流；Visible 為 false 時也保留最近一次的計算結果）。</summary>
        public IReadOnlyList<GhostGesture> Gestures => _show;
        /// <summary>這回合閒置了幾秒。</summary>
        public float Idle { get; private set; }

        public void Tick(float dt, in HintContext c)
        {
            if (!c.PlayerTurn) { _wasTurn = false; Idle = 0f; Visible = false; return; }
            if (!_wasTurn) { _wasTurn = true; Idle = 0f; }
            bool blocked = !Enabled || c.MenuOpen || c.VoiceBusy || c.TutorialActive;
            if (blocked)
            {
                // 擋住的時間不算閒置：選單關掉後要再等滿
                Idle = 0f;
                Visible = false;
                return;
            }
            Idle += dt;
            LegalGhosts(c, _legal);
            _show.Clear();
            foreach (var g in _legal) if (Successes(g) < SuppressAfter) _show.Add(g);
            Visible = Idle >= IdleSeconds && _show.Count > 0;
        }

        /// <summary>玩家開始動手（手在移動、或出現任何手勢事件）：收起提示、重新計時。</summary>
        public void NoteActivity()
        {
            Idle = 0f;
            Visible = false;
        }

        /// <summary>合法動作 → 示範手勢（順序：過牌、跟／下注、棄牌、All-in）。可以過牌時不建議棄牌。</summary>
        public static void LegalGhosts(in HintContext c, List<GhostGesture> into)
        {
            into.Clear();
            if (c.CanCheck) into.Add(GhostGesture.Knock);
            if (c.CanCall || c.CanBet) into.Add(GhostGesture.Bet);
            if (c.CanFold && !c.CanCheck) into.Add(GhostGesture.Fold);
            if (c.CanAllIn) into.Add(GhostGesture.AllIn);
        }
    }

    /// <summary>教學與提示需要知道的語音狀態（Poker.Game 不能引用 Poker.Voice，由 UX 接上）。</summary>
    public interface ITutorialVoice
    {
        /// <summary>語音對話可以用（同意過、開著、有麥克風權限）。</summary>
        bool Available { get; }
        /// <summary>正在收音或等角色回話。</summary>
        bool Busy { get; }
        /// <summary>玩家講完送出的次數（累計）。</summary>
        int Utterances { get; }
    }
}
