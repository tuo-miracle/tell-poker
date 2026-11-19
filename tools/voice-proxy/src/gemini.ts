// Gemini generateContent：一次完成「聽懂玩家的話」與「角色回話」，輸出結構化 JSON。

import { CHARACTERS, isCharacter, type Addressee, type Character } from "./addressee.ts";
import type { Fact, FactKind, PublicState } from "./facts.ts";
import { systemPrompt } from "./characters.ts";

export type Intent = "on_topic" | "off_topic" | "banter" | "ignore";
const INTENTS: Intent[] = ["on_topic", "off_topic", "banter", "ignore"];
const FACTS: FactKind[] = ["pot", "stack", "to_call", "blinds", "turn"];

export type FetchFn = (url: string | URL | Request, init?: RequestInit) => Promise<Response>;

export interface Directive { mood?: string; tell?: string; intensity?: number }
export interface TalkInput {
  audioBase64: string;
  addressee: Addressee;
  state: PublicState;
  directives: Partial<Record<Character, Directive>>;
}
export interface ModelReply {
  speaker: Character | null;
  transcript: string;
  text: string;
  intent: Intent;
  fact: Fact | null;
  emotion: string;
}

const CARD = /^[2-9TJQKA][shdc]$/;
const str = (x: unknown, max = 40) => (typeof x === "string" ? x.slice(0, max) : "");
const num = (x: unknown) => (typeof x === "number" && Number.isFinite(x) ? x : 0);

/** 白名單複製公開狀態：多傳的欄位（例如底牌）一律丟掉。 */
export function sanitizeState(s: any): PublicState {
  s = s ?? {};
  return {
    phase: str(s.phase), hand: num(s.hand), street: str(s.street),
    board: Array.isArray(s.board) ? s.board.filter((c: unknown) => typeof c === "string" && CARD.test(c)).slice(0, 5) : [],
    blinds: { small: num(s.blinds?.small), big: num(s.blinds?.big) },
    pot: num(s.pot), toAct: s.toAct ? str(s.toAct) : null, button: s.button ? str(s.button) : null,
    youToCall: num(s.youToCall),
    seats: Array.isArray(s.seats) ? s.seats.slice(0, 10).map((x: any) => ({ name: str(x?.name), stack: num(x?.stack), bet: num(x?.bet), status: str(x?.status) })) : [],
    actions: Array.isArray(s.actions) ? s.actions.slice(-40).map((a: any) => ({ street: str(a?.street), who: str(a?.who), action: str(a?.action), amount: num(a?.amount) })) : [],
  };
}

export function sanitizeDirectives(d: any): Partial<Record<Character, Directive>> {
  const out: Partial<Record<Character, Directive>> = {};
  if (!d || typeof d !== "object") return out;
  for (const [k, v] of Object.entries(d)) {
    if (!isCharacter(k) || !v || typeof v !== "object") continue;
    const x = v as any;
    out[k] = { mood: str(x.mood, 20), tell: str(x.tell, 20), intensity: Math.max(0, Math.min(1, num(x.intensity))) };
  }
  return out;
}

export const RESPONSE_SCHEMA = {
  type: "OBJECT",
  properties: {
    transcript: { type: "STRING" },
    speaker: { type: "STRING", enum: [...CHARACTERS] },
    intent: { type: "STRING", enum: INTENTS },
    fact: {
      type: "OBJECT", nullable: true,
      properties: { kind: { type: "STRING", enum: FACTS }, seat: { type: "STRING", nullable: true } },
      required: ["kind"],
    },
    text: { type: "STRING" },
    emotion: { type: "STRING" },
  },
  required: ["transcript", "speaker", "intent", "fact", "text", "emotion"],
  propertyOrdering: ["transcript", "speaker", "intent", "fact", "text", "emotion"],
};

export function buildGeminiRequest(input: TalkInput, opts: { thinkingLevel?: string } = {}) {
  const state = sanitizeState(input.state);
  const ctx = [
    `Addressee: ${input.addressee} (the character the player is looking at)`,
    `Game state (public only): ${JSON.stringify(state)}`,
    `Acting directives: ${JSON.stringify(sanitizeDirectives(input.directives))}`,
  ].join("\n");
  const generationConfig: Record<string, unknown> = {
    responseMimeType: "application/json",
    responseSchema: RESPONSE_SCHEMA,
    temperature: 0.8,
  };
  if (opts.thinkingLevel) generationConfig.thinkingConfig = { thinkingLevel: opts.thinkingLevel };
  return {
    systemInstruction: { parts: [{ text: systemPrompt() }] },
    contents: [{ role: "user", parts: [{ inlineData: { mimeType: "audio/wav", data: input.audioBase64 } }, { text: ctx }] }],
    generationConfig,
  };
}

export function parseGeminiResponse(json: any): ModelReply {
  const cand = json?.candidates?.[0];
  if (!cand) throw new Error("gemini: no candidate" + (json?.promptFeedback?.blockReason ? ` (${json.promptFeedback.blockReason})` : ""));
  const text = (cand.content?.parts ?? []).filter((p: any) => !p.thought && typeof p.text === "string").map((p: any) => p.text).join("");
  let o: any;
  try { o = JSON.parse(text.replace(/^\s*```(?:json)?|```\s*$/g, "")); }
  catch { throw new Error("gemini: reply is not JSON"); }
  const fk = o?.fact?.kind;
  return {
    speaker: isCharacter(o?.speaker) ? o.speaker : null,
    transcript: str(o?.transcript, 400),
    text: str(o?.text, 300),
    intent: INTENTS.includes(o?.intent) ? o.intent : "on_topic",
    fact: FACTS.includes(fk) ? { kind: fk, seat: typeof o.fact.seat === "string" ? o.fact.seat.toLowerCase() : null } : null,
    emotion: str(o?.emotion, 20) || "neutral",
  };
}

export async function callGemini(fetchFn: FetchFn, cfg: { apiKey: string; model: string; thinkingLevel?: string }, input: TalkInput): Promise<ModelReply> {
  const url = `https://generativelanguage.googleapis.com/v1beta/models/${cfg.model}:generateContent`;
  const res = await fetchFn(url, {
    method: "POST",
    headers: { "content-type": "application/json", "x-goog-api-key": cfg.apiKey },
    body: JSON.stringify(buildGeminiRequest(input, { thinkingLevel: cfg.thinkingLevel })),
  });
  if (!res.ok) throw new Error(`gemini http ${res.status}: ${(await res.text()).slice(0, 200)}`);
  return parseGeminiResponse(await res.json());
}
