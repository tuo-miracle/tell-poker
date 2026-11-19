// 第 4 位對手「老手」Silas（2026-11-02）：語音對話也能跟他說話（名字、個性、罐頭句、聲音）。
import { test } from "node:test";
import assert from "node:assert/strict";
import { CHARACTERS, isCharacter, nameInTranscript, resolveSpeaker } from "../src/addressee.ts";
import { CANNED, PERSONAS, cannedLine, systemPrompt } from "../src/characters.ts";
import { RESPONSE_SCHEMA, parseGeminiResponse } from "../src/gemini.ts";
import { REPLY_FN } from "../src/live.ts";
import { VOICES } from "../src/elevenlabs.ts";

test("Silas is a character the player can address", () => {
  assert.ok(CHARACTERS.includes("silas" as never));
  assert.ok(isCharacter("silas"));
  assert.equal(nameInTranscript("Silas, are you bluffing?"), "silas");
  assert.equal(resolveSpeaker("how many chips do you have?", "silas"), "silas");
  assert.equal(nameInTranscript("silasx"), null);
});

test("Silas has a tight-aggressive, few-words, dry persona and his own canned lines", () => {
  assert.match(PERSONAS.silas, /^Silas/);
  assert.match(PERSONAS.silas, /tight/i);
  assert.match(PERSONAS.silas, /aggressive/i);
  assert.match(PERSONAS.silas, /dry/i);
  assert.doesNotMatch(PERSONAS.rocco, /veteran/i, "only Silas is the veteran (avoid confusing the model)");
  assert.equal(CANNED.silas.length, 2);
  for (const c of CHARACTERS) if (c !== "silas") for (const l of CANNED.silas) assert.ok(!CANNED[c].includes(l), `${l} is also ${c}'s`);
  assert.equal(cannedLine("silas", 1), CANNED.silas[1]);
  assert.match(systemPrompt(), /Silas/);
  assert.match(systemPrompt(), /Pip, Rocco, Vex, Silas, Dot/);
  assert.match(systemPrompt(), /you, pip, rocco, vex or silas/);
});

test("models may answer as Silas (schema enums) and Silas speaks with Bill", () => {
  assert.deepEqual(RESPONSE_SCHEMA.properties.speaker.enum, [...CHARACTERS]);
  assert.deepEqual(REPLY_FN.parameters.properties.speaker.enum, [...CHARACTERS]);
  assert.equal(VOICES.silas, "pqHfZKP75CvOlQylNhV4");
  const r = parseGeminiResponse({ candidates: [{ content: { parts: [{ text: JSON.stringify({ transcript: "Silas?", speaker: "silas", intent: "banter", fact: null, text: "Hm.", emotion: "dry" }) }] } }] });
  assert.equal(r.speaker, "silas");
});
