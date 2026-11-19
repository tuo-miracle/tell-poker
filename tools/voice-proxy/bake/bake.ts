// 預錄台詞的合成腳本（手動、會花 ElevenLabs 額度；可重跑，只合成改過的句子）。
// 用法（Node ≥ 22.18）：cd tools/voice-proxy && node bake/bake.ts [--count] [--max-chars 30000] [--concurrency 2] [--attempts 3] [--force] [--no-verify] [--only id,id]
//   --count：只算這次要合成幾句、幾個字元（依聲音列；不呼叫 API）。
//   --only：只重做這些句子（其他句子舊檔還在就保留，即使模型不同）；例：只修 verify-report.json 裡的壞句。
//   預設每句合成完就聽檢（qa.ts：Gemini 逐字轉錄後比對原文），沒過就重合成，最多 --attempts 次；
//   還是沒過就不寫入（同一句的舊音檔留著），最後 exit 1。--force：沒過也寫入。--no-verify：不聽檢（不建議）。
// 模型：固定台詞用 eleven_multilingual_v2（2026-10-14 起；flash 會多念含糊的字）；即時回話仍用 flash（src/，為了延遲）。
// 輸入：bake/lines.json（Unity 端 SpeechInventoryTests 以 SPEECH_EXPORT=1 匯出）。
// 輸出：Assets/_Project/Resources/Speech/<voice>/<id>.mp3 與 Resources/Speech/manifest.json。
// 金鑰：ELEVENLABS_API_KEY 或 .secrets/elevenlabs.key（本 checkout 或主 checkout；只在執行時讀，不印出）。

import { existsSync, mkdirSync, readFileSync, rmSync, writeFileSync } from "node:fs";
import { dirname, join, resolve } from "node:path";
import { fileURLToPath } from "node:url";
import { bakeLines, planBake, FORMAT, type Line, type Manifest } from "./plan.ts";
import { QA_MODEL, mp3Duration, verifyClips, voiceMedians, type QaCache } from "./qa.ts";
import { secret } from "./secrets.ts";

const here = dirname(fileURLToPath(import.meta.url));
const checkout = resolve(here, "../../..");
const resources = join(checkout, "Assets/_Project/Resources");
const manifestPath = join(resources, "Speech/manifest.json");
const linesPath = join(here, "lines.json");

function arg(name: string): string | undefined {
  const i = process.argv.indexOf(name);
  return i >= 0 ? process.argv[i + 1] : undefined;
}
const countOnly = process.argv.includes("--count");
const maxChars = Number(arg("--max-chars") ?? 30000);
const concurrency = Number(arg("--concurrency") ?? 2); // 目前的 ElevenLabs 方案最多 2 個同時請求
const model = process.env.TTS_MODEL ?? "eleven_multilingual_v2";
const only = arg("--only") ? new Set(arg("--only")!.split(",").map(x => x.trim()).filter(Boolean)) : undefined;
const noVerify = process.argv.includes("--no-verify");
const force = process.argv.includes("--force");
const attempts = Number(arg("--attempts") ?? 3);
const cachePath = join(here, "verify-cache.json");

const lines: Line[] = JSON.parse(readFileSync(linesPath, "utf8")).lines;
const manifest: Manifest | null = existsSync(manifestPath) ? JSON.parse(readFileSync(manifestPath, "utf8")) : null;
const mp3 = (file: string) => join(resources, file + ".mp3");
const exists = (file: string) => existsSync(mp3(file));

const plan = planBake(lines, manifest, exists, model, FORMAT, only);
const byVoice: Record<string, number> = {};
for (const l of lines) byVoice[l.voice] = (byVoice[l.voice] ?? 0) + l.text.length;
console.log(`[bake] ${lines.length} lines, ${plan.totalChars} characters in total (${Object.entries(byVoice).map(([v, n]) => v + " " + n).join(", ")})`);
console.log(`[bake] this run: ${plan.bake.length} to synthesize (${plan.chars} characters), ${plan.keep.length} up to date, ${plan.remove.length} to remove`);
for (const [v, n] of Object.entries(plan.byVoice)) console.log(`[bake]   ${v}: ${n!.lines} lines, ${n!.chars} characters`);
if (countOnly) process.exit(0);
if (plan.chars > maxChars) { console.error(`[bake] over budget (${plan.chars} > ${maxChars}); pass --max-chars to allow`); process.exit(2); }

// 聽檢：秒／字中位數用「已在 manifest 的同模型音檔＋第一批新音檔」算一次，之後的重試沿用
const cache: QaCache = existsSync(cachePath) ? JSON.parse(readFileSync(cachePath, "utf8")) : {};
let medians: Record<string, number> | undefined;
const verify = noVerify ? undefined : async (batch: { line: Line; bytes: Uint8Array }[]) => {
  const clips = batch.map(b => ({ id: b.line.id, voice: b.line.voice, text: b.line.text, bytes: b.bytes }));
  if (!medians) {
    const kept = plan.keep.filter(m => m.model === model).map(m => ({ id: m.id, voice: m.voice, text: m.text, seconds: mp3Duration(new Uint8Array(readFileSync(mp3(m.file)))) }));
    medians = voiceMedians([...kept, ...clips.map(c => ({ ...c, seconds: mp3Duration(c.bytes) }))]);
  }
  try {
    const r = await verifyClips({ clips, fetch, apiKey: secret(checkout, "GEMINI_API_KEY", "gemini.key"), model: process.env.QA_MODEL ?? QA_MODEL, cache, medians, concurrency: 6, log: l => console.log(l) });
    return r.results;
  } finally { writeFileSync(cachePath, JSON.stringify(cache, null, 1) + "\n"); }
};

const r = await bakeLines({
  lines, manifest, fetch, apiKey: secret(checkout, "ELEVENLABS_API_KEY", "elevenlabs.key"), model, maxChars, concurrency, verify, attempts, force, only,
  write: (file, bytes) => { mkdirSync(dirname(mp3(file)), { recursive: true }); writeFileSync(mp3(file), bytes); },
  remove: file => { for (const p of [mp3(file), mp3(file) + ".meta"]) if (existsSync(p)) rmSync(p); },
  exists,
  log: l => console.log(l),
});
mkdirSync(dirname(manifestPath), { recursive: true });
writeFileSync(manifestPath, JSON.stringify(r.manifest, null, 1) + "\n");
console.log(`[bake] done: synthesized ${r.baked} lines, ${r.chars} characters (including retries); removed ${r.removed}; manifest ${manifestPath}`);
for (const f of r.failed) console.log(`[bake] ${force ? "FORCED" : "FAILED"} ${f.id} "${f.text}": ${f.issues.join("; ")}`);
if (r.failed.length && !force) { console.log(`[bake] ${r.failed.length} lines failed QA (see above); rephrase or rerun, or pass --force`); process.exitCode = 1; }
