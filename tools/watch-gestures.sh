#!/usr/bin/env bash
# 即時看頭盔上的手勢診斷：[Gesture]（為什麼沒觸發）與 [GestureEvent]（實際判斷到的手勢）。
# adb 背景服務可能被 Unity 的 Android 建置重啟而斷線，所以斷線後自動重連（-T 1 只取重連後的新 log，避免重複）。
# 用法：tools/watch-gestures.sh [輸出檔]
ADB="${ADB:-/Applications/Unity/Hub/Editor/6000.3.26f1/PlaybackEngines/AndroidPlayer/SDK/platform-tools/adb}"
OUT="${1:-$(cd "$(dirname "$0")/.." && pwd)/Logs/gesture-watch.log}"
mkdir -p "$(dirname "$OUT")"
while true; do
  "$ADB" wait-for-device
  "$ADB" logcat -T 1 -s Unity:V | grep --line-buffered -E "\[Gesture\]|\[GestureEvent\]" >> "$OUT"
  sleep 1
done
