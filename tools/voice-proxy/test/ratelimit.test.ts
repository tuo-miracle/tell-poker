import { test } from "node:test";
import assert from "node:assert/strict";
import { RateLimiter, limitKey, isValidDevice, deviceTag } from "../src/ratelimit.ts";

const opts = (now: () => number, o: Partial<ConstructorParameters<typeof RateLimiter>[0]> = {}) =>
  ({ capacity: 100, refillPerSec: 100, deviceDailyCap: 0, globalDailyCap: 0, now, ...o });

test("token bucket allows a burst then refills over time", () => {
  let t = 0;
  const rl = new RateLimiter(opts(() => t, { capacity: 3, refillPerSec: 0.5 })); // 3 次，之後每 2 秒 1 次
  assert.ok(rl.take("a").ok && rl.take("a").ok && rl.take("a").ok);
  assert.deepEqual(rl.take("a"), { ok: false, code: "rate_limited" });
  assert.ok(rl.take("b").ok, "keys are independent");
  t = 2000;
  assert.ok(rl.take("a").ok);
  assert.equal(rl.take("a").ok, false);
});

test("per-device isolation: one device hammering does not throttle another", () => {
  let t = 0;
  const rl = new RateLimiter(opts(() => t, { capacity: 6, refillPerSec: 0.2, deviceDailyCap: 80 }));
  for (let i = 0; i < 6; i++) assert.ok(rl.take("dev:a").ok);
  assert.equal(rl.take("dev:a").ok, false);
  for (let i = 0; i < 6; i++) assert.ok(rl.take("dev:b").ok, "fresh device still has its own burst");
});

test("per-device daily cap answers device_daily_cap and resets at UTC midnight", () => {
  let t = Date.UTC(2026, 9, 14, 23, 0, 0);
  const rl = new RateLimiter(opts(() => t, { deviceDailyCap: 3 }));
  for (let i = 0; i < 3; i++) assert.ok(rl.take("dev:a").ok);
  assert.deepEqual(rl.take("dev:a"), { ok: false, code: "device_daily_cap" });
  assert.ok(rl.take("dev:b").ok, "other devices are not capped");
  t = Date.UTC(2026, 9, 14, 23, 59, 59);
  assert.equal(rl.take("dev:a").ok, false, "still the same UTC day");
  t = Date.UTC(2026, 9, 15, 0, 0, 1);
  assert.ok(rl.take("dev:a").ok, "new UTC day resets the device cap");
});

test("global daily cap is a backstop over all devices and resets at UTC midnight", () => {
  let t = Date.UTC(2026, 9, 14, 23, 59, 0);
  const rl = new RateLimiter(opts(() => t, { deviceDailyCap: 80, globalDailyCap: 3 }));
  assert.ok(rl.take("dev:a").ok);
  assert.ok(rl.take("dev:b").ok);
  assert.ok(rl.take("dev:a").ok);
  assert.deepEqual(rl.take("dev:c"), { ok: false, code: "daily_cap" }, "4th call of the day is refused even for a fresh device");
  t = Date.UTC(2026, 9, 15, 0, 0, 1);
  assert.ok(rl.take("dev:c").ok, "new UTC day resets the global cap");
});

test("refused calls do not count toward daily caps", () => {
  let t = 0;
  const rl = new RateLimiter(opts(() => t, { capacity: 1, refillPerSec: 0, deviceDailyCap: 2, globalDailyCap: 2 }));
  assert.ok(rl.take("dev:a").ok);
  for (let i = 0; i < 5; i++) assert.equal(rl.take("dev:a").code, "rate_limited");
  assert.ok(rl.take("dev:b").ok, "global budget not burned by refused calls");
});

test("caps of 0 mean unlimited", () => {
  const rl = new RateLimiter(opts(() => 0, { capacity: 1000, refillPerSec: 1000 }));
  for (let i = 0; i < 500; i++) assert.equal(rl.take("a").ok, true);
});

test("memory stays bounded: least recently used entries are evicted beyond maxEntries", () => {
  let t = 0;
  const rl = new RateLimiter(opts(() => t, { capacity: 1, refillPerSec: 0, maxEntries: 3 }));
  for (const k of ["a", "b", "c"]) assert.ok(rl.take(k).ok);
  assert.equal(rl.take("a").ok, false, "a is now the most recent");
  assert.ok(rl.take("d").ok);
  assert.equal(rl.size, 3);
  assert.ok(rl.take("b").ok, "b (least recently used) was evicted, so it starts fresh");
  assert.equal(rl.take("a").ok, false, "a was kept");
  for (let i = 0; i < 1000; i++) rl.take("x" + i);
  assert.equal(rl.size, 3);
});

