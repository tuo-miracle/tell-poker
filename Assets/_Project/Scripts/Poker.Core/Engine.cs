using System;
using System.Collections.Generic;

namespace Poker.Core
{
    /// <summary>
    /// No-Limit Texas Hold'em 規則引擎（依 Poker TDA Rules 2026 v1.1）。「指令進、事件出」，純函式、確定性：
    /// 相同的初始狀態＋相同的指令序列，一定產生相同的事件與狀態（可重播、可搬到伺服器）。
    /// <para>TDA rules used by permission of the Poker TDA, Copyright 2026, http://www.pokertda.com, All rights reserved.</para>
    /// </summary>
    public static class Engine
    {
        /// <summary>
        /// 套用一個指令。成功時回傳新狀態與事件；失敗時回傳錯誤，且 <see cref="ApplyResult.State"/> 就是傳入的
        /// <paramref name="state"/>（完全不變）。
        /// </summary>
        public static ApplyResult Apply(HandState state, Command command)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            if (command == null) throw new ArgumentNullException(nameof(command));
            switch (command)
            {
                case StartHand sh: return Start(state, sh);
                case PlayerCommand pc: return Act(state, pc);
                default: return Fail(state, EngineErrorCode.UnknownCommand, "未知的指令：" + command.GetType().Name);
            }
        }

