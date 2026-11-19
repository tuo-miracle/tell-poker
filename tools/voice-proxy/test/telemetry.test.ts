import { test } from "node:test";
import assert from "node:assert/strict";
import { mkdtempSync, mkdirSync, readdirSync, readFileSync, rmSync, writeFileSync, existsSync } from "node:fs";
import { tmpdir } from "node:os";
import { join } from "node:path";
import { gzipSync } from "node:zlib";
import { createHandler, type HandlerConfig } from "../src/handler.ts";
import { FsTelemetryStore } from "../src/fsdata.ts";
import { deviceTag } from "../src/ratelimit.ts";
import { limiter, DEV_A, DEV_B } from "./helpers.ts";

const DAY = "2026-10-14";
const NOW = Date.UTC(2026, 9, 14, 12);

function setup(o: { sessionMax?: number; totalMax?: number; now?: () => number; telemetryLimiter?: HandlerConfig["telemetryLimiter"]; noStore?: boolean } = {}) {
  const root = mkdtempSync(join(tmpdir(), "tell-telemetry-"));
  const logs: string[] = [];
  const store = new FsTelemetryStore(join(root, "telemetry"), { sessionMaxBytes: o.sessionMax ?? 2_000_000, totalMaxBytes: o.totalMax ?? 200_000_000 });
  const cfg: HandlerConfig = {
    geminiKey: "G", elevenKey: "E", tokens: ["tok"], model: "m", ttsModel: "t", ttsSampleRate: 22050, maxAudioSec: 10,
    fetch: async () => new Response("unexpected upstream call", { status: 500 }),
    now: () => 0, limiter: limiter({ capacity: 1, refillPerSec: 0 }), log: l => logs.push(l),
    telemetry: o.noStore ? undefined : store, telemetryLimiter: o.telemetryLimiter, wallNow: o.now ?? (() => NOW),
  };
  const handler = createHandler(cfg);
  return { handler, root, dir: join(root, "telemetry"), logs, cleanup: () => rmSync(root, { recursive: true, force: true }) };
}

const line = (msg: string, i = 0) => JSON.stringify({ t: "2026-10-14T12:00:00.000Z", s: i, l: "log", m: msg });
const body = (n = 3) => Array.from({ length: n }, (_, i) => line(`[Playtest] event ${i}`, i)).join("\n") + "\n";

function post(data: string | Uint8Array, h: Record<string, string> = {}) {
  return new Request("http://x/telemetry", {
    method: "POST",
    headers: { "x-voice-token": "tok", "x-voice-device": DEV_A, "x-tell-session": "a1b2c3d4", "x-tell-build": "0.3.1", "content-type": "application/x-ndjson", ...h },
    body: data,
  });
}

test("POST /telemetry appends lines to /data/telemetry/<day>/<dev6>_<session>.ndjson with a session header line", async () => {
  const { handler, dir, cleanup } = setup();
  try {
    const res = await handler(post(body(3)));
    assert.equal(res.status, 200);
    assert.equal((await res.json()).lines, 3);
    assert.equal((await handler(post(body(2)))).status, 200);
    const dev = await deviceTag(DEV_A);
    const file = join(dir, DAY, `${dev}_a1b2c3d4.ndjson`);
    const lines = readFileSync(file, "utf8").trimEnd().split("\n").map(l => JSON.parse(l));
    assert.equal(lines.length, 1 + 3 + 2);
    assert.deepEqual({ ...lines[0], received: undefined }, { type: "session", session: "a1b2c3d4", build: "0.3.1", dev, day: DAY, received: undefined });
    assert.equal(lines[1].m, "[Playtest] event 0");
    assert.ok(!readFileSync(file, "utf8").includes(DEV_A), "raw device id never stored");
  } finally { cleanup(); }
});

test("gzip bodies are accepted", async () => {
  const { handler, dir, cleanup } = setup();
  try {
    const res = await handler(post(gzipSync(Buffer.from(body(4))), { "content-encoding": "gzip" }));
    assert.equal(res.status, 200);
    const files = readdirSync(join(dir, DAY));
    assert.equal(readFileSync(join(dir, DAY, files[0]), "utf8").trimEnd().split("\n").length, 5);
  } finally { cleanup(); }
});

test("needs the app token and a valid device id and session id", async () => {
  const { handler, dir, cleanup } = setup();
  try {
    assert.equal((await handler(post(body(), { "x-voice-token": "nope" }))).status, 401);
    for (const bad of ["", "nope", "a".repeat(200)]) {
      const r = await handler(post(body(), { "x-voice-device": bad }));
      assert.equal(r.status, 400, `device ${bad}`);
      assert.equal((await r.json()).code, "bad_device");
    }
    for (const bad of ["", "xyz", "../../etc", "A1B2C3D4E5F6A1B2C3D4E5F6A1B2C3D4E5"]) {
      const r = await handler(post(body(), { "x-tell-session": bad }));
      assert.equal(r.status, 400, `session ${bad}`);
      assert.equal((await r.json()).code, "bad_session");
    }
    assert.equal((await handler(post(body(), { "x-tell-build": "x/../y" }))).status, 400);
    assert.equal((await handler(new Request("http://x/telemetry", { method: "GET", headers: { "x-voice-token": "tok" } }))).status, 405);
    assert.ok(!existsSync(join(dir, DAY)), "nothing written");
  } finally { cleanup(); }
});

