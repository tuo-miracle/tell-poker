#!/usr/bin/env bash
# 本機語音對話開發：開語音中繼服務（tools/voice-proxy，聽 127.0.0.1:8787；WebSocket /live 與 POST /talk 同一個埠），並讓接上 USB 的 Quest 透過 adb reverse 連到它。
# 用法：tools/voice-dev.sh            # 開中繼＋adb reverse（Ctrl-C 結束）
#       tools/voice-dev.sh --no-adb   # 只開中繼（例如 Editor 裡測）
# 金鑰：環境變數 GEMINI_API_KEY／ELEVENLABS_API_KEY，或 VOICE_SECRETS_DIR／<repo>/.secrets／主 checkout 的 .secrets 下的
#       gemini.key、elevenlabs.key（.gitignore 已忽略；不要放進 Assets 或 APK）。
# Quest 要連這個本機中繼（明文 http）：建置時 TELL_ALLOW_HTTP=1 VOICE_URL=http://127.0.0.1:8787 tools/build-quest.sh（release 預設不允許明文 HTTP）。
# 其他環境變數見 tools/voice-proxy/src/node-server.ts（PORT、VOICE_TOKENS、GEMINI_MODEL、GEMINI_THINKING_LEVEL…）。
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
PORT="${PORT:-8787}"

# 需要 Node ≥ 22.18（直接執行 TypeScript）；預設的 node 太舊就找 nvm 裡最新的
need_node() { "$1" -e 'const [a,b]=process.versions.node.split(".").map(Number); process.exit(a>22||(a===22&&b>=18)?0:1)' 2>/dev/null; }
NODE="${NODE:-node}"
if ! need_node "$NODE"; then
  NODE=""
  for n in $(ls -d "$HOME"/.nvm/versions/node/v*/bin/node 2>/dev/null | sort -V -r); do
    if need_node "$n"; then NODE="$n"; break; fi
  done
  [ -n "$NODE" ] || { echo "需要 Node ≥ 22.18（nvm install 24）"; exit 1; }
fi
echo "node: $NODE ($("$NODE" --version))"

if [ "${1:-}" != "--no-adb" ]; then
  if command -v adb >/dev/null 2>&1; then
    adb reverse "tcp:${PORT}" "tcp:${PORT}" && echo "adb reverse tcp:${PORT} -> 開發機 127.0.0.1:${PORT}"
  else
    echo "找不到 adb：Quest 連不到中繼（只在本機測就加 --no-adb）"
  fi
fi

cd "$ROOT/tools/voice-proxy"
exec env PORT="$PORT" "$NODE" src/node-server.ts