        /// <summary>
        /// 查詢 <paramref name="seat"/> 目前可做的合法行動。不是該座位的回合（或沒有牌局）時回傳 <c>IsTurn = false</c>。
        /// </summary>
        public static LegalActions GetLegalActions(HandState state, int seat)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            if (state.Phase != HandPhase.Betting || seat < 0 || seat >= state.SeatCount || seat != state.ToAct)
                return LegalActions.NotYourTurn;
            return ComputeLegal(state, seat);
        }

        // ───────────── InterpretChipCommit ─────────────

        internal const string RuleAllIn = "TDA 47-B(2)：推出最後的籌碼一律視為 all-in";
        internal const string RuleClosed = "TDA 49-A：短 all-in 未重開下注，只能跟注，多出的籌碼退回";
        internal const string RuleHalf = "TDA 45-A：加注額達前一次完整下注／加注的 50% 但不足最小加注 → 須補成最小加注";
        internal const string RuleHalfCall = "TDA 45-A：加注額不足前一次完整下注／加注的 50% → 視為跟注，多出的籌碼退回";
        internal const string RuleUnderBet = "TDA 54-A：開局下注少於最小下注 → 補成最小下注";
        internal const string RuleUndercallMandatory = "TDA 53-B：單挑面對下注、或多人面對開局下注時，不足跟注額須補足跟注";
        internal const string RuleUndercallDiscretion = "TDA 53-B：多人面對加注時不足跟注額屬 TD 裁量，請玩家重新確認";

        /// <summary>
        /// 把「玩家這次推出的籌碼數 <paramref name="amount"/>」（不含本街先前已下的注）轉成合法指令，供手勢層使用。
        /// <list type="bullet">
        /// <item>等於全部籌碼 → AllIn（TDA 47-B(2)）；若加注權已被短 all-in 關閉則視為跟注（49-A）。</item>
        /// <item>等於跟注額 → Call；沒有要跟時 0 枚 → Check。</item>
        /// <item>達到最小加注 → Raise／Bet 到該總額。</item>
        /// <item>介於跟注與最小加注之間：多出的部分 ≥ 前一次完整下注／加注增量的 50% → 強制最小加注，否則視為跟注並退回多餘籌碼
        ///   （TDA 2026 Rule 45-A 與 47-B(1)；Addendum Rule 47 Ex.2：跟注 1100、最小加注增量 600，推出 ≥1400 須加到 1700）。
        ///   註：此 50% 規則在 TDA 2026 是 Rule 45-A（舊版條號 43/44 已改）。</item>
        /// <item>開局下注不足最小下注 → 補到最小下注（54-A）。</item>
        /// <item>不足跟注額：單挑或面對開局下注 → 強制補足跟注；其他情況屬 TD 裁量，回傳 Invalid（53-B）。</item>
        /// </list>
        /// 本函式不改變狀態。回傳的 <see cref="ChipCommitInterpretation.Adjustment"/> 告訴 UI 要補或退多少籌碼。
        /// </summary>
        public static ChipCommitInterpretation InterpretChipCommit(HandState state, int seat, int amount)
        {
            var la = GetLegalActions(state, seat);
            if (!la.IsTurn) return Invalid(amount, "不是此座位的回合");
            if (amount < 0) return Invalid(amount, "籌碼數不可為負");
            if (amount > la.Stack) return Invalid(amount, "推出的籌碼多於剩餘籌碼");

            int toCall = Math.Max(0, la.CurrentBet - la.StreetBet);
            bool raiseOpen = la.CanBet || la.CanRaise;

            if (amount == 0)
                return toCall == 0
                    ? Make(ChipCommitKind.Check, new Check(seat), 0, 0, "過牌")
                    : Invalid(0, "面對下注時須跟注或棄牌");

            if (amount == la.Stack)
            {
                if (la.CanAllIn)
                    return Make(ChipCommitKind.AllIn, new AllIn(seat), amount, la.Stack, RuleAllIn);
                // 加注權關閉（或沒有對手可回應）且籌碼多於跟注額：只能跟注
                return CallOrCheck(seat, amount, la, RuleClosed);
            }

            if (toCall > 0 && amount < la.CallAmount)
            {
                bool headsUp = CountLive(state) == 2;
                bool facingOpener = state.BetCount <= 1;
                return headsUp || facingOpener
                    ? Make(ChipCommitKind.Call, new Call(seat), amount, la.CallAmount, RuleUndercallMandatory)
                    : Invalid(amount, RuleUndercallDiscretion);
            }

            if (!raiseOpen) return CallOrCheck(seat, amount, la, RuleClosed);

            int to = la.StreetBet + amount;
            if (to >= la.MinTo) return BetOrRaise(seat, to, amount, la, "達到最小加注");

            if (la.CurrentBet == 0)
                return BetOrRaise(seat, la.MinTo, amount, la, RuleUnderBet);

            int excess = amount - toCall; // 超過跟注額的部分
            if (excess > 0 && 2L * excess >= state.LastFullRaise)
                return BetOrRaise(seat, la.MinTo, amount, la, RuleHalf);
            return CallOrCheck(seat, amount, la, excess > 0 ? RuleHalfCall : "跟注");
        }

        private static ChipCommitInterpretation BetOrRaise(int seat, int to, int pushed, LegalActions la, string rule)
        {
            int committed = to - la.StreetBet;
            if (to >= la.MaxTo) return Make(ChipCommitKind.AllIn, new AllIn(seat), pushed, la.Stack, rule);
            return la.CanBet
                ? Make(ChipCommitKind.Bet, new Bet(seat, to), pushed, committed, rule)
                : Make(ChipCommitKind.Raise, new Raise(seat, to), pushed, committed, rule);
        }

        private static ChipCommitInterpretation CallOrCheck(int seat, int pushed, LegalActions la, string rule)
        {
            return la.CanCheck
                ? Make(ChipCommitKind.Check, new Check(seat), pushed, 0, rule)
                : Make(ChipCommitKind.Call, new Call(seat), pushed, la.CallAmount, rule);
        }

        private static ChipCommitInterpretation Make(ChipCommitKind kind, PlayerCommand cmd, int pushed, int committed, string rule)
            => new ChipCommitInterpretation(kind, cmd, pushed, committed, rule);

        private static ChipCommitInterpretation Invalid(int pushed, string why)
            => new ChipCommitInterpretation(ChipCommitKind.Invalid, null, pushed, 0, why);

        // ───────────── StartHand ─────────────

        private static ApplyResult Start(HandState state, StartHand cmd)
        {
            if (state.Phase == HandPhase.Betting) return Fail(state, EngineErrorCode.HandInProgress, "目前牌局尚未結束");
            if (cmd.SmallBlind <= 0 || cmd.BigBlind <= 0 || cmd.SmallBlind > cmd.BigBlind)
                return Fail(state, EngineErrorCode.InvalidBlinds, "盲注必須為正整數且小盲 ≤ 大盲");
            int n = state.SeatCount;
            int liveCount = 0;
            for (int i = 0; i < n; i++) if (state.SeatArray[i].Stack > 0) liveCount++;
            if (liveCount < 2) return Fail(state, EngineErrorCode.NotEnoughPlayers, "有籌碼的玩家不足 2 人");

            int button, sbSeat, bbSeat, sbPosition;
            if (cmd.Button.HasValue || state.HandNumber == 0)
            {
                int b;
                if (cmd.Button.HasValue)
                {
                    b = cmd.Button.Value;
                    if (b < 0 || b >= n || state.SeatArray[b].Stack <= 0)
                        return Fail(state, EngineErrorCode.InvalidButton, "按鈕必須是有籌碼的座位：" + b);
                }
                else b = NextWithChips(state, n - 1);
                button = b;
                if (liveCount == 2)
                {
                    // TDA 36-C：單挑時按鈕＝小盲
                    sbSeat = b;
                    bbSeat = NextWithChips(state, b);
                }
                else
                {
                    sbSeat = NextWithChips(state, b);
                    bbSeat = NextWithChips(state, sbSeat);
                }
                sbPosition = sbSeat;
            }
            else
            {
                // 自動輪轉（TDA Rule 34／36-B dead button）：大盲一定前進到上一手大盲之後的下一位有籌碼者，
                // 小盲位置＝上一手大盲座位（若其已出局則本手無小盲），按鈕＝上一手小盲位置（可能是空位）。
                int prevBb = state.BigBlindSeat;
                bbSeat = NextWithChips(state, prevBb);
                if (liveCount == 2)
                {
                    // TDA 36-C：單挑按鈕＝小盲；由「大盲前進」決定，所以上一手的大盲不會連續兩次當大盲。
                    sbSeat = NextWithChips(state, bbSeat);
                    button = sbSeat;
                    sbPosition = sbSeat;
                }
                else
                {
                    sbPosition = prevBb;
                    sbSeat = state.SeatArray[prevBb].Stack > 0 ? prevBb : -1;
                    button = state.SmallBlindPosition;
                }
            }

            // 發牌順序：按鈕左邊第一位有籌碼者起，順時針；按鈕最後拿牌。
            var order = new List<int>(liveCount);
            for (int k = 1; k <= n; k++)
            {
                int i = (button + k) % n;
                if (state.SeatArray[i].Stack > 0) order.Add(i);
            }

            Card[] deck;
            var presetError = BuildDeck(state, cmd, order, out deck);
            if (presetError != null) return Fail(state, EngineErrorCode.InvalidPreset, presetError);

            var s = state.Clone();
            var events = new List<GameEvent>();
            var startStacks = s.Stacks();
            for (int i = 0; i < n; i++)
            {
                ref var x = ref s.SeatArray[i];
                x.StreetBet = 0; x.Committed = 0; x.HasActed = false; x.ActedLevel = 0;
                x.Hole1 = default; x.Hole2 = default;
                x.Status = x.Stack > 0 ? SeatStatus.Active : SeatStatus.Out;
            }
            s.HandNumber = state.HandNumber + 1;
            s.Seed = cmd.Seed;
            s.Button = button; s.SmallBlindSeat = sbSeat; s.BigBlindSeat = bbSeat; s.SmallBlindPosition = sbPosition;
            s.SmallBlind = cmd.SmallBlind; s.BigBlind = cmd.BigBlind;
            s.Phase = HandPhase.Betting; s.Street = Street.Preflop;
            s.BoardList = new List<Card>();
            s.DeckCards = deck; s.DeckPosition = 0;
            s.CurrentBet = 0; s.LastFullRaise = cmd.BigBlind; s.LastAggressor = -1; s.BetCount = 0;

            var inHand = order.ToArray();
            Array.Sort(inHand);
            events.Add(new HandStarted(s.HandNumber, button, sbSeat, bbSeat, cmd.SmallBlind, cmd.BigBlind, startStacks, inHand));

            if (sbSeat >= 0) PostBlind(s, sbSeat, BlindType.Small, cmd.SmallBlind, events);
            PostBlind(s, bbSeat, BlindType.Big, cmd.BigBlind, events);
            // 跟注水位＝實際貼出的最高盲注（TDA 53-B：貼出的大盲即開局下注）；最小加注增量仍為完整大盲（45-A）。
            for (int i = 0; i < n; i++) s.CurrentBet = Math.Max(s.CurrentBet, s.SeatArray[i].StreetBet);
            s.BetCount = 1;

            int m = order.Count;
            for (int k = 0; k < m; k++)
            {
                ref var x = ref s.SeatArray[order[k]];
                x.Hole1 = deck[k];
                x.Hole2 = deck[m + k];
                events.Add(new HoleCardsDealt(order[k], new[] { x.Hole1, x.Hole2 }));
            }
            s.DeckPosition = 2 * m;

            Advance(s, events, bbSeat);
            return Ok(s, events);
        }

        private static void PostBlind(HandState s, int seat, BlindType type, int blind, List<GameEvent> events)
        {
            ref var x = ref s.SeatArray[seat];
            int amt = Math.Min(blind, x.Stack);
            x.Stack -= amt; x.StreetBet += amt; x.Committed += amt;
            if (x.Stack == 0) x.Status = SeatStatus.AllIn;
            events.Add(new BlindPosted(seat, type, amt, x.Stack == 0));
        }

        /// <summary>依種子洗牌；有指定牌時把指定牌放到對應的發牌位置，其餘位置依洗好的順序補上。</summary>
        private static string BuildDeck(HandState state, StartHand cmd, List<int> order, out Card[] deck)
        {
            var shuffled = Deck.Shuffled(cmd.Seed);
            deck = null;
            var preset = cmd.Preset;
            int m = order.Count;
            if (preset == null) { deck = new List<Card>(shuffled).ToArray(); return null; }

            var result = new Card[52];
            ulong used = 0;
            bool Use(Card c, int pos, ref string err)
            {
                if (!c.IsValid) { err = "指定牌含無效牌"; return false; }
                if ((used & c.Mask) != 0) { err = "指定牌重複：" + c; return false; }
                used |= c.Mask; result[pos] = c; return true;
            }
            string error = null;
            foreach (var kv in preset.HoleCards)
            {
                int k = order.IndexOf(kv.Key);
                if (k < 0) return "指定底牌的座位沒有參與本手：" + kv.Key;
                if (kv.Value.Count != 2) return "每個座位必須指定剛好 2 張底牌";
                if (!Use(kv.Value[0], k, ref error) || !Use(kv.Value[1], m + k, ref error)) return error;
            }
            if (preset.Board.Count > 5) return "公共牌最多 5 張";
            int[] boardPos = { 2 * m + 1, 2 * m + 2, 2 * m + 3, 2 * m + 5, 2 * m + 7 };
            for (int i = 0; i < preset.Board.Count; i++)
                if (!Use(preset.Board[i], boardPos[i], ref error)) return error;
            int src = 0;
            for (int pos = 0; pos < 52; pos++)
            {
                if (result[pos].IsValid) continue;
                while ((used & shuffled[src].Mask) != 0) src++;
                result[pos] = shuffled[src];
                used |= shuffled[src].Mask;
            }
            deck = result;
            return null;
        }

        // ───────────── 玩家行動 ─────────────

        private static ApplyResult Act(HandState state, PlayerCommand cmd)
        {
            if (state.Phase != HandPhase.Betting) return Fail(state, EngineErrorCode.NoHandInProgress, "目前沒有進行中的牌局");
            int seat = cmd.Seat;
            if (seat < 0 || seat >= state.SeatCount) return Fail(state, EngineErrorCode.InvalidSeat, "座位編號不合法：" + seat);
            if (seat != state.ToAct) return Fail(state, EngineErrorCode.NotYourTurn, "目前輪到座位 " + state.ToAct);
            var la = ComputeLegal(state, seat);

            var s = state.Clone();
            var events = new List<GameEvent>();
            switch (cmd)
            {
                case Fold _:
                {
                    ref var x = ref s.SeatArray[seat];
                    x.Status = SeatStatus.Folded;
                    x.HasActed = true;
                    events.Add(new PlayerActed(seat, ActionType.Fold, 0, x.StreetBet, x.Stack, false, false));
                    break;
                }
                case Check _:
                {
                    if (!la.CanCheck) return Fail(state, EngineErrorCode.IllegalAction, "面對下注不能過牌");
                    ref var x = ref s.SeatArray[seat];
                    x.HasActed = true;
                    x.ActedLevel = s.CurrentBet;
                    events.Add(new PlayerActed(seat, ActionType.Check, 0, x.StreetBet, x.Stack, false, false));
                    break;
                }
                case Call _:
                    if (!la.CanCall) return Fail(state, EngineErrorCode.IllegalAction, "沒有需要跟注的金額，請過牌");
                    PutChips(s, seat, la.StreetBet + la.CallAmount, events);
                    break;
                case Bet b:
                    if (state.CurrentBet != 0) return Fail(state, EngineErrorCode.IllegalAction, "本街已有下注，請用加注（Raise）");
                    if (!la.CanBet) return Fail(state, EngineErrorCode.IllegalAction, "目前不能下注");
                    if (b.Amount < la.MinTo || b.Amount > la.MaxTo)
                        return Fail(state, EngineErrorCode.InvalidAmount, $"下注額必須介於 {la.MinTo} 與 {la.MaxTo}");
                    PutChips(s, seat, b.Amount, events);
                    break;
                case Raise r:
                    if (state.CurrentBet == 0) return Fail(state, EngineErrorCode.IllegalAction, "本街尚無下注，請用下注（Bet）");
                    if (!la.CanRaise) return Fail(state, EngineErrorCode.IllegalAction, "目前不能加注（加注權未重開或沒有對手可回應）");
                    if (r.ToAmount < la.MinTo || r.ToAmount > la.MaxTo)
                        return Fail(state, EngineErrorCode.InvalidAmount, $"加注到的金額必須介於 {la.MinTo} 與 {la.MaxTo}");
                    PutChips(s, seat, r.ToAmount, events);
                    break;
                case AllIn _:
                    if (!la.CanAllIn) return Fail(state, EngineErrorCode.IllegalAction, "目前不能 all-in（只能跟注或棄牌）");
                    PutChips(s, seat, la.AllInTo, events);
                    break;
                default:
                    return Fail(state, EngineErrorCode.UnknownCommand, "未知的指令：" + cmd.GetType().Name);
            }
            Advance(s, events, seat);
            return Ok(s, events);
        }

        /// <summary>把座位本街下注補到 <paramref name="to"/>；處理完整加注／短 all-in。</summary>
        private static void PutChips(HandState s, int seat, int to, List<GameEvent> events)
        {
            ref var x = ref s.SeatArray[seat];
            int add = to - x.StreetBet;
            x.Stack -= add; x.StreetBet = to; x.Committed += add;
            bool allIn = x.Stack == 0;
            if (allIn) x.Status = SeatStatus.AllIn;

            ActionType type = ActionType.Call;
            bool full = false;
            if (to > s.CurrentBet)
            {
                type = s.CurrentBet == 0 ? ActionType.Bet : ActionType.Raise;
                int increment = to - s.CurrentBet;
                // TDA 45-A：達到「本輪最大的前一次完整下注／加注」才算完整加注；否則為短 all-in，不改變最小加注單位（49-A）。
                if (increment >= s.LastFullRaise) { full = true; s.LastFullRaise = increment; }
                s.CurrentBet = to;
                s.LastAggressor = seat;
                s.BetCount++;
            }
            x.HasActed = true;
            x.ActedLevel = s.CurrentBet;
            events.Add(new PlayerActed(seat, type, add, to, x.Stack, allIn, full));
        }

        private static LegalActions ComputeLegal(HandState s, int seat)
        {
            var x = s.SeatArray[seat];
            int toCall = Math.Max(0, s.CurrentBet - x.StreetBet);
            var la = new LegalActions
            {
                IsTurn = true,
                CanFold = true,
                CanCheck = toCall == 0,
                CanCall = toCall > 0,
                CallAmount = Math.Min(toCall, x.Stack),
                Stack = x.Stack,
                StreetBet = x.StreetBet,
                CurrentBet = s.CurrentBet,
                AllInTo = x.StreetBet + x.Stack,
            };
            // TDA 49-A：已行動者只有在「自上次行動後所面對的增加額」達到完整加注時才重開（短 all-in 可累計）。
            bool rightsOpen = !x.HasActed || s.CurrentBet - x.ActedLevel >= s.LastFullRaise;
            bool canRaise = rightsOpen && x.Stack > toCall && OthersCanCallMore(s, seat);
            if (canRaise)
            {
                la.CanBet = s.CurrentBet == 0;
                la.CanRaise = s.CurrentBet > 0;
                la.MaxTo = x.StreetBet + x.Stack;
                la.MinTo = Math.Min(s.CurrentBet + s.LastFullRaise, la.MaxTo);
            }
            la.CanAllIn = canRaise || (x.Stack > 0 && x.Stack <= toCall);
            return la;
        }

        // ───────────── 推進：下一位、收池、發牌、攤牌 ─────────────

        private static void Advance(HandState s, List<GameEvent> events, int from)
        {
            int n = s.SeatCount;
            while (true)
            {
                if (CountLive(s) == 1) { FinishUncontested(s, events); return; }

                for (int k = 1; k <= n; k++)
                {
                    int i = (from + k) % n;
                    if (NeedsToAct(s, i)) { s.ToAct = i; return; }
                }

                // 本輪下注結束
                EndRound(s, events);
                int active = CountActive(s);
                if (s.Street == Street.River)
                {
                    Showdown(s, events, revealAsAllIn: active < 2, alreadyRevealed: false);
                    return;
                }
                if (active >= 2)
                {
                    DealStreet(s, events);
                    s.LastAggressor = -1;
                    from = s.Button;
                    continue;
                }
                // 有人 all-in 且不可能再有下注：先全部亮牌（TDA 17），再直接發完剩下的公共牌
                events.Add(new ShowdownRevealed(ShowdownOrder(s), true));
                while (s.Street != Street.River) DealStreet(s, events);
                Showdown(s, events, revealAsAllIn: true, alreadyRevealed: true);
                return;
            }
        }

        private static bool NeedsToAct(HandState s, int seat)
        {
            var x = s.SeatArray[seat];
            if (x.Status != SeatStatus.Active) return false;
            if (x.StreetBet < s.CurrentBet) return true;
            // 尚未行動（例如大盲的選擇權）——但若沒有任何對手還能行動，就不需要再問
            return !x.HasActed && OthersCanAct(s, seat);
        }

        private static void EndRound(HandState s, List<GameEvent> events)
        {
            ReturnUncalled(s, events);
            events.Add(new PotsUpdated(BuildPots(s).ToArray()));
            for (int i = 0; i < s.SeatCount; i++)
            {
                ref var x = ref s.SeatArray[i];
                x.StreetBet = 0; x.HasActed = false; x.ActedLevel = 0;
            }
            s.CurrentBet = 0;
            s.LastFullRaise = s.BigBlind;
            s.BetCount = 0;
            s.ToAct = -1;
        }

        private static void DealStreet(HandState s, List<GameEvent> events)
        {
            s.DeckPosition++; // 燒牌
            int count = s.Street == Street.Preflop ? 3 : 1;
            var cards = new Card[count];
            for (int i = 0; i < count; i++) cards[i] = s.DeckCards[s.DeckPosition++];
            s.BoardList.AddRange(cards);
            s.Street = s.Street + 1;
            events.Add(new StreetDealt(s.Street, cards, s.BoardList.ToArray()));
        }

        /// <summary>
        /// 退還沒人跟注的部分：投入最多者超出「其他任何人（含已棄牌者）的最高投入」的金額。
        /// （通行做法；TDA 只在 16-B／67-A 提到此退還，沒有獨立條文。）
        /// </summary>
        private static void ReturnUncalled(HandState s, List<GameEvent> events)
        {
            int top = -1, topAmt = 0, second = 0;
            for (int i = 0; i < s.SeatCount; i++)
            {
                var x = s.SeatArray[i];
                if (x.Status == SeatStatus.Out) continue;
                int c = x.Committed;
                if (c > topAmt) { second = topAmt; topAmt = c; top = i; }
                else if (c > second) second = c;
            }
            if (top < 0 || topAmt <= second || !s.SeatArray[top].IsLive) return;
            int excess = topAmt - second;
            ref var t = ref s.SeatArray[top];
            t.Stack += excess;
            t.Committed -= excess;
            t.StreetBet = Math.Max(0, t.StreetBet - excess);
            if (t.Status == SeatStatus.AllIn) t.Status = SeatStatus.Active; // 退回後又有籌碼
            events.Add(new UncalledBetReturned(top, excess));
        }

        /// <summary>
        /// 分層法建池：依未棄牌者的不同投入額由小到大分層，每層向所有人（含已棄牌者）收取該層的籌碼；
        /// 有資格者＝未棄牌且投入 ≥ 該層者。索引 0 為主池。
        /// </summary>
        private static List<Pot> BuildPots(HandState s)
        {
            var levels = new List<int>();
            for (int i = 0; i < s.SeatCount; i++)
            {
                var x = s.SeatArray[i];
                if (x.IsLive && x.Committed > 0 && !levels.Contains(x.Committed)) levels.Add(x.Committed);
            }
            levels.Sort();
            var pots = new List<Pot>();
            int prev = 0;
            foreach (int level in levels)
            {
                int amount = 0;
                var eligible = new List<int>();
                for (int i = 0; i < s.SeatCount; i++)
                {
                    var x = s.SeatArray[i];
                    amount += Math.Min(x.Committed, level) - Math.Min(x.Committed, prev);
                    if (x.IsLive && x.Committed >= level) eligible.Add(i);
                }
                pots.Add(new Pot(amount, eligible.ToArray()));
                prev = level;
            }
            // 防禦：已棄牌者投入高於所有未棄牌者（理論上退還後不會發生）→ 併入最後一個池
            int leftover = 0;
            for (int i = 0; i < s.SeatCount; i++) leftover += Math.Max(0, s.SeatArray[i].Committed - prev);
            if (leftover > 0)
            {
                if (pots.Count == 0)
                {
                    var live = new List<int>();
                    for (int i = 0; i < s.SeatCount; i++) if (s.SeatArray[i].IsLive) live.Add(i);
                    pots.Add(new Pot(leftover, live.ToArray()));
                }
                else
                {
                    var last = pots[pots.Count - 1];
                    pots[pots.Count - 1] = new Pot(last.Amount + leftover, ToArray(last.EligibleSeats));
                }
            }
            return pots;
        }

        private static void FinishUncontested(HandState s, List<GameEvent> events)
        {
            ReturnUncalled(s, events);
            var pots = BuildPots(s);
            events.Add(new PotsUpdated(pots.ToArray()));
            int winner = -1;
            for (int i = 0; i < s.SeatCount; i++) if (s.SeatArray[i].IsLive) winner = i;
            for (int k = 0; k < pots.Count; k++)
            {
                s.SeatArray[winner].Stack += pots[k].Amount;
                events.Add(new PotAwarded(k, pots[k].Amount, new[] { new PotShare(winner, pots[k].Amount) }, Array.Empty<int>(), null));
            }
            CompleteHand(s, events);
        }

        private static void Showdown(HandState s, List<GameEvent> events, bool revealAsAllIn, bool alreadyRevealed)
        {
            if (!alreadyRevealed) events.Add(new ShowdownRevealed(ShowdownOrder(s), revealAsAllIn));
            var pots = BuildPots(s);
            int n = s.SeatCount;
            var values = new int[n];
            ulong boardMask = 0;
            foreach (var c in s.BoardList) boardMask |= c.Mask;
            for (int i = 0; i < n; i++)
            {
                var x = s.SeatArray[i];
                if (x.IsLive) values[i] = HandEvaluator.EvaluateMask(boardMask | x.Hole1.Mask | x.Hole2.Mask).Value;
            }
            for (int k = 0; k < pots.Count; k++)
            {
                var pot = pots[k];
                int best = int.MinValue;
                foreach (int seat in pot.EligibleSeats) best = Math.Max(best, values[seat]);
                // 贏家依「按鈕左邊第一位起、順時針」排序（TDA Rule 21：零頭給按鈕左邊第一位；Rule 23：每個邊池各自分配）
                var winners = new List<int>();
                for (int j = 1; j <= n; j++)
                {
                    int seat = (s.Button + j) % n;
                    if (values[seat] == best && Contains(pot.EligibleSeats, seat)) winners.Add(seat);
                }
                int share = pot.Amount / winners.Count;
                int odd = pot.Amount % winners.Count;
                var shares = new PotShare[winners.Count];
                var oddSeats = new int[odd];
                for (int w = 0; w < winners.Count; w++)
                {
                    int amt = share + (w < odd ? 1 : 0);
                    if (w < odd) oddSeats[w] = winners[w];
                    shares[w] = new PotShare(winners[w], amt);
                    s.SeatArray[winners[w]].Stack += amt;
                }
                events.Add(new PotAwarded(k, pot.Amount, shares, oddSeats, new HandValue(best)));
            }
            CompleteHand(s, events);
        }

        /// <summary>
        /// 攤牌順序（TDA Rule 18-A）：最後一輪下注中最後一位主動下注／加注者先亮；該輪沒人下注則由按鈕左邊第一位先亮；之後順時針。
        /// </summary>
        private static ShownHand[] ShowdownOrder(HandState s)
        {
            int n = s.SeatCount;
            int first = -1;
            if (s.LastAggressor >= 0 && s.SeatArray[s.LastAggressor].IsLive) first = s.LastAggressor;
            else
                for (int k = 1; k <= n && first < 0; k++)
                {
                    int i = (s.Button + k) % n;
                    if (s.SeatArray[i].IsLive) first = i;
                }
            var list = new List<ShownHand>();
            for (int k = 0; k < n; k++)
            {
                int i = (first + k) % n;
                var x = s.SeatArray[i];
                if (x.IsLive) list.Add(new ShownHand(i, new[] { x.Hole1, x.Hole2 }));
            }
            return list.ToArray();
        }

        private static void CompleteHand(HandState s, List<GameEvent> events)
        {
            for (int i = 0; i < s.SeatCount; i++)
            {
                ref var x = ref s.SeatArray[i];
                x.StreetBet = 0; x.Committed = 0;
            }
            s.Phase = HandPhase.Idle;
            s.ToAct = -1;
            events.Add(new HandEnded(s.Stacks()));
        }

        // ───────────── 小工具 ─────────────

        private static int NextWithChips(HandState s, int from)
        {
            int n = s.SeatCount;
            for (int k = 1; k <= n; k++)
            {
                int i = (from + k) % n;
                if (s.SeatArray[i].Stack > 0) return i;
            }
            return -1;
        }

        /// <summary>
        /// 是否有其他未棄牌、未 all-in 的玩家能投入超過目前水位（否則加注沒有人能跟，只會被退還，因此不允許）。
        /// </summary>
        private static bool OthersCanCallMore(HandState s, int seat)
        {
            for (int i = 0; i < s.SeatCount; i++)
            {
                var x = s.SeatArray[i];
                if (i != seat && x.Status == SeatStatus.Active && x.Stack + x.StreetBet > s.CurrentBet) return true;
            }
            return false;
        }

        private static bool OthersCanAct(HandState s, int seat)
        {
            for (int i = 0; i < s.SeatCount; i++)
                if (i != seat && s.SeatArray[i].Status == SeatStatus.Active) return true;
            return false;
        }

        private static int CountLive(HandState s)
        {
            int c = 0;
            for (int i = 0; i < s.SeatCount; i++) if (s.SeatArray[i].IsLive) c++;
            return c;
        }

        private static int CountActive(HandState s)
        {
            int c = 0;
            for (int i = 0; i < s.SeatCount; i++) if (s.SeatArray[i].Status == SeatStatus.Active) c++;
            return c;
        }

        private static bool Contains(IReadOnlyList<int> list, int v)
        {
            for (int i = 0; i < list.Count; i++) if (list[i] == v) return true;
            return false;
        }

        private static int[] ToArray(IReadOnlyList<int> list)
        {
            var a = new int[list.Count];
            for (int i = 0; i < a.Length; i++) a[i] = list[i];
            return a;
        }

        private static ApplyResult Ok(HandState s, List<GameEvent> events) => new ApplyResult(s, events.AsReadOnly(), null);

        private static ApplyResult Fail(HandState original, EngineErrorCode code, string message)
            => new ApplyResult(original, Array.Empty<GameEvent>(), new EngineError(code, message));
    }
}
