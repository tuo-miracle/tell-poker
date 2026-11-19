// Cloudflare Workers 進入點（尚未部署）：金鑰用 `wrangler secret put GEMINI_API_KEY` / `ELEVENLABS_API_KEY`，token 用 VOICE_TOKENS。
// 限流在 Workers 上只在單一 isolate 內有效；正式上線要改成 Durable Object 或 Rate Limiting binding。

import { createHandler, thinking, type HandlerConfig } from "./handler.ts";
import { createLiveSession } from "./session.ts";
import { openUpstream } from "./upstream.ts";

declare const WebSocketPair: any; // Workers 內建（沒有拉 @cloudflare/workers-types）
import { RateLimiter, limiterOptionsFromEnv } from "./ratelimit.ts";
import { TtsBudget, ttsBudgetOptionsFromEnv } from "./ttsbudget.ts";

interface Env {
  GEMINI_API_KEY: string;
  ELEVENLABS_API_KEY: string;
  VOICE_TOKENS: string;
  GEMINI_MODEL?: string;
  GEMINI_THINKING_LEVEL?: string;
  GEMINI_LIVE_MODEL?: string;
  TTS_SAMPLE_RATE?: string;
  VOICE_DEVICE_CAPACITY?: string;
  VOICE_DEVICE_PER_SEC?: string;
  VOICE_DEVICE_DAILY_CAP?: string;
  VOICE_DAILY_CAP?: string;
  TTS_MONTHLY_CREDIT_BUDGET?: string;
  TTS_CREDIT_PER_CHAR?: string;
}

let limiter: RateLimiter | null = null;
let handler: ((r: Request) => Promise<Response>) | null = null;
let cfg: HandlerConfig | null = null;

export default {
  async fetch(req: Request, env: Env): Promise<Response> {
    limiter ??= new RateLimiter(limiterOptionsFromEnv(env as unknown as Record<string, string | undefined>));
    cfg ??= {
      geminiKey: env.GEMINI_API_KEY,
      elevenKey: env.ELEVENLABS_API_KEY,
      tokens: (env.VOICE_TOKENS ?? "").split(",").map(s => s.trim()).filter(Boolean),
      model: env.GEMINI_MODEL ?? "gemini-3.8-flash",
      thinkingLevel: thinking(env.GEMINI_THINKING_LEVEL),
      ttsModel: "eleven_flash_v2_5",
      ttsSampleRate: Number(env.TTS_SAMPLE_RATE ?? 22050),
      maxAudioSec: 10,
      fetch: (u, i) => fetch(u, i),
      now: () => Date.now(),
      limiter,
      log: line => console.log(line),
      // Workers 沒有檔案系統：TTS 用量只在這個 isolate 的記憶體（正式上線改 KV／Durable Object）；/telemetry 回 501
      ttsBudget: new TtsBudget(ttsBudgetOptionsFromEnv(env as unknown as Record<string, string | undefined>)),
    };
    handler ??= createHandler(cfg);
    const url = new URL(req.url);
    if (url.pathname === "/live" && req.headers.get("upgrade")?.toLowerCase() === "websocket") {
      // 串流路徑（未在 Workers 上實測；本機用 node-server.ts）
      const pair = new WebSocketPair();
      const [client, server] = Object.values(pair) as any[];
      server.accept();
      const session = createLiveSession({
        ...cfg, liveModel: env.GEMINI_LIVE_MODEL ?? "gemini-3.8-live", liveTimeoutMs: 2000, openUpstream,
        setTimer: (fn, ms) => { const h = setTimeout(fn, ms); return () => clearTimeout(h); },
      }, { sendText: s => server.send(s), sendBinary: b => server.send(b), close: () => server.close(1000) },
      req.headers.get("x-voice-token") ?? url.searchParams.get("token"),
      req.headers.get("x-voice-device") ?? url.searchParams.get("device"));
      server.addEventListener("message", (ev: any) => typeof ev.data === "string" ? session.text(ev.data) : session.binary(new Uint8Array(ev.data)));
      server.addEventListener("close", () => session.closed());
      return new Response(null, { status: 101, webSocket: client } as any);
    }
    return handler(req);
  },
};
