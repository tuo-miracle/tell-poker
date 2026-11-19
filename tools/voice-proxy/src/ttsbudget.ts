// ElevenLabs 即時回話的每月額度保險（UTC 月）：記實際送去配音的字數 × 每字 credit，存檔（重開不歸零）。
// 到預算就不配音：回話照給文字並帶 code "tts_budget"，Unity 改說預錄的罐頭句（有聲音＋字幕，既有的「沒有音訊」路徑）。
// 到 80% 每個 UTC 日記一次警告；到上限每日也記一次。預設值見 deploy/vr-poker-api/docker-compose.yml。

export interface BudgetStore { load(): string | null; save(text: string): void }

export interface TtsBudgetOptions {
  /** 每月預算（credits）；0＝不限。 */
  budgetCredits: number;
  /** 每字幾 credit（eleven_flash_v2_5＝0.5）。 */
  creditPerChar: number;
  store?: BudgetStore;
  now?: () => number;
  log?: (line: string) => void;
}

interface Usage { month: string; chars: number; credits: number; warnedDay?: string; reachedDay?: string }

export const TTS_BUDGET_CODE = "tts_budget";
const WARN_RATIO = 0.8;

const monthOf = (t: number) => new Date(t).toISOString().slice(0, 7);
const dayOf = (t: number) => new Date(t).toISOString().slice(0, 10);

export function memoryStore(): BudgetStore {
  let text: string | null = null;
  return { load: () => text, save: s => { text = s; } };
}

export class TtsBudget {
  private readonly o: Required<TtsBudgetOptions>;
  private u: Usage;

  constructor(o: TtsBudgetOptions) {
    this.o = { store: memoryStore(), now: Date.now, log: () => {}, ...o };
    this.u = this.load();
  }

  private load(): Usage {
    const month = monthOf(this.o.now());
    try {
      const j = JSON.parse(this.o.store.load() ?? "null");
      if (j && typeof j.month === "string" && Number.isFinite(j.chars) && Number.isFinite(j.credits))
        return { month: j.month, chars: j.chars, credits: j.credits, warnedDay: j.warnedDay, reachedDay: j.reachedDay };
    } catch { /* 壞檔：從零開始 */ }
    return { month, chars: 0, credits: 0 };
  }

  private save() {
    try { this.o.store.save(JSON.stringify(this.u)); }
    catch (e) { this.o.log(`[voice] tts usage not saved: ${(e as Error).message}`); }
  }

  /** 換月就歸零。回傳現在時間。 */
  private roll(): number {
    const t = this.o.now();
    const month = monthOf(t);
    if (month !== this.u.month) { this.u = { month, chars: 0, credits: 0 }; this.save(); }
    return t;
  }

  private check(t: number) {
    const b = this.o.budgetCredits;
    if (b <= 0) return;
    const day = dayOf(t);
    if (this.u.credits >= b) {
      if (this.u.reachedDay !== day) {
        this.u.reachedDay = day; this.save();
        this.o.log(`[voice] tts budget reached: ${fmt(this.u.credits)}/${b} credits in ${this.u.month}; live replies use baked lines until next month`);
      }
    } else if (this.u.credits >= b * WARN_RATIO && this.u.warnedDay !== day) {
      this.u.warnedDay = day; this.save();
      this.o.log(`[voice] WARNING tts budget 80%: ${fmt(this.u.credits)}/${b} credits in ${this.u.month}`);
    }
  }

  /** 這次可以配音嗎（還沒到預算）。 */
  allow(): boolean {
    const t = this.roll();
    this.check(t);
    return this.o.budgetCredits <= 0 || this.u.credits < this.o.budgetCredits;
  }

  /** 記一筆實際送去配音（且成功）的字數。 */
  record(chars: number) {
    if (!(chars > 0)) return;
    const t = this.roll();
    this.u.chars += chars;
    this.u.credits += chars * this.o.creditPerChar;
    this.save();
    this.check(t);
  }

  stats() {
    this.roll();
    const b = this.o.budgetCredits;
    return {
      month: this.u.month,
      chars: this.u.chars,
      creditsThisMonth: round2(this.u.credits),
      budget: b,
      percent: b > 0 ? round2((this.u.credits / b) * 100) : 0,
      creditPerChar: this.o.creditPerChar,
    };
  }
}

const round2 = (x: number) => Math.round(x * 100) / 100;
const fmt = (x: number) => String(round2(x));

/** 環境變數：TTS_MONTHLY_CREDIT_BUDGET（15000；0＝不限）、TTS_CREDIT_PER_CHAR（0.5，flash）。 */
export function ttsBudgetOptionsFromEnv(env: Record<string, string | undefined>): { budgetCredits: number; creditPerChar: number } {
  const n = (v: string | undefined, d: number) => (v !== undefined && v !== "" && Number.isFinite(Number(v)) ? Number(v) : d);
  return { budgetCredits: n(env.TTS_MONTHLY_CREDIT_BUDGET, 15000), creditPerChar: n(env.TTS_CREDIT_PER_CHAR, 0.5) };
}
