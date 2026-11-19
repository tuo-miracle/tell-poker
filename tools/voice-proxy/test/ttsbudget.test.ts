import { test } from "node:test";
import assert from "node:assert/strict";
import { mkdtempSync, readFileSync, rmSync } from "node:fs";
import { tmpdir } from "node:os";
import { join } from "node:path";
import { TtsBudget, memoryStore, ttsBudgetOptionsFromEnv } from "../src/ttsbudget.ts";
import { fileStore } from "../src/fsdata.ts";

const OCT = Date.UTC(2026, 9, 14, 12);
const NOV = Date.UTC(2026, 10, 1, 0, 0, 1);

function budget(o: { budget?: number; per?: number; now?: () => number; store?: ReturnType<typeof memoryStore> } = {}) {
  const logs: string[] = [];
  const b = new TtsBudget({ budgetCredits: o.budget ?? 100, creditPerChar: o.per ?? 0.5, store: o.store ?? memoryStore(), now: o.now ?? (() => OCT), log: l => logs.push(l) });
  return { b, logs };
}

test("accumulates characters into credits for the current UTC month", () => {
  const { b } = budget();
  b.record(40);
  b.record(20);
  const s = b.stats();
  assert.equal(s.month, "2026-10");
  assert.equal(s.chars, 60);
  assert.equal(s.creditsThisMonth, 30);
  assert.equal(s.budget, 100);
  assert.equal(s.percent, 30);
  assert.equal(b.allow(), true);
});

test("blocks once credits reach the budget", () => {
  const { b } = budget();
  b.record(199);
  assert.equal(b.allow(), true, "99.5 credits < 100");
  b.record(1);
  assert.equal(b.allow(), false, "100 credits = budget");
});

test("budget 0 means no limit", () => {
  const { b } = budget({ budget: 0 });
  b.record(1_000_000);
  assert.equal(b.allow(), true);
  assert.equal(b.stats().percent, 0);
});

test("a new UTC month starts from zero", () => {
  let t = OCT;
  const { b } = budget({ now: () => t });
  b.record(300);
  assert.equal(b.allow(), false);
  t = NOV;
  assert.equal(b.allow(), true);
  assert.equal(b.stats().month, "2026-11");
  assert.equal(b.stats().creditsThisMonth, 0);
});

test("credits are kept per record, so changing the factor only affects later characters", () => {
  const store = memoryStore();
  budget({ store, per: 1 }).b.record(10);
  const { b } = budget({ store, per: 0.5 });
  b.record(10);
  assert.equal(b.stats().creditsThisMonth, 15);
});

test("warns once per UTC day at 80%, and once per day when the budget is reached", () => {
  let t = OCT;
  const { b, logs } = budget({ now: () => t });
  b.record(150);
  assert.equal(logs.filter(l => l.includes("80%")).length, 0);
  b.record(10); // 80 credits
  b.record(2);
  assert.equal(logs.filter(l => l.includes("80%")).length, 1);
  t += 86_400_000;
  b.record(2);
  assert.equal(logs.filter(l => l.includes("80%")).length, 2, "again the next day");
  b.record(100);
  b.allow(); b.allow();
  assert.equal(logs.filter(l => l.includes("budget reached")).length, 1);
});

test("usage survives a restart through the file store", () => {
  const dir = mkdtempSync(join(tmpdir(), "tts-budget-"));
  try {
    const path = join(dir, "tts-usage.json");
    budget({ store: fileStore(path) }).b.record(120);
    assert.deepEqual(JSON.parse(readFileSync(path, "utf8")).month, "2026-10");
    const { b } = budget({ store: fileStore(path) });
    assert.equal(b.stats().creditsThisMonth, 60);
    b.record(80);
    assert.equal(b.allow(), false);
  } finally { rmSync(dir, { recursive: true, force: true }); }
});

test("a missing or corrupt usage file starts from zero", () => {
  const dir = mkdtempSync(join(tmpdir(), "tts-budget-"));
  try {
    const store = fileStore(join(dir, "nope", "tts-usage.json"));
    assert.equal(budget({ store }).b.stats().creditsThisMonth, 0);
    const bad = { load: () => "{not json", save: () => {} };
    assert.equal(budget({ store: bad }).b.stats().creditsThisMonth, 0);
  } finally { rmSync(dir, { recursive: true, force: true }); }
});

test("env defaults: 15000 credits per month, 0.5 credit per character (flash)", () => {
  assert.deepEqual(ttsBudgetOptionsFromEnv({}), { budgetCredits: 15000, creditPerChar: 0.5 });
  assert.deepEqual(ttsBudgetOptionsFromEnv({ TTS_MONTHLY_CREDIT_BUDGET: "40000", TTS_CREDIT_PER_CHAR: "1" }), { budgetCredits: 40000, creditPerChar: 1 });
});
