#!/usr/bin/env bash
# 以 batchmode 執行 Unity 測試並印出摘要。
# 用法：tools/run-unity-tests.sh <EditMode|PlayMode> [testFilter（regex，可省略）]
set -u
PLATFORM="${1:-EditMode}"
FILTER="${2:-}"
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
OUT="$ROOT/Logs"
UNITY="${UNITY:-/Applications/Unity/Hub/Editor/6000.3.26f1/Unity.app/Contents/MacOS/Unity}"
mkdir -p "$OUT"
RESULTS="$OUT/${PLATFORM}-results.xml"
LOG="$OUT/${PLATFORM}.log"
rm -f "$RESULTS"
ARGS=(-batchmode -releaseCodeOptimization --burst-disable-compilation -projectPath "$ROOT" -runTests -testPlatform "$PLATFORM" -testResults "$RESULTS" -logFile "$LOG")
[ -n "$FILTER" ] && ARGS+=(-testFilter "$FILTER")
"$UNITY" "${ARGS[@]}"
CODE=$?
if [ -f "$RESULTS" ]; then
  python3 - "$RESULTS" <<'PY'
import sys, xml.etree.ElementTree as ET
r = ET.parse(sys.argv[1]).getroot()
print(f"total={r.get('total')} passed={r.get('passed')} failed={r.get('failed')} skipped={r.get('skipped')}")
for tc in r.iter('test-case'):
    if tc.get('result') != 'Passed':
        msg = tc.find('./failure/message')
        print(f"  FAIL {tc.get('fullname')}: {(msg.text or '').strip()[:400] if msg is not None else ''}")
PY
else
  echo "沒有產生測試結果（多半是編譯錯誤）：$LOG"
  grep -E "error CS" "$LOG" | sort -u | head -20
fi
exit $CODE
