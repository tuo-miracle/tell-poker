"""Shared PokerKit driver for pk_replay.py / pk_gen.py.

Seat -> PokerKit player-index mapping (verified against pokerkit 0.7.7,
see state.py get_effective_blind_or_straddle / _begin_betting):

  * 3+ active players: index 0 = small blind, 1 = big blind, ..., last = button
    (clockwise starting from sbSeat).
  * Heads-up (2 active players): PokerKit reverses the blinds
    (blinds_or_straddles[not player_index]), so index 0 = BIG blind and
    index 1 = SMALL blind = button.  Preflop index 1 (button) acts first,
    postflop index 0 (big blind) acts first.
"""

import warnings
from collections import deque

from pokerkit import Automation, NoLimitTexasHoldem

AUTOMATIONS = (
    Automation.ANTE_POSTING,
    Automation.BET_COLLECTION,
    Automation.BLIND_OR_STRADDLE_POSTING,
    Automation.HAND_KILLING,
    Automation.CHIPS_PUSHING,
    Automation.CHIPS_PULLING,
    Automation.HOLE_CARDS_SHOWING_OR_MUCKING,
    Automation.RUNOUT_COUNT_SELECTION,
)
# Not automated on purpose: HOLE_DEALING / BOARD_DEALING (we deal the recorded
# cards) and CARD_BURNING (we burn explicit unused cards so a burn can never
# collide with a recorded hole/board card).

RANKS = "23456789TJQKA"
SUITS = "cdhs"
ALL_CARDS = [r + s for r in RANKS for s in SUITS]


class HandError(Exception):
    pass


def split_cards(s):
    return [s[i:i + 2] for i in range(0, len(s), 2)]


def pk_order(seats, button, sb_seat, bb_seat):
    """Return the list of original seat numbers in PokerKit player-index order."""
    n = len(seats)
    active = [i for i in range(n) if seats[i] > 0]
    if len(active) < 2:
        raise HandError(f"need >=2 seated players, got {len(active)}")
    if sb_seat not in active or bb_seat not in active:
        raise HandError(f"sbSeat/bbSeat must be seated players: sb={sb_seat} bb={bb_seat}")
    if len(active) == 2:
        if button != sb_seat:
            raise HandError(f"heads-up requires button == sbSeat (button={button}, sbSeat={sb_seat})")
        if bb_seat == sb_seat:
            raise HandError("heads-up sbSeat == bbSeat")
        return [bb_seat, sb_seat]  # PokerKit heads-up: index0 = BB, index1 = SB/button
    cw = sorted(active, key=lambda s: (s - sb_seat) % n)
    if cw[1] != bb_seat:
        raise HandError(
            f"bbSeat {bb_seat} is not the next seated player after sbSeat {sb_seat} (clockwise order {cw})"
        )
    return cw


class PkHand:
    """One NL hold'em hand driven through PokerKit with recorded cards."""

    def __init__(self, seats, button, sb_seat, bb_seat, sb, bb, hole, board):
        self.seat_count = len(seats)
        self.order = pk_order(seats, button, sb_seat, bb_seat)  # pk index -> seat
        self.seat_to_idx = {s: i for i, s in enumerate(self.order)}
        n = len(self.order)
        self.start_stacks = [seats[s] for s in self.order]
        self.state = NoLimitTexasHoldem.create_state(
            AUTOMATIONS,
            True,            # ante trimming (no antes anyway)
            0,               # no antes
            (sb, bb),        # blinds (sb, bb); small-blind first
            bb,              # min bet = big blind
            tuple(self.start_stacks),
            n,
        )
        self.board = split_cards(board)
        if len(self.board) != 5:
            raise HandError(f"board must have 5 cards: {board!r}")
        used = list(self.board)
        for seat in self.order:
            cards = split_cards(hole[str(seat)])
            if len(cards) != 2:
                raise HandError(f"seat {seat} needs 2 hole cards")
            used += cards
        if len(set(used)) != len(used):
            raise HandError("duplicate cards in hole/board")
        self.spares = deque(c for c in ALL_CARDS if c not in set(used))
        self._board_pos = 0
        with warnings.catch_warnings():
            warnings.simplefilter("ignore")
            for idx, seat in enumerate(self.order):
                self.state.deal_hole(hole[str(seat)], idx)
        self.advance()

    # -- progression -------------------------------------------------------
    def advance(self):
        """Burn / deal board until someone must act or the hand is over."""
        st = self.state
        with warnings.catch_warnings():
            warnings.simplefilter("ignore")
            guard = 0
            while st.status and st.actor_index is None:
                guard += 1
                if guard > 50:
                    raise HandError(f"state machine stuck (no actor, status still True)")
                if st.can_burn_card():
                    st.burn_card(self.spares.popleft())
                elif st.can_deal_board():
                    cnt = st.board_dealing_count
                    cards = self.board[self._board_pos:self._board_pos + cnt]
                    self._board_pos += cnt
                    st.deal_board("".join(cards))
                else:
                    raise HandError("state machine stuck (cannot burn/deal, no actor)")

    @property
    def over(self):
        return not self.state.status

    @property
    def actor_seat(self):
        i = self.state.actor_index
        return None if i is None else self.order[i]

    def pointless_check_pending(self):
        """PokerKit 要求某人行動，但他沒有要跟的金額、也不能加注（其他人都已 all-in/棄牌）。
        Poker.Core 不會要求這種行動（TDA 17：直接亮牌），差異測試把它當成已知差異自動補上 check。"""
        st = self.state
        if st.actor_index is None:
            return False
        with warnings.catch_warnings():
            warnings.simplefilter("ignore")
            return st.checking_or_calling_amount == 0 and not st.can_complete_bet_or_raise_to()

    def auto_check(self):
        with warnings.catch_warnings():
            warnings.simplefilter("ignore")
            self.state.check_or_call()
        self.advance()

    # -- actions -----------------------------------------------------------
    def apply(self, act):
        """Apply a record action dict.  Raises HandError/ValueError on illegal."""
        st = self.state
        seat = act["seat"]
        if st.actor_index is None:
            raise HandError(f"no player to act (hand over={self.over}) but action for seat {seat}")
        if self.order[st.actor_index] != seat:
            raise HandError(
                f"turn-order mismatch: PokerKit expects seat {self.order[st.actor_index]} to act, record says seat {seat}"
            )
        t = act["type"]
        with warnings.catch_warnings():
            # promote PokerKit UserWarnings (e.g. "no reason to fold") to errors
            warnings.simplefilter("error", UserWarning)
            if t == "fold":
                st.fold()
            elif t in ("check", "call"):
                st.check_or_call()
            elif t in ("bet", "raise", "allin"):
                to = act["to"]
                if to > max(st.bets):
                    st.complete_bet_or_raise_to(to)
                else:
                    st.check_or_call()
            else:
                raise HandError(f"unknown action type {t!r}")
        self.advance()

    def final_by_seat(self):
        out = [0] * self.seat_count
        for idx, seat in enumerate(self.order):
            out[seat] = int(self.state.stacks[idx])
        return out
