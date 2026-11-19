namespace Poker.AI
{
    /// <summary>
    /// AI 角色當下的內心狀態，透過表演讓玩家「讀」到（真實或假裝的）。
    /// 角色層（卡通或之後的寫實）只看這些抽象指令，不知道 AI 的牌，換外型時大腦不用動。
    /// </summary>
    public enum Mood { Neutral, Nervous, Confident, Bluffing, Hesitant, Startled, Annoyed, Happy, Dejected }

    /// <summary>可以讀出破綻的小動作（tells）。</summary>
    public enum Tell
    {
        None,
        FidgetChips,     // 手指撥弄籌碼
        GlanceAtCards,   // 偷瞄自己的牌
        GlanceAtChips,   // 瞄自己的籌碼（想下注）
        LeanBack,        // 往後靠（放鬆、自信）
        LeanIn,          // 往前傾（興奮、有興趣）
        Freeze,          // 刻意不動
        Sweat,           // 冒汗
        TapFingers,      // 手指敲桌（不耐、猶豫）
        Swallow,         // 吞口水
        AvoidEyeContact, // 避開玩家的視線
        StareDown,       // 盯著玩家
    }

    public enum LookTarget { Player, OwnCards, OwnChips, Pot, Board, OtherSeat, Away }

    /// <summary>
    /// 一條表演指令。AI 每個決策點或對事件反應時發出；角色層依 Intensity 與「是否被玩家注視」決定演出幅度。
    /// </summary>
    public sealed class PerformanceCue
    {
        public int Seat;
        public Mood Mood = Mood.Neutral;
        /// <summary>演出強度 0–1。被玩家注視時，角色層會再放大。</summary>
        public float Intensity = 0.5f;
        public Tell Tell = Tell.None;
        public LookTarget Look = LookTarget.Player;
        /// <summary>要看的另一個座位（Look == OtherSeat 時）。</summary>
        public int LookSeat = -1;
        /// <summary>台詞 id（對應字幕與咕噥聲），沒有就是 null。</summary>
        public string LineId;
        /// <summary>英文字幕（比賽要求英文介面）。</summary>
        public string Subtitle;
        /// <summary>持續秒數（0 = 直到下一條指令）。</summary>
        public float Duration;
    }
}
