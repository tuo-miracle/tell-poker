// 誰來回話：逐字稿裡叫到的名字優先，其次是玩家盯著的角色，都沒有就是荷官 Dot。

export const CHARACTERS = ["pip", "rocco", "vex", "dot", "silas"] as const;
export type Character = (typeof CHARACTERS)[number];
export type Addressee = Character | "auto";

const NAME = /\b(pip|rocco|vex|silas|dot|dealer)\b/i;

export function isCharacter(x: unknown): x is Character {
  return typeof x === "string" && (CHARACTERS as readonly string[]).includes(x);
}

export function nameInTranscript(transcript: string): Character | null {
  const m = NAME.exec(transcript ?? "");
  if (!m) return null;
  const n = m[1].toLowerCase();
  return n === "dealer" ? "dot" : (n as Character);
}

export function resolveSpeaker(transcript: string, addressee: Addressee): Character {
  return nameInTranscript(transcript) ?? (isCharacter(addressee) ? addressee : "dot");
}
