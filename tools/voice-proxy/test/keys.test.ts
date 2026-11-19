import { test } from "node:test";
import assert from "node:assert/strict";
import { mkdtempSync, writeFileSync } from "node:fs";
import { tmpdir } from "node:os";
import { join } from "node:path";
import { loadKeys } from "../src/keys.ts";

test("env vars win over files", () => {
  const d = mkdtempSync(join(tmpdir(), "vk"));
  writeFileSync(join(d, "gemini.key"), "fileG\n");
  writeFileSync(join(d, "elevenlabs.key"), "fileE\n");
  const k = loadKeys({ GEMINI_API_KEY: "envG" }, [d]);
  assert.equal(k.gemini, "envG");
  assert.equal(k.eleven, "fileE", "trimmed file content");
  assert.equal(k.source.eleven, join(d, "elevenlabs.key"));
});

test("first directory that has the file is used; missing keys throw without printing values", () => {
  const a = mkdtempSync(join(tmpdir(), "vk"));
  const b = mkdtempSync(join(tmpdir(), "vk"));
  writeFileSync(join(b, "gemini.key"), "G2");
  writeFileSync(join(b, "elevenlabs.key"), "E2");
  assert.equal(loadKeys({}, [a, b]).gemini, "G2");
  assert.throws(() => loadKeys({}, [a]), (e: Error) => /gemini/i.test(e.message) && !/G2/.test(e.message));
});
