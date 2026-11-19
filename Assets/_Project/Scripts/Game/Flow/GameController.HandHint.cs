using System.Collections.Generic;
using Poker.AI;
using Poker.Core;
using UnityEngine;

namespace Poker.Game
{
    /// <summary>
    /// 牌力提示（2026-10-14，給不會打牌的評審）：每一手玩家第一次看牌（咪牌掀到 Tutorial.PeekThreshold 或拿起來）時，
    /// 牌和玩家之間出現一行小字（Strong hand／Decent hand／Weak hand／Drawing — could improve），之後每一條新的街（牌發下來之後）更新；
    /// 棄牌、一手結束就收起來。Dot 只在這次開機第一次小聲說一次（有聲音＋同一句字幕），之後只有字。設定「Hand hints」可以關。
    /// 隱私：只用玩家自己的底牌＋公共牌（HandStrength，對「還在這一手的 AI 人數」個隨機手牌），結果只畫在玩家面前、只寫進 log，
    /// 不放進 PlayerSignals、Session 或公開牌桌狀態——AI 與語音對話的模型都拿不到（HandHintFlowTests 的隱私測試）。
    /// </summary>
    public sealed partial class GameController
    {
        bool _handHintsEnabled = true;
        /// <summary>設定裡的「Hand hints: On/Off」（預設開）。關掉時馬上收起來；打開之後下一次看牌才出現。</summary>
        public bool HandHintsEnabled
        {
            get => _handHintsEnabled;
            set
            {
                _handHintsEnabled = value;
                if (!value) HideHandHint();
            }
        }
        /// <summary>現在顯示的牌力提示（沒顯示是 null）。</summary>
        public HandHint? HandHintShown { get; private set; }
        /// <summary>牌旁邊那行字（沒顯示是 null）。</summary>
        public string HandHintText => HandHintShown.HasValue ? HandStrength.Label(HandHintShown.Value) : null;
        /// <summary>這一手算過的提示（第一次看牌、之後每一條街；測試與除錯用）。</summary>
        public IReadOnlyList<HandHint> HandHintHistory => _hintHistory;
        /// <summary>Dot 小聲說過幾次（這次開機最多一次）。</summary>
        public int HandHintWhispers { get; private set; }

        readonly List<HandHint> _hintHistory = new List<HandHint>();
        WorldLabel _hintLabel;
        bool _lookedThisHand;
        double _whisperedAt = double.NegativeInfinity;

        void InitHandHint()
        {
            _hintLabel = new WorldLabel(Props.Root, "HandHint", 0.0026f, new Color(0.85f, 1f, 0.85f));
        }

        void BeforeHandHintHand()
        {
            _lookedThisHand = false;
            _hintHistory.Clear();
            HideHandHint();
        }

        /// <summary>玩家看到自己的牌了（咪牌夠高或拿起來）：這一手第一次就算提示。回傳 Dot 是不是剛小聲說了。</summary>
        bool NotePlayerLooked()
        {
            if (_lookedThisHand || !HandHintsEnabled || _playerFolded) return false;
            _lookedThisHand = true;
            return UpdateHandHint();
        }

        /// <summary>新的一條街發完：看過牌的話更新。</summary>
        void OnStreetForHandHint()
        {
            if (_lookedThisHand && HandHintsEnabled && !_playerFolded) UpdateHandHint();
        }

        bool UpdateHandHint()
        {
            var hole = Session.HoleOf(Human);
            if (hole.Count < 2) return false;
            int opponents = 0;
            foreach (var seat in Session.AiSeats)
            {
                var st = Session.State.Seat(seat).Status;
                if (st != SeatStatus.Folded && st != SeatStatus.Out) opponents++;
            }
            var board = Session.State.Board;
            var hint = HandStrength.Classify(hole[0], hole[1], board, Mathf.Max(1, opponents));
            HandHintShown = hint;
            _hintHistory.Add(hint);
            var at = (Spots.PlayerCard(0) + Spots.PlayerCard(1)) * 0.5f + new Vector3(0f, 0.025f, -0.065f);
            _hintLabel.Show(HandStrength.Label(hint), at);
            if (Head != null) _hintLabel.Face(Head.position);
            string street = board.Count == 0 ? "preflop" : board.Count == 3 ? "flop" : board.Count == 4 ? "turn" : "river";
            Playtest.Event("hint", "label=" + hint.ToString().ToLowerInvariant() + " street=" + street);
            if (HandHintWhispers > 0) return false;
            HandHintWhispers++;
            _whisperedAt = Now;
            Voice.Say(DealerLines.HandHintWhisper(hint), Now, "handhint", force: true, mood: Mood.Happy);
            return true;
        }

        /// <summary>Dot 剛剛（這一幀）小聲說了牌力：教學的看牌那一步就不再讚美。</summary>
        bool JustWhispered => Now - _whisperedAt < 1e-6;

        void HideHandHint()
        {
            HandHintShown = null;
            _hintLabel?.Hide();
        }

        void TickHandHint()
        {
            if (HandHintShown.HasValue && Head != null) _hintLabel.Face(Head.position);
        }
    }
}
