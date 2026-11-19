import { test } from "node:test";
import assert from "node:assert/strict";
import { applyFacts, templateFor, numbersIn } from "../src/facts.ts";
import { sampleState } from "./helpers.ts";

test("placeholders are filled with real numbers from state", () => {
  const r = applyFacts({ text: "Pot's {pot}, and you owe {to_call}.", fact: { kind: "pot" } }, sampleState(), "dot");
  assert.equal(r.text, "Pot's 60, and you owe 20.");
  assert.equal(r.repaired, false);
});

test("stack placeholder with seat name", () => {
  const r = applyFacts({ text: "I'm sitting on {stack:rocco}, kid.", fact: { kind: "stack", seat: "rocco" } }, sampleState(), "rocco");
  assert.equal(r.text, "I'm sitting on 870, kid.");
});

test("wrong number in reply is repaired with the template answer", () => {
  const r = applyFacts({ text: "The pot is 500, easy.", fact: { kind: "pot" } }, sampleState(), "dot");
  assert.equal(r.repaired, true);
  assert.match(r.text, /\b60\b/);
  assert.doesNotMatch(r.text, /500/);
});

test("fact without the expected number gets the template", () => {
  const r = applyFacts({ text: "Plenty in there.", fact: { kind: "pot" } }, sampleState(), "dot");
  assert.equal(r.repaired, true);
  assert.equal(r.text, "The pot is 60.");
});

test("invented number without a fact drops that sentence", () => {
  const r = applyFacts({ text: "Nice try. I won 4000 last night!", fact: null }, sampleState(), "rocco");
  assert.equal(r.text, "Nice try.");
  assert.equal(r.repaired, true);
});

test("small numbers and card ranks are allowed", () => {
  const r = applyFacts({ text: "Three of us left, and that 7 scares me.", fact: null }, sampleState(), "pip");
  assert.equal(r.repaired, false);
});

test("numbers with commas are compared by value", () => {
  assert.deepEqual(numbersIn("1,000 chips and 20"), [1000, 20]);
});

test("templates speak in first/second person", () => {
  const s = sampleState();
  assert.equal(templateFor({ kind: "stack", seat: "rocco" }, s, "rocco"), "I have 870.");
  assert.equal(templateFor({ kind: "stack", seat: "you" }, s, "dot"), "You have 950.");
  assert.equal(templateFor({ kind: "stack", seat: "vex" }, s, "dot"), "Vex has 1000.");
  assert.equal(templateFor({ kind: "to_call" }, s, "dot"), "You need 20 to call.");
  assert.equal(templateFor({ kind: "blinds" }, s, "dot"), "Blinds are 5 and 10.");
  assert.equal(templateFor({ kind: "turn" }, s, "dot"), "It's your turn.");
  s.youToCall = 0;
  assert.equal(templateFor({ kind: "to_call" }, s, "dot"), "Nothing to call. You can check.");
});
