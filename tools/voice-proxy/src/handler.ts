// POST /talk：玩家的一段語音 → Gemini（辨識＋角色回話）→ 填事實數字 → ElevenLabs 配音 → JSON。
// fetch handler 形式：Node 用 node-server.ts 轉接，之後可以直接放上 Cloudflare Workers（worker.ts）。
// 本月 TTS 額度用完（ttsbudget.ts）：照回文字、audio null、code "tts_budget"（Unity 改說預錄句）。/telemetry 見 telemetry.ts。

import { CHARACTERS, resolveSpeaker, type Addressee } from "./addressee.ts";
import { applyFacts } from "./facts.ts";
import { callGemini, sanitizeState, type FetchFn, type ModelReply } from "./gemini.ts";
import { castVoice, parseCast, synthesize } from "./elevenlabs.ts";
import { cannedLine } from "./characters.ts";
import { isPg } from "./filter.ts";
import { servePage } from "./pages.ts";
import { base64ToBytes, bytesToBase64, parseWav } from "./wav.ts";
import { deviceTag, isValidDevice, limitKey, LIMIT_MESSAGES, type RateLimiter } from "./ratelimit.ts";
import { TTS_BUDGET_CODE, type TtsBudget } from "./ttsbudget.ts";
import { handleTelemetry, type TelemetryStore } from "./telemetry.ts";

export interface HandlerConfig {
  geminiKey: string;
  elevenKey: string;
  tokens: string[];
  model: string;
  thinkingLevel?: string;
  ttsModel: string;
  ttsSampleRate: number;
  maxAudioSec: number;
  fetch: FetchFn;
  now: () => number;
  limiter: RateLimiter;
  log: (line: string) => void;
  /** ElevenLabs 每月額度保險（沒有＝不限、不計）。 */
  ttsBudget?: TtsBudget;
  /** 開發版遙測的存放處（Node 才有；沒有＝/telemetry 回 501）。 */
  telemetry?: TelemetryStore;
  /** 遙測自己的限流（和語音分開，不吃語音額度）。 */
  telemetryLimiter?: RateLimiter;
  /** 牆上時間（遙測的日期）；預設 Date.now。now 可能是 performance.now。 */
  wallNow?: () => number;
}

const MAX_BODY_CHARS = 2_000_000; // 約 1.4 MB 音訊，遠超 10 秒 16 kHz（320 KB）

function json(status: number, body: unknown): Response {
  return new Response(JSON.stringify(body), { status, headers: { "content-type": "application/json" } });
}

