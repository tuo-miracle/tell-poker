// 限流：每台裝置一個 token bucket＋每日上限（沒帶裝置 id 時退回用 token 當鍵），外加全體的每日上限當預算保險。
// 裝置 id 是 App 安裝時隨機產生的 GUID（不是硬體識別碼），只用來分開計數；log 只記 sha256 的前 6 碼。
// 記憶體有上限：超過 maxEntries 就踢掉最久沒用的；換 UTC 日時整個清掉（每日計數本來就歸零）。
// Workers 上是每個 isolate 各自計數；正式上線改 Durable Object／KV。

export type LimitCode = "rate_limited" | "device_daily_cap" | "daily_cap";
export type LimitResult = { ok: true; code?: undefined } | { ok: false; code: LimitCode };

export interface LimiterOptions {
  /** 每個鍵的 bucket 容量（連續講幾句）。 */
  capacity: number;
  /** 每秒補回幾句。 */
  refillPerSec: number;
  /** 每個鍵每個 UTC 日最多幾句（0＝不限）。 */
  deviceDailyCap: number;
  /** 所有鍵加總每個 UTC 日最多幾句（0＝不限）：通行碼外流或有人換 id 洗量時的預算保險。 */
  globalDailyCap: number;
  /** 最多記幾個鍵（預設 10,000，約 1 MB）。 */
  maxEntries?: number;
  now?: () => number;
}

interface Entry { tokens: number; at: number; used: number }

const DAY_MS = 86_400_000;

export class RateLimiter {
  private readonly o: Required<LimiterOptions>;
  private readonly entries = new Map<string, Entry>(); // 插入順序＝最近使用順序（用到就移到最後）
  private day = -1;
  private turnsToday = 0;
  private devicesToday = 0;
  private devicesAtCap = 0;

  constructor(o: LimiterOptions) {
    this.o = { maxEntries: 10_000, now: Date.now, ...o };
  }

  get size(): number { return this.entries.size; }

  private rollover(t: number) {
    const day = Math.floor(t / DAY_MS);
    if (day === this.day) return;
    this.day = day;
    this.entries.clear();
    this.turnsToday = this.devicesToday = this.devicesAtCap = 0;
  }

  take(key: string): LimitResult {
    const t = this.o.now();
    this.rollover(t);
    if (this.o.globalDailyCap > 0 && this.turnsToday >= this.o.globalDailyCap) return { ok: false, code: "daily_cap" };
    let e = this.entries.get(key);
    if (e) this.entries.delete(key);
    else {
      e = { tokens: this.o.capacity, at: t, used: 0 };
      this.devicesToday++;
      while (this.entries.size >= this.o.maxEntries) this.entries.delete(this.entries.keys().next().value!);
    }
    this.entries.set(key, e);
    if (this.o.deviceDailyCap > 0 && e.used >= this.o.deviceDailyCap) return { ok: false, code: "device_daily_cap" };
    e.tokens = Math.min(this.o.capacity, e.tokens + ((t - e.at) / 1000) * this.o.refillPerSec);
    e.at = t;
    if (e.tokens < 1) return { ok: false, code: "rate_limited" };
    e.tokens -= 1;
    e.used++;
    this.turnsToday++;
    if (this.o.deviceDailyCap > 0 && e.used === this.o.deviceDailyCap) this.devicesAtCap++;
    return { ok: true };
  }

  /** GET /stats：今天（UTC）的計數。鍵被踢掉又回來時 devicesToday 會多算（只在超過 maxEntries 時發生）。 */
  stats() {
    this.rollover(this.o.now());
    return {
      day: new Date(this.day * DAY_MS).toISOString().slice(0, 10),
      turnsToday: this.turnsToday,
      devicesToday: this.devicesToday,
      devicesAtCap: this.devicesAtCap,
      globalDailyCap: this.o.globalDailyCap,
      deviceDailyCap: this.o.deviceDailyCap,
      entries: this.entries.size,
    };
  }
}

/** GUID 格式（有沒有連字號都行）、只有 hex 與 '-'、最長 64。 */
const DEVICE_RE = /^[0-9a-f]{8}-?[0-9a-f]{4}-?[0-9a-f]{4}-?[0-9a-f]{4}-?[0-9a-f]{12}$/i;
export function isValidDevice(id: string): boolean {
  return id.length <= 64 && DEVICE_RE.test(id);
}

/** 限流的鍵：有裝置 id 用裝置，沒有就用 token（舊版 App）。呼叫前先用 isValidDevice 檢查。 */
export function limitKey(token: string, device: string | null | undefined): string {
  return device ? "dev:" + device.toLowerCase() : "tok:" + token;
}

/** log 用：sha256(裝置 id) 的前 6 碼；沒有 id 回 "-"。 */
export async function deviceTag(device: string | null | undefined): Promise<string> {
  if (!device) return "-";
  const d = await crypto.subtle.digest("SHA-256", new TextEncoder().encode(device.toLowerCase()));
  return Array.from(new Uint8Array(d).subarray(0, 3), b => b.toString(16).padStart(2, "0")).join("");
}

export const LIMIT_MESSAGES: Record<LimitCode, string> = {
  rate_limited: "rate limited",
  device_daily_cap: "device daily cap reached",
  daily_cap: "daily cap reached",
};

/** 環境變數 → LimiterOptions（node-server.ts／worker.ts 共用）。預設值的成本理由見 deploy/vr-poker-api/docker-compose.yml。 */
export function limiterOptionsFromEnv(env: Record<string, string | undefined>): LimiterOptions {
  const n = (v: string | undefined, d: number) => (v !== undefined && v !== "" && Number.isFinite(Number(v)) ? Number(v) : d);
  return {
    capacity: n(env.VOICE_DEVICE_CAPACITY ?? env.VOICE_RATE_CAPACITY, 6),
    refillPerSec: n(env.VOICE_DEVICE_PER_SEC ?? env.VOICE_RATE_PER_SEC, 0.2),
    deviceDailyCap: n(env.VOICE_DEVICE_DAILY_CAP, 80),
    globalDailyCap: n(env.VOICE_DAILY_CAP, 3000),
    maxEntries: n(env.VOICE_DEVICE_MAX_ENTRIES, 10_000),
  };
}
