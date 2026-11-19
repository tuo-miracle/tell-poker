using System;
using System.Collections.Generic;

namespace Poker.AI
{
    /// <summary>籌碼是怎麼進下注區的。</summary>
    public enum ChipDelivery { None, Placed, Thrown }

    /// <summary>
    /// 玩家的「可觀察行為」訊號，由遊戲層（手勢層）在玩家行動時或注視變化時填入。
    /// 全是桌上任何人都看得到的肢體行為，不含任何牌的資訊。
    /// </summary>
    public sealed class PlayerSignals
    {
        /// <summary>這手到目前的咪牌累計秒數。</summary>
        public float PeekSeconds;
        /// <summary>輪到玩家到做出動作的猶豫秒數。</summary>
        public float HesitationSeconds;
        /// <summary>下注是丟的還是放的（沒下注為 None）。</summary>
        public ChipDelivery Delivery;
        /// <summary>丟／放籌碼時的手速（m/s）。</summary>
        public float ThrowSpeed;
        /// <summary>玩家正在盯著看的 AI 座位；-1 表示沒有。</summary>
        public int GazeSeat = -1;
        /// <summary>已連續盯著的秒數。</summary>
        public float GazeSeconds;
        /// <summary>這次下注的金額佔底池比例（沒下注為 0），給「大額下注」反應用。</summary>
        public float BetPotFraction;
        /// <summary>遊戲時鐘（秒），用於衰減。</summary>
        public double Time;

        // ───── 這一回合的即時訊號（live talk，2026-10-14；PlayerHabits 量的，全是看得到的肢體行為）─────
        /// <summary>有即時訊號（下面的欄位有效）。</summary>
        public bool Live;
        /// <summary>輪到玩家到做出決定的秒數。</summary>
        public float ThinkSeconds;
        /// <summary>這一回合看牌（咪牌或拿起來看）的次數。</summary>
        public int Looks;
        /// <summary>手伸向籌碼、沒下注又縮回的次數。</summary>
        public int PullBacks;
        /// <summary>籌碼放回去（含從下注區撈回）的次數。</summary>
        public int PutBacks;
        /// <summary>捏著籌碼舉在空中（超過約 1.2 秒）的次數。</summary>
        public int HeldCount;
        /// <summary>手放到牌上（可能要棄）但沒棄。</summary>
        public bool TowardCards;
        /// <summary>這次是下注／加注／全下。</summary>
        public bool Aggressive;
        /// <summary>這次是棄牌。</summary>
        public bool Folded;
    }

    /// <summary>AI 讀玩家用的線索（即時訊號整理後）。</summary>
    public enum ReadCue { LongThink, Snap, ReLook, ReachRetreat, Fiddle, TowardCards }

    /// <summary>照讀到的判斷打了之後，攤牌的結果：讀對、被騙、不知道（沒攤牌或沒照它打）。</summary>
    public enum ReadOutcome { None, Right, Fooled }

    /// <summary>某個 AI 這一手對玩家的判斷。</summary>
    public sealed class LiveBelief
    {
        /// <summary>"weak" 或 "strong"。</summary>
        public string Belief;
        /// <summary>讀到的線索（依影響大小）。</summary>
        public List<ReadCue> Cues = new List<ReadCue>();
        /// <summary>對決策勝率的偏移（已含敏感度、強度、信任度與上限；正值＝覺得玩家弱）。</summary>
        public double Effect;
        /// <summary>這個判斷真的用在某次決策上了。</summary>
        public bool Used;
        public double Time;
        /// <summary>攤牌後學過了（同一個判斷只學一次）。</summary>
        public bool Learned;

        public static string Name(ReadCue c)
        {
            switch (c)
            {
                case ReadCue.LongThink: return "long think";
                case ReadCue.Snap: return "snap bet";
                case ReadCue.ReLook: return "re-peek";
                case ReadCue.ReachRetreat: return "reach-then-retreat";
                case ReadCue.Fiddle: return "chip fiddling";
                default: return "hand on the cards";
            }
        }

        /// <summary>英文的線索清單，例如 "long think, re-peek"。</summary>
        public string Describe() => string.Join(", ", Cues.ConvertAll(Name));
    }

