// 開發版遙測（只有測試版 App 會送；正式版不送，見 tools/build-quest.sh 的 TELL_RELEASE）：
// POST /telemetry：一批 NDJSON（每行一個 JSON 物件＝一行 Unity log，已在 App 端過濾成白名單 tag），可 gzip。
//   header：x-voice-token（App 通行碼）、x-voice-device（安裝 id，必填，格式同限流）、x-tell-session（每次開 App 隨機 8–32 hex）、
//           x-tell-build（版本字串，可省略）、content-encoding: gzip（可省略）。
//   存檔：<資料目錄>/telemetry/<UTC 日>/<裝置雜湊 6 碼>_<session>.ndjson（新檔第一行是 {"type":"session",...}）；不存原始裝置 id。
//   上限：每次請求 ≤ 256 KB（壓縮前後都算）、每行 ≤ 4096 字、每個 session 檔 2 MB、全部 200 MB（超過先刪最舊的日子）。
// GET /telemetry/sessions?day=YYYY-MM-DD：那天的 session 清單與大小；不帶 day 列出各日。
// 只有 Node（有掛載的資料目錄）才開；Workers 回 501。

import { deviceTag, isValidDevice, limitKey, LIMIT_MESSAGES, type RateLimiter } from "./ratelimit.ts";

export const MAX_REQUEST_BYTES = 256 * 1024;
export const MAX_LINE_CHARS = 4096;

export interface SessionInfo { file: string; dev: string; session: string; bytes: number; modified: string }
export interface DayInfo { day: string; sessions: number; bytes: number }
export type AppendResult = { ok: true; created: boolean } | { ok: false; code: "session_full" | "storage_full" };

export interface SessionMeta { day: string; dev: string; session: string; build: string; received: string }

export interface TelemetryStore {
  append(meta: SessionMeta, lines: string[]): AppendResult;
  sessions(day: string): SessionInfo[];
  days(): DayInfo[];
  totalBytes(): number;
}

export interface TelemetryDeps {
  store?: TelemetryStore;
  limiter?: RateLimiter;
  wallNow: () => number;
  log: (line: string) => void;
}

const SESSION_RE = /^[0-9a-f]{8,32}$/;
const BUILD_RE = /^[A-Za-z0-9._+-]{1,40}$/;
const DAY_RE = /^\d{4}-\d{2}-\d{2}$/;
const CONTROL_RE = /[\u0000-\u0008\u000b-\u001f\u007f]/;

function json(status: number, body: unknown): Response {
  return new Response(JSON.stringify(body), { status, headers: { "content-type": "application/json" } });
}

/** gzip 解壓，超過 max 就停（防壓縮炸彈）。 */
async function gunzipLimited(data: Uint8Array, max: number): Promise<Uint8Array | "too_large" | "bad"> {
  try {
    const reader = new Blob([data as BlobPart]).stream().pipeThrough(new DecompressionStream("gzip")).getReader();
    const parts: Uint8Array[] = [];
    let total = 0;
    for (;;) {
      const { value, done } = await reader.read();
      if (done) break;
      total += value.length;
      if (total > max) { await reader.cancel().catch(() => {}); return "too_large"; }
      parts.push(value);
    }
    const out = new Uint8Array(total);
    let at = 0;
    for (const p of parts) { out.set(p, at); at += p.length; }
    return out;
  } catch { return "bad"; }
}

/** 每行要是 JSON 物件、沒有控制字元（含跳脫後的 NUL）、不超過 MAX_LINE_CHARS；回傳整理好的行，不合格回 null。 */
export function parseLines(text: string): string[] | null {
  const out: string[] = [];
  for (let raw of text.split("\n")) {
    if (raw.endsWith("\r")) raw = raw.slice(0, -1);
    if (!raw) continue;
    if (raw.length > MAX_LINE_CHARS || CONTROL_RE.test(raw) || raw.includes("\\u0000")) return null;
    let o: unknown;
    try { o = JSON.parse(raw); } catch { return null; }
    if (!o || typeof o !== "object" || Array.isArray(o)) return null;
    out.push(raw);
  }
  return out.length ? out : null;
}

