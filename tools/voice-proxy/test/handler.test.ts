import { test } from "node:test";
import assert from "node:assert/strict";
import { createHandler, type HandlerConfig } from "../src/handler.ts";
import type { RateLimiter } from "../src/ratelimit.ts";
import { TtsBudget, memoryStore } from "../src/ttsbudget.ts";
import { parseWav, base64ToBytes } from "../src/wav.ts";
import { sampleState, silentWavBase64, limiter, DEV_A, DEV_B } from "./helpers.ts";

type Reply = Record<string, unknown>;

function setup(reply: Reply | null, opts: { ttsStatus?: number; geminiStatus?: number; limiter?: RateLimiter; ttsBudget?: TtsBudget } = {}) {
  const calls: { url: string; body: string }[] = [];
  const fake = async (u: string | URL | Request, init?: RequestInit) => {
    const url = String(u);
    calls.push({ url, body: String(init?.body ?? "") });
    if (url.includes("generativelanguage")) {
      if (opts.geminiStatus) return new Response("err", { status: opts.geminiStatus });
      return new Response(JSON.stringify({ candidates: [{ content: { parts: [{ text: JSON.stringify(reply) }] } }] }));
    }
    if (url.includes("elevenlabs")) {
      if (opts.ttsStatus) return new Response("err", { status: opts.ttsStatus });
      return new Response(new Uint8Array(2205 * 2));
    }
    return new Response("?", { status: 404 });
  };
  let t = 0;
  const logs: string[] = [];
  const cfg: HandlerConfig = {
    geminiKey: "G", elevenKey: "E", tokens: ["tok"], model: "gemini-3.8-flash",
    ttsModel: "eleven_flash_v2_5", ttsSampleRate: 22050, maxAudioSec: 10,
    fetch: fake, now: () => (t += 5), limiter: opts.limiter ?? limiter(), log: l => logs.push(l), ttsBudget: opts.ttsBudget,
  };
  return { handler: createHandler(cfg), calls, logs };
}

function talk(body: unknown, token = "tok", device?: string) {
  return new Request("http://x/talk", {
    method: "POST",
    headers: { "content-type": "application/json", "x-voice-token": token, ...(device !== undefined ? { "x-voice-device": device } : {}) },
    body: JSON.stringify(body),
  });
}

const okBody = (extra: Reply = {}) => ({ audio: silentWavBase64(1.5), addressee: "pip", state: sampleState(), directives: {}, ...extra });

const rocco: Reply = {
  speaker: "pip", transcript: "Rocco, how much have you got?", text: "I've got {stack:rocco}, rookie.",
  intent: "on_topic", fact: { kind: "stack", seat: "rocco" }, emotion: "smug",
};

test("rejects missing or wrong token", async () => {
  const { handler } = setup(rocco);
  assert.equal((await handler(talk(okBody(), "nope"))).status, 401);
});

test("rejects audio longer than the limit", async () => {
  const { handler, calls } = setup(rocco);
  const res = await handler(talk(okBody({ audio: silentWavBase64(10.5) })));
  assert.equal(res.status, 413);
  assert.equal(calls.length, 0);
});

test("rejects bad bodies", async () => {
  const { handler } = setup(rocco);
  assert.equal((await handler(talk({ addressee: "pip" }))).status, 400);
  assert.equal((await handler(talk(okBody({ audio: "AAAA" })))).status, 400);
  assert.equal((await handler(new Request("http://x/talk", { method: "GET" }))).status, 405);
  assert.equal((await handler(new Request("http://x/other", { method: "POST" }))).status, 404);
});

test("rate limits per token when no device id is sent", async () => {
  const { handler } = setup(rocco, { limiter: limiter({ capacity: 1, refillPerSec: 0.001 }) });
  assert.equal((await handler(talk(okBody()))).status, 200);
  const res = await handler(talk(okBody()));
  assert.equal(res.status, 429);
  assert.equal((await res.json()).code, "rate_limited");
});

test("rate limits per device: one device throttled, another device on the same token is not", async () => {
  const { handler } = setup(rocco, { limiter: limiter({ capacity: 1, refillPerSec: 0.001 }) });
  assert.equal((await handler(talk(okBody(), "tok", DEV_A))).status, 200);
  assert.equal((await handler(talk(okBody(), "tok", DEV_A))).status, 429);
  assert.equal((await handler(talk(okBody(), "tok", DEV_B))).status, 200);
  assert.equal((await handler(talk(okBody(), "tok"))).status, 200, "token-only key is separate from devices");
});

