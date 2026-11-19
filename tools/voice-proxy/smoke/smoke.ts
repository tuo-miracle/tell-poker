// 手動冒煙測試（不進 CI，會花一點 API 額度）：
//   node smoke/smoke.ts ["要問的英文句子"] [addressee]
// 1. 用 ElevenLabs（Dot 的聲音）合成一句英文問題，存成 16 kHz WAV（smoke/out/，已忽略；同一句會重用）
// 2. 用真的 Gemini／ElevenLabs 跑一次 /talk handler（同程序，不必先開 server）
// 3. 印出 transcript／回話／各階段毫秒，回話音檔存到 smoke/out/reply.wav

import { existsSync, mkdirSync, readFileSync, writeFileSync } from "node:fs";
import { createHash } from "node:crypto";
import { execFileSync } from "node:child_process";
import { dirname, join, resolve } from "node:path";
import { fileURLToPath } from "node:url";
import { createHandler } from "../src/handler.ts";
import { loadKeys } from "../src/keys.ts";
import { RateLimiter } from "../src/ratelimit.ts";
import { synthesize } from "../src/elevenlabs.ts";
import { base64ToBytes, bytesToBase64, parseWav } from "../src/wav.ts";

const here = dirname(fileURLToPath(import.meta.url));
const checkout = resolve(here, "../../..");
let main: string | null = null;
try { main = dirname(execFileSync("git", ["-C", checkout, "rev-parse", "--path-format=absolute", "--git-common-dir"], { encoding: "utf8" }).trim()); } catch {}
const keys = loadKeys(process.env, [process.env.VOICE_SECRETS_DIR, join(checkout, ".secrets"), main && join(main, ".secrets")].filter((d): d is string => !!d));

const question = process.argv[2] ?? "Dot, how much is in the pot?";
const addressee = process.argv[3] ?? "rocco";
const out = join(here, "out");
mkdirSync(out, { recursive: true });
const qPath = join(out, "q-" + createHash("sha1").update(question).digest("hex").slice(0, 8) + ".wav");

if (!existsSync(qPath)) {
  const t = performance.now();
  const wav = await synthesize((u, i) => fetch(u, i), { apiKey: keys.eleven, model: "eleven_flash_v2_5", sampleRate: 16000 }, "dot", question);
  writeFileSync(qPath, wav);
  console.log(`question WAV synthesized in ${Math.round(performance.now() - t)} ms -> ${qPath}`);
}
const qWav = new Uint8Array(readFileSync(qPath));
console.log(`question: "${question}" (${parseWav(qWav).durationSec.toFixed(2)} s), addressee=${addressee}`);

const state = {
  phase: "betting", hand: 4, street: "flop", board: ["Ah", "7d", "2c"], blinds: { small: 5, big: 10 },
  pot: 60, toAct: "you", button: "rocco", youToCall: 20,
  seats: [
    { name: "you", stack: 950, bet: 0, status: "active" },
    { name: "pip", stack: 980, bet: 0, status: "folded" },
    { name: "rocco", stack: 870, bet: 20, status: "active" },
    { name: "vex", stack: 1000, bet: 0, status: "folded" },
  ],
  actions: [
    { street: "preflop", who: "rocco", action: "raise", amount: 30 },
    { street: "preflop", who: "you", action: "call", amount: 30 },
    { street: "flop", who: "rocco", action: "bet", amount: 20 },
  ],
};

const handler = createHandler({
  geminiKey: keys.gemini, elevenKey: keys.eleven, tokens: ["smoke"],
  model: process.env.GEMINI_MODEL ?? "gemini-3.8-flash", thinkingLevel: process.env.GEMINI_THINKING_LEVEL || undefined,
  ttsModel: "eleven_flash_v2_5", ttsSampleRate: 22050, maxAudioSec: 10,
  fetch: (u, i) => fetch(u, i), now: () => performance.now(), limiter: new RateLimiter(5, 1), log: l => console.log(l),
});
const res = await handler(new Request("http://local/talk", {
  method: "POST", headers: { "x-voice-token": "smoke", "content-type": "application/json" },
  body: JSON.stringify({ audio: bytesToBase64(qWav), addressee, state, directives: { rocco: { mood: "Confident", tell: "LeanBack", intensity: 0.6 } } }),
}));
const j: any = await res.json();
console.log(`status ${res.status}`);
console.log(`transcript: ${j.transcript}`);
console.log(`speaker:    ${j.speaker}  intent: ${j.intent}  emotion: ${j.emotion}  fact: ${JSON.stringify(j.fact)}  repaired: ${j.repaired}`);
console.log(`reply:      ${j.text}`);
console.log(`timings ms: ${JSON.stringify(j.timings, (_k, v) => (typeof v === "number" ? Math.round(v) : v))}`);
if (j.error) console.log(`error:      ${j.error}`);
if (j.audio) {
  const wav = base64ToBytes(j.audio);
  writeFileSync(join(out, "reply.wav"), wav);
  console.log(`reply audio: ${parseWav(wav).durationSec.toFixed(2)} s @ ${parseWav(wav).sampleRate} Hz -> ${join(out, "reply.wav")}`);
}
