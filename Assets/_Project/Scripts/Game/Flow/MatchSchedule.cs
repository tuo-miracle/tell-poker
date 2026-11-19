using System;
using System.Collections.Generic;

namespace Poker.Game
{
    /// <summary>一局為什麼結束。</summary>
    public enum GameEndReason { None, PlayerBusted, PlayerWonAll, HandLimit, TimeUp }

    /// <summary>
    /// 一局（約 8 分鐘）的節奏：盲注每 HandsPerLevel 手升一級（Levels 是基本盲注的倍數），
    /// 打滿 MaxHands 手、超過 TimeLimitSeconds（一手打完才檢查）、玩家輸光或贏光就結束。
    /// </summary>
    [Serializable]
    public sealed class MatchSchedule
    {
        /// <summary>每一級的盲注倍數（第 0 級 = GameConfig 的 SmallBlind／BigBlind）。</summary>
        public int[] Levels = { 1, 2, 4 };
        public int HandsPerLevel = 4;
        public int MaxHands = 12;
        public float TimeLimitSeconds = 480f;

        /// <summary>這一局第 handIndex 手（從 0 起算）的盲注級數。</summary>
        public int LevelFor(int handIndex)
        {
            if (Levels == null || Levels.Length == 0) return 0;
            int level = Math.Max(0, handIndex) / Math.Max(1, HandsPerLevel);
            return Math.Min(level, Levels.Length - 1);
        }

        public (int, int) BlindsFor(int handIndex, int smallBlind, int bigBlind)
        {
            int m = Levels == null || Levels.Length == 0 ? 1 : Math.Max(1, Levels[LevelFor(handIndex)]);
            return (smallBlind * m, bigBlind * m);
        }

        /// <summary>一手結束後檢查：這一局該不該結束。</summary>
        public GameEndReason Check(int handsPlayed, double elapsedSeconds, IReadOnlyList<int> stacks, int humanSeat)
        {
            if (stacks[humanSeat] <= 0) return GameEndReason.PlayerBusted;
            int alive = 0;
            for (int i = 0; i < stacks.Count; i++) if (stacks[i] > 0) alive++;
            if (alive <= 1) return GameEndReason.PlayerWonAll;
            if (MaxHands > 0 && handsPlayed >= MaxHands) return GameEndReason.HandLimit;
            if (TimeLimitSeconds > 0f && elapsedSeconds >= TimeLimitSeconds) return GameEndReason.TimeUp;
            return GameEndReason.None;
        }

        /// <summary>名次（1 = 籌碼最多；同額算同名次）。</summary>
        public static int PlaceOf(IReadOnlyList<int> stacks, int seat)
        {
            int place = 1;
            for (int i = 0; i < stacks.Count; i++) if (i != seat && stacks[i] > stacks[seat]) place++;
            return place;
        }
    }
}
