#!/usr/bin/env bash
# 從 Quest 拉回頭盔內錄製的 .hands 檔到 Assets/_Project/Tests/Gestures/Recordings/。
# 用法：tools/pull-recordings.sh [--clear]   （--clear：每個檔拉回且大小一致後，才刪除頭盔上的那個檔）
# 錄製檔在頭盔的 Application.persistentDataPath/recordings/，也就是 /sdcard/Android/data/<appId>/files/recordings/
set -u
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
ADB="${ADB:-/Applications/Unity/Hub/Editor/6000.3.26f1/PlaybackEngines/AndroidPlayer/SDK/platform-tools/adb}"
APP_ID="com.maxlai.tellpoker"   # ProjectConfigurator.ApplicationId
REMOTE="/sdcard/Android/data/${APP_ID}/files/recordings"
DEST="$ROOT/Assets/_Project/Tests/Gestures/Recordings"

if [ ! -x "$ADB" ]; then echo "找不到 adb：$ADB（可用 ADB=... 覆寫）"; exit 1; fi
DEVICES=$("$ADB" devices | awk 'NR>1 && $2=="device" {print $1}')
if [ -z "$DEVICES" ]; then
  echo "沒有連線的裝置（adb devices 為空或未授權）。請接上 USB 並在頭盔內允許 USB 偵錯。"
  "$ADB" devices
  exit 1
fi
if [ "$(echo "$DEVICES" | wc -l | tr -d ' ')" -gt 1 ]; then
  echo "偵測到多台裝置，請設定 ANDROID_SERIAL 指定一台：" ; echo "$DEVICES"; [ -z "${ANDROID_SERIAL:-}" ] && exit 1
fi

FILES=$("$ADB" shell "ls $REMOTE/*.hands 2>/dev/null" | tr -d '\r')
if [ -z "$FILES" ]; then echo "頭盔上沒有錄製檔（$REMOTE）"; exit 0; fi

mkdir -p "$DEST"
CLEAR=0; [ "${1:-}" = "--clear" ] && CLEAR=1
COUNT=0; DELETED=0
while IFS= read -r f; do
  [ -z "$f" ] && continue
  name=$(basename "$f")
  if "$ADB" pull "$f" "$DEST/$name" </dev/null >/dev/null; then
    COUNT=$((COUNT+1))
    local_size=$(stat -f%z "$DEST/$name")
    echo "  $name (${local_size} bytes)"
    if [ $CLEAR -eq 1 ]; then
      remote_size=$("$ADB" shell "stat -c %s '$f'" </dev/null | tr -d '\r')
      if [ "$remote_size" = "$local_size" ] && "$ADB" shell "rm '$f'" </dev/null; then DELETED=$((DELETED+1)); else echo "    （大小不符或刪除失敗，保留頭盔上的檔）"; fi
    fi
  else
    echo "  拉取失敗：$f"
  fi
done <<< "$FILES"
echo "拉回 ${COUNT} 個檔案到 $DEST"
[ $CLEAR -eq 1 ] && echo "已刪除頭盔上 ${DELETED} 個已確認拉回的檔案"
exit 0
