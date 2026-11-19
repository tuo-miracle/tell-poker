// /live：一段對話一條 WebSocket（Unity ⇄ 中繼）。玩家一開口 Unity 就連上並邊講邊送 PCM，中繼轉送給 Gemini Live；
// 講完（Unity 的 VAD 送 end）→ Live 呼叫 reply() → 填事實數字 → ElevenLabs 串流配音 → PCM 一塊塊回給 Unity。
// Live 失敗、逾時或沒呼叫 reply() 時，改用整段音訊走原本的 generateContent（同一條連線，Unity 感覺不到）。
//
// 連線：token 在 x-voice-token header（或 ?token=）；裝置 id 在 ?device=（或 x-voice-device header），用來分開限流。
// 訊息（Unity → 中繼）：文字 {"type":"start",addressee,state,directives,rate,voices?,token?}（voices：座位 → 寫實角色的聲音鍵，例 {"pip":"marcus"}）、二進位 PCM16 mono、文字 {"type":"end"}／{"type":"cancel"}。
// 訊息（中繼 → Unity）：文字 {"type":"reply",speaker,transcript,text,intent,emotion,fact,repaired,audioRate,path}、
//                      二進位 PCM16 mono（audioRate）、文字 {"type":"done",timings,error?}；錯誤時 {"type":"error",error,code?}
//                      （code：rate_limited／device_daily_cap／daily_cap／bad_device；限流時 Unity 不要再用 /talk 重送）。
//                      本月 TTS 額度用完（ttsbudget.ts）時 reply 帶 code "tts_budget"、audioRate 0，不送音訊；done 也帶 code。

import { CHARACTERS, resolveSpeaker, type Addressee } from "./addressee.ts";
import { applyFacts } from "./facts.ts";
import { callGemini, sanitizeState, type ModelReply } from "./gemini.ts";
import { castVoice, parseCast, synthesize, synthesizeStream } from "./elevenlabs.ts";
import { cannedLine } from "./characters.ts";
import { isPg } from "./filter.ts";
import { buildLiveSetup, liveUrl, parseToolCall } from "./live.ts";
import { bytesToBase64, parseWav, pcm16ToWav } from "./wav.ts";
import type { HandlerConfig } from "./handler.ts";
import { deviceTag, isValidDevice, limitKey, LIMIT_MESSAGES } from "./ratelimit.ts";
import { TTS_BUDGET_CODE } from "./ttsbudget.ts";

export interface UpstreamEvents { open(): void; message(text: string): void; close(code: number, reason: string): void }
export interface Upstream { send(data: string): void; close(): void }
export type OpenUpstream = (url: string, ev: UpstreamEvents) => Upstream;

export interface LiveConfig extends HandlerConfig {
  liveModel: string;
  /** 講完後等 Live 回 reply() 的上限（毫秒），超過就改走 generateContent。 */
  liveTimeoutMs: number;
  /** Live 的 thinkingBudget（預設 0；null＝不送）。 */
  liveThinkingBudget?: number | null;
  openUpstream: OpenUpstream;
  setTimer: (fn: () => void, ms: number) => () => void;
}

export interface ClientLink { sendText(s: string): void; sendBinary(b: Uint8Array): void; close(): void }

let cannedN = 0;

