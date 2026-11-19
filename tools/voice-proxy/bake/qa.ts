// 預錄台詞的自動聽檢：Gemini 逐字轉錄 mp3 → 和原文做正規化後的逐字比對，抓 ElevenLabs 多念、漏念、念錯、拖太長。
// 2026-10-14 起：Quest 上聽到 dot.invalid.cannotcheck 多了一串含糊的字（"uh, try or need it"），之前 358 句都沒人聽過。

import { createHash } from "node:crypto";
import type { FetchFn } from "../src/gemini.ts";

/** 轉錄 prompt／送出的音訊處理改了就加一，快取自動失效。 */
export const PROMPT_VERSION = 3;
export const QA_MODEL = "gemini-3.8-flash";

// ---------- 正規化 ----------

const ONES = ["zero", "one", "two", "three", "four", "five", "six", "seven", "eight", "nine", "ten",
  "eleven", "twelve", "thirteen", "fourteen", "fifteen", "sixteen", "seventeen", "eighteen", "nineteen"];
const TENS = ["", "", "twenty", "thirty", "forty", "fifty", "sixty", "seventy", "eighty", "ninety"];

export function numberToWords(n: number): string {
  if (n < 20) return ONES[n];
  if (n < 100) return TENS[Math.floor(n / 10)] + (n % 10 ? " " + ONES[n % 10] : "");
  for (const [size, name] of [[1e9, "billion"], [1e6, "million"], [1e3, "thousand"], [100, "hundred"]] as const) {
    if (n >= size) {
      const rest = n % size;
      return numberToWords(Math.floor(n / size)) + " " + name + (rest ? " " + numberToWords(rest) : "");
    }
  }
  return String(n);
}

/** 轉錄常見的名字拼法。 */
const ALIASES: Record<string, string> = { vecks: "vex", vexx: "vex", rocko: "rocco", roco: "rocco", rokko: "rocco", pipp: "pip" };
const MUMBLE = "zzmumblezz";

function canonWord(w: string): string {
  if (/^u+m+$|^e+r+m+$/.test(w)) return "um";
  if (/^u+h+$|^e+r+$/.test(w)) return "uh";
  if (/^h+m+$|^m{2,}$|^mhm+$/.test(w)) return "hm";
  if (/^o+h+$/.test(w)) return "oh";
  if (/^a+w+$/.test(w)) return "aw";
  if (w === "ok") return "okay";
  if (w === MUMBLE) return "[mumble]";
  return ALIASES[w] ?? w;
}

