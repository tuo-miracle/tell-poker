using System;
using System.Collections.Generic;
using Poker.Gestures;

namespace Poker.Game
{
    /// <summary>教學 v4 的步驟：第 1 手看牌、面對 Rocco 的加注做決定；第 2 手（拿到怪物牌）照 Dot 教的「等幾秒再下注」騙 Rocco。</summary>
    public enum TutorialStep { Peek, Decide, Bet, Done }
    public enum TutorialAction { Peeked, Looked, Called, Raised, Folded }
    /// <summary>教學時桌面要發光的地方。GazeTarget＝指向要盯著看的角色（Rocco）。</summary>
    public enum TutorialGlow { None, Cards, ChipsAndBetZone, KnockSpot, FoldLine, GazeTarget }

    /// <summary>
    /// 荷官 Dot 帶的教學（v4，2026-10-14）：評審只玩一次、約 3 分鐘，很多人不會打牌，所以只教核心玩法「雙向讀心」——
    /// 第 1 手：看牌（咪牌或拿起來都算）→ 牌力提示 → Rocco 加注 → 玩家想的時候 Rocco 就用即時系統讀他、說出來 → 跟或棄（都算）→ 揭曉「Rocco read you: …」；
    /// 第 2 手：拿到 AA，Dot 教「Rocco 以為伸手慢＝牌弱，等幾秒再下注」→ 下注（等了：Rocco 跟、輸、「You played me!」；沒等：Rocco 棄、「下次等等看」）。
    /// 敲桌過牌、All-in、攤牌翻牌、盯人、語音對話改在正式牌局第一次用得到時教（JustInTimeTips）。
    /// 不會自動跳過、不替玩家做：卡住時升級提示——這一步可以做的時間滿 Hint1Seconds 秒重播示範手＋重講，
    /// 滿 Hint2Seconds 秒依辨識器「差一點」的原因（NoteNearMiss）給具體建議，之後重新計時循環。
    /// 有聲音的提示每一步最多 MaxHintCycles 輪就安靜（HintsQuieted；示範手與發光照舊，遊戲層負責）；玩家又有動作（NoteActivity、差一點）才再給一輪。
    /// 2026-10-14 實機：頭盔放著，Dot 在看牌那一步輪流重講與給建議講了 2 小時。
    /// 只有手腕選單的「Skip tutorial」（SkipAll）能跳過整個教學。教學中只接受目前這一步的動作（看牌任何時候都可以）。
    /// </summary>
    public sealed class Tutorial
    {
        /// <summary>教學版本（存進 ProgressData.TutorialVersion）。1＝舊的 4 步；2＝示範手教學；3＝兩手、九步的情境教學；4＝短的讀心教學。</summary>
        public const int CurrentVersion = 4;
        public const float Hint1Seconds = 10f, Hint2Seconds = 25f;
        /// <summary>每一步有聲音的提示最多幾輪（一輪＝重講＋具體建議），之後安靜到玩家又有動作。</summary>
        public const int MaxHintCycles = 2;
        /// <summary>咪牌掀到這個程度才算看到牌。</summary>
        public const float PeekThreshold = 0.45f;

        /// <summary>這份存檔要不要教。</summary>
        public static bool IsNeeded(ProgressData p) => p == null || p.CompletedTutorialVersion < CurrentVersion;

        public TutorialStep Step { get; private set; } = TutorialStep.Peek;
        public bool IsActive => Step != TutorialStep.Done;
        /// <summary>玩家從選單跳過了整個教學。</summary>
        public bool SkippedByPlayer { get; private set; }
        /// <summary>這一步「可以做」的累計秒數（遊戲層只在可以做、選單關著、Dot 沒在講話時 Tick）；每輪提示後歸零。</summary>
        public float StepTime { get; private set; }
        /// <summary>這一步可以做的總秒數（不因提示循環歸零；試玩 log 的 stuck 秒數）。</summary>
        public float TotalStepTime { get; private set; }
        /// <summary>這一輪已給到第幾級提示（0＝還沒、1＝重播＋重講、2＝具體建議）。</summary>
        public int HintLevel { get; private set; }
        /// <summary>這一步最近一次「差一點」的原因（辨識器的 trace 或遊戲層判定）；換步驟就清掉。</summary>
        public string LastNearMiss { get; private set; }

