// bake／verify 共用：從環境變數或 .secrets/ 讀金鑰（本 checkout 或主 checkout；只在執行時讀，不印出）。

import { existsSync, readFileSync } from "node:fs";
import { execFileSync } from "node:child_process";
import { dirname, join } from "node:path";

function mainCheckout(checkout: string): string | null {
  try {
    const common = execFileSync("git", ["-C", checkout, "rev-parse", "--path-format=absolute", "--git-common-dir"], { encoding: "utf8" }).trim();
    return dirname(common);
  } catch { return null; }
}

export function secret(checkout: string, envName: string, file: string): string {
  const env = process.env[envName]?.trim();
  if (env) return env;
  const main = mainCheckout(checkout);
  for (const d of [process.env.VOICE_SECRETS_DIR, join(checkout, ".secrets"), main && join(main, ".secrets")]) {
    if (!d) continue;
    const p = join(d, file);
    if (existsSync(p)) { const v = readFileSync(p, "utf8").trim(); if (v) return v; }
  }
  throw new Error(`missing ${envName} (env or .secrets/${file})`);
}