    /// <summary>AI 從玩家行為「注意到」的事（用來挑表演與台詞）。</summary>
    [Flags]
    public enum PlayerNotice
    {
        None = 0,
        LongHesitation = 1,
        SnapAction = 2,
        LongPeek = 4,
        ThrownChips = 8,
        GentlePlace = 16,
        StaredAtMe = 64,
        BigBet = 128,
    }

    /// <summary>讀玩家的全域設定（含無障礙開關）。<see cref="MaxShift"/> 硬上限 0.08。</summary>
    public sealed class PlayerReadSettings
    {
        /// <summary>舊的單次觀察上限（下注後的猶豫、看牌秒數、丟籌碼力道）：±0.08 勝率。</summary>
        public const float HardMaxShift = 0.08f;
        /// <summary>總上限（含即時訊號的讀人）。</summary>
        public const float LiveMaxShift = 0.15f;
        /// <summary>全域設定（遊戲層的選單改這個）。</summary>
        public static readonly PlayerReadSettings Global = new PlayerReadSettings();

        float _max = HardMaxShift;
        /// <summary>是否讓讀到的訊號影響決策（關掉時仍可有表演反應）。</summary>
        public bool Enabled = true;
        /// <summary>是否對玩家行為做表演反應（嚇到、台詞）。</summary>
        public bool ReactionsEnabled = true;
        /// <summary>整體強度 0–1。</summary>
        public float Intensity = 1f;
        /// <summary>攤牌後學習（被騙就打折、讀對就恢復）。關掉只給自我對弈比較「沒有防騙」用。</summary>
        public bool LearnFromShowdown = true;
        /// <summary>被騙一次，那些線索的信任度乘這個；讀對一次加 TrustRecover（上限 1）。數值見 LiveReadSelfPlayTests。</summary>
        public double FooledDiscount = 0.4, TrustRecover = 0.15;
        /// <summary>衰減時間常數（秒）：偏移每經過這麼久剩 1/e。</summary>
        public float DecaySeconds = 45f;
        /// <summary>單次觀察偏移上限，設定值會被夾在 0–0.08。</summary>
        public float MaxShift { get => _max; set => _max = Math.Max(0f, Math.Min(HardMaxShift, value)); }

        /// <summary>猶豫多久算長考（秒）。</summary>
        public float TankSeconds = 6f;
        /// <summary>多快算秒下（秒）。</summary>
        public float SnapSeconds = 1f;
        /// <summary>咪牌多久算久（秒）。</summary>
        public float LongPeekSeconds = 4f;
        /// <summary>丟籌碼多快算用力（m/s）。</summary>
        public float ThrowFastSpeed = 1.5f;
        /// <summary>被盯多久才有反應（秒）。</summary>
        public float GazeSeconds = 1f;
        /// <summary>下注佔底池多少算大額。</summary>
        public float BigBetPot = 0.9f;
    }

    /// <summary>
    /// 某個 AI 對玩家的「讀心」：把公開的肢體訊號轉成對玩家牌力估計的微調（正值＝覺得玩家較弱 → AI 勝率加）。
    /// 每條規則對所有玩家一樣（不會因為玩家真的在詐唬就讀得出來），偏移有上限、隨時間指數衰減。
    /// </summary>
    public sealed class PlayerRead
    {
        readonly int _mySeat;
        readonly float _sensitivity;
        readonly PlayerReadSettings _settings;
        double _shift;     // 未縮放（sensitivity × intensity 之前）的偏移
        double _shiftTime;

        /// <summary>建立。</summary>
        /// <param name="mySeat">此 AI 的座位（判斷玩家是不是在盯我）。</param>
        /// <param name="sensitivity">個性的敏感度 0–1。</param>
        /// <param name="settings">設定；null 用 <see cref="PlayerReadSettings.Global"/>。</param>
        public PlayerRead(int mySeat, float sensitivity, PlayerReadSettings settings = null)
        {
            _mySeat = mySeat;
            _sensitivity = Math.Max(0f, Math.Min(1f, sensitivity));
            _settings = settings ?? PlayerReadSettings.Global;
        }

        /// <summary>使用中的設定。</summary>
        public PlayerReadSettings Settings => _settings;