        public event Action<TutorialStep> StepCompleted;
        /// <summary>該給提示了（步驟、等級 1 或 2、最近的差一點原因或 null）。</summary>
        public event Action<TutorialStep, int, string> HintDue;
        /// <summary>這一步的提示用完了、開始安靜（每次安靜一次）。</summary>
        public event Action<TutorialStep> HintsQuieted;

        int _cyclesLeft = MaxHintCycles;
        /// <summary>提示用完了：不再出聲，等玩家有動作（NoteActivity／NoteNearMiss）或回來（RestartIdle）。</summary>
        public bool HintsQuiet => _cyclesLeft <= 0;

        /// <summary>回報玩家做了某個動作；是目前這一步就前進並回傳 true。</summary>
        public bool Report(TutorialAction a)
        {
            if (!IsActive || !Matches(Step, a)) return false;
            var done = Step;
            ResetStep();
            Step = Step + 1;
            StepCompleted?.Invoke(done);
            return true;
        }

        /// <summary>這一步可以做的時間往前推；到點就發提示。永遠不會自己跳過。</summary>
        public void Tick(float dt)
        {
            if (!IsActive) return;
            StepTime += dt;
            TotalStepTime += dt;
            if (HintsQuiet) return;
            if (HintLevel == 0 && StepTime >= Hint1Seconds)
            {
                HintLevel = 1;
                HintDue?.Invoke(Step, 1, LastNearMiss);
            }
            else if (HintLevel == 1 && StepTime >= Hint2Seconds)
            {
                var why = LastNearMiss;
                HintLevel = 0;
                StepTime = 0f;
                _cyclesLeft--;
                HintDue?.Invoke(Step, 2, why);
                if (HintsQuiet) HintsQuieted?.Invoke(Step);
            }
        }

        /// <summary>玩家有動作（手在桌上方動、手勢事件）：安靜中就再給一輪提示（從 0 計時）；還沒安靜就不影響（看的是這一步卡了多久）。</summary>
        public void NoteActivity()
        {
            if (IsActive && HintsQuiet) Wake();
        }

        /// <summary>暫停（沒有手、失焦）之後回來：這一輪從 0 重新計時，最多再一輪提示（不是整套重來）。</summary>
        public void RestartIdle()
        {
            if (IsActive) Wake();
        }

        void Wake()
        {
            _cyclesLeft = 1;
            StepTime = 0f;
            HintLevel = 0;
        }

        /// <summary>記下這一步「差一點」的原因（下一次具體建議用）。</summary>
        public void NoteNearMiss(string reason)
        {
            if (!IsActive || string.IsNullOrEmpty(reason)) return;
            LastNearMiss = reason;
            NoteActivity();   // 差一點也是玩家在試
        }

        /// <summary>手腕選單的「Skip tutorial」：整個教學結束。</summary>
        public void SkipAll()
        {
            if (!IsActive) return;
            SkippedByPlayer = true;
            Step = TutorialStep.Done;
            ResetStep();
        }

        void ResetStep()
        {
            StepTime = 0f;
            TotalStepTime = 0f;
            HintLevel = 0;
            LastNearMiss = null;
            _cyclesLeft = MaxHintCycles;
        }

        /// <summary>教學中這個手勢要不要處理：看牌一律可以；籌碼在決定與下注那一步；棄牌只在第 1 手的決定；其他（敲桌、All-in、翻牌）不教。</summary>
        public bool Allows(GestureKind k)
        {
            if (!IsActive) return true;
            switch (k)
            {
                case GestureKind.PeekChanged:
                case GestureKind.CardsLifted:
                case GestureKind.CardsReturned:
                    return true;
                case GestureKind.ChipsPicked:
                case GestureKind.ChipCountChanged:
                case GestureKind.ChipsReleased:
                case GestureKind.BetRetracted:
                case GestureKind.BetCommitted:
                    return Step == TutorialStep.Decide || Step == TutorialStep.Bet;
                case GestureKind.FoldPushed:
                    return Step == TutorialStep.Decide;
                default:
                    return false;
            }
        }