test("entries from previous UTC days are dropped at rollover", () => {
  let t = Date.UTC(2026, 9, 14, 12);
  const rl = new RateLimiter(opts(() => t));
  for (let i = 0; i < 50; i++) rl.take("dev:" + i);
  assert.equal(rl.size, 50);
  t = Date.UTC(2026, 9, 15, 0, 0, 1);
  rl.take("dev:new");
  assert.equal(rl.size, 1);
});

test("stats: turns today, distinct devices today, devices at cap; reset at UTC midnight", () => {
  let t = Date.UTC(2026, 9, 14, 10);
  const rl = new RateLimiter(opts(() => t, { deviceDailyCap: 2, globalDailyCap: 100 }));
  rl.take("dev:a"); rl.take("dev:a"); rl.take("dev:a"); // a 到上限（第 3 次被拒）
  rl.take("dev:b");
  rl.take("tok:t");
  assert.deepEqual(rl.stats(), { day: "2026-10-14", turnsToday: 4, devicesToday: 3, devicesAtCap: 1, globalDailyCap: 100, deviceDailyCap: 2, entries: 3 });
  t = Date.UTC(2026, 9, 15, 0, 0, 1);
  assert.deepEqual(rl.stats(), { day: "2026-10-15", turnsToday: 0, devicesToday: 0, devicesAtCap: 0, globalDailyCap: 100, deviceDailyCap: 2, entries: 0 });
});

test("limitKey: device when present, token otherwise", () => {
  assert.equal(limitKey("tok", "0f8fad5b-d9cb-469f-a165-70867728950e"), "dev:0f8fad5b-d9cb-469f-a165-70867728950e");
  assert.equal(limitKey("tok", "0F8FAD5B-D9CB-469F-A165-70867728950E"), "dev:0f8fad5b-d9cb-469f-a165-70867728950e", "case-insensitive");
  assert.equal(limitKey("tok", null), "tok:tok");
  assert.equal(limitKey("tok", ""), "tok:tok");
});

test("isValidDevice accepts uuid-ish ids and rejects garbage", () => {
  assert.ok(isValidDevice("0f8fad5b-d9cb-469f-a165-70867728950e"));
  assert.ok(isValidDevice("0f8fad5bd9cb469fa16570867728950e"));
  for (const bad of ["short", "0f8fad5b-d9cb-469f-a165-70867728950e; drop", "<script>", "x".repeat(40), "a".repeat(65), "g".repeat(32), "../../etc/passwd"])
    assert.equal(isValidDevice(bad), false, bad);
});

test("deviceTag is a 6-char sha256 prefix, never the raw id", async () => {
  const id = "0f8fad5b-d9cb-469f-a165-70867728950e";
  const tag = await deviceTag(id);
  assert.match(tag, /^[0-9a-f]{6}$/);
  assert.notEqual(tag, id.slice(0, 6));
  assert.equal(tag, await deviceTag(id.toUpperCase()), "same id, same tag");
  assert.equal(await deviceTag(null), "-");
});

test("limiterOptionsFromEnv: defaults and overrides", async () => {
  const { limiterOptionsFromEnv } = await import("../src/ratelimit.ts");
  assert.deepEqual(limiterOptionsFromEnv({}), { capacity: 6, refillPerSec: 0.2, deviceDailyCap: 80, globalDailyCap: 3000, maxEntries: 10_000 });
  assert.deepEqual(limiterOptionsFromEnv({ VOICE_DEVICE_CAPACITY: "4", VOICE_DEVICE_PER_SEC: "0.1", VOICE_DEVICE_DAILY_CAP: "0", VOICE_DAILY_CAP: "500", VOICE_DEVICE_MAX_ENTRIES: "50" }),
    { capacity: 4, refillPerSec: 0.1, deviceDailyCap: 0, globalDailyCap: 500, maxEntries: 50 });
  assert.equal(limiterOptionsFromEnv({ VOICE_RATE_CAPACITY: "8" }).capacity, 8, "old env name still works");
  assert.equal(limiterOptionsFromEnv({ VOICE_DAILY_CAP: "abc" }).globalDailyCap, 3000, "garbage falls back to the default");
});