/** 小寫、去標點、縮寫展開、數字轉英文字、語助詞統一拼法（連續的 hm 併成一個）。 */
export function normalizeWords(s: string): string[] {
  let t = s.toLowerCase().replace(/[’‘`]/g, "'");
  // [mumbling]、(unintelligible) 之類的標記；停頓／呼吸不算
  t = t.replace(/\[([^\]]*)\]|\(([^)]*)\)/g, (_m, a, b) => /pause|silence|breath/.test(a ?? b ?? "") ? " " : ` ${MUMBLE} `);
  t = t.replace(/(\d),(?=\d{3}\b)/g, "$1").replace(/%/g, " percent");
  t = t.replace(/\d+/g, d => " " + numberToWords(Number(d)) + " ");
  t = t.replace(/\bcan't\b/g, "can not").replace(/\bcannot\b/g, "can not").replace(/\bwon't\b/g, "will not")
    .replace(/\blet's\b/g, "let us").replace(/n't\b/g, " not")
    .replace(/'re\b/g, " are").replace(/'m\b/g, " am").replace(/'ll\b/g, " will").replace(/'ve\b/g, " have").replace(/'d\b/g, " would")
    .replace(/'s\b/g, " 's");
  t = t.replace(/[^a-z' ]+/g, " ").replace(/(^|\s)'(?!s\b)|'(?=\s|$)/g, " ");
  const out: string[] = [];
  for (const raw of t.split(/\s+/)) {
    if (!raw) continue;
    const w = canonWord(raw);
    if (w === "hm" && out[out.length - 1] === "hm") continue;
    out.push(w);
  }
  return out;
}

const FILLERS = new Set(["um", "uh", "hm", "[mumble]"]);
/** 猶豫的語助詞互相算對（原文寫 "Eh," 念成 "Ah," 不算錯）。 */
const HESITATIONS = new Set(["um", "uh", "eh", "ah"]);
/** 聽起來一樣、轉錄只是拼法不同的字。 */
const HOMOPHONES = [["two", "to", "too"], ["four", "for", "fore"], ["eight", "ate"], ["one", "won"], ["read", "reed"], ["right", "write"], ["their", "there"], ["hole", "whole"]];
const HOMO = new Map(HOMOPHONES.flatMap(g => g.map(w => [w, g[0]] as const)));
const S_FORMS = new Set(["'s", "is", "has"]);
function same(a: string, b: string): boolean {
  if (a === b) return true;
  if (HOMO.has(a) && HOMO.get(a) === HOMO.get(b)) return true;
  if (HESITATIONS.has(a) && HESITATIONS.has(b)) return true;
  return S_FORMS.has(a) && S_FORMS.has(b);
}

export interface TextDiff { ok: boolean; extra: string[]; missing: string[]; substituted: { want: string; heard: string }[]; fillers: number; want: string[]; heard: string[] }

/** 逐字對齊（編輯距離），回報多出、漏掉、替換的字。 */
export function compareText(intended: string, heardText: string): TextDiff {
  const a = normalizeWords(intended), b = normalizeWords(heardText);
  const n = a.length, m = b.length;
  const d: number[][] = Array.from({ length: n + 1 }, (_, i) => Array.from({ length: m + 1 }, (_, j) => (i === 0 ? j : j === 0 ? i : 0)));
  for (let i = 1; i <= n; i++)
    for (let j = 1; j <= m; j++)
      d[i][j] = Math.min(d[i - 1][j - 1] + (same(a[i - 1], b[j - 1]) ? 0 : 1), d[i - 1][j] + 1, d[i][j - 1] + 1);
  const extra: string[] = [], missing: string[] = [], substituted: { want: string; heard: string }[] = [];
  let i = n, j = m;
  while (i > 0 || j > 0) {
    if (i > 0 && j > 0 && same(a[i - 1], b[j - 1]) && d[i][j] === d[i - 1][j - 1]) { i--; j--; }
    else if (j > 0 && d[i][j] === d[i][j - 1] + 1) { extra.push(b[--j]); }
    else if (i > 0 && d[i][j] === d[i - 1][j] + 1) { missing.push(a[--i]); }
    else { substituted.push({ want: a[--i], heard: b[--j] }); }
  }
  extra.reverse(); missing.reverse(); substituted.reverse();
  return { ok: !extra.length && !missing.length && !substituted.length, extra, missing, substituted, fillers: extra.filter(w => FILLERS.has(w)).length, want: a, heard: b };
}

// ---------- 長度 ----------

const MP3_BITRATES: Record<"1" | "2", number[]> = {
  "1": [0, 32, 40, 48, 56, 64, 80, 96, 112, 128, 160, 192, 224, 256, 320],
  "2": [0, 8, 16, 24, 32, 40, 48, 56, 64, 80, 96, 112, 128, 144, 160],
};
const MP3_RATES: Record<number, number[]> = { 3: [44100, 48000, 32000], 2: [22050, 24000, 16000], 0: [11025, 12000, 8000] };

interface Mp3Frame { offset: number; len: number; seconds: number; samples: number; rate: number; kbps: number }

/** 逐一找出 Layer III 幀（跳過 ID3v2 與無效位元組）。 */
function* mp3Frames(b: Uint8Array): Generator<Mp3Frame> {
  let p = 0;
  if (b.length >= 10 && b[0] === 0x49 && b[1] === 0x44 && b[2] === 0x33)
    p = 10 + ((b[6] & 0x7f) << 21 | (b[7] & 0x7f) << 14 | (b[8] & 0x7f) << 7 | (b[9] & 0x7f));
  while (p + 4 <= b.length) {
    const ver = (b[p + 1] >> 3) & 3, layer = (b[p + 1] >> 1) & 3, bi = b[p + 2] >> 4, si = (b[p + 2] >> 2) & 3;
    if (b[p] !== 0xff || (b[p + 1] & 0xe0) !== 0xe0 || ver === 1 || layer !== 1 || bi === 0 || bi === 15 || si === 3) { p++; continue; }
    const rate = MP3_RATES[ver][si], kbps = MP3_BITRATES[ver === 3 ? "1" : "2"][bi], pad = (b[p + 2] >> 1) & 1;
    const samples = ver === 3 ? 1152 : 576;
    const len = Math.floor((samples / 8) * kbps * 1000 / rate) + pad;
    yield { offset: p, len, seconds: samples / rate, samples, rate, kbps };
    p += len;
  }
}

/** 數 Layer III 幀算秒數（跳過 ID3v2）；不是 mp3 回 0。 */
export function mp3Duration(b: Uint8Array): number {
  let seconds = 0;
  for (const f of mp3Frames(b)) seconds += f.seconds;
  return seconds;
}

/**
 * 前後各補約 seconds 秒的靜音幀（標頭照抄第一幀、side info 全 0＝沒有音訊資料）。
 * 2026-10-14 實測：0.5 s 的單字片段直接送 Gemini 會被編出整句話（"one" → "He went to the cinema…"），前後補 0.8 s 靜音後 3/3 都正確。
 */
export function padMp3(b: Uint8Array, seconds: number): Uint8Array {
  const first = mp3Frames(b).next();
  if (first.done) return b;
  const f = first.value;
  const header = b.slice(f.offset, f.offset + 4);
  header[2] &= ~0x02; // 不要 padding 位元組
  const len = Math.floor((f.samples / 8) * f.kbps * 1000 / f.rate);
  const silent = new Uint8Array(len);
  silent.set(header);
  const n = Math.ceil(seconds / f.seconds);
  const body = b.subarray(f.offset);
  const out = new Uint8Array(2 * n * len + body.length);
  for (let i = 0; i < n; i++) out.set(silent, i * len);
  out.set(body, n * len);
  for (let i = 0; i < n; i++) out.set(silent, n * len + body.length + i * len);
  return out;
}

export interface Timed { id: string; voice: string; text: string; seconds: number }

/** 念出來的字數（不併 hm、不展開縮寫）：算長度用。 */
function spokenWords(text: string): number {
  return text.split(/[\s—–-]+/).filter(w => /[A-Za-z0-9]/.test(w)).length;
}

/** 各聲音「秒／字」的中位數（只用 3 字以上的句子）。 */
export function voiceMedians(clips: Timed[]): Record<string, number> {
  const by: Record<string, number[]> = {};
  for (const c of clips) {
    const w = spokenWords(c.text);
    if (w >= 3 && c.seconds > 0) (by[c.voice] ??= []).push(c.seconds / w);
  }
  const out: Record<string, number> = {};
  for (const [v, xs] of Object.entries(by)) { xs.sort((x, y) => x - y); out[v] = xs[Math.floor(xs.length / 2)]; }
  return out;
}

/** 比同聲音中位數慢很多的句子：秒數 > 2 × 中位數 × 字數 + 0.6 s（短片段的頭尾靜音不算）。 */
export function durationFlags(clips: Timed[], medians = voiceMedians(clips)): Map<string, string> {
  const out = new Map<string, string>();
  for (const c of clips) {
    const med = medians[c.voice], w = Math.max(1, spokenWords(c.text));
    if (!med || !(c.seconds > 0)) continue;
    if (c.seconds > 2 * med * w + 0.6)
      out.set(c.id, `${c.seconds.toFixed(2)} s for ${w} words = ${(c.seconds / w).toFixed(2)} s/word (voice median ${med.toFixed(2)} s/word)`);
  }
  return out;
}

// ---------- 判定 ----------

export interface ClipCheck { ok: boolean; issues: string[]; diff: TextDiff }

export function checkClip(c: { text: string; transcript: string; durationFlag?: string }): ClipCheck {
  const diff = compareText(c.text, c.transcript);
  const issues: string[] = [];
  if (diff.extra.length) issues.push("extra: " + diff.extra.join(" "));
  if (diff.missing.length) issues.push("missing: " + diff.missing.join(" "));
  if (diff.substituted.length) issues.push("substituted: " + diff.substituted.map(s => `${s.want}→${s.heard}`).join(", "));
  if (c.durationFlag) issues.push("duration: " + c.durationFlag);
  return { ok: issues.length === 0, issues, diff };
}

// ---------- Gemini 轉錄 ----------

const PROMPT = [
  "Transcribe this voice clip verbatim, exactly as spoken. The clip can be very short: a single word or a two-word fragment.",
  "Write only what is actually audible in this clip. Never guess, continue or invent speech; if you hear one word, return one word; if there is no speech, return an empty string.",
  "Do not correct, complete or tidy anything: include every filler sound (uh, um, hmm), repeated words, false starts and extra words.",
  "If some speech is mumbled or unintelligible, write [mumble] at that spot. Write numbers as words. Use normal punctuation.",
  "Do not describe non-speech sounds. Return JSON {\"transcript\": \"...\"}.",
].join("\n");

export function buildTranscribeRequest(audioBase64: string, opts: { thinkingLevel?: string } = {}) {
  const generationConfig: Record<string, unknown> = {
    temperature: 0,
    responseMimeType: "application/json",
    responseSchema: { type: "OBJECT", properties: { transcript: { type: "STRING" } }, required: ["transcript"] },
  };
  if (opts.thinkingLevel) generationConfig.thinkingConfig = { thinkingLevel: opts.thinkingLevel };
  return {
    contents: [{ role: "user", parts: [{ inlineData: { mimeType: "audio/mpeg", data: audioBase64 } }, { text: PROMPT }] }],
    generationConfig,
  };
}

function candidateText(json: any): string {
  const cand = json?.candidates?.[0];
  if (!cand) throw new Error("gemini: no candidate" + (json?.promptFeedback?.blockReason ? ` (${json.promptFeedback.blockReason})` : ""));
  return (cand.content?.parts ?? []).filter((p: any) => !p.thought && typeof p.text === "string").map((p: any) => p.text).join("");
}

export function parseTranscript(json: any): string {
  const o = JSON.parse(candidateText(json).replace(/^\s*```(?:json)?|```\s*$/g, ""));
  if (typeof o?.transcript !== "string") throw new Error("gemini: no transcript");
  return o.transcript;
}

async function geminiJson(fetchFn: FetchFn, cfg: { apiKey: string; model: string; retryDelayMs?: number }, request: unknown): Promise<any> {
  const url = `https://generativelanguage.googleapis.com/v1beta/models/${cfg.model}:generateContent`;
  const body = JSON.stringify(request);
  for (let attempt = 0; ; attempt++) {
    let err: Error;
    try {
      const res = await fetchFn(url, { method: "POST", headers: { "content-type": "application/json", "x-goog-api-key": cfg.apiKey }, body });
      if (res.ok) return JSON.parse(candidateText(await res.json()).replace(/^\s*```(?:json)?|```\s*$/g, ""));
      err = new Error(`gemini http ${res.status}: ${(await res.text()).slice(0, 200)}`);
      if (res.status !== 429 && res.status < 500) throw err;
    } catch (e) { err = e as Error; if (/http 4(?!29)/.test(err.message)) throw err; }
    if (attempt >= 4) throw err;
    await new Promise(r => setTimeout(r, (cfg.retryDelayMs ?? 2000) * (attempt + 1)));
  }
}

export async function transcribe(fetchFn: FetchFn, cfg: { apiKey: string; model: string; thinkingLevel?: string; retryDelayMs?: number; padSeconds?: number }, bytes: Uint8Array): Promise<string> {
  const audio = padMp3(bytes, cfg.padSeconds ?? 0.8);
  const o = await geminiJson(fetchFn, cfg, buildTranscribeRequest(Buffer.from(audio).toString("base64"), { thinkingLevel: cfg.thinkingLevel }));
  if (typeof o?.transcript !== "string") throw new Error("gemini: no transcript");
  return o.transcript;
}

/**
 * 盲聽轉錄只有「換字」（沒有多念、最多漏一個短字）時的第二關：把原文給 Gemini，問音檔是不是照念。
 * 單獨一句 "Pot goes to Pip!" 常被盲聽寫成 "Pod/Part/Puck goes to Pip"（字尾 t/d 聽不出來），這不是 TTS 出錯；
 * 多念、漏念（真正的幻聽）一律不走這關，只看盲聽結果。
 */
export function judgeable(d: TextDiff): boolean {
  return d.extra.length === 0 && d.missing.length <= 1 && d.missing.every(w => w.length <= 3) && d.substituted.length + d.missing.length > 0;
}

const JUDGE_PROMPT = (text: string) => [
  `Script for this clip: "${text}"`,
  "Listen carefully. Does the speaker say exactly the words of the script — no extra words, no missing words, no different words, no mumbled or garbled parts?",
  "Accent, intonation and ordinary pronunciation variants are fine. Punctuation does not matter.",
  "Return JSON {\"match\": true|false, \"differences\": \"what differs, empty if match\"}.",
].join("\n");

export function buildJudgeRequest(audioBase64: string, text: string) {
  return {
    contents: [{ role: "user", parts: [{ inlineData: { mimeType: "audio/mpeg", data: audioBase64 } }, { text: JUDGE_PROMPT(text) }] }],
    generationConfig: {
      temperature: 0,
      responseMimeType: "application/json",
      responseSchema: { type: "OBJECT", properties: { match: { type: "BOOLEAN" }, differences: { type: "STRING" } }, required: ["match", "differences"] },
    },
  };
}

export async function judge(fetchFn: FetchFn, cfg: { apiKey: string; model: string; retryDelayMs?: number; padSeconds?: number }, bytes: Uint8Array, text: string): Promise<{ match: boolean; differences: string }> {
  const o = await geminiJson(fetchFn, cfg, buildJudgeRequest(Buffer.from(padMp3(bytes, cfg.padSeconds ?? 0.8)).toString("base64"), text));
  return { match: o?.match === true, differences: typeof o?.differences === "string" ? o.differences : "" };
}

/** 快取鍵：音檔內容＋原文＋prompt 版本。 */
export function qaKey(bytes: Uint8Array, text: string): string {
  return createHash("sha256").update(bytes).update(`|${text}|v${PROMPT_VERSION}`).digest("hex").slice(0, 20);
}

export interface ClipInput { id: string; voice: string; text: string; bytes: Uint8Array }
export interface ClipResult { id: string; voice: string; text: string; seconds: number; ok: boolean; issues: string[]; transcripts: string[]; warn?: boolean; note?: string }
export type QaCache = Record<string, { transcripts: string[]; judge?: { match: boolean; differences: string } }>;

export interface VerifyOptions {
  clips: ClipInput[];
  fetch: FetchFn;
  apiKey: string;
  model: string;
  /** 會被改寫（呼叫端負責存檔）。 */
  cache: QaCache;
  /** 各聲音秒／字中位數；不給就用這批 clips 算。 */
  medians?: Record<string, number>;
  thinkingLevel?: string;
  concurrency?: number;
  retryDelayMs?: number;
  log?: (line: string) => void;
}

/**
 * 每句轉錄一次；字對不上時再轉錄一次（第二意見），兩次都對不上才算壞。
 * 兩次都只差「換字」時走 judge（給原文的引導檢查），通過算 warn（報告列出，建議人耳聽一次）。
 * 長度異常（durationFlags）直接算壞：多出來的通常是含糊、轉錄不出來的聲音。
 */
export async function verifyClips(o: VerifyOptions): Promise<{ results: ClipResult[]; calls: number }> {
  const timed = o.clips.map(c => ({ id: c.id, voice: c.voice, text: c.text, seconds: mp3Duration(c.bytes) }));
  const flags = durationFlags(timed, o.medians ?? voiceMedians(timed));
  const results: ClipResult[] = new Array(o.clips.length);
  let calls = 0, next = 0;
  const cfg = { apiKey: o.apiKey, model: o.model, thinkingLevel: o.thinkingLevel, retryDelayMs: o.retryDelayMs };
  const ask = async (c: ClipInput) => { calls++; return transcribe(o.fetch, cfg, c.bytes); };
  const wordIssues = (ch: ClipCheck) => ch.issues.filter(i => !i.startsWith("duration"));
  const size = (d: TextDiff) => d.extra.length + d.missing.length + d.substituted.length;
  const worker = async () => {
    for (let k = next++; k < o.clips.length; k = next++) {
      const c = o.clips[k], key = `${o.model}:${qaKey(c.bytes, c.text)}`;
      const entry = (o.cache[key] ??= { transcripts: [] });
      if (!entry.transcripts.length) entry.transcripts.push(await ask(c));
      const durationFlag = flags.get(c.id);
      let check = checkClip({ text: c.text, transcript: entry.transcripts[0], durationFlag });
      let note: string | undefined, warn = false, heard = entry.transcripts[0];
      let issues = check.issues;
      if (wordIssues(check).length) {
        if (entry.transcripts.length < 2) entry.transcripts.push(await ask(c));
        const second = checkClip({ text: c.text, transcript: entry.transcripts[1], durationFlag });
        if (!wordIssues(second).length) { note = "first transcription disagreed: " + wordIssues(check).join("; "); check = second; heard = entry.transcripts[1]; issues = check.issues; }
        else {
          if (size(second.diff) < size(check.diff)) { check = second; heard = entry.transcripts[1]; issues = check.issues; }
          if (judgeable(check.diff)) {
            if (!entry.judge) { calls++; entry.judge = await judge(o.fetch, cfg, c.bytes, c.text); }
            const dur = issues.filter(i => i.startsWith("duration"));
            if (entry.judge.match) { warn = true; note = `blind transcription: ${wordIssues(check).join("; ")}; guided check says it matches`; issues = dur; }
            else issues = [...issues, "guided check: " + (entry.judge.differences || "mismatch")];
          }
        }
      }
      const ok = issues.length === 0;
      results[k] = { id: c.id, voice: c.voice, text: c.text, seconds: timed[k].seconds, ok, issues, transcripts: entry.transcripts, ...(warn ? { warn } : {}), ...(note ? { note } : {}) };
      o.log?.(`[verify] ${ok ? (warn ? "warn" : "ok  ") : "FAIL"} ${c.id} "${heard}"${ok ? (warn ? "  " + note : "") : "  " + issues.join("; ")}`);
    }
  };
  await Promise.all(Array.from({ length: Math.max(1, o.concurrency ?? 4) }, worker));
  return { results, calls };
}
