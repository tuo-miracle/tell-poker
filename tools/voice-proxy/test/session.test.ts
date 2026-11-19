import { test } from "node:test";
import assert from "node:assert/strict";
import { createLiveSession, type LiveConfig } from "../src/session.ts";
import type { RateLimiter } from "../src/ratelimit.ts";
import { TtsBudget, memoryStore } from "../src/ttsbudget.ts";
import { parseWav, base64ToBytes } from "../src/wav.ts";
import { sampleState, limiter, DEV_A, DEV_B } from "./helpers.ts";

type Ev = { open(): void; message(t: string): void; close(code: number, reason: string): void };

function fakeUpstream() {
  const u = {
    url: "", sent: [] as any[], closed: false, ev: null as Ev | null,
    open() { u.ev!.open(); },
    msg(o: unknown) { u.ev!.message(JSON.stringify(o)); },
    drop(code = 1011, reason = "boom") { u.ev!.close(code, reason); },
  };
  return u;
}

function streamOf(chunks: Uint8Array[]) {
  return new ReadableStream<Uint8Array>({ start(c) { for (const x of chunks) c.enqueue(x); c.close(); } });
}

interface Opts { ttsStream?: number; ttsFull?: number; gemini?: unknown; geminiStatus?: number; tokens?: string[]; limiter?: RateLimiter; device?: string | null; ttsBudget?: TtsBudget }

function setup(o: Opts = {}) {
  const up = fakeUpstream();
  const calls: { url: string; body: string }[] = [];
  const fetch = async (u: string | URL | Request, init?: RequestInit) => {
    const url = String(u);
    calls.push({ url, body: String(init?.body ?? "") });
    if (url.includes("generativelanguage")) {
      if (o.geminiStatus) return new Response("err", { status: o.geminiStatus });
      return new Response(JSON.stringify({ candidates: [{ content: { parts: [{ text: JSON.stringify(o.gemini) }] } }] }));
    }
    if (url.includes("/stream")) {
      if (o.ttsStream) return new Response("err", { status: o.ttsStream });
      return new Response(streamOf([new Uint8Array(301).fill(1), new Uint8Array(500).fill(2), new Uint8Array(199).fill(3)]));
    }
    if (url.includes("elevenlabs")) {
      if (o.ttsFull) return new Response("err", { status: o.ttsFull });
      return new Response(new Uint8Array(800));
    }
    return new Response("?", { status: 404 });
  };
  const timers: { fn: () => void; ms: number; cancelled: boolean }[] = [];
  let t = 0;
  const logs: string[] = [];
  const cfg: LiveConfig = {
    geminiKey: "G", elevenKey: "E", tokens: o.tokens ?? ["tok"], model: "gemini-3.8-flash", thinkingLevel: "low",
    ttsModel: "eleven_flash_v2_5", ttsSampleRate: 22050, maxAudioSec: 10,
    fetch, now: () => (t += 10), limiter: o.limiter ?? limiter(), log: l => logs.push(l), ttsBudget: o.ttsBudget,
    liveModel: "gemini-3.8-live", liveTimeoutMs: 3000,
    openUpstream: (url, ev) => { up.url = url; up.ev = ev; return { send: d => up.sent.push(JSON.parse(d)), close: () => { up.closed = true; } }; },
    setTimer: (fn, ms) => { const x = { fn, ms, cancelled: false }; timers.push(x); return () => { x.cancelled = true; }; },
  };
  const out = { texts: [] as any[], bins: [] as Uint8Array[], closed: false };
  const s = createLiveSession(cfg, { sendText: x => out.texts.push(JSON.parse(x)), sendBinary: b => out.bins.push(b), close: () => { out.closed = true; } }, "tok", o.device ?? null);
  const fire = () => { for (const x of timers) if (!x.cancelled) { x.cancelled = true; x.fn(); } };
  return { s, up, out, calls, logs, fire, timers };
}

const tick = () => new Promise(r => setImmediate(r));
async function until(cond: () => boolean, n = 200) { for (let i = 0; i < n && !cond(); i++) await tick(); assert.ok(cond(), "condition never became true"); }

const start = (extra: Record<string, unknown> = {}) => JSON.stringify({ type: "start", addressee: "pip", state: sampleState(), directives: {}, rate: 16000, ...extra });
const pcm = (ms: number) => new Uint8Array(32 * ms);

const potCall = { toolCall: { functionCalls: [{ id: "a", name: "reply", args: {
  transcript: "Dot, how much is in the pot?", speaker: "dot", intent: "on_topic", fact_kind: "pot", fact_seat: "null",
  text: "There is {pot} in the pot.", emotion: "neutral" } }] } };

