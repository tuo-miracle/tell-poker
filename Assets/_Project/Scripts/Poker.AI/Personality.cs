namespace Poker.AI
{
    /// <summary>AI 角色原型（對應角色層的 CharacterKind）。</summary>
    public enum Archetype { Rookie, Loudmouth, Cold, Veteran }

    /// <summary>自我對弈統計的目標帶（研究筆記 §3.3 的社群參考範圍）。</summary>
    public struct StatTargets
    {
        /// <summary>入池率下限／上限。</summary>
        public float VpipMin, VpipMax;
        /// <summary>翻前加注率下限／上限。</summary>
        public float PfrMin, PfrMax;
        /// <summary>翻牌後攻擊因子（(下注+加注)/跟注）下限／上限。</summary>
        public float AfMin, AfMax;
    }

    /// <summary>
    /// AI 個性（純資料）。數值是「目標」與「傾向」，由 Policy 轉成實際決策；用自我對弈驗證統計落在 <see cref="Targets"/> 內。
    /// 每次取用預設都是新的複本，可安心修改。
    /// </summary>
    public sealed class Personality
    {
        /// <summary>顯示名稱。</summary>
        public string Name;
        /// <summary>原型（決定台詞集與演出風格）。</summary>
        public Archetype Archetype;
        /// <summary>目標入池率 VPIP（0–1）。</summary>
        public float Vpip;
        /// <summary>目標翻前加注率 PFR（0–1）。</summary>
        public float Pfr;
        /// <summary>翻牌後攻擊度（0–1）：有牌時下注／加注而非過牌／跟注的傾向。</summary>
        public float Aggression;
        /// <summary>虛張頻率（0–1）：沒牌時仍下注／加注的傾向。</summary>
        public float BluffFrequency;
        /// <summary>面對下注的棄牌傾向（0–1）：越高越需要好的勝率才跟。</summary>
        public float FoldToBet;
        /// <summary>破綻可信度（0–1）：表演與真實狀態一致的比例。</summary>
        public float TellCredibility;
        /// <summary>反向演出機率（0–1）：不演真實狀態時，演「相反」而不是面無表情的比例。</summary>
        public float ReverseActing;
        /// <summary>讀玩家的敏感度（0–1），乘在 PlayerRead 的偏移上。</summary>
        public float ReadSensitivity;
        /// <summary>多話程度（0–1），影響台詞觸發機率。</summary>
        public float Talkativeness;
        /// <summary>演出強度基準（0–1）。</summary>
        public float Expressiveness;
        /// <summary>下注尺寸（底池比例）。</summary>
        public float BetSizePot;
        /// <summary>思考時間基準（秒）與難度加成（秒）。</summary>
        public float ThinkBase, ThinkSpread;
        /// <summary>統計目標帶。</summary>
        public StatTargets Targets;

        /// <summary>緊張新手 Pip：緊、被動，破綻幾乎都可信、強度高。</summary>
        public static Personality Rookie => new Personality
        {
            Name = "Pip", Archetype = Archetype.Rookie,
            Vpip = 0.19f, Pfr = 0.07f, Aggression = 0.25f, BluffFrequency = 0.03f, FoldToBet = 0.7f,
            TellCredibility = 0.92f, ReverseActing = 0f, ReadSensitivity = 0.5f, Talkativeness = 0.45f, Expressiveness = 0.85f,
            BetSizePot = 0.5f, ThinkBase = 1.4f, ThinkSpread = 2.0f,
            Targets = new StatTargets { VpipMin = 0.14f, VpipMax = 0.24f, PfrMin = 0.04f, PfrMax = 0.11f, AfMin = 0.4f, AfMax = 1.5f },
        };

        /// <summary>大嘴巴老手 Rocco：鬆、兇，可信度中。</summary>
        public static Personality Loudmouth => new Personality
        {
            Name = "Rocco", Archetype = Archetype.Loudmouth,
            Vpip = 0.33f, Pfr = 0.24f, Aggression = 0.85f, BluffFrequency = 0.22f, FoldToBet = 0.3f,
            TellCredibility = 0.65f, ReverseActing = 0.3f, ReadSensitivity = 1f, Talkativeness = 0.8f, Expressiveness = 0.75f,
            BetSizePot = 0.8f, ThinkBase = 0.8f, ThinkSpread = 1.2f,
            Targets = new StatTargets { VpipMin = 0.28f, VpipMax = 0.40f, PfrMin = 0.20f, PfrMax = 0.30f, AfMin = 2.5f, AfMax = 6f },
        };

        /// <summary>冷面賭徒 Vex：平衡、偶爾虛張，可信度低，會反向演出。</summary>
        public static Personality Cold => new Personality
        {
            Name = "Vex", Archetype = Archetype.Cold,
            Vpip = 0.22f, Pfr = 0.17f, Aggression = 0.8f, BluffFrequency = 0.12f, FoldToBet = 0.5f,
            TellCredibility = 0.35f, ReverseActing = 0.7f, ReadSensitivity = 0.8f, Talkativeness = 0.25f, Expressiveness = 0.4f,
            BetSizePot = 0.66f, ThinkBase = 1.8f, ThinkSpread = 1.0f,
            Targets = new StatTargets { VpipMin = 0.18f, VpipMax = 0.27f, PfrMin = 0.14f, PfrMax = 0.22f, AfMin = 1.8f, AfMax = 4f },
        };

        /// <summary>
        /// 老手 Silas（2026-11-02 加的第 4 位對手，坐荷官右側）：緊凶（玩的牌少、玩就加注）、話少、乾冷幽默；
        /// 破綻可信度介於 Vex 與 Rocco 之間（大多面無表情，偶爾反著演）。目標帶用 5 人桌自我對弈量（VeteranTests）。
        /// </summary>
        public static Personality Veteran => new Personality
        {
            Name = "Silas", Archetype = Archetype.Veteran,
            Vpip = 0.20f, Pfr = 0.17f, Aggression = 0.85f, BluffFrequency = 0.08f, FoldToBet = 0.55f,
            TellCredibility = 0.55f, ReverseActing = 0.4f, ReadSensitivity = 0.9f, Talkativeness = 0.2f, Expressiveness = 0.35f,
            BetSizePot = 0.7f, ThinkBase = 1.6f, ThinkSpread = 1.2f,
            Targets = new StatTargets { VpipMin = 0.15f, VpipMax = 0.23f, PfrMin = 0.12f, PfrMax = 0.19f, AfMin = 2.0f, AfMax = 5f },
        };

        /// <summary>依原型取預設。</summary>
        public static Personality For(Archetype a) =>
            a == Archetype.Rookie ? Rookie : a == Archetype.Loudmouth ? Loudmouth : a == Archetype.Veteran ? Veteran : Cold;
    }
}
