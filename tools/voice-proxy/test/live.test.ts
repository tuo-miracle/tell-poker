import { test } from "node:test";
import assert from "node:assert/strict";
import { buildLiveSetup, parseToolCall } from "../src/live.ts";
import { sampleState } from "./helpers.ts";

test("setup asks for audio-native model with a reply() function and our own activity detection", () => {
  const m = buildLiveSetup("gemini-3.8-live", { addressee: "rocco", state: sampleState(), directives: { rocco: { mood: "Bluffing", tell: "FidgetChips", intensity: 0.7 } } });
  const s = m.setup;
  assert.equal(s.model, "models/gemini-3.8-live");
  assert.deepEqual(s.generationConfig.responseModalities, ["AUDIO"]);
  assert.deepEqual(s.generationConfig.thinkingConfig, { thinkingBudget: 0 }, "no thinking by default (2026-10-13: 560 ms vs 800 ms)");
  assert.equal(s.realtimeInputConfig.automaticActivityDetection.disabled, true);
  const fn = s.tools[0].functionDeclarations[0];
  assert.equal(fn.name, "reply");
  assert.deepEqual(fn.parameters.properties.intent.enum, ["on_topic", "off_topic", "banter", "ignore"]);
  assert.deepEqual(fn.parameters.properties.fact_kind.enum, ["none", "pot", "stack", "to_call", "blinds", "turn"]);
  const sys = s.systemInstruction.parts[0].text;
  assert.match(sys, /Rocco/);
  assert.match(sys, /call the reply function/i);
  assert.match(sys, /"pot":60/);
  assert.match(sys, /Addressee: rocco/);
  assert.match(sys, /Bluffing/);
});

test("thinking budget is configurable and can be left out", () => {
  const input = { addressee: "dot" as const, state: sampleState(), directives: {} };
  assert.deepEqual(buildLiveSetup("m", input, { thinkingBudget: 256 }).setup.generationConfig.thinkingConfig, { thinkingBudget: 256 });
  assert.equal(buildLiveSetup("m", input, { thinkingBudget: null }).setup.generationConfig.thinkingConfig, undefined);
});

test("hole cards never reach the Live setup", () => {
  const s: any = sampleState();
  s.seats[1].hole = ["As", "Ks"];
  s.holeCards = { pip: ["Qd", "Qc"] };
  const text = JSON.stringify(buildLiveSetup("m", { addressee: "pip", state: s, directives: {} }));
  assert.doesNotMatch(text, /"As\\?"|Ks|Qd|Qc|hole\\?"/);
});

test("tool call becomes a model reply; 'none'/'null' facts and seats are dropped", () => {
  const r = parseToolCall({ toolCall: { functionCalls: [{ id: "1", name: "reply", args: {
    transcript: "Dot, how much is in the pot?", speaker: "dot", intent: "on_topic", fact_kind: "pot", fact_seat: "null",
    text: "There is {pot} in the pot.", emotion: "neutral" } }] } });
  assert.deepEqual(r, { speaker: "dot", transcript: "Dot, how much is in the pot?", intent: "on_topic", fact: { kind: "pot", seat: null }, text: "There is {pot} in the pot.", emotion: "neutral" });
  const b = parseToolCall({ toolCall: { functionCalls: [{ name: "reply", args: { transcript: "bluffing?", speaker: "Rocco", intent: "banter", fact_kind: "none", text: "Maybe.", emotion: "" } }] } });
  assert.equal(b!.fact, null);
  assert.equal(b!.emotion, "neutral");
  assert.equal(b!.speaker, null, "unknown casing is not a character");
  const st = parseToolCall({ toolCall: { functionCalls: [{ name: "reply", args: { transcript: "x", speaker: "dot", intent: "weird", fact_kind: "stack", fact_seat: "Vex", text: "t", emotion: "calm" } }] } });
  assert.deepEqual(st!.fact, { kind: "stack", seat: "vex" });
  assert.equal(st!.intent, "on_topic");
});

test("messages without a reply() call are not replies", () => {
  assert.equal(parseToolCall({ serverContent: { turnComplete: true } }), null);
  assert.equal(parseToolCall({ toolCall: { functionCalls: [{ name: "other", args: {} }] } }), null);
});
