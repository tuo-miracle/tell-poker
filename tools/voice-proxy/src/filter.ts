// PG 過濾：很粗的黑名單，命中就換成罐頭台詞（模型本身也被要求 PG）。

const BLOCK = /\b(fuck\w*|shit\w*|bitch\w*|cunt\w*|asshole\w*|dick\w*|bastard\w*|whore\w*|slut\w*|fag\w*|nigg\w*|retard\w*|damn)\b/i;

export function isPg(text: string): boolean {
  return !BLOCK.test(text);
}
