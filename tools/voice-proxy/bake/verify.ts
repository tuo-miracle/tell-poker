// 預錄台詞聽檢（手動、會花一點 Gemini 額度；有快取，重跑只檢查改過的音檔）。
// 用法（Node ≥ 22.18）：cd tools/voice-proxy && node bake/verify.ts [--only id,id] [--concurrency 4]
// 輸入：Assets/_Project/Resources/Speech/manifest.json ＋ mp3。
// 輸出：bake/verify-report.json（每句的轉錄與問題）、bake/verify-cache.json（轉錄快取，鍵＝音檔內容＋原文＋prompt 版本）。
// 有壞句時 exit 1。判定規則見 qa.ts。

import { existsSync, readFileSync, writeFileSync } from "node:fs";
import { dirname, join, resolve } from "node:path";
import { fileURLToPath } from "node:url";
import { QA_MODEL, mp3Duration, verifyClips, voiceMedians, type QaCache } from "./qa.ts";
import { secret } from "./secrets.ts";
import type { Manifest } from "./plan.ts";

const here = dirname(fileURLToPath(import.meta.url));
const checkout = resolve(here, "../../..");
const resources = join(checkout, "Assets/_Project/Resources");
export const cachePath = join(here, "verify-cache.json");
const reportPath = join(here, "verify-report.json");

function arg(name: string): string | undefined {
  const i = process.argv.indexOf(name);
  return i >= 0 ? process.argv[i + 1] : undefined;
}
const only = arg("--only")?.split(",").map(s => s.trim()).filter(Boolean);
const concurrency = Number(arg("--concurrency") ?? 4);
const model = process.env.QA_MODEL ?? QA_MODEL;

const manifest: Manifest = JSON.parse(readFileSync(join(resources, "Speech/manifest.json"), "utf8"));
const cache: QaCache = existsSync(cachePath) ? JSON.parse(readFileSync(cachePath, "utf8")) : {};
const all = manifest.lines.map(l => ({ id: l.id, voice: l.voice, text: l.text, bytes: new Uint8Array(readFileSync(join(resources, l.file + ".mp3"))) }));
const clips = only ? all.filter(c => only.includes(c.id)) : all;
// 中位數一律用整批算，--only 時才不會被少數句子帶偏
const medians = voiceMedians(all.map(c => ({ ...c, seconds: mp3Duration(c.bytes) })));

const save = () => writeFileSync(cachePath, JSON.stringify(cache, null, 1) + "\n");
const timer = setInterval(save, 10000);
let r;
try {
  r = await verifyClips({ clips, fetch, apiKey: secret(checkout, "GEMINI_API_KEY", "gemini.key"), model, cache, medians, concurrency, log: l => console.log(l) });
} finally { clearInterval(timer); save(); }

const failed = r.results.filter(x => !x.ok), warned = r.results.filter(x => x.ok && x.warn);
const report = {
  date: new Date().toISOString(), model, promptNote: "see bake/qa.ts PROMPT", ttsModel: manifest.model,
  checked: r.results.length, passed: r.results.length - failed.length, failed: failed.length, warnings: warned.length, geminiCalls: r.calls, medians,
  failures: failed.map(x => ({ id: x.id, voice: x.voice, intended: x.text, heard: x.transcripts, seconds: +x.seconds.toFixed(2), issues: x.issues })),
  // 盲聽只差換字、引導檢查說有照念：算過，但建議人耳聽一次
  warned: warned.map(x => ({ id: x.id, intended: x.text, heard: x.transcripts, note: x.note })),
  results: r.results.map(x => ({ id: x.id, ok: x.ok, seconds: +x.seconds.toFixed(2), heard: x.transcripts[x.transcripts.length - 1], ...(x.note ? { note: x.note } : {}) })),
};
writeFileSync(reportPath, JSON.stringify(report, null, 1) + "\n");
for (const w of report.warned) console.log(`[verify] warn ${w.id}: want "${w.intended}" heard "${w.heard.join('" / "')}"`);
console.log(`[verify] ${report.passed}/${report.checked} ok, ${report.warnings} of them warnings (${(100 * report.passed / Math.max(1, report.checked)).toFixed(1)}%), ${r.calls} Gemini calls; report ${reportPath}`);
for (const f of report.failures) console.log(`[verify] BAD ${f.id}: want "${f.intended}" heard "${f.heard.join('" / "')}" — ${f.issues.join("; ")}`);
process.exitCode = failed.length ? 1 : 0;
