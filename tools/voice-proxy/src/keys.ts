// 只在 Node 本機用：從環境變數或 .secrets 目錄讀 API 金鑰（Workers 上改用 wrangler secret）。不印出金鑰。

import { existsSync, readFileSync } from "node:fs";
import { join } from "node:path";

export interface Keys { gemini: string; eleven: string; source: { gemini: string; eleven: string } }

function pick(envVal: string | undefined, envName: string, file: string, dirs: string[]): [string, string] {
  if (envVal && envVal.trim()) return [envVal.trim(), "env " + envName];
  for (const d of dirs) {
    const p = join(d, file);
    if (existsSync(p)) {
      const v = readFileSync(p, "utf8").trim();
      if (v) return [v, p];
    }
  }
  throw new Error(`missing ${envName}: set the env var or put ${file} in one of: ${dirs.join(", ")}`);
}

export function loadKeys(env: Record<string, string | undefined>, dirs: string[]): Keys {
  const [gemini, gs] = pick(env.GEMINI_API_KEY, "GEMINI_API_KEY", "gemini.key", dirs);
  const [eleven, es] = pick(env.ELEVENLABS_API_KEY, "ELEVENLABS_API_KEY", "elevenlabs.key", dirs);
  return { gemini, eleven, source: { gemini: gs, eleven: es } };
}
