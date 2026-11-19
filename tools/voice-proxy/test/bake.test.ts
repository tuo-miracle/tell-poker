import { test } from "node:test";
import assert from "node:assert/strict";
import { bakeLines, fileFor, lineHash, planBake, FORMAT, type Line, type Manifest } from "../bake/plan.ts";
import { CAST_VOICES, VOICES } from "../src/elevenlabs.ts";

const MODEL = "eleven_flash_v2_5";
const lines: Line[] = [
  { id: "dot.tutorial.peek", voice: "dot", text: "Palm over your cards. Lift to peek." },
  { id: "rocco.value.01", voice: "rocco", text: "Pay up, friend. Pay up." },
  { id: "vex.num.three", voice: "vex", text: "three" },
];

function fakeFetch(opts: { status?: number[] } = {}) {
  const calls: { url: string; body: any; key: string | null }[] = [];
  const statuses = [...(opts.status ?? [])];
  const fetch = async (u: string | URL | Request, init?: RequestInit) => {
    const url = String(u);
    const body = JSON.parse(String(init?.body ?? "{}"));
    calls.push({ url, body, key: new Headers(init?.headers).get("xi-api-key") });
    const status = statuses.length ? statuses.shift()! : 200;
    if (status !== 200) return new Response("busy", { status });
    return new Response(new Uint8Array([0xff, 0xfb, 0x90, 0x00, body.text.length]));
  };
  return { fetch, calls };
}

function memoryFs() {
  const files = new Map<string, Uint8Array>();
  return {
    files,
    write: (p: string, b: Uint8Array) => { files.set(p, b); },
    remove: (p: string) => { files.delete(p); },
    exists: (p: string) => files.has(p),
  };
}

test("file names are Resources paths without dots in the asset name", () => {
  assert.equal(fileFor(lines[0]), "Speech/dot/dot_tutorial_peek");
  assert.equal(fileFor({ id: "pip.fb.it's", voice: "pip", text: "x" }), "Speech/pip/pip_fb_it_s");
});

test("hash depends on voice, model, format and text", () => {
  const h = lineHash(VOICES.dot, MODEL, FORMAT, "Hello.");
  assert.match(h, /^[0-9a-f]{16}$/);
  assert.notEqual(h, lineHash(VOICES.dot, MODEL, FORMAT, "Hello!"));
  assert.notEqual(h, lineHash(VOICES.pip, MODEL, FORMAT, "Hello."));
  assert.notEqual(h, lineHash(VOICES.dot, "other", FORMAT, "Hello."));
});

test("first run bakes everything and counts characters", () => {
  const fs = memoryFs();
  const p = planBake(lines, null, fs.exists, MODEL);
  assert.equal(p.bake.length, 3);
  assert.equal(p.keep.length, 0);
  assert.equal(p.chars, lines.reduce((n, l) => n + l.text.length, 0));
  assert.equal(p.totalChars, p.chars);
});

test("bake writes mp3 files and a manifest, then a second run calls nothing", async () => {
  const fs = memoryFs();
  const { fetch, calls } = fakeFetch();
  const r = await bakeLines({ lines, manifest: null, fetch, apiKey: "K", model: MODEL, write: fs.write, remove: fs.remove, exists: fs.exists, log: () => {} });
  assert.equal(calls.length, 3);
  const dot = calls.find(c => c.body.text.startsWith("Palm"))!;
  assert.ok(dot.url.startsWith(`https://api.elevenlabs.io/v1/text-to-speech/${VOICES.dot}?`), dot.url);
  assert.ok(dot.url.includes("output_format=" + FORMAT));
  assert.equal(dot.body.model_id, MODEL);
  assert.equal(dot.key, "K");
  assert.ok(fs.exists("Speech/dot/dot_tutorial_peek"));
  assert.equal(r.manifest.lines.length, 3);
  const m = r.manifest.lines[0];
  assert.deepEqual(Object.keys(m).sort(), ["bytes", "file", "hash", "id", "model", "text", "voice", "voiceId"]);
  assert.equal(m.voiceId, VOICES.dot);
  assert.equal(r.chars, p3chars());
  assert.equal(r.manifest.format, FORMAT);

  const again = await bakeLines({ lines, manifest: r.manifest, fetch, apiKey: "K", model: MODEL, write: fs.write, remove: fs.remove, exists: fs.exists, log: () => {} });
  assert.equal(calls.length, 3, "nothing changed: no API calls");
  assert.equal(again.chars, 0);
  assert.deepEqual(again.manifest, r.manifest);
});

