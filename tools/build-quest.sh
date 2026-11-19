#!/usr/bin/env bash
# batchmode 建出 Quest APK：先套用專案設定（ProjectConfigurator），再建置 Android。
# 用法：tools/build-quest.sh [--skip-configure]
# 環境變數：VOICE_URL（中繼網址）；TELL_TELEMETRY（開發版遙測，預設 1＝開：把白名單 tag 的 log 送到中繼的 /telemetry，
#          沒有音訊、沒有玩家講的話）；TELL_RELEASE=1（正式版：遙測一律關，就算 TELL_TELEMETRY=1）。
# release 不允許明文 HTTP（中繼是 https）。要連本機中繼（tools/voice-dev.sh）測：TELL_ALLOW_HTTP=1 VOICE_URL=http://127.0.0.1:8787 tools/build-quest.sh
#   （TELL_ALLOW_HTTP 由 ProjectConfigurator 讀；只給開發建置，交件的 APK 不要設）
# 開發用場景開關（ProjectConfigurator 重建 Main 場景時由 MainSceneBuilder 讀）：TELL_DEV_TOOLS=1（錄製面板、除錯看板）；
#   TELL_REALISTIC_PREVIEW=1（寫實角色版：5 人桌、寫實牌手與荷官；比賽影片與送件都用這版，2026-11-05 起）。都會改到 Main.unity。
# 產出：Builds/Quest/TellPoker.apk、Builds/Quest/project-setup-report.json；log 在 Logs/quest-*.log
set -u
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
UNITY="${UNITY:-/Applications/Unity/Hub/Editor/6000.3.26f1/Unity.app/Contents/MacOS/Unity}"
OUT="$ROOT/Builds/Quest"
LOGS="$ROOT/Logs"
APK="$OUT/TellPoker.apk"
REPORT="$OUT/project-setup-report.json"
mkdir -p "$OUT" "$LOGS"

# -releaseCodeOptimization：Debug 模式下 Unity 的 MonoDebuggerHandling 會對剛編出的 Burst JIT bundle 呼叫 dlsym，
# 本機 macOS 因此以 "Code Signature Invalid" SIGKILL 掉 Unity（exit 137）。Release 模式避開這條路徑，Burst（含 player AOT）照常啟用。

first_errors() {
  grep -nE "error CS|BuildFailedException|Exception:|FAILURE:|What went wrong|\[BuildScripts\] RESULT|\[ProjectConfigurator\].*failed" "$1" | head -15
}

case "${VOICE_URL:-}" in
  http://*)
    if [ "${TELL_ALLOW_HTTP:-}" != "1" ]; then
      echo "錯誤：VOICE_URL 是明文 http（${VOICE_URL}），release 建置不允許；本機測試請加 TELL_ALLOW_HTTP=1"
      exit 2
    fi
    if [ "${1:-}" = "--skip-configure" ]; then
      echo "警告：--skip-configure 不會重新套用 TELL_ALLOW_HTTP；上一次 ProjectConfigurator 的設定照用"
    fi ;;
esac

# 語音中繼的通行碼：從 .secrets/voice-client.token 產生 Resources/VoiceClient.json（.gitignore；不進原始碼）
# worktree 沒有 .secrets：用 SECRETS_DIR 指向主 checkout 的 .secrets，不要把金鑰複製進 worktree
SECRETS="${SECRETS_DIR:-$ROOT/.secrets}"
VOICE_JSON="$ROOT/Assets/_Project/Resources/VoiceClient.json"
TELEMETRY=true
if [ "${TELL_RELEASE:-0}" = "1" ]; then
  TELEMETRY=false
  [ "${TELL_TELEMETRY:-}" = "1" ] && echo "警告：TELL_RELEASE=1，忽略 TELL_TELEMETRY=1（正式版不送遙測）"
elif [ "${TELL_TELEMETRY:-1}" != "1" ]; then
  TELEMETRY=false
fi
if [ -s "$SECRETS/voice-client.token" ]; then
  python3 -c 'import json,sys; print(json.dumps({"url": sys.argv[1], "token": open(sys.argv[2]).read().strip(), "telemetry": sys.argv[3] == "true"}))' \
    "${VOICE_URL:-https://vr-poker-api.elebymax.com}" "$SECRETS/voice-client.token" "$TELEMETRY" > "$VOICE_JSON"
  echo "語音中繼：${VOICE_URL:-https://vr-poker-api.elebymax.com}（通行碼已寫入 VoiceClient.json）；開發版遙測：${TELEMETRY}"
else
  rm -f "$VOICE_JSON" "$VOICE_JSON.meta"
  echo "警告：沒有 $SECRETS/voice-client.token，語音對話會走離線備案（worktree 請設 SECRETS_DIR）"
fi

if [ "${1:-}" != "--skip-configure" ]; then
  echo "== [1/2] ProjectConfigurator.Apply（log: Logs/quest-configure.log）"
  "$UNITY" -batchmode -nographics -releaseCodeOptimization --burst-disable-compilation -projectPath "$ROOT" -buildTarget Android \
    -executeMethod Poker.Editor.Build.ProjectConfigurator.ApplyAndExit \
    -reportFile "$REPORT" -logFile "$LOGS/quest-configure.log"
  CODE=$?
  if [ $CODE -ne 0 ]; then
    if grep -q "\[ProjectConfigurator\] APPLY_OK" "$LOGS/quest-configure.log"; then
      # 設定已完成，只是 Unity 在關閉時崩潰（本機已知問題，exit 134），不影響結果
      echo "警告：設定已完成，但 Unity 關閉時異常結束（exit ${CODE}），繼續建置"
    else
      echo "設定失敗（exit ${CODE}）："
      first_errors "$LOGS/quest-configure.log"
      exit $CODE
    fi
  fi
  if [ -f "$REPORT" ]; then
    python3 - "$REPORT" <<'PY'
import json, sys, collections
r = json.load(open(sys.argv[1]))
todo = [t for t in r.get("tasksStatus", []) if not t.get("isDone")]
count = collections.Counter(t.get("level") for t in todo)
print(f"Project Setup 未完成：Required={count.get('Required', 0)} Recommended={count.get('Recommended', 0)} Optional={count.get('Optional', 0)}")
for t in todo:
    if t.get("level") in ("Required", "Recommended"):
        print(f"  [{t.get('level')}] {t.get('group')}: {(t.get('message') or '').strip()[:160]}")
PY
  fi
fi

echo "== [2/2] Android build（log: Logs/quest-build.log）"
rm -f "$APK"
"$UNITY" -batchmode -nographics -releaseCodeOptimization --burst-disable-compilation -projectPath "$ROOT" -buildTarget Android \
  -executeMethod Poker.Editor.Build.BuildScripts.BuildQuest -logFile "$LOGS/quest-build.log"
CODE=$?
grep -h "\[BuildScripts\] RESULT" "$LOGS/quest-build.log" | tail -1
if [ $CODE -ne 0 ] && [ -f "$APK" ] && grep -q "\[BuildScripts\] RESULT=Succeeded" "$LOGS/quest-build.log"; then
  echo "警告：建置已完成，但 Unity 關閉時異常結束（exit ${CODE}），視為成功"
  CODE=0
fi
if [ $CODE -eq 0 ] && [ -f "$APK" ]; then
  echo "成功：${APK}（$(du -h "$APK" | cut -f1)，$(stat -f%z "$APK") bytes）"
else
  echo "失敗（exit ${CODE}），前幾個錯誤："
  first_errors "$LOGS/quest-build.log"
  exit 1
fi
