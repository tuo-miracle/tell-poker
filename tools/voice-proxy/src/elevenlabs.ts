// ElevenLabs 配音：要裸 PCM（16-bit mono），包成 WAV 回給 Unity（Android 上不用解 mp3）。

import { isCharacter, type Character } from "./addressee.ts";
import type { FetchFn } from "./gemini.ts";
import { pcm16ToWav } from "./wav.ts";

/** 2026-10-13 定案（docs/specs/2026-10-13-voice-chat-design.md §6）。 */
export const VOICES: Record<Character, string> = {
  pip: "cgSgspJ2msm6clMCkdW9",   // Jessica
  rocco: "N2lVS1w4EtoT3dr4eOWO", // Callum
  vex: "pFZP5JQG7iQjIQuC4Bku",   // Lily
  dot: "EXAVITQu4vr4xnSDxMaL",   // Sarah (2026-10-30: dealer is a woman)
  silas: "pqHfZKP75CvOlQylNhV4", // Bill (2026-11-02: Veteran seat; Reginald uses this voice too)
};

/**
 * 寫實角色自己的聲音（2026-11-02，docs/specs/2026-10-13-voice-chat-design.md §6.1）：台詞依座位個性，聲音依角色。
 * 沿用座位聲音的角色不在這裡：Hank＝"rocco"（Callum）、Reginald＝"silas"（Bill，老手座位），坐原本的座位時直接用現有音檔。
 */
export const CAST_VOICES = {
  marcus: "cjVigY5qzO86Huf0OWal",   // Eric
  jace: "TX3LPaxmHKxFdv7VOQHJ",     // Liam
  erik: "nPczCjzI2devNBz1zQrb",     // Brian
} as const;

/** 聲音鍵：座位聲音（pip／rocco／vex／dot／silas）或寫實角色的聲音；也是預錄音檔的資料夾名。 */
export type Voice = Character | keyof typeof CAST_VOICES;
export const VOICE_IDS: Record<Voice, string> = { ...VOICES, ...CAST_VOICES };

export function isVoice(x: unknown): x is Voice {
  return typeof x === "string" && Object.prototype.hasOwnProperty.call(VOICE_IDS, x);
}

/** Unity 送來的「座位 → 聲音」（寫實角色坐在哪）；不認得的座位或聲音丟掉。 */
export function parseCast(x: unknown): Partial<Record<Character, Voice>> {
  const out: Partial<Record<Character, Voice>> = {};
  if (!x || typeof x !== "object" || Array.isArray(x)) return out;
  for (const [k, v] of Object.entries(x as Record<string, unknown>)) if (isCharacter(k) && isVoice(v)) out[k] = v;
  return out;
}

/** 這個座位回話用哪個聲音（沒給就是座位原本的聲音）。 */
export function castVoice(speaker: Character, cast: Partial<Record<Character, Voice>>): Voice {
  return cast[speaker] ?? speaker;
}

export interface TtsConfig { apiKey: string; model: string; sampleRate: 16000 | 22050 | 24000 | number }

export async function synthesize(fetchFn: FetchFn, cfg: TtsConfig, voice: Voice, text: string): Promise<Uint8Array> {
  const url = `https://api.elevenlabs.io/v1/text-to-speech/${VOICE_IDS[voice]}?output_format=pcm_${cfg.sampleRate}`;
  const res = await fetchFn(url, {
    method: "POST",
    headers: { "content-type": "application/json", "xi-api-key": cfg.apiKey, accept: "audio/pcm" },
    body: JSON.stringify({ text, model_id: cfg.model }),
  });
  if (!res.ok) throw new Error(`elevenlabs http ${res.status}: ${(await res.text()).slice(0, 200)}`);
  const pcm = new Uint8Array(await res.arrayBuffer());
  return pcm16ToWav(pcm.length & 1 ? pcm.subarray(0, pcm.length - 1) : pcm, cfg.sampleRate);
}

/**
 * 串流配音（/stream）：PCM 一到就交給 onChunk（每塊都是偶數位元組＝整數個 16-bit 樣本）。
 * 2026-10-13 實測第一塊約 160 ms（整段要 270 ms，長句 400 ms）。還沒拿到任何音訊就失敗時丟例外，讓呼叫端改用整段。
 */
export async function synthesizeStream(fetchFn: FetchFn, cfg: TtsConfig, voice: Voice, text: string, onChunk: (pcm: Uint8Array) => void): Promise<void> {
  const url = `https://api.elevenlabs.io/v1/text-to-speech/${VOICE_IDS[voice]}/stream?output_format=pcm_${cfg.sampleRate}`;
  const res = await fetchFn(url, {
    method: "POST",
    headers: { "content-type": "application/json", "xi-api-key": cfg.apiKey, accept: "audio/pcm" },
    body: JSON.stringify({ text, model_id: cfg.model }),
  });
  if (!res.ok || !res.body) throw new Error(`elevenlabs http ${res.status}: ${(await res.text()).slice(0, 200)}`);
  const reader = res.body.getReader();
  let carry: number | null = null;
  for (;;) {
    const { value, done } = await reader.read();
    if (done) break;
    if (!value || !value.length) continue;
    let b = value;
    if (carry !== null) { const x = new Uint8Array(b.length + 1); x[0] = carry; x.set(b, 1); b = x; carry = null; }
    if (b.length & 1) { carry = b[b.length - 1]; b = b.subarray(0, b.length - 1); }
    if (b.length) onChunk(b);
  }
}
