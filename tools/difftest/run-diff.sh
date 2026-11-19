#!/usr/bin/env bash
# Poker.Core 與 PokerKit 的雙向差異測試。
# 用法：PY=<有裝 pokerkit 的 python> tools/difftest/run-diff.sh [手數=10000] [種子=1] [輸出目錄=tools/difftest/out]
#   方向 A：C# 產生 → PokerKit 重播（pk_replay.py）
#   方向 B：PokerKit 產生（pk_gen.py）→ C# 重播
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
D="$ROOT/tools/difftest"
N="${1:-10000}"; SEED="${2:-1}"; OUT="${3:-$D/out}"
PY="${PY:-python3}"
mkdir -p "$D/bin" "$OUT"
SRC=$(find "$ROOT/Assets/_Project/Scripts/Poker.Core" -name "*.cs")
csc -nologo -warn:0 -langversion:9 -optimize+ -out:"$D/bin/Diff.exe" "$D/DiffMain.cs" $SRC

echo "== A: C# gen -> PokerKit replay ($N hands, seed $SEED)"
mono "$D/bin/Diff.exe" gen "$N" "$SEED" > "$OUT/cs_gen.jsonl"
set +e
"$PY" "$D/pk_replay.py" "$OUT/cs_gen.jsonl" > "$OUT/pk_replay.jsonl"; A=$?
echo "== B: PokerKit gen -> C# replay ($N hands, seed $SEED)"
"$PY" "$D/pk_gen.py" "$N" "$SEED" > "$OUT/pk_gen.jsonl"
mono "$D/bin/Diff.exe" replay "$OUT/pk_gen.jsonl" > "$OUT/cs_replay.jsonl"; B=$?
set -e
echo "A exit=$A  B exit=$B  (details: $OUT/pk_replay.jsonl, $OUT/cs_replay.jsonl)"
[ $A -eq 0 ] && [ $B -eq 0 ]
