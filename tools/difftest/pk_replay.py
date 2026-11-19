#!/usr/bin/env python
"""Replay a difftest JSONL file through PokerKit and compare final stacks.

Usage:  pk_replay.py INPUT.jsonl > OUT.jsonl      (INPUT may be '-' for stdin)

One output line per input hand:
  {"id":..., "ok":bool, "pk_final":[...by original seat] | null, "error": null | "..."}
A summary goes to stderr.  Exit code is 0 iff every hand is consistent.
See FORMAT.md for the input format and pk_common.py for the seat mapping.
"""

import json
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

from pk_common import PkHand  # noqa: E402


def replay(rec):
    """Return (pk_final or None, error or None, tags)."""
    tags = set()
    try:
        h = PkHand(
            rec["seats"], rec["button"], rec["sbSeat"], rec["bbSeat"],
            rec["sb"], rec["bb"], rec["hole"], rec["board"],
        )
    except Exception as e:  # noqa: BLE001 - report everything as a hand error
        return None, f"setup: {type(e).__name__}: {e}", tags

    for idx, act in enumerate(rec["actions"]):
        try:
            while h.actor_seat is not None and h.actor_seat != act["seat"] and h.pointless_check_pending():
                h.auto_check()
                tags.add("pk_pointless_check")
            h.apply(act)
        except Exception as e:  # noqa: BLE001
            return None, f"action index {idx} ({json.dumps(act, separators=(',', ':'))}): {type(e).__name__}: {e}", tags
    while not h.over and h.pointless_check_pending():
        h.auto_check()
        tags.add("pk_pointless_check")
    if not h.over:
        st = h.state
        return None, (
            f"actions exhausted after {len(rec['actions'])} but PokerKit hand is not over "
            f"(next actor seat {h.actor_seat}, street {st.street_index}, bets {list(st.bets)})"
        ), tags
    final = h.final_by_seat()
    if sum(final) != sum(rec["seats"]):
        return final, f"chip conservation violated in PokerKit result: sum(final)={sum(final)} sum(seats)={sum(rec['seats'])}", tags
    return final, None, tags


def main(argv):
    if len(argv) != 2:
        print(__doc__, file=sys.stderr)
        return 2
    src = sys.stdin if argv[1] == "-" else open(argv[1], encoding="utf-8")
    total = consistent = mismatch = illegal = 0
    classes = {}
    for line in src:
        line = line.strip()
        if not line:
            continue
        total += 1
        rec = json.loads(line)
        pk_final, err, tags = replay(rec)
        if err is not None and "cannot raise in face of a non-full all-in wager" in err:
            # 已知差異：多個短 all-in 累計達完整加注時，TDA 49-A 重開下注；PokerKit 0.7.7 拒絕（DiffRegressionTests.Diff2878）
            tags.add("pk_rejects_cumulative_reopen")
            ok = True
            consistent += 1
        elif err is not None:
            illegal += 1
            ok = False
        elif "final" in rec and rec["final"] != pk_final:
            if rec.get("final_pk_oddchip") == pk_final:
                # 已知差異：零頭分配政策（TDA 21/23 逐池、逐枚；PokerKit 合併池且零頭全給一人）
                tags.add("oddchip_policy")
                ok = True
                consistent += 1
            else:
                mismatch += 1
                ok = False
                err = f"final mismatch: record={rec['final']} pokerkit={pk_final}"
        else:
            ok = True
            consistent += 1
        cls = ",".join(sorted(tags)) if ok and tags else ("identical" if ok else "UNEXPLAINED")
        classes[cls] = classes.get(cls, 0) + 1
        print(json.dumps({"id": rec.get("id"), "ok": ok, "class": cls, "pk_final": pk_final, "error": err},
                         separators=(",", ":")))
    print(
        f"pk_replay: total={total} consistent={consistent} inconsistent={total - consistent} "
        f"(final_mismatch={mismatch}, illegal_or_error_hands={illegal})",
        file=sys.stderr,
    )
    print("pk_replay classes: " + ", ".join(f"{k}={v}" for k, v in sorted(classes.items())), file=sys.stderr)
    return 0 if consistent == total else 1


if __name__ == "__main__":
    sys.exit(main(sys.argv))
