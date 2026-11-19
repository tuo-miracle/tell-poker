// Gemini Live API（BidiGenerateContent WebSocket）：玩家講話時就邊送音訊，講完約 0.8 s 就有結構化回話。
// Live 的模型只支援 AUDIO 輸出（2026-10-13 實測 TEXT 會被拒），所以回話改成 function call：模型不開口，只呼叫 reply()，
// 參數就是原本的 JSON 欄位；之後一樣由 facts.ts 填數字、ElevenLabs 配音。

import { CHARACTERS, isCharacter, type Addressee } from "./addressee.ts";
import { systemPrompt } from "./characters.ts";
import { sanitizeState, sanitizeDirectives, type ModelReply, type Intent } from "./gemini.ts";
import type { FactKind, PublicState } from "./facts.ts";

const INTENTS: Intent[] = ["on_topic", "off_topic", "banter", "ignore"];
const FACTS: FactKind[] = ["pot", "stack", "to_call", "blinds", "turn"];

export const LIVE_HOST = "wss://generativelanguage.googleapis.com/ws/google.ai.generativelanguage.v1beta.GenerativeService.BidiGenerateContent";
/** Live 的 WebSocket 只能用 query 帶金鑰（不要記到 log）。 */
export const liveUrl = (apiKey: string) => `${LIVE_HOST}?key=${encodeURIComponent(apiKey)}`;

export const REPLY_FN = {
  name: "reply",
  description: "Deliver the character's reply. Always call this exactly once per player utterance instead of speaking.",
  parameters: {
    type: "OBJECT",
    properties: {
      transcript: { type: "STRING" },
      speaker: { type: "STRING", enum: [...CHARACTERS] },
      intent: { type: "STRING", enum: INTENTS },
      fact_kind: { type: "STRING", enum: ["none", ...FACTS] },
      fact_seat: { type: "STRING" },
      text: { type: "STRING" },
      emotion: { type: "STRING" },
    },
    required: ["transcript", "speaker", "intent", "fact_kind", "text", "emotion"],
  },
};

export interface LiveInput { addressee: Addressee; state: PublicState; directives: unknown }

/** thinkingBudget：預設 0（2026-10-13 實測 gemini-3.8-live 講完到 reply() 中位數 560 ms，有思考時 800 ms；thinkingLevel 不支援）；null＝不送。 */
export function buildLiveSetup(model: string, input: LiveInput, opts: { thinkingBudget?: number | null } = {}) {
  const sys = [
    systemPrompt(),
    "",
    "Never speak out loud. For every player utterance, call the reply function exactly once with these fields: transcript, speaker, intent, fact_kind (none when there is no fact) and fact_seat, text, emotion.",
    "",
    `Addressee: ${input.addressee} (the character the player is looking at)`,
    `Game state (public only): ${JSON.stringify(sanitizeState(input.state))}`,
    `Acting directives: ${JSON.stringify(sanitizeDirectives(input.directives))}`,
  ].join("\n");
  const budget = opts.thinkingBudget === undefined ? 0 : opts.thinkingBudget;
  const generationConfig: Record<string, unknown> = { responseModalities: ["AUDIO"] };
  if (budget !== null) generationConfig.thinkingConfig = { thinkingBudget: budget };
  return {
    setup: {
      model: "models/" + model,
      generationConfig,
      systemInstruction: { parts: [{ text: sys }] },
      tools: [{ functionDeclarations: [REPLY_FN] }],
      realtimeInputConfig: { automaticActivityDetection: { disabled: true } },
    },
  };
}

const str = (x: unknown, max = 40) => (typeof x === "string" ? x.slice(0, max) : "");

/** 伺服器訊息裡的 reply() 呼叫 → ModelReply；不是就回 null。 */
export function parseToolCall(msg: any): ModelReply | null {
  const call = (msg?.toolCall?.functionCalls ?? []).find((f: any) => f?.name === "reply");
  if (!call) return null;
  const a = call.args ?? {};
  const kind = FACTS.includes(a.fact_kind) ? (a.fact_kind as FactKind) : null;
  const seatRaw = str(a.fact_seat).toLowerCase();
  const seat = seatRaw && seatRaw !== "null" && seatRaw !== "none" ? seatRaw : null;
  return {
    speaker: isCharacter(a.speaker) ? a.speaker : null,
    transcript: str(a.transcript, 400),
    text: str(a.text, 300),
    intent: INTENTS.includes(a.intent) ? a.intent : "on_topic",
    fact: kind ? { kind, seat } : null,
    emotion: str(a.emotion, 20) || "neutral",
  };
}
