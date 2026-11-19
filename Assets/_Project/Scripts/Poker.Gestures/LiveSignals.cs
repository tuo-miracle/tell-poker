using System.Collections.Generic;

namespace Poker.Gestures
{
    /// <summary>即時訊號的種類（live talk，2026-10-14）。</summary>
    public enum HabitSignalKind { TurnStarted, Thinking, Look, ReachChips, PullBack, ChipsHeld, ChipsPutBack, TowardCards, GazeDwell, PlayerSpoke }

    /// <summary>玩家回合中的一個即時訊號（Time＝手部資料的時鐘）。</summary>
    public readonly struct HabitSignal
    {
        public readonly HabitSignalKind Kind;
        public readonly double Time;
        public readonly Hand Hand;
        public readonly int Count;
        public readonly int Target;
        public HabitSignal(HabitSignalKind kind, double time, Hand hand = Hand.Right, int count = 0, int target = -1)
        { Kind = kind; Time = time; Hand = hand; Count = count; Target = target; }
        public override string ToString() => $"{Time:0.00} {Kind}" + (Count != 0 ? " n=" + Count : "") + (Target >= 0 ? " @" + Target : "");
    }

    /// <summary>這一回合到目前的訊號統計（給 AI 讀人）。</summary>
    public sealed class TurnHabits
    {
        public float ThinkSeconds;
        public int ThinkMilestones, Looks, ChipReaches, PullBacks, PutBacks, ChipsHeldCount;
        public float HeldSeconds;
        public bool TowardCards, Spoke;
    }
}
