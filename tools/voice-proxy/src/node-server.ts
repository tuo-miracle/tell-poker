// Node http → fetch handler 轉接。本機開發預設只聽 127.0.0.1（Quest 透過 adb reverse 連進來）；伺服器上用 HOST=0.0.0.0 跑在 docker 裡、經 Cloudflare Tunnel 對外。
// 用法：node src/node-server.ts（Node ≥ 22.18，直接跑 TypeScript；/live 的上游 WebSocket client 要 Node ≥ 22）。
// 端點：POST /talk（整段，備案）、WebSocket /live（串流，主路徑，見 session.ts）、GET /health、GET /stats（要通行碼；今天的用量與本月 TTS 額度）、
//      POST /telemetry、GET /telemetry/sessions（要通行碼；開發版遙測，見 telemetry.ts）、GET /privacy 與 /（公開網頁，見 pages.ts）。
// 環境變數：PORT（8787）、VOICE_TOKENS（逗號分隔，預設 tell-dev）、VOICE_SECRETS_DIR、GEMINI_API_KEY、ELEVENLABS_API_KEY、
//          GEMINI_MODEL（gemini-3.8-flash；/talk 與 /live 的備案）、GEMINI_THINKING_LEVEL（low；none＝不送）、
//          GEMINI_LIVE_MODEL（gemini-3.8-live）、LIVE_TIMEOUT_MS（2000）、LIVE_THINKING_BUDGET（0；none＝不送）、TTS_SAMPLE_RATE（22050）、
//          限流（每台裝置，見 ratelimit.ts）：VOICE_DEVICE_CAPACITY（6）、VOICE_DEVICE_PER_SEC（0.2）、VOICE_DEVICE_DAILY_CAP（80）、
//          VOICE_DAILY_CAP（全體每日 3000）、VOICE_DEVICE_MAX_ENTRIES（10000）；0＝不限。
//          DATA_DIR（可寫的資料目錄；docker 裡是 /data，本機預設 <checkout>/Logs/voice-proxy-data）：
//            tts-usage.json（ElevenLabs 每月用量，見 ttsbudget.ts）：TTS_MONTHLY_CREDIT_BUDGET（15000；0＝不限）、TTS_CREDIT_PER_CHAR（0.5）；
//            telemetry/（開發版遙測，見 telemetry.ts）：TELEMETRY（1；0＝關）、TELEMETRY_SESSION_MAX_BYTES（2 MB）、TELEMETRY_TOTAL_MAX_BYTES（200 MB）。

import { createServer } from "node:http";
import { execFileSync } from "node:child_process";
import { dirname, join, resolve } from "node:path";
import { fileURLToPath } from "node:url";
import { createHandler, thinking, type HandlerConfig } from "./handler.ts";
import { createLiveSession } from "./session.ts";
import { attachLive } from "./node-live.ts";
import { openUpstream } from "./upstream.ts";
import { loadKeys } from "./keys.ts";
import { RateLimiter, limiterOptionsFromEnv } from "./ratelimit.ts";
import { TtsBudget, ttsBudgetOptionsFromEnv } from "./ttsbudget.ts";
import { telemetryOptionsFromEnv } from "./telemetry.ts";
import { FsTelemetryStore, fileStore } from "./fsdata.ts";

const here = dirname(fileURLToPath(import.meta.url));
const checkout = resolve(here, "../../..");

function mainCheckout(): string | null {
  try {
    const common = execFileSync("git", ["-C", checkout, "rev-parse", "--path-format=absolute", "--git-common-dir"], { encoding: "utf8" }).trim();
    return dirname(common);
  } catch { return null; }
}

const env = process.env;
const dirs = [env.VOICE_SECRETS_DIR, join(checkout, ".secrets"), mainCheckout() && join(mainCheckout()!, ".secrets")].filter((d): d is string => !!d);
const keys = loadKeys(env, dirs);
console.log(`[voice] keys: gemini from ${keys.source.gemini}, elevenlabs from ${keys.source.eleven}`);

const limits = limiterOptionsFromEnv(env);
console.log(`[voice] limits: per device ${limits.capacity} burst, ${limits.refillPerSec}/s, ${limits.deviceDailyCap}/day; all devices ${limits.globalDailyCap}/day (0 = no cap)`);

