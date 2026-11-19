// 預錄台詞：lines.json（Unity 的 SpeechInventory 匯出）→ ElevenLabs → Assets/_Project/Resources/Speech/<voice>/<id>.mp3 ＋ manifest.json。
// voice 是聲音鍵：座位聲音（pip／rocco／vex／dot）或寫實角色的聲音（marcus…，id 是「marcus:pip.value.01」＝用 Marcus 的聲音說 Pip 座位的那句）。
// 可重跑：manifest 記每句的 hash（聲音 id＋模型＋格式＋原文），只有改過或檔案不見的句子才重新合成，刪掉的句子連檔案一起刪。

import { createHash } from "node:crypto";
import type { FetchFn } from "../src/gemini.ts";
import { isVoice, VOICE_IDS, type Voice } from "../src/elevenlabs.ts";

/** 22.05 kHz、32 kbps 單聲道 mp3（Unity 匯入後在 Android 轉成 Vorbis）。 */
export const FORMAT = "mp3_22050_32";

export interface Line { id: string; voice: Voice; text: string }
export interface ManifestLine { id: string; voice: Voice; voiceId: string; model: string; text: string; file: string; hash: string; bytes: number }
export interface Manifest { model: string; format: string; lines: ManifestLine[] }

/** Resources 底下的路徑（不含副檔名）；檔名裡的點與其他符號換成底線（Unity 的資產名稱不要有點）。 */
export function fileFor(l: { id: string; voice: string }): string {
  return `Speech/${l.voice}/${l.id.replace(/[^A-Za-z0-9_-]/g, "_")}`;
}

export function lineHash(voiceId: string, model: string, format: string, text: string): string {
  return createHash("sha256").update(`${voiceId}|${model}|${format}|${text}`).digest("hex").slice(0, 16);
}

/** byVoice：這次要合成的句數與字元數，依聲音分（--count 印出來估額度）。 */
export interface BakePlan { keep: ManifestLine[]; bake: Line[]; remove: ManifestLine[]; chars: number; totalChars: number; byVoice: Partial<Record<Voice, { lines: number; chars: number }>> }

function voiceId(voice: string): string {
  if (!isVoice(voice)) throw new Error(`unknown voice "${voice}" in lines.json (add it to VOICES/CAST_VOICES in src/elevenlabs.ts)`);
  return VOICE_IDS[voice];
}

/** only：只重做這些 id；其他句子只要舊檔還在就照舊保留（即使模型不同）。 */
export function planBake(lines: Line[], manifest: Manifest | null, exists: (file: string) => boolean, model: string, format = FORMAT, only?: Set<string>): BakePlan {
  const old = new Map((manifest?.lines ?? []).map(m => [m.id, m] as const));
  const keep: ManifestLine[] = [], bake: Line[] = [];
  const byVoice: BakePlan["byVoice"] = {};
  let chars = 0, totalChars = 0;
  for (const l of lines) {
    totalChars += l.text.length;
    const m = old.get(l.id);
    const h = lineHash(voiceId(l.voice), model, format, l.text);
    const current = m && m.file === fileFor(l) && exists(m.file) && m.text === l.text;
    if (current && (m.hash === h || (only && !only.has(l.id)))) keep.push(m);
    else {
      bake.push(l); chars += l.text.length;
      const v = (byVoice[l.voice] ??= { lines: 0, chars: 0 });
      v.lines++; v.chars += l.text.length;
    }
  }
  const ids = new Set(lines.map(l => l.id));
  const remove = (manifest?.lines ?? []).filter(m => !ids.has(m.id));
  return { keep, bake, remove, chars, totalChars, byVoice };
}

export interface BakeOptions {
  lines: Line[];
  manifest: Manifest | null;
  fetch: FetchFn;
  apiKey: string;
  model: string;
  format?: string;
  /** 寫入／刪除 Resources 路徑（不含副檔名，呼叫端加 .mp3）。 */
  write: (file: string, bytes: Uint8Array) => void;
  remove: (file: string) => void;
  exists: (file: string) => boolean;
  log: (line: string) => void;
  /** 這次最多合成多少字元（超過就整個不做）。 */
  maxChars?: number;
  concurrency?: number;
  retryDelayMs?: number;
  /** 聽檢（bake.ts 預設接 qa.ts 的 verifyClips）：一次給一批剛合成的音檔，回每句 ok／issues。不給就不檢查。 */
  verify?: (batch: { line: Line; bytes: Uint8Array }[]) => Promise<{ id: string; ok: boolean; issues: string[] }[]>;
  /** 只重做這些 id（見 planBake）。 */
  only?: Set<string>;
  /** 每句最多合成幾次（含第一次），預設 3。 */
  attempts?: number;
  /** 聽檢沒過也寫進 manifest（最後一次的音檔）。 */
  force?: boolean;
}

export interface BakeFailure { id: string; text: string; issues: string[] }

