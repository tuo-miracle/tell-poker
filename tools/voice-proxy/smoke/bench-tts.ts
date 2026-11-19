// 手動量測（不進 CI）：ElevenLabs 配音，整段（/text-to-speech）對串流（/stream）的第一個位元組與完成時間。
//   node smoke/bench-tts.ts [--runs N]

import { execFileSync } from "node:child_process";
import { dirname, join, resolve } from "node:path";
import { fileURLToPath } from "node:url";
import { loadKeys } from "../src/keys.ts";
import { VOICES } from "../src/elevenlabs.ts";
import { stats } from "./questions.ts";

const here = dirname(fileURLToPath(import.meta.url));
const checkout = resolve(here, "../../..");
let main: string | null = null;
try { main = dirname(execFileSync("git", ["-C", checkout, "rev-parse", "--path-format=absolute", "--git-common-dir"], { encoding: "utf8" }).trim()); } catch {}
const keys = loadKeys(process.env, [process.env.VOICE_SECRETS_DIR, join(checkout, ".secrets"), main && join(main, ".secrets")].filter((d): d is string => !!d));
const i = process.argv.indexOf("--runs");
const runs = i >= 0 ? Number(process.argv[i + 1]) : 5;

const lines: [keyof typeof VOICES, string][] = [
  ["dot", "The pot is 60."],
  ["rocco", "That's for me to know and you to find out, rookie. Why don't you worry about calling that bet?"],
  ["pip", "Oh gosh, you just need to put in 20 to stay in the hand."],
  ["vex", "Does the weather change the cards in your hand? Focus on the table."],
];

for (const mode of ["full", "stream"] as const) {
  const first: number[] = [], done: number[] = [];
  for (let r = 0; r < runs; r++) {
    const [who, text] = lines[r % lines.length];
    const url = `https://api.elevenlabs.io/v1/text-to-speech/${VOICES[who]}${mode === "stream" ? "/stream" : ""}?output_format=pcm_22050`;
    const t0 = performance.now();
    const res = await fetch(url, { method: "POST", headers: { "content-type": "application/json", "xi-api-key": keys.eleven }, body: JSON.stringify({ text, model_id: "eleven_flash_v2_5" }) });
    if (!res.ok) { console.log(mode, "http", res.status, (await res.text()).slice(0, 160)); break; }
    const reader = res.body!.getReader();
    let f = 0, n = 0;
    for (;;) { const { value, done: d } = await reader.read(); if (d) break; if (!f && value.length) f = performance.now() - t0; n += value.length; }
    const t = performance.now() - t0;
    first.push(f); done.push(t);
    console.log(`${mode} ${who} first=${Math.round(f)} done=${Math.round(t)} audio=${(n / 2 / 22050).toFixed(2)}s`);
  }
  const a = stats(first), b = stats(done);
  console.log(`SUMMARY tts ${mode} n=${a.n} first median/p90=${a.median}/${a.p90} done=${b.median}/${b.p90}`);
}
