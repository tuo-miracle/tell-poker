// 手動量測（不進 CI，會花一點額度）：各 Gemini 模型（串流 SSE）回話的延遲與品質。
//   node smoke/bench-models.ts [model[:thinking] ...] [--runs N] [--nostream]
// thinking：low／minimal／none（不送）／budget0（2.5 系列 thinkingBudget=0）。
// 印出每次：首個位元組、回話第一句完成、text 欄位完成、整段完成（ms，從送出算起），以及 speaker／intent／fact 是否符合預期。

import { execFileSync } from "node:child_process";
import { dirname, join, resolve } from "node:path";
import { fileURLToPath } from "node:url";
import { loadKeys } from "../src/keys.ts";
import { buildGeminiRequest, parseGeminiResponse } from "../src/gemini.ts";
import { resolveSpeaker } from "../src/addressee.ts";
import { applyFacts } from "../src/facts.ts";
import { bytesToBase64 } from "../src/wav.ts";
import { QUESTIONS, STATE, DIRECTIVES, questionWav, stats } from "./questions.ts";

const here = dirname(fileURLToPath(import.meta.url));
const checkout = resolve(here, "../../..");
let main: string | null = null;
try { main = dirname(execFileSync("git", ["-C", checkout, "rev-parse", "--path-format=absolute", "--git-common-dir"], { encoding: "utf8" }).trim()); } catch {}
const keys = loadKeys(process.env, [process.env.VOICE_SECRETS_DIR, join(checkout, ".secrets"), main && join(main, ".secrets")].filter((d): d is string => !!d));

const args = process.argv.slice(2);
let runs = 1;
const ri = args.indexOf("--runs");
if (ri >= 0) { runs = Number(args[ri + 1]); args.splice(ri, 2); }
const noStream = args.includes("--nostream");
const models = args.filter(a => !a.startsWith("--"));
if (!models.length) models.push("gemini-3.8-flash:low");

const firstSentence = /"text"\s*:\s*"((?:[^"\\]|\\.)*?[.!?])(?:\s|")/;
const textDone = /"text"\s*:\s*"((?:[^"\\]|\\.)*)"/;

function body(audio: string, q: (typeof QUESTIONS)[number], thinking: string) {
  const b: any = buildGeminiRequest({ audioBase64: audio, addressee: q.addressee, state: STATE as any, directives: DIRECTIVES as any });
  if (thinking === "budget0") b.generationConfig.thinkingConfig = { thinkingBudget: 0 };
  else if (thinking !== "none") b.generationConfig.thinkingConfig = { thinkingLevel: thinking };
  return b;
}

for (const spec of models) {
  const [model, thinking = "low"] = spec.split(":");
  const rows: { ttfb: number; first: number; text: number; done: number; ok: boolean }[] = [];
  for (let r = 0; r < runs; r++) for (const q of QUESTIONS) {
    const audio = bytesToBase64(await questionWav(q, keys.eleven));
    const url = `https://generativelanguage.googleapis.com/v1beta/models/${model}:${noStream ? "generateContent" : "streamGenerateContent?alt=sse"}`;
    const t0 = performance.now();
    const res = await fetch(url, { method: "POST", headers: { "content-type": "application/json", "x-goog-api-key": keys.gemini }, body: JSON.stringify(body(audio, q, thinking)) });
    if (!res.ok) { console.log(`${spec} http ${res.status}: ${(await res.text()).slice(0, 160)}`); break; }
    let raw = "", ttfb = 0, first = 0, text = 0;
    if (noStream) {
      const j: any = await res.json();
      ttfb = first = text = performance.now() - t0;
      raw = (j.candidates?.[0]?.content?.parts ?? []).filter((p: any) => !p.thought).map((p: any) => p.text ?? "").join("");
    } else {
      const reader = res.body!.getReader();
      const dec = new TextDecoder();
      let buf = "";
      for (;;) {
        const { value, done } = await reader.read();
        if (done) break;
        if (!ttfb) ttfb = performance.now() - t0;
        buf += dec.decode(value, { stream: true });
        let nl;
        while ((nl = buf.indexOf("\n")) >= 0) {
          const line = buf.slice(0, nl).trim();
          buf = buf.slice(nl + 1);
          if (!line.startsWith("data:")) continue;
          const j = JSON.parse(line.slice(5));
          raw += (j.candidates?.[0]?.content?.parts ?? []).filter((p: any) => !p.thought).map((p: any) => p.text ?? "").join("");
          const now = performance.now() - t0;
          if (!first && firstSentence.test(raw)) first = now;
          if (!text && textDone.test(raw)) { text = now; if (!first) first = now; }
        }
      }
    }
    const done = performance.now() - t0;
    let reply;
    try { reply = parseGeminiResponse({ candidates: [{ content: { parts: [{ text: raw }] } }] }); }
    catch { console.log(`${spec} | ${q.say} | not JSON: ${raw.slice(0, 120)}`); continue; }
    const speaker = resolveSpeaker(reply.transcript, q.addressee);
    const filled = reply.intent === "ignore" ? { text: "", repaired: false } : applyFacts(reply, STATE as any, speaker);
    const ok = q.speaker.includes(speaker) && q.intent.includes(reply.intent) && (reply.fact?.kind ?? null) === q.fact && (!q.seat || reply.fact?.seat === q.seat);
    rows.push({ ttfb, first: first || done, text: text || done, done, ok });
    console.log(`${spec} | ttfb=${Math.round(ttfb)} first=${Math.round(first || done)} text=${Math.round(text || done)} done=${Math.round(done)} | ${ok ? "OK " : "BAD"} | "${reply.transcript}" -> ${speaker}/${reply.intent}/${reply.fact ? reply.fact.kind + (reply.fact.seat ? ":" + reply.fact.seat : "") : "-"} | ${filled.text}${filled.repaired ? " [repaired]" : ""}`);
  }
  if (rows.length) {
    const s = (k: "ttfb" | "first" | "text" | "done") => { const x = stats(rows.map(r => r[k])); return `${x.median}/${x.p90}`; };
    console.log(`SUMMARY ${spec}${noStream ? " (non-stream)" : ""} n=${rows.length} ok=${rows.filter(r => r.ok).length} median/p90 ttfb=${s("ttfb")} first=${s("first")} text=${s("text")} done=${s("done")}`);
  }
}