export function createHandler(cfg: HandlerConfig) {
  let counter = 0;
  return async function handle(req: Request): Promise<Response> {
    const url = new URL(req.url);
    const page = servePage(req, url.pathname);
    if (page) return page;
    if (url.pathname === "/health") return json(200, { ok: true, model: cfg.model });
    const token = req.headers.get("x-voice-token") ?? "";
    const tokenOk = !!token && cfg.tokens.includes(token);
    if (url.pathname === "/stats") {
      // 評審期間給協調者看用量（要 App 通行碼；只有計數，沒有任何 id）
      if (req.method !== "GET") return json(405, { error: "GET only" });
      if (!tokenOk) return json(401, { error: "bad token" });
      return json(200, { ...cfg.limiter.stats(), ...(cfg.ttsBudget ? { tts: cfg.ttsBudget.stats() } : {}) });
    }
    if (url.pathname === "/telemetry" || url.pathname === "/telemetry/sessions") {
      if (!tokenOk) return json(401, { error: "bad token" });
      return (await handleTelemetry(req, url.pathname, { store: cfg.telemetry, limiter: cfg.telemetryLimiter, wallNow: cfg.wallNow ?? Date.now, log: cfg.log }, token))!;
    }
    if (url.pathname !== "/talk") return json(404, { error: "not found" });
    if (req.method !== "POST") return json(405, { error: "POST only" });

    if (!tokenOk) return json(401, { error: "bad token" });
    const device = req.headers.get("x-voice-device") || null;
    if (device && !isValidDevice(device)) return json(400, { error: "bad device id", code: "bad_device" });
    const limit = cfg.limiter.take(limitKey(token, device));
    if (!limit.ok) return json(429, { error: LIMIT_MESSAGES[limit.code], code: limit.code });
    const dev = await deviceTag(device);

    const raw = await req.text();
    if (raw.length > MAX_BODY_CHARS) return json(413, { error: "body too large" });
    let body: any;
    try { body = JSON.parse(raw); } catch { return json(400, { error: "body is not JSON" }); }
    if (!body || typeof body.audio !== "string" || !body.state || typeof body.state !== "object") return json(400, { error: "need audio and state" });
    const addressee: Addressee = (CHARACTERS as readonly string[]).includes(body.addressee) ? body.addressee : "auto";
    let seconds: number;
    try { seconds = parseWav(base64ToBytes(body.audio)).durationSec; }
    catch (e) { return json(400, { error: "audio: " + (e as Error).message }); }
    if (seconds > cfg.maxAudioSec) return json(413, { error: `audio longer than ${cfg.maxAudioSec}s` });

    const state = sanitizeState(body.state);
    const t0 = cfg.now();
    let reply: ModelReply;
    try {
      reply = await callGemini(cfg.fetch, { apiKey: cfg.geminiKey, model: cfg.model, thinkingLevel: cfg.thinkingLevel },
        { audioBase64: body.audio, addressee, state, directives: body.directives ?? {} });
    } catch (e) {
      cfg.log(`[voice] gemini failed: ${(e as Error).message}`);
      return json(502, { error: "gemini: " + (e as Error).message, timings: { gemini: cfg.now() - t0 } });
    }
    const t1 = cfg.now();

    const speaker = resolveSpeaker(reply.transcript, addressee);
    let text = "";
    let repaired = false;
    if (reply.intent !== "ignore") {
      const f = applyFacts(reply, state, speaker);
      text = f.text;
      repaired = f.repaired;
      if (!text || !isPg(text)) { text = cannedLine(speaker, counter++); repaired = true; }
    }
    const t2 = cfg.now();

    let audio: string | null = null;
    let error: string | undefined;
    let code: string | undefined;
    if (text && cfg.ttsBudget && !cfg.ttsBudget.allow()) { code = TTS_BUDGET_CODE; error = "tts budget reached"; }
    else if (text) {
      try {
        audio = bytesToBase64(await synthesize(cfg.fetch, { apiKey: cfg.elevenKey, model: cfg.ttsModel, sampleRate: cfg.ttsSampleRate }, castVoice(speaker, parseCast(body.voices)), text));
        cfg.ttsBudget?.record(text.length);
      }
      catch (e) { error = "tts: " + (e as Error).message; }
    }
    const t3 = cfg.now();
    const timings = { gemini: t1 - t0, facts: t2 - t1, tts: t3 - t2, total: t3 - t0 };
    cfg.log(`[voice] dev=${dev} speaker=${speaker} (model=${reply.speaker ?? "-"}, addressee=${addressee}) intent=${reply.intent} repaired=${repaired} audioIn=${seconds.toFixed(1)}s ms=${JSON.stringify(timings)}${error ? " " + error.slice(0, 80) : ""}`);
    return json(200, {
      speaker, transcript: reply.transcript, text, intent: reply.intent, emotion: reply.emotion,
      fact: reply.fact, repaired, audio, audioFormat: audio ? "wav" : null, timings, ...(error ? { error } : {}), ...(code ? { code } : {}),
    });
  };
}

/** GEMINI_THINKING_LEVEL：預設 low（gemini-3.8-flash 不支援 minimal；2026-10-13 冒煙測試 low 2.7 s、預設 3.3 s）；"none" 不送。 */
export function thinking(v: string | undefined): string | undefined {
  if (v === "none") return undefined;
  return v || "low";
}