        /// <summary>最近一次觀察到的事。</summary>
        public PlayerNotice LastNotice { get; private set; }

        /// <summary>吃一筆訊號，回傳注意到的事。</summary>
        public PlayerNotice Observe(PlayerSignals s)
        {
            if (s == null) throw new ArgumentNullException(nameof(s));
            var st = _settings;
            var n = PlayerNotice.None;
            double delta = 0;
            bool acted = s.Delivery != ChipDelivery.None;
            if (s.Live)
            {
                // 即時訊號：這一回合的判斷取代舊的「猶豫／看牌秒數／丟籌碼」偏移（不重複算）；注意到的事照舊給表演用
                var b = ReadTurn(s);
                if (b != null) Belief = b;
                if (s.ThinkSeconds >= st.TankSeconds) n |= PlayerNotice.LongHesitation;
                if (s.GazeSeat == _mySeat && s.GazeSeconds >= st.GazeSeconds) n |= PlayerNotice.StaredAtMe;
                if (s.BetPotFraction >= st.BigBetPot) n |= PlayerNotice.BigBet;
                LastNotice = n;
                return n;
            }
            if (s.HesitationSeconds >= st.TankSeconds) { n |= PlayerNotice.LongHesitation; delta += 0.03; }
            else if (acted && s.HesitationSeconds > 0 && s.HesitationSeconds <= st.SnapSeconds) { n |= PlayerNotice.SnapAction; delta -= 0.02; }
            if (s.PeekSeconds >= st.LongPeekSeconds) { n |= PlayerNotice.LongPeek; delta += 0.02; }
            if (s.Delivery == ChipDelivery.Thrown && s.ThrowSpeed >= st.ThrowFastSpeed) { n |= PlayerNotice.ThrownChips; delta += 0.02; }
            else if (s.Delivery == ChipDelivery.Placed && s.ThrowSpeed < st.ThrowFastSpeed * 0.33f) { n |= PlayerNotice.GentlePlace; delta -= 0.015; }
            if (s.GazeSeat == _mySeat && s.GazeSeconds >= st.GazeSeconds) n |= PlayerNotice.StaredAtMe;
            if (s.BetPotFraction >= st.BigBetPot) n |= PlayerNotice.BigBet;

            if (delta != 0)
            {
                double cur = Decayed(s.Time);
                _shift = Clamp(cur + delta, st.MaxShift);
                _shiftTime = s.Time;
            }
            LastNotice = n;
            return n;
        }

        /// <summary>目前對玩家勝率的微調（加在 AI 自己的勝率上），已含開關、強度、敏感度、上限與衰減。</summary>
        public double CurrentShift(double now)
        {
            var st = _settings;
            if (!st.Enabled) return 0;
            double legacy = Clamp(Decayed(now) * _sensitivity * Math.Max(0f, Math.Min(1f, st.Intensity)), st.MaxShift);
            return Clamp(legacy + (Belief != null ? Belief.Effect : 0), PlayerReadSettings.LiveMaxShift);
        }

        // ───────────── 即時訊號的讀人（live talk，2026-10-14）─────────────

        /// <summary>信任度下限（被騙很多次也還留一點：玩家換回誠實時讀得回來）。</summary>
        public const double MinTrust = 0.05;

        readonly double[] _trust = { 1, 1, 1, 1, 1, 1 };

        /// <summary>這一手對玩家的判斷（沒有是 null）。新的一手清掉。</summary>
        public LiveBelief Belief { get; private set; }

        /// <summary>某個線索目前的信任度（0.05–1）：被它騙過就打折。這一局（這個 AI）一直記著。</summary>
        public double Trust(ReadCue c) => _trust[(int)c];

        /// <summary>新的一手：判斷清掉（信任度留著）。</summary>
        public void BeginHand() => Belief = null;

        /// <summary>這個判斷用在決策上了（之後攤牌才會拿來學）。</summary>
        public void MarkUsed() { if (Belief != null) Belief.Used = true; }

