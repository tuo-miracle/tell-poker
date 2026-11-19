import { test } from "node:test";
import assert from "node:assert/strict";
import { nameInTranscript, resolveSpeaker } from "../src/addressee.ts";

test("name in transcript wins over gaze", () => {
  assert.equal(resolveSpeaker("Rocco, are you bluffing?", "pip"), "rocco");
});

test("dealer word maps to dot", () => {
  assert.equal(nameInTranscript("Dealer, how much is the pot?"), "dot");
});

test("first mentioned name wins", () => {
  assert.equal(nameInTranscript("Vex, is Pip scared?"), "vex");
});

test("names match whole words only", () => {
  assert.equal(nameInTranscript("I'll pipe down and dot my i's"), "dot");
  assert.equal(nameInTranscript("pipeline vexing"), null);
});

test("falls back to gazed addressee, then dealer", () => {
  assert.equal(resolveSpeaker("are you bluffing?", "vex"), "vex");
  assert.equal(resolveSpeaker("what's the pot?", "auto"), "dot");
  assert.equal(resolveSpeaker("", "bogus" as never), "dot");
});