test("streams mic audio to Gemini Live, then replies with filled text and streamed PCM", async () => {
  const { s, up, out } = setup();
  s.text(start());
  assert.match(up.url, /BidiGenerateContent/);
  s.binary(pcm(100)); // 還沒 setupComplete：先排隊
  up.open();
  assert.equal(up.sent[0].setup.model, "models/gemini-3.8-live");
  assert.equal(up.sent.length, 1, "audio waits for setupComplete");
  up.msg({ setupComplete: {} });
  assert.deepEqual(up.sent[1], { realtimeInput: { activityStart: {} } });
  assert.equal(up.sent[2].realtimeInput.audio.mimeType, "audio/pcm;rate=16000");
  assert.equal(base64ToBytes(up.sent[2].realtimeInput.audio.data).length, 3200);
  s.binary(pcm(100));
  assert.equal(up.sent.length, 4);
  s.text(JSON.stringify({ type: "end" }));
  assert.deepEqual(up.sent[4], { realtimeInput: { activityEnd: {} } });
  up.msg(potCall);
  await until(() => out.closed);
  const reply = out.texts[0];
  assert.equal(reply.type, "reply");
  assert.equal(reply.speaker, "dot");
  assert.equal(reply.text, "There is 60 in the pot.");
  assert.equal(reply.audioRate, 22050);
  assert.equal(reply.path, "live");
  const total = out.bins.reduce((n, b) => n + b.length, 0);
  assert.equal(total, 1000);
  assert.ok(out.bins.every(b => b.length % 2 === 0), "PCM16 chunks stay sample-aligned");
  const done = out.texts[out.texts.length - 1];
  assert.equal(done.type, "done");
  for (const k of ["setup", "gemini", "facts", "ttsFirst", "firstAudio", "tts", "total"]) assert.equal(typeof done.timings[k], "number", k);
  assert.ok(up.closed, "upstream closed after the reply");
});

test("start.voices picks the character's own voice for the speaking seat (realistic cast)", async () => {
  const { s, up, out, calls, logs } = setup();
  s.text(start({ voices: { dot: "dot", pip: "marcus", rocco: "nobody" } }));
  up.open(); up.msg({ setupComplete: {} });
  s.binary(pcm(100));
  s.text(JSON.stringify({ type: "end" }));
  up.msg({ toolCall: { functionCalls: [{ id: "a", name: "reply", args: {
    transcript: "Pip, are you bluffing?", speaker: "pip", intent: "on_topic", fact_kind: "none", fact_seat: "null", text: "Me? Never.", emotion: "smug" } }] } });
  await until(() => out.closed);
  assert.equal(out.texts[0].speaker, "pip", "the seat still answers");
  const tts = calls.find(c => c.url.includes("elevenlabs"))!;
  assert.match(tts.url, /cjVigY5qzO86Huf0OWal/, "Marcus' voice, not Pip's");
  assert.ok(logs.some(l => /speaker=pip voice=marcus/.test(l)), logs.join("\n"));
});

test("unknown voice in start.voices falls back to the seat voice", async () => {
  const { s, up, out, calls } = setup();
  s.text(start({ voices: { dot: "evil" } }));
  up.open(); up.msg({ setupComplete: {} });
  s.binary(pcm(100));
  s.text(JSON.stringify({ type: "end" }));
  up.msg(potCall);
  await until(() => out.closed);
  assert.match(calls.find(c => c.url.includes("elevenlabs"))!.url, /EXAVITQu4vr4xnSDxMaL/);
});

test("wrong numbers from the model are replaced with state numbers", async () => {
  const { s, up, out } = setup();
  s.text(start()); up.open(); up.msg({ setupComplete: {} });
  s.text('{"type":"end"}');
  up.msg({ toolCall: { functionCalls: [{ name: "reply", args: { transcript: "pot?", speaker: "dot", intent: "on_topic", fact_kind: "pot", text: "The pot is 500.", emotion: "calm" } }] } });
  await until(() => out.closed);
  assert.equal(out.texts[0].text, "The pot is 60.");
  assert.equal(out.texts[0].repaired, true);
});

