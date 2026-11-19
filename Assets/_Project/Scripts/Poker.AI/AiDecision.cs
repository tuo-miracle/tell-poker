using Poker.Core;

namespace Poker.AI
{
    /// <summary>AI 的執行設定。</summary>
    public sealed class AiSettings
    {
        /// <summary>每次決策的 Monte Carlo 次數。</summary>
        public int MonteCarloTrials = 1000;
        /// <summary>看到新公共牌時估計反應用的次數（較少，只求方向）。</summary>
        public int ReactionTrials = 300;
        /// <summary>讀玩家設定（預設共用全域設定）。</summary>
        public PlayerReadSettings ReadSettings = PlayerReadSettings.Global;
    }

    /// <summary>一次決策的完整結果。</summary>
    public sealed class AiDecision
    {
        /// <summary>要送進 <see cref="Engine.Apply"/> 的合法指令。</summary>
        public PlayerCommand Command;
        /// <summary>真實意圖。</summary>
        public Intent Intent;
        /// <summary>Monte Carlo 勝率（未含讀玩家）。</summary>
        public double RawEquity;
        /// <summary>決策用的勝率（含讀玩家微調）。</summary>
        public double Equity;
        /// <summary>讀玩家造成的微調（|值| ≤ 0.08）。</summary>
        public double ReadShift;
        /// <summary>決策難度 0–1（越接近門檻越難）。</summary>
        public float Difficulty;
        /// <summary>建議思考時間（秒，0.8–3.5），遊戲層在這段時間內播放 <see cref="Cue"/>。</summary>
        public float ThinkSeconds;
        /// <summary>決策時的街。</summary>
        public Street Street;
        /// <summary>破綻演出（含真實／演出狀態）。</summary>
        public TellResult Tell;
        /// <summary>表演指令（<see cref="TellResult.Cue"/> 的捷徑）。</summary>
        public PerformanceCue Cue => Tell?.Cue;
    }
}
