// 事實數字：模型只寫佔位符（{pot}、{stack:rocco}…），這裡用牌局的真實數字填入；
// 回話裡出現狀態裡沒有的數字就修掉（有 fact 就換成模板句，沒有就刪掉那一句）。

import type { Character } from "./addressee.ts";

export type FactKind = "pot" | "stack" | "to_call" | "blinds" | "turn";
export interface Fact { kind: FactKind; seat?: string | null }

export interface SeatPublic { name: string; stack: number; bet: number; status: string }
export interface ActionPublic { street: string; who: string; action: string; amount: number }
export interface PublicState {
  phase: string;
  hand: number;
  street: string;
  board: string[];
  blinds: { small: number; big: number };
  pot: number;
  toAct: string | null;
  button: string | null;
  youToCall: number;
  seats: SeatPublic[];
  actions: ActionPublic[];
}

const SMALL_OK = 10; // 10 以下（人數、牌點）不檢查

const cap = (s: string) => (s ? s[0].toUpperCase() + s.slice(1) : s);

function seatOf(state: PublicState, name: string | null | undefined): SeatPublic | undefined {
  if (!name) return undefined;
  const n = name.toLowerCase();
  return state.seats.find(s => s.name.toLowerCase() === n);
}

export function numbersIn(text: string): number[] {
  const out: number[] = [];
  for (const m of text.matchAll(/\d[\d,]*(?:\.\d+)?/g)) {
    const v = Number(m[0].replace(/,/g, ""));
    if (Number.isFinite(v)) out.push(v);
  }
  return out;
}

function allowedNumbers(state: PublicState): Set<number> {
  const s = new Set<number>();
  const walk = (x: unknown) => {
    if (typeof x === "number") s.add(x);
    else if (Array.isArray(x)) x.forEach(walk);
    else if (x && typeof x === "object") Object.values(x).forEach(walk);
  };
  walk(state);
  return s;
}

function expectedNumbers(fact: Fact, state: PublicState): number[] | null {
  switch (fact.kind) {
    case "pot": return [state.pot];
    case "stack": { const s = seatOf(state, fact.seat); return s ? [s.stack] : null; }
    case "to_call": return state.youToCall > 0 ? [state.youToCall] : [];
    case "blinds": return [state.blinds.small, state.blinds.big];
    case "turn": return [];
  }
  return null;
}

export function templateFor(fact: Fact, state: PublicState, speaker: Character): string | null {
  switch (fact.kind) {
    case "pot": return `The pot is ${state.pot}.`;
    case "stack": {
      const s = seatOf(state, fact.seat);
      if (!s) return null;
      if (s.name === speaker) return `I have ${s.stack}.`;
      if (s.name === "you") return `You have ${s.stack}.`;
      return `${cap(s.name)} has ${s.stack}.`;
    }
    case "to_call": return state.youToCall > 0 ? `You need ${state.youToCall} to call.` : "Nothing to call. You can check.";
    case "blinds": return `Blinds are ${state.blinds.small} and ${state.blinds.big}.`;
    case "turn":
      if (!state.toAct) return "Nobody's up right now.";
      if (state.toAct === "you") return "It's your turn.";
      if (state.toAct === speaker) return "It's my turn.";
      return `It's ${cap(state.toAct)}'s turn.`;
  }
  return null;
}

function fill(text: string, state: PublicState): { text: string; unknown: boolean } {
  let unknown = false;
  const out = text.replace(/\{([a-z_]+)(?::([a-z]+))?\}/gi, (m: string, key: string, arg?: string) => {
    switch (key.toLowerCase()) {
      case "pot": return String(state.pot);
      case "to_call": return String(state.youToCall);
      case "sb": return String(state.blinds.small);
      case "bb": return String(state.blinds.big);
      case "blinds": return `${state.blinds.small} and ${state.blinds.big}`;
      case "turn": return state.toAct === "you" ? "you" : cap(state.toAct ?? "nobody");
      case "stack": { const s = seatOf(state, arg); if (s) return String(s.stack); break; }
      case "bet": { const s = seatOf(state, arg); if (s) return String(s.bet); break; }
    }
    unknown = true;
    return m; // 留著，後面整句刪掉

  });
  return { text: out, unknown };
}

export function applyFacts(reply: { text: string; fact: Fact | null }, state: PublicState, speaker: Character): { text: string; repaired: boolean } {
  const filled = fill(reply.text ?? "", state);
  let text = filled.text.replace(/\s+/g, " ").trim();
  const allowed = allowedNumbers(state);
  const bad = (n: number) => n > SMALL_OK && !allowed.has(n);
  const hasBad = filled.unknown || numbersIn(text).some(bad);

  if (reply.fact) {
    const expected = expectedNumbers(reply.fact, state);
    const present = numbersIn(text);
    const missing = expected === null || expected.some(n => !present.includes(n));
    if (hasBad || missing) {
      const t = templateFor(reply.fact, state, speaker);
      if (t) return { text: t, repaired: true };
    } else return { text, repaired: false };
  }
  if (!hasBad) return { text, repaired: false };
  const kept = text.split(/(?<=[.!?])\s+/).filter(s => !numbersIn(s).some(bad) && !/\{|\}/.test(s));
  return { text: kept.join(" ").trim(), repaired: true };
}