test("hole cards in the client state never reach Gemini", () => {
  const { s, up } = setup();
  const st: any = sampleState();
  st.seats[1].hole = ["As", "Ks"];
  s.text(start({ state: st }));
  up.open();
  assert.doesNotMatch(JSON.stringify(up.sent), /"As\\?"|Ks|hole\\?"/);
});

test("Live failure before a reply falls back to generateContent with the buffered audio", async () => {
  const { s, up, out, calls } = setup({ gemini: { transcript: "Pip, how much to call?", speaker: "pip", intent: "on_topic", fact: { kind: "to_call" }, text: "Um, {to_call}.", emotion: "nervous" } });
  s.text(start()); up.open(); up.msg({ setupComplete: {} });
  s.binary(pcm(500));
  s.text('{"type":"end"}');
  up.drop(1011, "internal");
  await until(() => out.closed);
  const g = calls.find(c => c.url.includes(":generateContent"))!;
  assert.ok(g, "non-streaming fallback called");
  const wav = JSON.parse(g.body).contents[0].parts[0].inlineData;
  assert.equal(wav.mimeType, "audio/wav");
  assert.ok(Math.abs(parseWav(base64ToBytes(wav.data)).durationSec - 0.5) < 1e-6);
  assert.equal(out.texts[0].text, "Um, 20.");
  assert.equal(out.texts[0].path, "fallback");
});

test("Live that never answers times out into the fallback", async () => {
  const { s, up, out, fire, timers } = setup({ gemini: { transcript: "hi", speaker: "pip", intent: "banter", fact: null, text: "Oh, hi!", emotion: "shy" } });
  s.text(start()); up.open(); up.msg({ setupComplete: {} });
  s.binary(pcm(200));
  s.text('{"type":"end"}');
  assert.ok(timers.some(t => t.ms === 3000 && !t.cancelled));
  fire();
  await until(() => out.closed);
  assert.equal(out.texts[0].text, "Oh, hi!");
  assert.equal(out.texts[0].path, "fallback");
  up.msg(potCall); // 逾時後才到：不理
  await tick();
  assert.equal(out.texts.filter(t => t.type === "reply").length, 1);
});

test("streaming TTS failure falls back to one-shot TTS; both failing still sends reply and done", async () => {
  const a = setup({ ttsStream: 500 });
  a.s.text(start()); a.up.open(); a.up.msg({ setupComplete: {} }); a.s.text('{"type":"end"}'); a.up.msg(potCall);
  await until(() => a.out.closed);
  assert.equal(a.out.bins.reduce((n, b) => n + b.length, 0), 800);
  const b = setup({ ttsStream: 500, ttsFull: 401 });
  b.s.text(start()); b.up.open(); b.up.msg({ setupComplete: {} }); b.s.text('{"type":"end"}'); b.up.msg(potCall);
  await until(() => b.out.closed);
  assert.equal(b.out.bins.length, 0);
  assert.equal(b.out.texts[0].text, "There is 60 in the pot.");
  const done = b.out.texts[b.out.texts.length - 1];
  assert.equal(done.type, "done");
  assert.match(done.error, /tts/);
});

test("ignore intent replies with no text and no audio", async () => {
  const { s, up, out, calls } = setup();
  s.text(start()); up.open(); up.msg({ setupComplete: {} }); s.text('{"type":"end"}');
  up.msg({ toolCall: { functionCalls: [{ name: "reply", args: { transcript: "", speaker: "pip", intent: "ignore", fact_kind: "none", text: "whatever", emotion: "" } }] } });
  await until(() => out.closed);
  assert.equal(out.texts[0].intent, "ignore");
  assert.equal(out.texts[0].text, "");
  assert.equal(out.bins.length, 0);
  assert.ok(!calls.some(c => c.url.includes("elevenlabs")));
});

test("bad token or rate limit closes with an error before touching Gemini", () => {
  const a = setup({ tokens: ["other"] });
  a.s.text(start());
  assert.equal(a.out.texts[0].type, "error");
  assert.match(a.out.texts[0].error, /token/);
  assert.ok(a.out.closed);
  assert.equal(a.up.url, "");
  const lim = limiter({ capacity: 1, refillPerSec: 0 });
  lim.take("tok:tok");
  const b = setup({ limiter: lim });
  b.s.text(start());
  assert.match(b.out.texts[0].error, /rate/);
  assert.equal(b.out.texts[0].code, "rate_limited");
});

test("live path is limited per device: same device throttled, another device still talks", () => {
  const lim = limiter({ capacity: 1, refillPerSec: 0 });
  const a1 = setup({ limiter: lim, device: DEV_A });
  a1.s.text(start());
  assert.equal(a1.out.texts.length, 0, "first turn goes through");
  assert.match(a1.up.url, /BidiGenerateContent/);
  const a2 = setup({ limiter: lim, device: DEV_A });
  a2.s.text(start());
  assert.equal(a2.out.texts[0].code, "rate_limited");
  assert.equal(a2.up.url, "");
  const b = setup({ limiter: lim, device: DEV_B });
  b.s.text(start());
  assert.equal(b.out.texts.length, 0);
});

test("live path: device daily cap closes with code device_daily_cap", () => {
  const lim = limiter({ deviceDailyCap: 1 });
  setup({ limiter: lim, device: DEV_A }).s.text(start());
  const a = setup({ limiter: lim, device: DEV_A });
  a.s.text(start());
  assert.equal(a.out.texts[0].type, "error");
  assert.equal(a.out.texts[0].code, "device_daily_cap");
  assert.ok(a.out.closed);
  assert.equal(lim.stats().devicesAtCap, 1);
});

test("live path: garbage device id is refused with bad_device before the limiter", () => {
  const lim = limiter();
  const a = setup({ limiter: lim, device: "'; drop table" });
  a.s.text(start());
  assert.equal(a.out.texts[0].code, "bad_device");
  assert.ok(a.out.closed);
  assert.equal(a.up.url, "");
  assert.equal(lim.stats().turnsToday, 0);
});

test("live log line carries the device hash prefix, not the id", async () => {
  const { s, up, out, logs } = setup({ device: DEV_A });
  s.text(start()); up.open(); up.msg({ setupComplete: {} });
  s.text('{"type":"end"}');
  up.msg(potCall);
  await until(() => out.closed);
  const line = logs.find(l => l.includes(" path="))!;
  assert.match(line, /dev=[0-9a-f]{6}\b/);
  assert.ok(!line.includes(DEV_A.slice(0, 8)));
});

test("audio beyond the limit ends the turn by itself", () => {
  const { s, up } = setup();
  s.text(start()); up.open(); up.msg({ setupComplete: {} });
  for (let i = 0; i < 11; i++) s.binary(pcm(1000));
  const ends = up.sent.filter(m => m.realtimeInput?.activityEnd);
  assert.equal(ends.length, 1);
  const audio = up.sent.filter(m => m.realtimeInput?.audio).reduce((n, m) => n + base64ToBytes(m.realtimeInput.audio.data).length, 0);
  assert.equal(audio, 32000 * 10);
});

test("logs per-stage milliseconds including first audio", async () => {
  const { s, up, out, logs } = setup();
  s.text(start()); up.open(); up.msg({ setupComplete: {} }); s.text('{"type":"end"}'); up.msg(potCall);
  await until(() => out.closed);
  assert.ok(logs.some(l => /path=live/.test(l) && /firstAudio/.test(l)), logs.join("\n"));
});

test("when speech ends before Live is ready, the reply timeout restarts at setupComplete", () => {
  const { s, up, timers } = setup();
  s.text(start()); up.open();
  s.binary(pcm(300));
  s.text('{"type":"end"}');
  assert.equal(timers.filter(t => !t.cancelled).length, 1, "covers a setup that never completes");
  up.msg({ setupComplete: {} });
  const live = timers.filter(t => !t.cancelled);
  assert.equal(live.length, 1, "old timer cancelled, new one started");
  assert.notEqual(live[0], timers[0]);
  assert.deepEqual(up.sent[up.sent.length - 1], { realtimeInput: { activityEnd: {} } }, "queued audio then end flushed in order");
});

// ---- TTS 額度保險 ----
const NOW_OCT = () => Date.UTC(2026, 9, 14, 12);

test("live path counts the streamed reply's characters into the monthly budget", async () => {
  const ttsBudget = new TtsBudget({ budgetCredits: 1000, creditPerChar: 0.5, store: memoryStore(), now: NOW_OCT });
  const { s, up, out } = setup({ ttsBudget });
  s.text(start()); up.open(); up.msg({ setupComplete: {} }); s.text('{"type":"end"}'); up.msg(potCall);
  await until(() => out.closed);
  assert.equal(ttsBudget.stats().chars, "There is 60 in the pot.".length);
});

test("live path over the budget: reply text with code tts_budget, audioRate 0, no ElevenLabs, done carries the code", async () => {
  const ttsBudget = new TtsBudget({ budgetCredits: 10, creditPerChar: 0.5, store: memoryStore(), now: NOW_OCT });
  ttsBudget.record(20);
  const { s, up, out, calls } = setup({ ttsBudget });
  s.text(start()); up.open(); up.msg({ setupComplete: {} }); s.text('{"type":"end"}'); up.msg(potCall);
  await until(() => out.closed);
  const reply = out.texts.find(t => t.type === "reply");
  assert.equal(reply.text, "There is 60 in the pot.");
  assert.equal(reply.audioRate, 0);
  assert.equal(reply.code, "tts_budget");
  assert.equal(out.bins.length, 0);
  assert.ok(!calls.some(c => c.url.includes("elevenlabs")));
  const done = out.texts[out.texts.length - 1];
  assert.equal(done.type, "done");
  assert.equal(done.code, "tts_budget");
  assert.equal(done.error, "tts budget reached");
});