function p3chars() { return lines.reduce((n, l) => n + l.text.length, 0); }

test("changed text re-bakes only that line; removed lines delete their file", async () => {
  const fs = memoryFs();
  const { fetch, calls } = fakeFetch();
  const first = await bakeLines({ lines, manifest: null, fetch, apiKey: "K", model: MODEL, write: fs.write, remove: fs.remove, exists: fs.exists, log: () => {} });
  const changed: Line[] = [{ ...lines[0], text: "Palm over your cards, then lift to peek." }, lines[1]];
  const p = planBake(changed, first.manifest, fs.exists, MODEL);
  assert.deepEqual(p.bake.map(l => l.id), ["dot.tutorial.peek"]);
  assert.deepEqual(p.remove.map(l => l.id), ["vex.num.three"]);
  const r = await bakeLines({ lines: changed, manifest: first.manifest, fetch, apiKey: "K", model: MODEL, write: fs.write, remove: fs.remove, exists: fs.exists, log: () => {} });
  assert.equal(calls.length, 4);
  assert.equal(r.manifest.lines.length, 2);
  assert.equal(r.manifest.lines[0].text, changed[0].text);
  assert.ok(!fs.exists("Speech/vex/vex_num_three"));
});

test("a missing file is baked again even when the manifest says it is current", () => {
  const fs = memoryFs();
  const manifest: Manifest = {
    model: MODEL, format: FORMAT,
    lines: lines.map(l => ({ id: l.id, voice: l.voice, voiceId: VOICES[l.voice], model: MODEL, text: l.text, file: fileFor(l), hash: lineHash(VOICES[l.voice], MODEL, FORMAT, l.text), bytes: 5 })),
  };
  fs.write("Speech/dot/dot_tutorial_peek", new Uint8Array(5));
  const p = planBake(lines, manifest, fs.exists, MODEL);
  assert.deepEqual(p.bake.map(l => l.id).sort(), ["rocco.value.01", "vex.num.three"]);
});

test("refuses to spend more than the character budget", async () => {
  const fs = memoryFs();
  const { fetch, calls } = fakeFetch();
  await assert.rejects(
    bakeLines({ lines, manifest: null, fetch, apiKey: "K", model: MODEL, write: fs.write, remove: fs.remove, exists: fs.exists, log: () => {}, maxChars: 10 }),
    /budget/);
  assert.equal(calls.length, 0);
});

test("retries when ElevenLabs is busy", async () => {
  const fs = memoryFs();
  const { fetch, calls } = fakeFetch({ status: [429] });
  const r = await bakeLines({ lines: [lines[2]], manifest: null, fetch, apiKey: "K", model: MODEL, write: fs.write, remove: fs.remove, exists: fs.exists, log: () => {}, retryDelayMs: 1 });
  assert.equal(calls.length, 2);
  assert.equal(r.manifest.lines.length, 1);
});

// 聽檢：verify 回傳每句 ok／issues；壞的重合成，最多 attempts 次
function fakeVerify(badUntil: Record<string, number>) {
  const seen: Record<string, number> = {};
  const batches: string[][] = [];
  const verify = async (batch: { line: Line; bytes: Uint8Array }[]) => {
    batches.push(batch.map(b => b.line.id));
    return batch.map(b => {
      const n = (seen[b.line.id] = (seen[b.line.id] ?? 0) + 1);
      const ok = n > (badUntil[b.line.id] ?? 0);
      return { id: b.line.id, ok, issues: ok ? [] : ["extra: uh"] };
    });
  };
  return { verify, batches };
}