        /// <summary>
        /// 攤牌看到玩家的牌之後：照判斷打過的才學——判斷「弱」但玩家有牌、或判斷「強」但玩家沒牌＝被騙，那些線索打折；讀對了慢慢恢復。
        /// </summary>
        public ReadOutcome Learn(bool playerWasStrong)
        {
            var b = Belief;
            if (b == null || !b.Used || b.Learned) return ReadOutcome.None;
            b.Learned = true;
            bool fooled = b.Belief == "weak" ? playerWasStrong : !playerWasStrong;
            if (_settings.LearnFromShowdown)
            {
                foreach (var c in b.Cues)
                {
                    int i = (int)c;
                    _trust[i] = fooled ? Math.Max(MinTrust, _trust[i] * _settings.FooledDiscount) : Math.Min(1.0, _trust[i] + _settings.TrustRecover);
                }
                // 被「演弱」騙了的人，演強多半也是演的（反之亦然）：反方向的線索也打折（少一點）
                if (fooled)
                {
                    double cross = Math.Sqrt(_settings.FooledDiscount);
                    foreach (ReadCue c in Enum.GetValues(typeof(ReadCue)))
                        if ((c == ReadCue.Snap) == (b.Belief == "weak")) _trust[(int)c] = Math.Max(MinTrust, _trust[(int)c] * cross);
                }
            }
            return fooled ? ReadOutcome.Fooled : ReadOutcome.Right;
        }

        /// <summary>
        /// 每條線索的原始權重（正值＝玩家弱）。依據：一般現場撲克的常識（想很久、一直看牌、伸手又縮回、玩籌碼＝不確定或弱；秒下＝有牌），
        /// 大小用自我對弈校準（LiveReadSelfPlayTests）：誠實的玩家會被讀到、但演假破綻的人只能騙到前幾次。
        /// </summary>
        public static double Weight(ReadCue c, PlayerSignals s)
        {
            switch (c)
            {
                case ReadCue.LongThink: return s.ThinkSeconds >= 12f ? 0.07 : s.ThinkSeconds >= 7f ? 0.05 : 0;
                case ReadCue.Snap: return !s.Folded && s.ThinkSeconds > 0f && s.ThinkSeconds <= 1.5f && (s.Aggressive || s.Delivery != ChipDelivery.None) ? -0.06 : 0;
                case ReadCue.ReLook: return s.Looks >= 2 ? 0.03 + 0.02 * (Math.Min(s.Looks, 4) - 2) : 0;
                case ReadCue.ReachRetreat: return s.PullBacks >= 1 ? 0.05 + 0.02 * (Math.Min(s.PullBacks, 2) - 1) : 0;
                case ReadCue.Fiddle: return s.PutBacks >= 1 || s.HeldCount >= 1 ? 0.04 : 0;
                default: return s.TowardCards ? 0.04 : 0;
            }
        }

        LiveBelief ReadTurn(PlayerSignals s)
        {
            if (s.Folded) return null;
            double sum = 0;
            var parts = new List<(ReadCue c, double w)>();
            foreach (ReadCue c in Enum.GetValues(typeof(ReadCue)))
            {
                double w = Weight(c, s) * _trust[(int)c];
                if (w == 0) continue;
                parts.Add((c, w));
                sum += w;
            }
            double eff = Clamp(sum * _sensitivity * Math.Max(0f, Math.Min(1f, _settings.Intensity)), PlayerReadSettings.LiveMaxShift);
            if (Math.Abs(eff) < 0.005) return null;
            parts.Sort((a, b) => Math.Abs(b.w).CompareTo(Math.Abs(a.w)));
            var belief = new LiveBelief { Belief = eff > 0 ? "weak" : "strong", Effect = eff, Time = s.Time };
            foreach (var x in parts) if (Math.Sign(x.w) == Math.Sign(eff)) belief.Cues.Add(x.c);
            return belief;
        }

        /// <summary>清除記憶（換玩家、重開局）。</summary>
        public void Reset()
        {
            _shift = 0; LastNotice = PlayerNotice.None; Belief = null;
            for (int i = 0; i < _trust.Length; i++) _trust[i] = 1;
        }

        double Decayed(double now)
        {
            if (_shift == 0) return 0;
            double dt = Math.Max(0, now - _shiftTime);
            return _shift * Math.Exp(-dt / Math.Max(0.001, _settings.DecaySeconds));
        }

        static double Clamp(double v, double max) => v > max ? max : v < -max ? -max : v;
    }
}