test("device daily cap answers 429 with code device_daily_cap", async () => {
  const { handler } = setup(rocco, { limiter: limiter({ deviceDailyCap: 1 }) });
  assert.equal((await handler(talk(okBody(), "tok", DEV_A))).status, 200);
  const res = await handler(talk(okBody(), "tok", DEV_A));
  assert.equal(res.status, 429);
  assert.equal((await res.json()).code, "device_daily_cap");
});

test("global daily cap answers 429 with code daily_cap", async () => {
  const { handler } = setup(rocco, { limiter: limiter({ globalDailyCap: 1 }) });
  assert.equal((await handler(talk(okBody(), "tok", DEV_A))).status, 200);
  const res = await handler(talk(okBody(), "tok", DEV_B));
  assert.equal(res.status, 429);
  assert.equal((await res.json()).code, "daily_cap");
});

test("garbage device id is a 400 before touching the limiter or Gemini", async () => {
  const lim = limiter({ capacity: 1, refillPerSec: 0 });
  const { handler, calls } = setup(rocco, { limiter: lim });
  for (const bad of ["nope", "<x>".repeat(30), "a".repeat(200)]) {
    const res = await handler(talk(okBody(), "tok", bad));
    assert.equal(res.status, 400, bad);
    assert.equal((await res.json()).code, "bad_device");
  }
  assert.equal(calls.length, 0);
  assert.equal(lim.stats().turnsToday, 0);
});

test("log line carries a short device hash, never the raw id", async () => {
  const { handler, logs } = setup(rocco);
  assert.equal((await handler(talk(okBody(), "tok", DEV_A))).status, 200);
  const line = logs.find(l => l.includes("speaker="))!;
  assert.match(line, /dev=[0-9a-f]{6}\b/);
  assert.ok(!line.includes(DEV_A) && !line.includes(DEV_A.slice(0, 8)));
});

test("GET /stats needs the app token and returns today's counts", async () => {
  const { handler } = setup(rocco, { limiter: limiter({ deviceDailyCap: 2, globalDailyCap: 50 }) });
  assert.equal((await handler(new Request("http://x/stats"))).status, 401);
  assert.equal((await handler(new Request("http://x/stats", { headers: { "x-voice-token": "nope" } }))).status, 401);
  await handler(talk(okBody(), "tok", DEV_A));
  await handler(talk(okBody(), "tok", DEV_A)); // 到上限
  assert.equal((await handler(talk(okBody(), "tok", DEV_A))).status, 429);
  await handler(talk(okBody(), "tok", DEV_B));
  const res = await handler(new Request("http://x/stats", { headers: { "x-voice-token": "tok" } }));
  assert.equal(res.status, 200);
  const j = await res.json();
  assert.equal(j.turnsToday, 3);
  assert.equal(j.devicesToday, 2);
  assert.equal(j.devicesAtCap, 1);
  assert.equal(j.globalDailyCap, 50);
  assert.equal(j.deviceDailyCap, 2);
  assert.match(j.day, /^\d{4}-\d{2}-\d{2}$/);
  assert.equal((await handler(new Request("http://x/stats", { method: "POST", headers: { "x-voice-token": "tok" } }))).status, 405);
});

test("happy path: name in transcript picks the speaker, facts filled, audio returned", async () => {
  const { handler, calls } = setup(rocco);
  const res = await handler(talk(okBody()));
  assert.equal(res.status, 200);
  const j = await res.json();
  assert.equal(j.speaker, "rocco");
  assert.equal(j.text, "I've got 870, rookie.");
  assert.equal(j.transcript, "Rocco, how much have you got?");
  assert.equal(j.intent, "on_topic");
  assert.equal(j.emotion, "smug");
  const wav = parseWav(base64ToBytes(j.audio));
  assert.equal(wav.sampleRate, 22050);
  for (const k of ["gemini", "facts", "tts", "total"]) assert.equal(typeof j.timings[k], "number", k);
  const tts = calls.find(c => c.url.includes("elevenlabs"))!;
  assert.match(tts.url, /N2lVS1w4EtoT3dr4eOWO/, "Rocco's voice");
  assert.doesNotMatch(calls.map(c => c.body).join(""), /"G"|"E"/, "keys never go into bodies");
});

test("/talk voices map: the seat answers with the realistic character's voice", async () => {
  const { handler, calls } = setup(rocco);
  const res = await handler(talk(okBody({ voices: { rocco: "erik" } } as Reply)));
  assert.equal((await res.json()).speaker, "rocco");
  assert.match(calls.find(c => c.url.includes("elevenlabs"))!.url, /nPczCjzI2devNBz1zQrb/, "Erik's voice");
});

