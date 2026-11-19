// Node 專用：資料目錄（docker 裡是掛載的 /data，見 deploy/vr-poker-api/docker-compose.yml）上的小檔案存取。
// Workers 沒有檔案系統：那裡不建這些（TTS 用量只在記憶體、/telemetry 回 501）。

import { appendFileSync, existsSync, mkdirSync, readdirSync, readFileSync, renameSync, rmSync, statSync, writeFileSync } from "node:fs";
import { dirname, join } from "node:path";
import type { BudgetStore } from "./ttsbudget.ts";
import type { AppendResult, DayInfo, SessionInfo, SessionMeta, TelemetryStore } from "./telemetry.ts";

/** 一個 JSON 檔：讀不到回 null；寫入先寫暫存檔再改名（寫到一半當機不會留下壞檔）。 */
export function fileStore(path: string): BudgetStore {
  return {
    load() {
      try { return readFileSync(path, "utf8"); } catch { return null; }
    },
    save(text: string) {
      mkdirSync(dirname(path), { recursive: true });
      const tmp = path + ".tmp";
      writeFileSync(tmp, text);
      renameSync(tmp, path);
    },
  };
}

const DAY_DIR = /^\d{4}-\d{2}-\d{2}$/;
const FILE_RE = /^([0-9a-f]{6})_([0-9a-f]{8,32})\.ndjson$/;

/**
 * 遙測檔：<root>/<UTC 日>/<裝置雜湊 6 碼>_<session>.ndjson。每次寫入前掃一次目錄算總量（檔案不多，幾十個日子 × 幾個 session）。
 * 超過總量先刪最舊的日子（今天不刪）；session 檔滿了寫一行 {"type":"capped"} 後就拒收。
 */
export class FsTelemetryStore implements TelemetryStore {
  private readonly capped = new Set<string>();
  private readonly root: string;
  private readonly o: { sessionMaxBytes: number; totalMaxBytes: number };

  constructor(root: string, o: { sessionMaxBytes: number; totalMaxBytes: number }) {
    this.root = root;
    this.o = o;
  }

  private dayDirs(): string[] {
    try { return readdirSync(this.root).filter(d => DAY_DIR.test(d)).sort(); } catch { return []; }
  }

  private dayBytes(day: string): { files: number; bytes: number } {
    let files = 0, bytes = 0;
    try {
      for (const f of readdirSync(join(this.root, day))) {
        try { const st = statSync(join(this.root, day, f)); if (st.isFile()) { files++; bytes += st.size; } } catch {}
      }
    } catch {}
    return { files, bytes };
  }

  totalBytes(): number {
    return this.dayDirs().reduce((n, d) => n + this.dayBytes(d).bytes, 0);
  }

  append(meta: SessionMeta, lines: string[]): AppendResult {
    const dir = join(this.root, meta.day);
    const file = join(dir, `${meta.dev}_${meta.session}.ndjson`);
    const created = !existsSync(file);
    const header = created ? JSON.stringify({ type: "session", session: meta.session, build: meta.build, dev: meta.dev, day: meta.day, received: meta.received }) + "\n" : "";
    const data = header + lines.join("\n") + "\n";
    const size = Buffer.byteLength(data);

    const current = created ? 0 : statSync(file).size;
    if (current + size > this.o.sessionMaxBytes) {
      if (!created && !this.capped.has(file)) {
        this.capped.add(file);
        appendFileSync(file, JSON.stringify({ type: "capped", received: meta.received, maxBytes: this.o.sessionMaxBytes }) + "\n");
      }
      return { ok: false, code: "session_full" };
    }

    let total = this.totalBytes();
    while (total + size > this.o.totalMaxBytes) {
      const oldest = this.dayDirs().find(d => d !== meta.day);
      if (!oldest) return { ok: false, code: "storage_full" };
      rmSync(join(this.root, oldest), { recursive: true, force: true });
      total = this.totalBytes();
    }
    mkdirSync(dir, { recursive: true });
    appendFileSync(file, data);
    return { ok: true, created };
  }

  sessions(day: string): SessionInfo[] {
    if (!DAY_DIR.test(day)) return [];
    let names: string[];
    try { names = readdirSync(join(this.root, day)); } catch { return []; }
    const out: SessionInfo[] = [];
    for (const f of names.sort()) {
      const m = FILE_RE.exec(f);
      if (!m) continue;
      try {
        const st = statSync(join(this.root, day, f));
        out.push({ file: f, dev: m[1], session: m[2], bytes: st.size, modified: st.mtime.toISOString() });
      } catch {}
    }
    return out;
  }

  days(): DayInfo[] {
    return this.dayDirs().map(day => { const b = this.dayBytes(day); return { day, sessions: b.files, bytes: b.bytes }; });
  }
}
