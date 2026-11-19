using System;
using System.Collections.Generic;
using Poker.AI;
using Poker.Core;

namespace Poker.Game
{
    /// <summary>
    /// 教學 v4 的兩手劇本（固定牌＋腳本化的 AI，玩家不可能輸很多）。四人桌：玩家 0（大盲）、Pip 1、Rocco 2（按鈕）、Vex 3（小盲）；
    /// Pip、Vex 翻牌前都棄，等於玩家單挑 Rocco（同 Fool Rocco 的座位）。5 人桌（寫實預覽）老手坐 3 號當按鈕、Vex 4 號小盲，老手也棄（ButtonFor）。
    /// 第 1 手（玩家 Ah9c＝Decent，Rocco KsQd，牌面 9s 6d 2c / Jh / 3s）：Rocco 開到 30 → 玩家跟（或加注、或棄）→ 之後一律過牌 → 一對 9 贏 K 高。
    /// 第 2 手（玩家 AsAh，Rocco KcQc，牌面 9d 7c 2h Ks 4d；和 Fool Rocco 第 2 手一樣）：Rocco 跛入 → 玩家下注 →
    /// 等了（輪到你到下注落定 ≥ SlowBetSeconds）Rocco 讀成虛張而跟、攤牌輸；沒等 Rocco 棄牌。
    /// </summary>
    public static class TutorialScript
    {
        public const int Seats = 4;
        /// <summary>Rocco 當按鈕：Vex 小盲、玩家大盲，Pip 先行動（棄）→ Rocco → Vex（棄）→ 玩家。</summary>
        public const int Button = ReadSeat;
        /// <summary>讀玩家、被玩家騙的人（Rocco）。</summary>
        public const int ReadSeat = 2;
        public const int PipSeat = 1;
        public const int VexSeat = 3;
        /// <summary>教學中一次最多下到幾個大盲（教學不能一手輸太多）。</summary>
        public const int RaiseCapBlinds = 15;
        /// <summary>
        /// 第 2 手：輪到你到下注落定這麼久算「等了」。比一般快下注（伸手約 1 秒＋實機捏起到放開中位數 1.65 秒）長，
        /// 等 3 秒再下注一定超過；不看第一次伸手，手碰巧移到牌附近不算出手（2026-11-05 實機）。
        /// </summary>
        public const float SlowBetSeconds = 4.5f;

        public const string Hand1PlayerHole = "Ah9c", Hand1RoccoHole = "KsQd", Hand1Board = "9s6d2cJh3s";
        public const string Hand2PlayerHole = FoolRocco.Hand2PlayerHole, Hand2RoccoHole = FoolRocco.Hand2RoccoHole, Hand2Board = FoolRocco.Hand2Board;

        /// <summary>第 1 手玩家什麼都沒露出來時的揭曉卡（文字標籤，不是台詞）。</summary>
        public const string RevealNothing = "Rocco read you: nothing";

        /// <summary>4 人桌（正式版）或 5 人桌（寫實預覽，荷官右側多一位老手，2026-11-02；老手和 Pip、Vex 一樣翻牌前棄牌）。</summary>
        public static bool Supports(int seatCount) => seatCount == Seats || seatCount == Seats + 1;
        /// <summary>按鈕位（同 FoolRocco.ButtonFor）：玩家大盲、Vex 小盲。</summary>
        public static int ButtonFor(int seats) => FoolRocco.ButtonFor(seats);
        /// <summary>Vex 的座位（最後一位）。</summary>
        public static int VexSeatFor(int seats) => seats - 1;

        /// <summary>第 hand 手的固定牌（hand＝1 或 2；Pip、Vex 從洗好的牌補）。</summary>
        public static CardPreset Preset(int hand)
        {
            bool h2 = hand == 2;
            return CardPreset.Parse(new Dictionary<int, string> { { 0, h2 ? Hand2PlayerHole : Hand1PlayerHole }, { ReadSeat, h2 ? Hand2RoccoHole : Hand1RoccoHole } },
                h2 ? Hand2Board : Hand1Board);
        }

        /// <summary>
        /// 腳本化的 AI（第 hand 手、輪到 seat）：Pip、Vex 能過就過、不然棄；Rocco 第 1 手翻牌前開到 3 個大盲、面對玩家加注就跟，
        /// 第 2 手翻牌前只跟大盲、面對玩家的下注依讀牌（roccoCalls）跟或棄；其他時候只過牌（不會對玩家下注）。
        /// </summary>
        public static PlayerCommand Ai(int hand, HandState s, int seat, bool roccoCalls = false)
        {
            var la = Engine.GetLegalActions(s, seat);
            if (seat != ReadSeat) return la.CanCheck ? new Check(seat) : (PlayerCommand)new Fold(seat);
            if (!la.CanCheck && s.LastAggressor == 0)
            {
                if (hand == 2 && !roccoCalls) return new Fold(seat);
                return la.CanCall ? new Call(seat) : la.CanAllIn ? new AllIn(seat) : (PlayerCommand)new Fold(seat);
            }
            int bb = s.BigBlind;
            if (hand == 1 && s.Street == Street.Preflop && la.CanRaise && s.CurrentBet == bb)
                return new Raise(seat, Math.Min(la.MaxTo, Math.Max(la.MinTo, 3 * bb)));
            if (la.CanCheck) return new Check(seat);
            return la.CanCall ? new Call(seat) : (PlayerCommand)new Fold(seat);
        }
    }
}
