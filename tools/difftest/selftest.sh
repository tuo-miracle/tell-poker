#!/bin/sh
# Self-check: generate 200 hands with PokerKit and replay them; must be 200/200 consistent.
# Usage: PY=/path/to/venv/bin/python ./selftest.sh [N] [SEED]
set -e
DIR=$(cd "$(dirname "$0")" && pwd)
PY=${PY:-python}
N=${1:-200}; SEED=${2:-1}
TMP=$(mktemp -d)
"$PY" "$DIR/pk_gen.py" "$N" "$SEED" > "$TMP/gen.jsonl"
"$PY" "$DIR/pk_replay.py" "$TMP/gen.jsonl" > "$TMP/replay.jsonl"
echo "selftest OK ($N hands, seed $SEED)"
