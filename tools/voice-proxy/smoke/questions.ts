// 量測用的英文問題：用 ElevenLabs 合成一次存成 16 kHz WAV（smoke/out/q/，已忽略），之後重複使用。
// 每題附上預期的 speaker／intent／fact，用來檢查回話品質。

import { existsSync, mkdirSync, readFileSync, writeFileSync } from "node:fs";
import { createHash } from "node:crypto";
import { dirname, join } from "node:path";
import { fileURLToPath } from "node:url";
import { synthesize } from "../src/elevenlabs.ts";
import type { Addressee } from "../src/addressee.ts";
import type { FactKind } from "../src/facts.ts";

export interface Question {
  say: string;
  addressee: Addressee;
  speaker: string[];
  intent: string[];        // 可接受的 intent
  fact: FactKind | null;
  seat?: string;
}

export const QUESTIONS: Question[] = [
  { say: "Dot, how much is in the pot?", addressee: "rocco", speaker: ["dot"], intent: ["on_topic"], fact: "pot" },
  { say: "Are you bluffing right now?", addressee: "rocco", speaker: ["rocco"], intent: ["banter"], fact: null },
  { say: "How many chips does Vex have left?", addressee: "dot", speaker: ["dot", "vex"], intent: ["on_topic"], fact: "stack", seat: "vex" },
  { say: "How much do I need to call?", addressee: "pip", speaker: ["pip"], intent: ["on_topic"], fact: "to_call" },
  { say: "What's the weather like outside today?", addressee: "vex", speaker: ["vex"], intent: ["off_topic", "banter"], fact: null },
  { say: "Whose turn is it?", addressee: "dot", speaker: ["dot"], intent: ["on_topic"], fact: "turn" },
];

const here = dirname(fileURLToPath(import.meta.url));

export async function questionWav(q: Question, elevenKey: string): Promise<Uint8Array> {
  const dir = join(here, "out", "q");
  mkdirSync(dir, { recursive: true });
  const p = join(dir, createHash("sha1").update(q.say).digest("hex").slice(0, 10) + ".wav");
  if (!existsSync(p)) {
    const wav = await synthesize((u, i) => fetch(u, i), { apiKey: elevenKey, model: "eleven_flash_v2_5", sampleRate: 16000 }, "dot", q.say);
    writeFileSync(p, wav);
  }
  return new Uint8Array(readFileSync(p));
}

export const STATE = {
  phase: "betting", hand: 4, street: "flop", board: ["Ah", "7d", "2c"], blinds: { small: 5, big: 10 },
  pot: 60, toAct: "you", button: "rocco", youToCall: 20,
  seats: [
    { name: "you", stack: 950, bet: 0, status: "active" },
    { name: "pip", stack: 980, bet: 0, status: "active" },
    { name: "rocco", stack: 870, bet: 20, status: "active" },
    { name: "vex", stack: 1000, bet: 0, status: "active" },
  ],
  actions: [
    { street: "preflop", who: "rocco", action: "raise", amount: 30 },
    { street: "preflop", who: "you", action: "call", amount: 30 },
    { street: "flop", who: "rocco", action: "bet", amount: 20 },
  ],
};

export const DIRECTIVES = { rocco: { mood: "Confident", tell: "LeanBack", intensity: 0.6 }, pip: { mood: "Nervous", tell: "FidgetChips", intensity: 0.5 } };

export function stats(xs: number[]): { median: number; p90: number; n: number } {
  const s = [...xs].sort((a, b) => a - b);
  const q = (p: number) => s[Math.min(s.length - 1, Math.ceil(p * s.length) - 1)];
  return { median: Math.round(s.length % 2 ? s[(s.length - 1) / 2] : (s[s.length / 2 - 1] + s[s.length / 2]) / 2), p90: Math.round(q(0.9)), n: s.length };
}
