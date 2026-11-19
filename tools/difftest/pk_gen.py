#!/usr/bin/env python
"""Generate N random NL hold'em hands with PokerKit as the rules oracle.

Usage:  pk_gen.py N SEED > OUT.jsonl

Output format: FORMAT.md (one JSON object per line, "final" = PokerKit result).
Every action is chosen among PokerKit-legal ones, biased toward all-ins, short
all-ins, min-raises, min-raise+1 and assorted raise-to sizes.
"""

import json
import os
import random
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

from pk_common import ALL_CARDS, RANKS, SUITS, PkHand  # noqa: E402

BLINDS = [(1, 2), (5, 10), (25, 50)]
MAX_ACTIONS = 200


def gen_stacks(rng, bb, sb):
    n = rng.randint(2, 9)
    seats = []
    for _ in range(n):
        r = rng.random()
        if r < 0.12:
            seats.append(0)  # empty / busted seat
        elif r < 0.30:
            seats.append(rng.randint(1, bb - 1))  # < 1 BB (may even be < SB)
        elif r < 0.40:
            seats.append(rng.choice([sb, bb, 2 * bb, 3 * bb, bb + sb]))  # edge values
        elif r < 0.60:
            seats.append(rng.randint(bb, 3 * bb))  # 1-3 BB
        elif r < 0.80:
            seats.append(rng.randint(3 * bb, 40 * bb))
        else:
            seats.append(rng.randint(40 * bb, 300 * bb))  # deep
    # occasionally duplicate a stack (equal all-ins -> exact side pots / chops)
    live = [i for i, s in enumerate(seats) if s > 0]
    if len(live) >= 2 and rng.random() < 0.15:
        a, b = rng.sample(live, 2)
        seats[b] = seats[a]
    while sum(1 for s in seats if s > 0) < 2:
        seats[rng.randrange(n)] = rng.randint(1, 100 * bb)
    return seats


def positions(seats, rng):
    n = len(seats)
    active = [i for i in range(n) if seats[i] > 0]
    button = rng.choice(active)

    def nxt(s):
        j = (s + 1) % n
        while seats[j] <= 0:
            j = (j + 1) % n
        return j

    if len(active) == 2:
        return button, button, nxt(button)  # heads-up: button == SB
    sb_seat = nxt(button)
    return button, sb_seat, nxt(sb_seat)


def deal_cards(rng, active):
    """Return (hole dict, board string).  ~12% of hands put a broadway straight on
    the board so every live player ties (chops / odd-chip coverage)."""
    deck = ALL_CARDS[:]
    rng.shuffle(deck)
    if rng.random() < 0.12:
        suits = list("cdhs") + [rng.choice(SUITS)]
        rng.shuffle(suits)
        board_cards = [r + s for r, s in zip("TJQKA", suits)]
        rest = [c for c in deck if c not in board_cards]
    else:
        board_cards, rest = deck[:5], deck[5:]
    hole = {}
    for k, seat in enumerate(active):
        hole[str(seat)] = rest[2 * k] + rest[2 * k + 1]
    return hole, "".join(board_cards)


def choose_action(rng, h, profile, n_actions):
    """Pick a random PokerKit-legal action for the current actor.  Returns record dict."""
    st = h.state
    i = st.actor_index
    seat = h.order[i]
    stack, bet, cur = st.stacks[i], st.bets[i], max(st.bets)
    call_amt = st.checking_or_calling_amount or 0
    full = stack + bet  # all-in "to"
    cands = []  # (weight, action dict)

    if call_amt > 0:
        cands.append((profile["fold"], {"seat": seat, "type": "fold"}))
        if stack <= call_amt:
            # calling is already all-in
            cands.append((profile["call"], {"seat": seat, "type": "call"}))
            cands.append((profile["allin"], {"seat": seat, "type": "allin", "to": full}))
        else:
            cands.append((profile["call"], {"seat": seat, "type": "call"}))
    else:
        cands.append((profile["call"], {"seat": seat, "type": "check"}))

    if st.can_complete_bet_or_raise_to():
        lo = st.min_completion_betting_or_raising_to_amount
        hi = st.max_completion_betting_or_raising_to_amount
        kind = "raise" if cur > 0 else "bet"
        decay = 0.7 ** max(0, st.completion_betting_or_raising_count - 2)
        if n_actions > 120:
            decay *= 0.05

        def add(w, to, label=None):
            if to < lo or to > hi:
                return
            t = label or (kind if to < full else rng.choice(["allin", "allin", "allin", kind]))
            cands.append((w * (1 if t == "allin" or to == full else decay),
                          {"seat": seat, "type": t, "to": to}))

        add(profile["allin"], hi, "allin" if rng.random() < 0.8 else kind)
        add(profile["min"] * decay, lo)
        add(profile["min1"] * decay, lo + 1)
        if hi > lo:
            add(profile["rand"] * decay, rng.randint(lo, hi))
        # a few "named" sizes: 2.5x/3x of the current bet, pot, 2x min
        for to in {cur * 3, int(cur * 2.5), 2 * lo - cur, cur + max(1, cur // 2)}:
            add(profile["rand"] * 0.4 * decay, to)
        # short all-in that is not a full raise: only possible when hi == full < lo handled by PokerKit (lo capped by stack)

    weights = [max(w, 0) for w, _ in cands]
    if sum(weights) <= 0:
        return cands[0][1]
    return rng.choices([a for _, a in cands], weights=weights, k=1)[0]


PROFILES = [
    # aggressive: heavy on all-ins
    {"fold": 0.10, "call": 0.20, "allin": 0.40, "min": 0.15, "min1": 0.10, "rand": 0.15},
    # normal
    {"fold": 0.18, "call": 0.35, "allin": 0.18, "min": 0.15, "min1": 0.10, "rand": 0.20},
    # min-raise war
    {"fold": 0.08, "call": 0.25, "allin": 0.08, "min": 0.35, "min1": 0.25, "rand": 0.10},
    # passive
    {"fold": 0.15, "call": 0.60, "allin": 0.06, "min": 0.06, "min1": 0.04, "rand": 0.08},
]


def gen_hand(rng, hid):
    sb, bb = rng.choice(BLINDS)
    seats = gen_stacks(rng, bb, sb)
    button, sb_seat, bb_seat = positions(seats, rng)
    active = [i for i, s in enumerate(seats) if s > 0]
    hole, board = deal_cards(rng, active)
    profile = rng.choice(PROFILES)
    h = PkHand(seats, button, sb_seat, bb_seat, sb, bb, hole, board)
    actions = []
    while not h.over:
        if len(actions) > MAX_ACTIONS:
            raise RuntimeError(f"hand {hid}: more than {MAX_ACTIONS} actions")
        act = choose_action(rng, h, profile, len(actions))
        h.apply(act)  # raises if the generator picked something PokerKit rejects
        actions.append(act)
    final = h.final_by_seat()
    assert sum(final) == sum(seats), (hid, final, seats)
    return {
        "id": hid, "seats": seats, "button": button, "sbSeat": sb_seat, "bbSeat": bb_seat,
        "sb": sb, "bb": bb, "hole": hole, "board": board, "actions": actions, "final": final,
    }


def main(argv):
    if len(argv) != 3:
        print(__doc__, file=sys.stderr)
        return 2
    n, seed = int(argv[1]), int(argv[2])
    rng = random.Random(seed)
    for hid in range(n):
        print(json.dumps(gen_hand(rng, hid), separators=(",", ":")))
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))