        /// <summary>看牌：咪牌或拿起來；決定：跟、加注、棄牌都算；下注：只有下注（加注）。</summary>
        static bool Matches(TutorialStep s, TutorialAction a)
        {
            switch (s)
            {
                case TutorialStep.Peek: return a == TutorialAction.Peeked || a == TutorialAction.Looked;
                case TutorialStep.Decide: return a == TutorialAction.Called || a == TutorialAction.Raised || a == TutorialAction.Folded;
                case TutorialStep.Bet: return a == TutorialAction.Raised;
                default: return false;
            }
        }

        /// <summary>這一步在教學的第幾手。</summary>
        public static int HandOf(TutorialStep s) => s <= TutorialStep.Decide ? 1 : 2;

        // ================= 台詞（每一句都有預錄聲音，見 Lines） =================

        public const string Finished = "You're ready. Good luck!";
        /// <summary>從選單跳過教學。</summary>
        public const string SkipLine = "Okay, let's play!";
        /// <summary>遊戲層記的差一點原因：籌碼放開時不在下注區。</summary>
        public const string ReasonOutsideZone = "released outside the bet zone";
        /// <summary>棄牌差一點的原因加這個前綴（決定那一步籌碼與棄牌都可以，具體建議要分得出來）。</summary>
        public const string FoldReasonPrefix = "fold: ";

        const string PromptPeek = "Let's peek at your cards — cup your hand over them and lift your wrist.";
        const string PromptDecide = "Rocco raised, and he's reading you. Toss in chips to call, or push your cards forward to fold.";

        static readonly string[] Praises = { "Nice!", "Perfect!", "Great job!", "You got it!", "Well done!" };

        /// <summary>第 n 次讚美（輪流）。</summary>
        public static string Praise(int n) => Praises[((n % Praises.Length) + Praises.Length) % Praises.Length];

        public static string Prompt(TutorialStep s)
        {
            switch (s)
            {
                case TutorialStep.Peek: return PromptPeek;
                case TutorialStep.Decide: return PromptDecide;
                // 第 2 手：沿用 Fool Rocco 的教學句（已經預錄、聽檢過）
                case TutorialStep.Bet: return Poker.AI.FoolRocco.DotCoach.Text;
                default: return "";
            }
        }

        // 具體建議（第二級提示）
        const string TipWrist = "Lift your wrist a little higher.";
        const string TipFingertips = "Rest your fingertips on the cards, then lift your wrist.";
        const string TipPalmDown = "Turn your palm down over your cards.";
        const string TipOverCards = "Move your hand right over your cards.";
        const string TipPinchChips = "Pinch right on the chips.";
        const string TipZone = "Toss them onto the glowing zone.";
        const string TipFold = "Toss your cards toward the middle.";

        /// <summary>第二級提示：依這一步與差一點的原因（null＝沒看到嘗試）給一句具體建議。</summary>
        public static string Tip(TutorialStep s, string reason)
        {
            bool noAttempt = string.IsNullOrEmpty(reason);   // 沒有差一點的紀錄＝還沒試過：先教最基本的一步
            reason = reason ?? "";
            switch (s)
            {
                case TutorialStep.Peek:
                    if (noAttempt) return TipOverCards;
                    if (reason == "palm too high" || reason == "fingertips too high") return TipFingertips;
                    if (reason == "palm not down" || reason == "palm turned too far") return TipPalmDown;
                    if (reason == "hand beside the cards") return TipOverCards;
                    return TipWrist;
                case TutorialStep.Decide:
                case TutorialStep.Bet:
                    if (s == TutorialStep.Decide && reason.StartsWith(FoldReasonPrefix, StringComparison.Ordinal)) return TipFold;
                    if (reason == ReasonOutsideZone) return TipZone;
                    return TipPinchChips;
                default: return TipWrist;
            }
        }