export async function synthesizeMp3(fetchFn: FetchFn, apiKey: string, model: string, format: string, voice: Voice, text: string, retryDelayMs = 1500): Promise<Uint8Array> {
  const url = `https://api.elevenlabs.io/v1/text-to-speech/${voiceId(voice)}?output_format=${format}`;
  for (let attempt = 0; ; attempt++) {
    const res = await fetchFn(url, {
      method: "POST",
      headers: { "content-type": "application/json", "xi-api-key": apiKey, accept: "audio/mpeg" },
      body: JSON.stringify({ text, model_id: model }),
    });
    if (res.ok) return new Uint8Array(await res.arrayBuffer());
    const body = (await res.text()).slice(0, 200);
    if ((res.status === 429 || res.status >= 500) && attempt < 6) { await new Promise(r => setTimeout(r, retryDelayMs * (attempt + 1))); continue; }
    throw new Error(`elevenlabs http ${res.status}: ${body}`);
  }
}

export async function bakeLines(o: BakeOptions): Promise<{ manifest: Manifest; baked: number; chars: number; removed: number; failed: BakeFailure[] }> {
  const format = o.format ?? FORMAT;
  const plan = planBake(o.lines, o.manifest, o.exists, o.model, format, o.only);
  if (o.maxChars !== undefined && plan.chars > o.maxChars)
    throw new Error(`character budget: this run needs ${plan.chars} characters (> ${o.maxChars}); nothing was generated`);
  const done = new Map(plan.keep.map(m => [m.id, m] as const));
  const accept = (l: Line, bytes: Uint8Array) => {
    const file = fileFor(l);
    o.write(file, bytes);
    done.set(l.id, { id: l.id, voice: l.voice, voiceId: voiceId(l.voice), model: o.model, text: l.text, file, hash: lineHash(voiceId(l.voice), o.model, format, l.text), bytes: bytes.length });
  };
  let chars = 0, pending = [...plan.bake], quota = "";
  const last = new Map<string, { bytes: Uint8Array; issues: string[] }>();
  const attempts = Math.max(1, o.attempts ?? 3);
  for (let attempt = 1; attempt <= attempts && pending.length && !quota; attempt++) {
    const queue = [...pending], batch: { line: Line; bytes: Uint8Array }[] = [];
    const synthFailed: Line[] = [];
    const worker = async () => {
      for (let l = queue.shift(); l; l = queue.shift()) {
        let bytes: Uint8Array;
        // 額度用完就不要再打（每句都會 401，重試也沒用）
        if (quota) { last.set(l.id, { bytes: new Uint8Array(), issues: ["synthesis skipped: " + quota] }); synthFailed.push(l); continue; }
        try { bytes = await synthesizeMp3(o.fetch, o.apiKey, o.model, format, l.voice, l.text, o.retryDelayMs); }
        catch (e) {
          // 一句失敗（額度、併發上限）不要讓整批已合成的音檔白費
          last.set(l.id, { bytes: new Uint8Array(), issues: ["synthesis: " + (e as Error).message] });
          if (/quota_exceeded/.test((e as Error).message)) quota = "ElevenLabs quota exceeded";
          synthFailed.push(l); o.log(`[bake] ERROR ${l.id}: ${(e as Error).message}`); continue;
        }
        chars += l.text.length;
        batch.push({ line: l, bytes });
        o.log(`[bake] ${l.id} (${l.voice}) ${bytes.length} B "${l.text}"${attempt > 1 ? ` (attempt ${attempt})` : ""}`);
      }
    };
    await Promise.all(Array.from({ length: Math.max(1, o.concurrency ?? 4) }, worker));
    const verdicts = o.verify && batch.length ? new Map((await o.verify(batch)).map(v => [v.id, v] as const)) : null;
    pending = [...synthFailed];
    for (const b of batch) {
      const v = verdicts?.get(b.line.id);
      if (!o.verify || v?.ok) { accept(b.line, b.bytes); continue; }
      last.set(b.line.id, { bytes: b.bytes, issues: v?.issues ?? ["no verdict"] });
      pending.push(b.line);
    }
  }
  const failed: BakeFailure[] = [];
  const old = new Map((o.manifest?.lines ?? []).map(m => [m.id, m] as const));
  for (const l of pending) {
    const { bytes, issues } = last.get(l.id)!;
    failed.push({ id: l.id, text: l.text, issues });
    if (o.force && bytes.length) { accept(l, bytes); o.log(`[bake] FORCED ${l.id} despite: ${issues.join("; ")}`); continue; }
    const prev = old.get(l.id);
    // 同一句的舊音檔（例如換模型前那版）還在就先留著；原文改過的舊檔對不上，不留
    if (prev && prev.text === l.text && o.exists(prev.file)) { done.set(l.id, prev); o.log(`[bake] REJECTED ${l.id}, kept previous ${prev.model} clip: ${issues.join("; ")}`); }
    else o.log(`[bake] REJECTED ${l.id}, not in manifest: ${issues.join("; ")}`);
  }
  for (const m of plan.remove) { o.remove(m.file); o.log(`[bake] removed ${m.id}`); }
  const manifest: Manifest = { model: o.model, format, lines: o.lines.map(l => done.get(l.id)).filter((m): m is ManifestLine => !!m) };
  return { manifest, baked: plan.bake.length - failed.filter(f => !(o.force && last.get(f.id)!.bytes.length)).length, chars, removed: plan.remove.length, failed };
}