test("verified bake: a bad clip is re-synthesized until it passes", async () => {
  const fs = memoryFs();
  const { fetch, calls } = fakeFetch();
  const v = fakeVerify({ "dot.tutorial.peek": 2 });
  const r = await bakeLines({ lines, manifest: null, fetch, apiKey: "K", model: MODEL, write: fs.write, remove: fs.remove, exists: fs.exists, log: () => {}, verify: v.verify });
  assert.equal(calls.length, 5, "3 lines + 2 retries");
  assert.deepEqual(v.batches, [["dot.tutorial.peek", "rocco.value.01", "vex.num.three"], ["dot.tutorial.peek"], ["dot.tutorial.peek"]]);
  assert.equal(r.failed.length, 0);
  assert.equal(r.manifest.lines.length, 3);
  assert.equal(r.chars, lines.reduce((n, l) => n + l.text.length, 0) + 2 * lines[0].text.length, "retries cost characters too");
});

test("verified bake: still failing after the attempts is not written and not in the manifest", async () => {
  const fs = memoryFs();
  const { fetch, calls } = fakeFetch();
  const v = fakeVerify({ "dot.tutorial.peek": 99 });
  const r = await bakeLines({ lines, manifest: null, fetch, apiKey: "K", model: MODEL, write: fs.write, remove: fs.remove, exists: fs.exists, log: () => {}, verify: v.verify, attempts: 3 });
  assert.equal(calls.length, 5);
  assert.deepEqual(r.failed.map(f => f.id), ["dot.tutorial.peek"]);
  assert.deepEqual(r.failed[0].issues, ["extra: uh"]);
  assert.ok(!fs.exists("Speech/dot/dot_tutorial_peek"), "bad audio never written");
  assert.deepEqual(r.manifest.lines.map(l => l.id), ["rocco.value.01", "vex.num.three"]);
});

test("verified bake: a failing re-bake keeps the previous good clip of the same text", async () => {
  const fs = memoryFs();
  const { fetch } = fakeFetch();
  const first = await bakeLines({ lines, manifest: null, fetch, apiKey: "K", model: MODEL, write: fs.write, remove: fs.remove, exists: fs.exists, log: () => {} });
  const before = fs.files.get("Speech/dot/dot_tutorial_peek");
  const v = fakeVerify({ "dot.tutorial.peek": 99 });
  const r = await bakeLines({ lines, manifest: first.manifest, fetch, apiKey: "K", model: "better_model", write: fs.write, remove: fs.remove, exists: fs.exists, log: () => {}, verify: v.verify, attempts: 2 });
  assert.deepEqual(r.failed.map(f => f.id), ["dot.tutorial.peek"]);
  assert.equal(fs.files.get("Speech/dot/dot_tutorial_peek"), before);
  const kept = r.manifest.lines.find(l => l.id === "dot.tutorial.peek")!;
  assert.equal(kept.model, MODEL, "old entry kept as is");
  assert.equal(r.manifest.lines.find(l => l.id === "rocco.value.01")!.model, "better_model");
});

test("verified bake with force writes the failing clip anyway", async () => {
  const fs = memoryFs();
  const { fetch } = fakeFetch();
  const v = fakeVerify({ "vex.num.three": 99 });
  const r = await bakeLines({ lines, manifest: null, fetch, apiKey: "K", model: MODEL, write: fs.write, remove: fs.remove, exists: fs.exists, log: () => {}, verify: v.verify, attempts: 1, force: true });
  assert.deepEqual(r.failed.map(f => f.id), ["vex.num.three"]);
  assert.ok(fs.exists("Speech/vex/vex_num_three"));
  assert.equal(r.manifest.lines.length, 3);
});

test("one line failing to synthesize does not throw away the rest of the run", async () => {
  const fs = memoryFs();
  const { fetch } = fakeFetch({ status: [400, 200, 200, 400, 400] });
  const r = await bakeLines({ lines, manifest: null, fetch, apiKey: "K", model: MODEL, write: fs.write, remove: fs.remove, exists: fs.exists, log: () => {}, concurrency: 1 });
  assert.deepEqual(r.failed.map(f => f.id), ["dot.tutorial.peek"]);
  assert.match(r.failed[0].issues[0], /http 400/);
  assert.deepEqual(r.manifest.lines.map(l => l.id), ["rocco.value.01", "vex.num.three"]);
});