export function createLiveSession(cfg: LiveConfig, client: ClientLink, headerToken: string | null, device: string | null = null) {
  let phase: "init" | "streaming" | "ended" | "closed" = "init";
  let up: Upstream | null = null;
  let upReady = false, upFailed = "", answered = false;
  let rate = 16000, maxBytes = 0, bytes = 0;
  let addressee: Addressee = "auto";
  let state = sanitizeState({});
  let directives: unknown = {};
  let cast = parseCast(null);
  const pcm: Uint8Array[] = [];
  const queue: string[] = [];
  let cancelTimer: (() => void) | null = null;
  let devTag: Promise<string> = Promise.resolve("-");
  const t = { start: 0, setup: 0, end: 0, reply: 0, facts: 0, ttsStart: 0, ttsFirst: 0, ttsDone: 0 };

  const sendJson = (o: unknown) => { if (phase !== "closed") client.sendText(JSON.stringify(o)); };
  const finish = () => {
    if (phase === "closed") return;
    phase = "closed";
    cancelTimer?.(); cancelTimer = null;
    try { up?.close(); } catch {}
    client.close();
  };
  const fail = (error: string, code?: string) => { sendJson({ type: "error", error, ...(code ? { code } : {}) }); finish(); };
  const upSend = (m: unknown) => {
    const s = JSON.stringify(m);
    if (upReady && up) up.send(s); else queue.push(s);
  };

  function start(m: any) {
    const token = headerToken || (typeof m.token === "string" ? m.token : "");
    if (!token || !cfg.tokens.includes(token)) return fail("bad token");
    if (device && !isValidDevice(device)) return fail("bad device id", "bad_device");
    const limit = cfg.limiter.take(limitKey(token, device));
    if (!limit.ok) return fail(LIMIT_MESSAGES[limit.code], limit.code);
    devTag = deviceTag(device);
    addressee = (CHARACTERS as readonly string[]).includes(m.addressee) ? m.addressee : "auto";
    state = sanitizeState(m.state);
    directives = m.directives ?? {};
    cast = parseCast(m.voices);
    rate = Number.isInteger(m.rate) && m.rate >= 8000 && m.rate <= 48000 ? m.rate : 16000;
    maxBytes = Math.round(cfg.maxAudioSec * rate) * 2;
    phase = "streaming";
    t.start = cfg.now();
    try {
      up = cfg.openUpstream(liveUrl(cfg.geminiKey), {
        open: () => up?.send(JSON.stringify(buildLiveSetup(cfg.liveModel, { addressee, state, directives }, { thinkingBudget: cfg.liveThinkingBudget }))),
        message: onUpstream,
        close: (code, reason) => onUpstreamGone(`live closed ${code} ${String(reason).slice(0, 80)}`),
      });
    } catch (e) { onUpstreamGone("live connect: " + (e as Error).message); }
  }

  function onUpstream(text: string) {
    if (phase === "closed" || answered) return;
    let m: any;
    try { m = JSON.parse(text); } catch { return; }
    if (m.setupComplete) {
      t.setup = cfg.now();
      upReady = true;
      up!.send(JSON.stringify({ realtimeInput: { activityStart: {} } }));
      for (const s of queue.splice(0)) up!.send(s);
      // 玩家在 Live 準備好之前就講完了：從現在才開始算模型回話的時間
      if (phase === "ended") startReplyTimer();
      return;
    }
    const reply = parseToolCall(m);
    if (reply) { answered = true; cancelTimer?.(); try { up?.close(); } catch {} void deliver(reply, "live"); return; }
    if (m.serverContent?.turnComplete && phase === "ended") fallback("live: turn ended without reply()");
  }

  function onUpstreamGone(reason: string) {
    if (answered || phase === "closed") return;
    upFailed = reason;
    upReady = false;
    if (phase === "ended") fallback(reason);
  }

  function audio(b: Uint8Array) {
    if (phase !== "streaming") return;
    const room = maxBytes - bytes;
    const chunk = b.length > room ? b.subarray(0, room) : b;
    if (chunk.length) {
      pcm.push(chunk.slice());
      bytes += chunk.length;
      if (!upFailed) upSend({ realtimeInput: { audio: { data: bytesToBase64(chunk), mimeType: `audio/pcm;rate=${rate}` } } });
    }
    if (bytes >= maxBytes) end();
  }

  function end() {
    if (phase !== "streaming") return;
    phase = "ended";
    t.end = cfg.now();
    if (upFailed) { fallback(upFailed); return; }
    upSend({ realtimeInput: { activityEnd: {} } });
    startReplyTimer();
  }

  function startReplyTimer() {
    cancelTimer?.();
    cancelTimer = cfg.setTimer(() => fallback(`live timeout ${cfg.liveTimeoutMs} ms${upReady ? "" : " (no setup)"}`), cfg.liveTimeoutMs);
  }

  async function fallback(reason: string) {
    if (answered || phase === "closed") return;
    answered = true;
    cancelTimer?.(); cancelTimer = null;
    try { up?.close(); } catch {}
    cfg.log(`[voice] live -> fallback (${reason})`);
    const all = new Uint8Array(bytes);
    let at = 0;
    for (const c of pcm) { all.set(c, at); at += c.length; }
    let reply: ModelReply;
    try {
      reply = await callGemini(cfg.fetch, { apiKey: cfg.geminiKey, model: cfg.model, thinkingLevel: cfg.thinkingLevel },
        { audioBase64: bytesToBase64(pcm16ToWav(all, rate)), addressee, state, directives: (directives ?? {}) as any });
    } catch (e) {
      cfg.log(`[voice] gemini failed: ${(e as Error).message}`);
      return fail("gemini: " + (e as Error).message);
    }
    await deliver(reply, "fallback");
  }

  async function deliver(reply: ModelReply, path: "live" | "fallback") {
    if (phase === "closed") return;
    t.reply = cfg.now();
    const speaker = resolveSpeaker(reply.transcript, addressee);
    const voice = castVoice(speaker, cast);
    let text = "", repaired = false;
    if (reply.intent !== "ignore") {
      const f = applyFacts(reply, state, speaker);
      text = f.text; repaired = f.repaired;
      if (!text || !isPg(text)) { text = cannedLine(speaker, cannedN++); repaired = true; }
    }
    t.facts = cfg.now();
    // 額度用完：照給文字，不配音（Unity 改說預錄的罐頭句）
    const code = text && cfg.ttsBudget && !cfg.ttsBudget.allow() ? TTS_BUDGET_CODE : undefined;
    const voiced = !!text && !code;
    sendJson({ type: "reply", speaker, transcript: reply.transcript, text, intent: reply.intent, emotion: reply.emotion, fact: reply.fact, repaired, audioRate: voiced ? cfg.ttsSampleRate : 0, path, ...(code ? { code } : {}) });

    let error: string | undefined = code ? "tts budget reached" : undefined; // Unity 的 [Voice] done log 會印 proxyError=
    t.ttsStart = cfg.now();
    if (voiced) {
      const tts = { apiKey: cfg.elevenKey, model: cfg.ttsModel, sampleRate: cfg.ttsSampleRate };
      let sent = 0;
      const send = (b: Uint8Array) => {
        if (phase === "closed") return;
        if (!sent) t.ttsFirst = cfg.now();
        sent += b.length;
        client.sendBinary(b);
      };
      let billed = false;
      try { await synthesizeStream(cfg.fetch, tts, voice, text, send); billed = true; }
      catch (e) {
        if (sent) { error = "tts stream: " + (e as Error).message; billed = true; }
        else {
          try {
            const wav = await synthesize(cfg.fetch, tts, voice, text);
            billed = true;
            const info = parseWav(wav);
            const body = wav.subarray(info.dataOffset, info.dataOffset + info.dataLength);
            for (let i = 0; i < body.length; i += 16384) send(body.subarray(i, Math.min(body.length, i + 16384)));
          } catch (e2) { error = "tts: " + (e2 as Error).message; }
        }
      }
      if (billed) cfg.ttsBudget?.record(text.length);
    }
    t.ttsDone = cfg.now();
    const first = t.ttsFirst || t.ttsDone;
    const timings = {
      setup: t.setup ? t.setup - t.start : -1, audioIn: bytes / 2 / rate, gemini: t.reply - t.end, facts: t.facts - t.reply,
      ttsFirst: first - t.ttsStart, firstAudio: first - t.end, tts: t.ttsDone - t.ttsStart, total: t.ttsDone - t.end,
    };
    const ms = Object.fromEntries(Object.entries(timings).map(([k, v]) => [k, k === "audioIn" ? Number(v.toFixed(2)) : Math.round(v)]));
    const dev = await devTag;
    cfg.log(`[voice] live dev=${dev} path=${path} speaker=${speaker} voice=${voice} (model=${reply.speaker ?? "-"}, addressee=${addressee}) intent=${reply.intent} repaired=${repaired}${code ? " tts=budget" : ""} ms=${JSON.stringify(ms)}${error ? " " + error.slice(0, 80) : ""}`);
    sendJson({ type: "done", timings, ...(error ? { error } : {}), ...(code ? { code } : {}) });
    finish();
  }

  return {
    text(s: string) {
      if (phase === "closed") return;
      let m: any;
      try { m = JSON.parse(s); } catch { return fail("message is not JSON"); }
      if (m?.type === "start" && phase === "init") start(m);
      else if (m?.type === "end") end();
      else if (m?.type === "cancel") finish();
    },
    binary(b: Uint8Array) { audio(b); },
    closed() { finish(); },
  };
}
