import { test } from "node:test";
import assert from "node:assert/strict";
import { castVoice, parseCast, synthesize, synthesizeStream, CAST_VOICES, VOICE_IDS, VOICES } from "../src/elevenlabs.ts";
import { parseWav } from "../src/wav.ts";

test("voices match the design doc", () => {
  assert.deepEqual(VOICES, {
    pip: "cgSgspJ2msm6clMCkdW9",
    rocco: "N2lVS1w4EtoT3dr4eOWO",
    vex: "pFZP5JQG7iQjIQuC4Bku",
    dot: "EXAVITQu4vr4xnSDxMaL",
    silas: "pqHfZKP75CvOlQylNhV4", // Bill (2026-11-02: Veteran seat; Reginald too)
  });
});

test("realistic characters have their own built-in male voices, none shared with the seat voices", () => {
  assert.deepEqual(CAST_VOICES, {
    marcus: "cjVigY5qzO86Huf0OWal",   // Eric
    jace: "TX3LPaxmHKxFdv7VOQHJ",     // Liam
    erik: "nPczCjzI2devNBz1zQrb",     // Brian
  });
  assert.equal(VOICE_IDS.silas, "pqHfZKP75CvOlQylNhV4", "Veteran seat (Silas) = Bill; Reginald uses this voice too");
  const ids = Object.values(VOICE_IDS);
  assert.equal(new Set(ids).size, ids.length, "every voice key has a different ElevenLabs voice");
  assert.equal(VOICE_IDS.rocco, VOICES.rocco);
  assert.equal(VOICE_IDS.marcus, CAST_VOICES.marcus);
});

test("cast map from Unity: only known seats to known voices; the speaker falls back to its seat voice", () => {
  const cast = parseCast({ pip: "marcus", rocco: "evil", vex: 3, bob: "jace", dot: "dot" });
  assert.deepEqual(cast, { pip: "marcus", dot: "dot" });
  assert.deepEqual(parseCast(null), {});
  assert.deepEqual(parseCast("pip=marcus"), {});
  assert.equal(castVoice("pip", cast), "marcus");
  assert.equal(castVoice("rocco", cast), "rocco");
  assert.equal(castVoice("vex", {}), "vex");
});

test("synthesize speaks with a cast voice", async () => {
  let url = "";
  const fake = async (u: string | URL | Request) => { url = String(u); return new Response(new Uint8Array(8), { status: 200 }); };
  await synthesize(fake, { apiKey: "k", model: "m", sampleRate: 22050 }, "erik", "Hm.");
  assert.match(url, /text-to-speech\/nPczCjzI2devNBz1zQrb\?/);
  await synthesizeStream(fake, { apiKey: "k", model: "m", sampleRate: 22050 }, "jace", "Yo.", () => {}).catch(() => {});
  assert.match(url, /text-to-speech\/TX3LPaxmHKxFdv7VOQHJ\/stream/);
});

test("synthesize requests PCM from the speaker's voice and wraps it as WAV", async () => {
  let url = "", init: RequestInit | undefined;
  const fake = async (u: string | URL | Request, i?: RequestInit) => {
    url = String(u); init = i;
    return new Response(new Uint8Array(4410 * 2), { status: 200 });
  };
  const wav = await synthesize(fake, { apiKey: "EKEY", model: "eleven_flash_v2_5", sampleRate: 22050 }, "rocco", "Pot's 60.");
  assert.equal(url, "https://api.elevenlabs.io/v1/text-to-speech/N2lVS1w4EtoT3dr4eOWO?output_format=pcm_22050");
  assert.equal((init!.headers as Record<string, string>)["xi-api-key"], "EKEY");
  const body = JSON.parse(String(init!.body));
  assert.equal(body.model_id, "eleven_flash_v2_5");
  assert.equal(body.text, "Pot's 60.");
  const info = parseWav(wav);
  assert.equal(info.sampleRate, 22050);
  assert.ok(Math.abs(info.durationSec - 0.2) < 1e-6);
});

test("synthesize surfaces HTTP errors", async () => {
  const fake = async () => new Response("nope", { status: 401 });
  await assert.rejects(synthesize(fake, { apiKey: "k", model: "m", sampleRate: 22050 }, "dot", "hi"), /elevenlabs http 401/i);
});

test("synthesizeStream uses the /stream endpoint and hands out sample-aligned PCM chunks", async () => {
  let url = "";
  const fake = async (u: string | URL | Request) => {
    url = String(u);
    return new Response(new ReadableStream<Uint8Array>({ start(c) { c.enqueue(new Uint8Array([1, 2, 3])); c.enqueue(new Uint8Array([4])); c.enqueue(new Uint8Array([5, 6, 7])); c.close(); } }));
  };
  const got: number[][] = [];
  await synthesizeStream(fake, { apiKey: "k", model: "eleven_flash_v2_5", sampleRate: 22050 }, "vex", "Hm.", b => got.push(Array.from(b)));
  assert.equal(url, "https://api.elevenlabs.io/v1/text-to-speech/pFZP5JQG7iQjIQuC4Bku/stream?output_format=pcm_22050");
  assert.deepEqual(got, [[1, 2], [3, 4], [5, 6]], "odd trailing byte dropped, odd chunks re-aligned");
});

test("synthesizeStream surfaces HTTP errors before any audio", async () => {
  const fake = async () => new Response("nope", { status: 429 });
  await assert.rejects(synthesizeStream(fake, { apiKey: "k", model: "m", sampleRate: 22050 }, "dot", "hi", () => {}), /elevenlabs http 429/i);
});
