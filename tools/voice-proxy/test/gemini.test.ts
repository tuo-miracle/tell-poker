import { test } from "node:test";
import assert from "node:assert/strict";
import { buildGeminiRequest, parseGeminiResponse, callGemini, sanitizeState } from "../src/gemini.ts";
import { sampleState } from "./helpers.ts";

const input = () => ({
  audioBase64: "UklGRg==",
  addressee: "rocco" as const,
  state: sampleState(),
  directives: { rocco: { mood: "Bluffing", tell: "FidgetChips", intensity: 0.7 } },
});

test("request carries inline WAV audio, system prompt and a JSON schema", () => {
  const body = buildGeminiRequest(input());
  const parts = body.contents[0].parts;
  assert.equal(parts[0].inlineData.mimeType, "audio/wav");
  assert.equal(parts[0].inlineData.data, "UklGRg==");
  assert.match(body.systemInstruction.parts[0].text, /English/);
  assert.match(body.systemInstruction.parts[0].text, /Rocco/);
  assert.equal(body.generationConfig.responseMimeType, "application/json");
  const schema = body.generationConfig.responseSchema;
  assert.deepEqual(schema.properties.intent.enum, ["on_topic", "off_topic", "banter", "ignore"]);
  assert.deepEqual(schema.properties.fact.properties.kind.enum, ["pot", "stack", "to_call", "blinds", "turn"]);
  assert.match(parts[1].text, /"pot":60/);
  assert.match(parts[1].text, /Bluffing/);
  assert.match(parts[1].text, /addressee: rocco/i);
});

test("hole cards are stripped from state even if a client sends them", () => {
  const s: any = sampleState();
  s.seats[1].hole = ["As", "Ks"];
  s.seats[2].cards = ["Qd", "Qc"];
  s.holeCards = { pip: ["As", "Ks"] };
  const clean = JSON.stringify(sanitizeState(s));
  assert.doesNotMatch(clean, /As|Ks|Qd|Qc|hole/);
  const body = JSON.stringify(buildGeminiRequest({ ...input(), state: s }));
  assert.doesNotMatch(body, /"As"|"Ks"|"Qd"|"Qc"/);
});

test("parses JSON text and skips thought parts", () => {
  const r = parseGeminiResponse({
    candidates: [{ content: { parts: [
      { text: "thinking...", thought: true },
      { text: '{"speaker":"rocco","transcript":"hi","text":"Yo.","intent":"banter","fact":null,"emotion":"smug"}' },
    ] } }],
  });
  assert.equal(r.speaker, "rocco");
  assert.equal(r.intent, "banter");
  assert.equal(r.fact, null);
});

test("unknown intent becomes on_topic; missing text becomes empty", () => {
  const r = parseGeminiResponse({ candidates: [{ content: { parts: [{ text: '{"speaker":"x","intent":"weird"}' }] } }] });
  assert.equal(r.intent, "on_topic");
  assert.equal(r.text, "");
  assert.equal(r.transcript, "");
});

test("invalid response throws", () => {
  assert.throws(() => parseGeminiResponse({ candidates: [] }), /no candidate/i);
  assert.throws(() => parseGeminiResponse({ candidates: [{ content: { parts: [{ text: "not json" }] } }] }), /json/i);
});

test("callGemini posts to the model endpoint with the key in a header", async () => {
  let seen: { url: string; init: RequestInit } | null = null;
  const fake = async (url: string | URL | Request, init?: RequestInit) => {
    seen = { url: String(url), init: init! };
    return new Response(JSON.stringify({ candidates: [{ content: { parts: [{ text: '{"speaker":"rocco","transcript":"t","text":"x","intent":"on_topic","fact":null,"emotion":"calm"}' }] } }] }), { status: 200 });
  };
  const r = await callGemini(fake, { apiKey: "KEY123", model: "gemini-3.8-flash" }, input());
  assert.equal(r.text, "x");
  assert.equal(seen!.url, "https://generativelanguage.googleapis.com/v1beta/models/gemini-3.8-flash:generateContent");
  assert.equal((seen!.init.headers as Record<string, string>)["x-goog-api-key"], "KEY123");
  assert.doesNotMatch(String(seen!.init.body), /KEY123/);
});

test("callGemini surfaces HTTP errors", async () => {
  const fake = async () => new Response("quota", { status: 429 });
  await assert.rejects(callGemini(fake, { apiKey: "k", model: "m" }, input()), /gemini http 429/i);
});