const dataDir = env.DATA_DIR || join(checkout, "Logs", "voice-proxy-data");
const log = (line: string) => console.log(new Date().toISOString().slice(11, 19), line);
const budgetOpts = ttsBudgetOptionsFromEnv(env);
const ttsBudget = new TtsBudget({ ...budgetOpts, store: fileStore(join(dataDir, "tts-usage.json")), now: Date.now, log });
const tts = ttsBudget.stats();
console.log(`[voice] tts budget: ${tts.creditsThisMonth}/${tts.budget} credits in ${tts.month} (${tts.creditPerChar}/char; 0 = no cap), data in ${dataDir}`);
const telemetryOn = env.TELEMETRY !== "0";
const telemetry = telemetryOn ? new FsTelemetryStore(join(dataDir, "telemetry"), telemetryOptionsFromEnv(env)) : undefined;
console.log(`[voice] telemetry ${telemetryOn ? "on" : "off"}`);

const cfg: HandlerConfig = {
  geminiKey: keys.gemini,
  elevenKey: keys.eleven,
  tokens: (env.VOICE_TOKENS ?? "tell-dev").split(",").map(s => s.trim()).filter(Boolean),
  model: env.GEMINI_MODEL ?? "gemini-3.8-flash",
  thinkingLevel: thinking(env.GEMINI_THINKING_LEVEL),
  ttsModel: env.TTS_MODEL ?? "eleven_flash_v2_5",
  ttsSampleRate: Number(env.TTS_SAMPLE_RATE ?? 22050),
  maxAudioSec: 10,
  fetch: (u, i) => fetch(u, i),
  now: () => performance.now(),
  limiter: new RateLimiter({ ...limits, now: Date.now }),
  log,
  ttsBudget,
  telemetry,
  // 遙測自己的限流：App 每 10 s 送一批（暫停／離開時多送一次），每台裝置連續 20 批、每秒補 0.5
  telemetryLimiter: new RateLimiter({ capacity: 20, refillPerSec: 0.5, deviceDailyCap: 0, globalDailyCap: 0, now: Date.now }),
  wallNow: Date.now,
};
const handler = createHandler(cfg);
const liveCfg = {
  ...cfg,
  liveModel: env.GEMINI_LIVE_MODEL ?? "gemini-3.8-live",
  liveTimeoutMs: Number(env.LIVE_TIMEOUT_MS ?? 2000),
  liveThinkingBudget: env.LIVE_THINKING_BUDGET === "none" ? null : Number(env.LIVE_THINKING_BUDGET ?? 0),
  openUpstream,
  setTimer: (fn: () => void, ms: number) => { const h = setTimeout(fn, ms); return () => clearTimeout(h); },
};

const port = Number(env.PORT ?? 8787);
const server = createServer(async (req, res) => {
  try {
    const chunks: Buffer[] = [];
    for await (const c of req) chunks.push(c as Buffer);
    const body = chunks.length ? Buffer.concat(chunks) : undefined;
    const headers = new Headers();
    for (const [k, v] of Object.entries(req.headers)) if (typeof v === "string") headers.set(k, v);
    const request = new Request(`http://127.0.0.1:${port}${req.url}`, { method: req.method, headers, body: req.method === "GET" || req.method === "HEAD" ? undefined : body });
    const response = await handler(request);
    res.writeHead(response.status, Object.fromEntries(response.headers));
    res.end(Buffer.from(await response.arrayBuffer()));
  } catch (e) {
    console.error("[voice] adapter error", (e as Error).message);
    res.writeHead(500, { "content-type": "application/json" });
    res.end(JSON.stringify({ error: "internal" }));
  }
});
attachLive(server, (link, token, device) => createLiveSession(liveCfg, link, token, device));
const host = env.HOST ?? "127.0.0.1";   // 容器裡設 HOST=0.0.0.0（只在 docker 內部網路，經 Cloudflare Tunnel 對外）
server.listen(port, host, () => console.log(`[voice] listening on http://${host}:${port} (WebSocket /live, POST /talk, POST /telemetry, GET /health, GET /stats, GET /telemetry/sessions)`));