export const dayOf = (t: number) => new Date(t).toISOString().slice(0, 10);

/** /telemetry 與 /telemetry/sessions（token 已由呼叫端檢查過）。不是這兩個路徑回 null。 */
export async function handleTelemetry(req: Request, pathname: string, deps: TelemetryDeps, token: string): Promise<Response | null> {
  if (pathname === "/telemetry/sessions") {
    if (req.method !== "GET") return json(405, { error: "GET only" });
    if (!deps.store) return json(501, { error: "telemetry storage not configured" });
    const day = new URL(req.url).searchParams.get("day");
    if (day === null || day === "") return json(200, { days: deps.store.days(), totalBytes: deps.store.totalBytes() });
    if (!DAY_RE.test(day)) return json(400, { error: "day must be YYYY-MM-DD" });
    return json(200, { day, sessions: deps.store.sessions(day) });
  }
  if (pathname !== "/telemetry") return null;
  if (req.method !== "POST") return json(405, { error: "POST only" });
  if (!deps.store) return json(501, { error: "telemetry storage not configured" });

  const device = req.headers.get("x-voice-device") ?? "";
  if (!device || !isValidDevice(device)) return json(400, { error: "bad device id", code: "bad_device" });
  const session = (req.headers.get("x-tell-session") ?? "").toLowerCase();
  if (!SESSION_RE.test(session)) return json(400, { error: "bad session id", code: "bad_session" });
  const build = req.headers.get("x-tell-build") || "unknown";
  if (!BUILD_RE.test(build)) return json(400, { error: "bad build", code: "bad_build" });
  if (deps.limiter) {
    const limit = deps.limiter.take(limitKey(token, device));
    if (!limit.ok) return json(429, { error: LIMIT_MESSAGES[limit.code], code: limit.code });
  }

  let bytes = new Uint8Array(await req.arrayBuffer());
  if (bytes.length > MAX_REQUEST_BYTES) return json(413, { error: "body too large", code: "too_large" });
  if ((req.headers.get("content-encoding") ?? "").toLowerCase() === "gzip") {
    const r = await gunzipLimited(bytes, MAX_REQUEST_BYTES);
    if (r === "too_large") return json(413, { error: "body too large", code: "too_large" });
    if (r === "bad") return json(400, { error: "bad gzip", code: "bad_lines" });
    bytes = r;
  }
  let text: string;
  try { text = new TextDecoder("utf-8", { fatal: true }).decode(bytes); }
  catch { return json(400, { error: "body is not UTF-8 text", code: "bad_lines" }); }
  const lines = parseLines(text);
  if (!lines) return json(400, { error: "body must be NDJSON objects", code: "bad_lines" });

  const t = deps.wallNow();
  const dev = await deviceTag(device);
  const meta: SessionMeta = { day: dayOf(t), dev, session, build, received: new Date(t).toISOString() };
  const r = deps.store.append(meta, lines);
  if (!r.ok) {
    if (r.code === "session_full") return json(413, { error: "session log full", code: r.code });
    deps.log(`[telemetry] storage full (${deps.store.totalBytes()} bytes); refusing dev=${dev} session=${session}`);
    return json(507, { error: "telemetry storage full", code: r.code });
  }
  if (r.created) deps.log(`[telemetry] dev=${dev} session=${session} build=${build} -> ${meta.day}`);
  return json(200, { ok: true, lines: lines.length });
}

/** 環境變數：TELEMETRY_SESSION_MAX_BYTES（2 MB）、TELEMETRY_TOTAL_MAX_BYTES（200 MB）。 */
export function telemetryOptionsFromEnv(env: Record<string, string | undefined>): { sessionMaxBytes: number; totalMaxBytes: number } {
  const n = (v: string | undefined, d: number) => (v !== undefined && v !== "" && Number.isFinite(Number(v)) && Number(v) > 0 ? Number(v) : d);
  return { sessionMaxBytes: n(env.TELEMETRY_SESSION_MAX_BYTES, 2_000_000), totalMaxBytes: n(env.TELEMETRY_TOTAL_MAX_BYTES, 200_000_000) };
}