test("rejects bodies over 256 KB (raw or after gunzip) and non-text lines", async () => {
  const { handler, cleanup } = setup();
  try {
    const big = Array.from({ length: 80 }, (_, i) => line("[Playtest] " + "x".repeat(3500), i)).join("\n");
    assert.ok(big.length > 256 * 1024);
    let r = await handler(post(big));
    assert.equal(r.status, 413);
    r = await handler(post(gzipSync(Buffer.from(big)), { "content-encoding": "gzip" }));
    assert.equal(r.status, 413, "decompressed size is checked too");
    for (const bad of ["not json\n", "[1,2]\n", JSON.stringify({ m: "a\u0000b" }) + "\n", line("x".repeat(9000)) + "\n"]) {
      r = await handler(post(bad));
      assert.equal(r.status, 400, bad.slice(0, 20));
      assert.equal((await r.json()).code, "bad_lines");
    }
    r = await handler(post(new Uint8Array([0xff, 0xfe, 0x00, 0x01])));
    assert.equal(r.status, 400, "binary is refused");
    r = await handler(post(new Uint8Array([1, 2, 3]), { "content-encoding": "gzip" }));
    assert.equal(r.status, 400, "broken gzip");
  } finally { cleanup(); }
});

test("per-session cap: once full, further batches are refused with session_full", async () => {
  const { handler, dir, cleanup } = setup({ sessionMax: 1000 });
  try {
    assert.equal((await handler(post(body(3)))).status, 200);
    let r: Response;
    let n = 0;
    do { r = await handler(post(body(3))); n++; } while (r.status === 200 && n < 50);
    assert.equal(r.status, 413);
    assert.equal((await r.json()).code, "session_full");
    const file = join(dir, DAY, readdirSync(join(dir, DAY))[0]);
    const text = readFileSync(file, "utf8");
    assert.ok(text.length <= 1000 + 200, "stays near the cap");
    assert.match(text, /"type":"capped"/);
    assert.equal(text.match(/"type":"capped"/g)!.length, 1);
    // 別的 session 不受影響
    assert.equal((await handler(post(body(1), { "x-tell-session": "ffff0000" }))).status, 200);
  } finally { cleanup(); }
});

test("total cap deletes the oldest days first; refuses when today alone is full", async () => {
  const { handler, dir, cleanup } = setup({ totalMax: 3000 });
  try {
    for (const d of ["2026-10-10", "2026-10-12"]) {
      mkdirSync(join(dir, d), { recursive: true });
      writeFileSync(join(dir, d, "abcdef_00000000.ndjson"), "x".repeat(1400));
    }
    assert.equal((await handler(post(body(3)))).status, 200);
    assert.ok(!existsSync(join(dir, "2026-10-10")), "oldest day deleted");
    assert.ok(existsSync(join(dir, "2026-10-12")), "newer day kept while it fits");
    let r: Response;
    let n = 0;
    do { r = await handler(post(body(3), { "x-tell-session": (0x10000000 + n).toString(16) })); n++; } while (r.status === 200 && n < 100);
    assert.ok(!existsSync(join(dir, "2026-10-12")), "then the next oldest");
    assert.equal(r.status, 507);
    assert.equal((await r.json()).code, "storage_full");
  } finally { cleanup(); }
});

test("lines are filed under the UTC day they arrive", async () => {
  let t = Date.UTC(2026, 9, 14, 23, 59, 59);
  const { handler, dir, cleanup } = setup({ now: () => t });
  try {
    await handler(post(body(1)));
    t += 2000;
    await handler(post(body(1)));
    assert.deepEqual(readdirSync(dir).sort(), ["2026-10-14", "2026-10-15"]);
  } finally { cleanup(); }
});

test("GET /telemetry/sessions?day= lists sessions with sizes; without day lists days", async () => {
  const { handler, cleanup } = setup();
  try {
    await handler(post(body(3)));
    await handler(post(body(1), { "x-voice-device": DEV_B, "x-tell-session": "0000beef" }));
    const get = (q: string, token = "tok") => handler(new Request("http://x/telemetry/sessions" + q, { headers: { "x-voice-token": token } }));
    assert.equal((await get("?day=" + DAY, "nope")).status, 401);
    assert.equal((await get("?day=../etc")).status, 400);
    const j = await (await get("?day=" + DAY)).json();
    assert.equal(j.day, DAY);
    assert.equal(j.sessions.length, 2);
    const a = j.sessions.find((s: any) => s.session === "a1b2c3d4");
    assert.equal(a.dev, await deviceTag(DEV_A));
    assert.equal(a.file, `${a.dev}_a1b2c3d4.ndjson`);
    assert.ok(a.bytes > 0);
    assert.match(a.modified, /^2\d{3}-/);
    const days = await (await get("")).json();
    assert.deepEqual(days.days.map((d: any) => [d.day, d.sessions]), [[DAY, 2]]);
    assert.ok(days.totalBytes > 0);
    assert.equal((await (await get("?day=2020-01-01")).json()).sessions.length, 0);
  } finally { cleanup(); }
});

test("telemetry has its own per-device rate limit and never touches the voice limiter", async () => {
  const { handler, cleanup } = setup({ telemetryLimiter: limiter({ capacity: 2, refillPerSec: 0 }) });
  try {
    assert.equal((await handler(post(body(1)))).status, 200);
    assert.equal((await handler(post(body(1)))).status, 200);
    const r = await handler(post(body(1)));
    assert.equal(r.status, 429);
    assert.equal((await r.json()).code, "rate_limited");
    assert.equal((await handler(post(body(1), { "x-voice-device": DEV_B }))).status, 200);
  } finally { cleanup(); }
});

test("without a data directory (Workers) /telemetry answers 501", async () => {
  const { handler, cleanup } = setup({ noStore: true });
  try { assert.equal((await handler(post(body(1)))).status, 501); } finally { cleanup(); }
});