test("only: re-bakes just the named lines and keeps the others as they are", async () => {
  const fs = memoryFs();
  const { fetch, calls } = fakeFetch();
  const first = await bakeLines({ lines, manifest: null, fetch, apiKey: "K", model: MODEL, write: fs.write, remove: fs.remove, exists: fs.exists, log: () => {} });
  const p = planBake(lines, first.manifest, fs.exists, "better_model", FORMAT, new Set(["vex.num.three"]));
  assert.deepEqual(p.bake.map(l => l.id), ["vex.num.three"]);
  const r = await bakeLines({ lines, manifest: first.manifest, fetch, apiKey: "K", model: "better_model", write: fs.write, remove: fs.remove, exists: fs.exists, log: () => {}, only: new Set(["vex.num.three"]) });
  assert.equal(calls.length, 4);
  assert.deepEqual(r.manifest.lines.map(l => l.model), [MODEL, MODEL, "better_model"]);
});

test("quota exceeded stops the run instead of retrying every line", async () => {
  const fs = memoryFs();
  const calls: string[] = [];
  const fetch = async (_u: string | URL | Request, init?: RequestInit) => {
    calls.push(JSON.parse(String(init?.body)).text);
    return new Response('{"detail":{"code":"quota_exceeded"}}', { status: 401 });
  };
  const r = await bakeLines({ lines, manifest: null, fetch, apiKey: "K", model: MODEL, write: fs.write, remove: fs.remove, exists: fs.exists, log: () => {}, concurrency: 1 });
  assert.equal(calls.length, 1);
  assert.equal(r.failed.length, 3);
  assert.match(r.failed[2].issues[0], /quota/);
});

test("cast voices bake with their own ElevenLabs voice and their own folder", async () => {
  const fs = memoryFs();
  const { fetch, calls } = fakeFetch();
  const cast: Line[] = [{ id: "marcus:pip.value.01", voice: "marcus", text: "Oh! Um, I'll... put some in." }];
  assert.equal(fileFor(cast[0]), "Speech/marcus/marcus_pip_value_01");
  const r = await bakeLines({ lines: cast, manifest: null, fetch, apiKey: "K", model: MODEL, ...fs, log: () => {}, retryDelayMs: 1 });
  assert.ok(calls[0].url.startsWith(`https://api.elevenlabs.io/v1/text-to-speech/${CAST_VOICES.marcus}?`), calls[0].url);
  assert.equal(r.manifest.lines[0].voiceId, CAST_VOICES.marcus);
  assert.equal(r.manifest.lines[0].hash, lineHash(CAST_VOICES.marcus, MODEL, FORMAT, cast[0].text));
});

test("an unknown voice in lines.json is an error, not a silent default", () => {
  assert.throws(() => planBake([{ id: "x.1", voice: "nobody" as any, text: "hi" }], null, () => false, MODEL), /unknown voice "nobody"/);
});

test("plan counts what this run synthesizes per voice (for --count)", () => {
  const fs = memoryFs();
  const all: Line[] = [...lines, { id: "marcus:pip.value.01", voice: "marcus", text: "Abc." }, { id: "marcus:pip.num.three", voice: "marcus", text: "three" }];
  const p = planBake(all, null, fs.exists, MODEL);
  assert.deepEqual(p.byVoice.marcus, { lines: 2, chars: 9 });
  assert.deepEqual(p.byVoice.dot, { lines: 1, chars: lines[0].text.length });
  assert.equal(p.byVoice.jace, undefined);
});

test("the Veteran seat voice (silas) bakes with Bill", async () => {
  const fs = memoryFs();
  const { fetch, calls } = fakeFetch();
  await bakeLines({ lines: [{ id: "silas.value.01", voice: "silas", text: "Easy now." }], manifest: null, fetch, apiKey: "K", model: MODEL, ...fs, log: () => {}, retryDelayMs: 1 });
  assert.match(calls[0].url, /text-to-speech\/pqHfZKP75CvOlQylNhV4\?/);
});