test("ignore intent returns no audio and skips TTS", async () => {
  const { handler, calls } = setup({ speaker: "dot", transcript: "uh", text: "", intent: "ignore", fact: null, emotion: "neutral" });
  const j = await (await handler(talk(okBody({ addressee: "auto" })))).json();
  assert.equal(j.intent, "ignore");
  assert.equal(j.audio, null);
  assert.equal(calls.filter(c => c.url.includes("elevenlabs")).length, 0);
});

test("TTS failure still returns the text (subtitle-only)", async () => {
  const { handler } = setup(rocco, { ttsStatus: 500 });
  const res = await handler(talk(okBody()));
  assert.equal(res.status, 200);
  const j = await res.json();
  assert.equal(j.audio, null);
  assert.equal(j.text, "I've got 870, rookie.");
  assert.match(j.error, /tts/);
});

test("Gemini failure is a 502 with the stage", async () => {
  const { handler } = setup(rocco, { geminiStatus: 500 });
  const res = await handler(talk(okBody()));
  assert.equal(res.status, 502);
  assert.match((await res.json()).error, /gemini/);
});

test("non-PG reply is replaced by a clean in-character line", async () => {
  const { handler } = setup({ ...rocco, transcript: "hey", text: "Shut the fuck up.", fact: null, intent: "banter" });
  const j = await (await handler(talk(okBody({ addressee: "rocco" })))).json();
  assert.doesNotMatch(j.text, /fuck/i);
  assert.ok(j.text.length > 0);
});

test("health check", async () => {
  const { handler } = setup(rocco);
  const res = await handler(new Request("http://x/health"));
  assert.equal(res.status, 200);
});

test("thinking level defaults to low, none disables", async () => {
  const { thinking } = await import("../src/handler.ts");
  assert.equal(thinking(undefined), "low");
  assert.equal(thinking(""), "low");
  assert.equal(thinking("none"), undefined);
  assert.equal(thinking("high"), "high");
});

// ---- TTS 額度保險 ----
const NOW_OCT = () => Date.UTC(2026, 9, 14, 12);

test("/talk counts the characters sent to ElevenLabs into the monthly budget", async () => {
  const ttsBudget = new TtsBudget({ budgetCredits: 1000, creditPerChar: 0.5, store: memoryStore(), now: NOW_OCT });
  const { handler } = setup(rocco, { ttsBudget });
  const j = await (await handler(talk(okBody()))).json();
  assert.equal(j.text, "I've got 870, rookie.");
  assert.equal(ttsBudget.stats().chars, j.text.length);
  assert.equal(ttsBudget.stats().creditsThisMonth, j.text.length * 0.5);
});

test("/talk failed TTS is not counted", async () => {
  const ttsBudget = new TtsBudget({ budgetCredits: 1000, creditPerChar: 0.5, store: memoryStore(), now: NOW_OCT });
  const { handler } = setup(rocco, { ttsBudget, ttsStatus: 401 });
  await handler(talk(okBody()));
  assert.equal(ttsBudget.stats().chars, 0);
});

test("/talk over the budget skips ElevenLabs and replies text with code tts_budget", async () => {
  const ttsBudget = new TtsBudget({ budgetCredits: 10, creditPerChar: 0.5, store: memoryStore(), now: NOW_OCT });
  ttsBudget.record(20);
  const { handler, calls } = setup(rocco, { ttsBudget });
  const res = await handler(talk(okBody()));
  assert.equal(res.status, 200);
  const j = await res.json();
  assert.equal(j.text, "I've got 870, rookie.");
  assert.equal(j.audio, null);
  assert.equal(j.code, "tts_budget");
  assert.ok(!calls.some(c => c.url.includes("elevenlabs")), "no TTS call");
  assert.ok(calls.some(c => c.url.includes("generativelanguage")), "still understands the player");
});

test("GET /stats shows this month's TTS credits, budget and percent", async () => {
  const ttsBudget = new TtsBudget({ budgetCredits: 200, creditPerChar: 0.5, store: memoryStore(), now: NOW_OCT });
  ttsBudget.record(100);
  const { handler } = setup(rocco, { ttsBudget });
  const j = await (await handler(new Request("http://x/stats", { headers: { "x-voice-token": "tok" } }))).json();
  assert.deepEqual(j.tts, { month: "2026-10", chars: 100, creditsThisMonth: 50, budget: 200, percent: 25, creditPerChar: 0.5 });
  assert.equal(j.turnsToday, 0, "rate limit stats still there");
});
