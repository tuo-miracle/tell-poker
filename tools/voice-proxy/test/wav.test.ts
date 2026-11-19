import { test } from "node:test";
import assert from "node:assert/strict";
import { parseWav, pcm16ToWav, base64ToBytes, bytesToBase64 } from "../src/wav.ts";

test("pcm16ToWav writes a 44-byte PCM header that parseWav reads back", () => {
  const pcm = new Uint8Array(32000); // 1 s @ 16 kHz mono 16-bit
  const wav = pcm16ToWav(pcm, 16000);
  assert.equal(wav.length, 44 + 32000);
  const info = parseWav(wav);
  assert.equal(info.sampleRate, 16000);
  assert.equal(info.channels, 1);
  assert.equal(info.bitsPerSample, 16);
  assert.equal(info.dataLength, 32000);
  assert.ok(Math.abs(info.durationSec - 1) < 1e-9);
});

test("parseWav rejects non-RIFF data", () => {
  assert.throws(() => parseWav(new Uint8Array(100)), /RIFF/);
});

test("base64 round trip", () => {
  const b = new Uint8Array([0, 1, 2, 250, 255]);
  assert.deepEqual(base64ToBytes(bytesToBase64(b)), b);
});
