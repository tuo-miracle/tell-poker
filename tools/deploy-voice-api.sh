#!/usr/bin/env bash
# 把語音中繼部署到伺服器（docker compose：api＋cloudflared），對外網址 https://vr-poker-api.elebymax.com
# 用法：tools/deploy-voice-api.sh [ssh 目標，預設 max@192.168.1.249]
# 需要本機 .secrets/：gemini.key、elevenlabs.key、voice-client.token（App 用的通行碼）、vr-poker-tunnel.token（Cloudflare Tunnel）
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
HOST="${1:-max@192.168.1.249}"
DIR='~/vr-poker-api'
S="${SECRETS_DIR:-$ROOT/.secrets}"  # worktree 沒有 .secrets：用 SECRETS_DIR 指向主 checkout
for f in gemini.key elevenlabs.key voice-client.token vr-poker-tunnel.token; do [ -s "$S/$f" ] || { echo "缺 $S/$f"; exit 1; }; done

ssh "$HOST" "mkdir -p $DIR/voice-proxy $DIR/secrets $DIR/data && chmod 700 $DIR/secrets"
rsync -a --delete --exclude bake --exclude smoke --exclude test "$ROOT/tools/voice-proxy/" "$HOST:$DIR/voice-proxy/"
rsync -a "$ROOT/deploy/vr-poker-api/docker-compose.yml" "$HOST:$DIR/"
# 金鑰：經 ssh stdin 寫入，權限 600，不出現在指令列或 log
ssh "$HOST" "umask 077; cat > $DIR/secrets/gemini.key" < "$S/gemini.key"
ssh "$HOST" "umask 077; cat > $DIR/secrets/elevenlabs.key" < "$S/elevenlabs.key"
# VOICE_DAILY_CAP：全體每日上限（配音費用的最後防線）。預設 400，確認 ElevenLabs 方案額度後再用環境變數調高。
# TTS_MONTHLY_CREDIT_BUDGET／TTS_CREDIT_PER_CHAR：ElevenLabs 每月額度保險（沒設＝compose 的預設 15000／0.5）。
{ printf 'VOICE_TOKENS=%s\n' "$(cat "$S/voice-client.token")"; printf 'TUNNEL_TOKEN=%s\n' "$(cat "$S/vr-poker-tunnel.token")"; printf 'VOICE_DAILY_CAP=%s\n' "${VOICE_DAILY_CAP:-400}"
  for v in TTS_MONTHLY_CREDIT_BUDGET TTS_CREDIT_PER_CHAR TELEMETRY; do if [ -n "${!v:-}" ]; then printf '%s=%s\n' "$v" "${!v}"; fi; done; } | ssh "$HOST" "umask 077; cat > $DIR/.env"
# 程式碼是掛載進容器的 volume，compose 設定沒變時 up -d 不會重啟，所以要 restart api 才會載入新程式
ssh "$HOST" "cd $DIR && docker compose pull -q && docker compose up -d --remove-orphans && docker compose restart api && docker compose ps"
