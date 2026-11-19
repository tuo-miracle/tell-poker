// 手動量測（不進 CI，會花一點額度）：對一個跑著的中繼服務，從「玩家講完」到「第一塊回話音訊」的時間。
//   PORT=8788 node src/node-server.ts &   # 另開一個中繼
//   node smoke/bench-e2e.ts [--url http://127.0.0.1:8788] [--runs N] [--mode live|talk]
// live：模擬 Unity，按真實速度每 100 ms 送一塊 PCM，送完立刻送 end（＝VAD 判定講完的那一刻）；計 reply 訊息、第一塊音訊、done。
// talk：舊路徑，講完才 POST 整段 WAV，整段回來才有音訊。
// 都不含 VAD 的結尾靜音（目前 0.4 s）與 Unity 播放緩衝，報告時要另外加。

import { execFileSync } from "node:child_process";
import { dirname, join, resolve } from "node:path";
import { fileURLToPath } from "node:url";
import { loadKeys } from "../src/keys.ts";
import { parseWav, bytesToBase64 } from "../src/wav.ts";
import { QUESTIONS, STATE, DIRECTIVES, questionWav, stats } from "./questions.ts";

const here = dirname(fileURLToPath(import.meta.url));
const checkout = resolve(here, "../../..");
let main: string | null = null;
try { main = dirname(execFileSync("git", ["-C", checkout, "rev-parse", "--path-format=absolute", "--git-common-dir"], { encoding: "utf8" }).trim()); } catch {}
const keys = loadKeys(process.env, [process.env.VOICE_SECRETS_DIR, join(checkout, ".secrets"), main && join(main, ".secrets")].filter((d): d is string => !!d));

const args = process.argv.slice(2);
const opt = (k: string, d: string) => { const i = args.indexOf(k); return i < 0 ? d : args[i + 1]; };
const base = opt("--url", "http://127.0.0.1:8788");
const runs = Number(opt("--runs", "1"));
const mode = opt("--mode", "live");
// --burst：不照真實速度，start 之後立刻送完整段再送 end（模擬講得很短、Live 還沒 setup 完就講完）
const burst = args.includes("--burst");
const token = process.env.VOICE_TOKEN ?? "tell-dev";
const sleep = (ms: number) => new Promise(r => setTimeout(r, ms));

async function live(q: (typeof QUESTIONS)[number]) {
  const wav = await questionWav(q, keys.eleven);
  const info = parseWav(wav);
  const pcm = wav.subarray(info.dataOffset, info.dataOffset + info.dataLength);
  const ws = new WebSocket(base.replace(/^http/, "ws") + "/live?token=" + token);
  ws.binaryType = "arraybuffer";
  let end = 0, reply = 0, first = 0, done = 0, audio = 0;
  let meta: any = null, fin: any = null;
  const closed = new Promise<void>(r => { ws.onclose = () => r(); });
  ws.onmessage = ev => {
    const now = performance.now();
    if (typeof ev.data !== "string") { if (!first) first = now; audio += (ev.data as ArrayBuffer).byteLength; return; }
    const m = JSON.parse(ev.data);
    if (m.type === "reply") { reply = now; meta = m; }
    if (m.type === "done" || m.type === "error") { done = now; fin = m; }
  };
  await new Promise<void>(r => { ws.onopen = () => r(); });
  ws.send(JSON.stringify({ type: "start", addressee: q.addressee, state: STATE, directives: DIRECTIVES, rate: info.sampleRate }));
  const t0 = performance.now();
  for (let i = 0, k = 0; i < pcm.length; i += 3200, k++) {
    ws.send(pcm.subarray(i, i + 3200));
    const wait = t0 + (k + 1) * 100 - performance.now();
    if (!burst && wait > 0) await sleep(wait);
  }
  ws.send('{"type":"end"}');
  end = performance.now();
  await Promise.race([closed, sleep(15000)]);
  return { reply: reply - end, first: first ? first - end : NaN, done: done - end, audioSec: audio / 2 / (meta?.audioRate || 22050), meta, fin };
}

async function talk(q: (typeof QUESTIONS)[number]) {
  const wav = await questionWav(q, keys.eleven);
  const t0 = performance.now();
  const res = await fetch(base + "/talk", { method: "POST", headers: { "content-type": "application/json", "x-voice-token": token },
    body: JSON.stringify({ audio: bytesToBase64(wav), addressee: q.addressee, state: STATE, directives: DIRECTIVES }) });
  const j: any = await res.json();
  const t = performance.now() - t0;
  return { reply: t, first: j.audio ? t : NaN, done: t, audioSec: 0, meta: { ...j, path: "talk" }, fin: { timings: j.timings, error: j.error } };
}

const rows: { reply: number; first: number; done: number }[] = [];
for (let r = 0; r < runs; r++) for (const q of QUESTIONS) {
  const x = mode === "talk" ? await talk(q) : await live(q);
  rows.push(x);
  const m = x.meta ?? {};
  console.log(`${mode} reply=${Math.round(x.reply)} firstAudio=${Math.round(x.first)} done=${Math.round(x.done)} path=${m.path} | "${m.transcript}" -> ${m.speaker}/${m.intent}/${m.fact ? m.fact.kind : "-"} | ${m.text}${m.repaired ? " [repaired]" : ""}${x.fin?.error ? " ERROR " + x.fin.error : ""} | proxy ${JSON.stringify(x.fin?.timings ?? {}, (_k, v) => typeof v === "number" ? Math.round(v * 100) / 100 : v)}`);
}
const f = (k: "reply" | "first" | "done") => { const s = stats(rows.map(r => r[k]).filter(Number.isFinite)); return `${s.median}/${s.p90} (n=${s.n})`; };
console.log(`SUMMARY e2e ${mode} median/p90 ms after speech end (excl. VAD silence): reply=${f("reply")} firstAudio=${f("first")} done=${f("done")}`);