        /// <summary>
        /// 從辨識器的診斷行（GestureRecognizer.Trace）讀出和這一步有關的「差一點」原因；無關的行回傳 null。
        /// 咪牌：「peek near-miss … reason X」；籌碼：「chip near-miss … reason X」；棄牌（只有第 1 手的決定）：「fold-push reset Hand: X (…」→「fold: X」。
        /// </summary>
        public static string ReasonFromTrace(TutorialStep s, string trace)
        {
            if (string.IsNullOrEmpty(trace)) return null;
            switch (s)
            {
                case TutorialStep.Peek:
                    if (trace.Contains("cards grabbed")) return "grabbed, not lifted";
                    return trace.Contains("peek near-miss") ? After(trace, "reason ") : null;
                case TutorialStep.Decide:
                case TutorialStep.Bet:
                {
                    if (trace.Contains("chip near-miss")) return After(trace, "reason ");
                    if (s != TutorialStep.Decide) return null;
                    int i = trace.IndexOf("fold-push reset ", StringComparison.Ordinal);
                    if (i < 0) return null;
                    int c = trace.IndexOf(": ", i, StringComparison.Ordinal);
                    var why = c < 0 ? null : Clip(trace.Substring(c + 2));
                    return why == null ? null : FoldReasonPrefix + why;
                }
                default:
                    return null;
            }
        }

        /// <summary>key 之後的文字（到「 (」為止）。</summary>
        static string After(string s, string key)
        {
            int i = s.LastIndexOf(key, StringComparison.Ordinal);
            return i < 0 ? null : Clip(s.Substring(i + key.Length));
        }

        /// <summary>去掉括號裡的數值與頭尾空白。</summary>
        static string Clip(string s)
        {
            int p = s.IndexOf(" (", StringComparison.Ordinal);
            if (p >= 0) s = s.Substring(0, p);
            s = s.Trim();
            return s.Length == 0 ? null : s;
        }

        /// <summary>
        /// 教學要預錄的句子（id、原文）：提示、讚美、具體建議、結尾、跳過。沿用 v3 的句子保留原本的 id（不用重新合成）；
        /// 第 2 手的提示是 Fool Rocco 的句子，在 FoolRocco.Lines() 列，這裡不重複列。
        /// </summary>
        public static IEnumerable<(string id, string text)> Lines()
        {
            yield return ("dot.tutorial.peek", PromptPeek);
            yield return ("dot.tutorial.decide", PromptDecide);
            for (int i = 0; i < Praises.Length; i++) yield return ("dot.praise." + (i + 1), Praises[i]);
            yield return ("dot.tutorial.finished", Finished);
            yield return ("dot.tutorial.skip", SkipLine);
            yield return ("dot.tip.peek.wrist", TipWrist);
            yield return ("dot.tip.peek.fingertips", TipFingertips);
            yield return ("dot.tip.peek.palmdown", TipPalmDown);
            yield return ("dot.tip.peek.overcards", TipOverCards);
            yield return ("dot.tip.chips.pinch", TipPinchChips);
            yield return ("dot.tip.chips.zone", TipZone);
            yield return ("dot.tip.fold", TipFold);
        }

        public static TutorialGlow GlowFor(TutorialStep s)
        {
            switch (s)
            {
                case TutorialStep.Peek: return TutorialGlow.Cards;
                case TutorialStep.Decide:
                case TutorialStep.Bet:
                    return TutorialGlow.ChipsAndBetZone;
                default: return TutorialGlow.None;
            }
        }

        static readonly GhostGesture[] GhostPeek = { GhostGesture.Peek };
        static readonly GhostGesture[] GhostDecide = { GhostGesture.Bet, GhostGesture.Fold };
        static readonly GhostGesture[] GhostBet = { GhostGesture.Bet };

        /// <summary>這一步的示範手（決定那一步輪流示範跟注與棄牌）。</summary>
        public static GhostGesture[] GhostsFor(TutorialStep s) =>
            s == TutorialStep.Decide ? GhostDecide : s == TutorialStep.Bet ? GhostBet : GhostPeek;
    }
}
