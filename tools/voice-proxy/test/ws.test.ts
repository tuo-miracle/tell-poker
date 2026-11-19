import { test } from "node:test";
import assert from "node:assert/strict";
import { acceptKey, encodeFrame, FrameReader } from "../src/ws.ts";

function masked(opcode: number, payload: Uint8Array, fin = true): Uint8Array {
  const mask = [0x37, 0xfa, 0x21, 0x3d];
  const len = payload.length;
  const head = len < 126 ? [((fin ? 0x80 : 0) | opcode), 0x80 | len] : [((fin ? 0x80 : 0) | opcode), 0x80 | 126, len >> 8, len & 255];
  const out = new Uint8Array(head.length + 4 + len);
  out.set(head, 0);
  out.set(mask, head.length);
  for (let i = 0; i < len; i++) out[head.length + 4 + i] = payload[i] ^ mask[i & 3];
  return out;
}
const enc = (s: string) => new TextEncoder().encode(s);

test("handshake accept key matches RFC 6455 example", () => {
  assert.equal(acceptKey("dGhlIHNhbXBsZSBub25jZQ=="), "s3pPLMBiTxaQ9kYGzzhZRbK+xOo=");
});

test("reads masked text and binary frames, even split across chunks", () => {
  const got: [string, unknown][] = [];
  const r = new FrameReader({ text: t => got.push(["text", t]), binary: b => got.push(["bin", Array.from(b)]), ping: () => {}, close: () => got.push(["close", null]) });
  const a = masked(1, enc('{"type":"start"}'));
  const b = masked(2, new Uint8Array(300).fill(7));
  const all = new Uint8Array(a.length + b.length);
  all.set(a); all.set(b, a.length);
  r.push(all.subarray(0, 5));
  r.push(all.subarray(5, 40));
  r.push(all.subarray(40));
  assert.deepEqual(got[0], ["text", '{"type":"start"}']);
  assert.equal(got[1][0], "bin");
  assert.equal((got[1][1] as number[]).length, 300);
  assert.ok((got[1][1] as number[]).every(x => x === 7));
});

test("joins fragmented messages and reports ping and close", () => {
  const got: string[] = [];
  let pinged = 0, closed = 0;
  const r = new FrameReader({ text: t => got.push(t), binary: () => {}, ping: () => pinged++, close: () => closed++ });
  r.push(masked(1, enc("hel"), false));
  r.push(masked(9, enc("p")));
  r.push(masked(0, enc("lo"), true));
  r.push(masked(8, new Uint8Array(0)));
  assert.deepEqual(got, ["hello"]);
  assert.equal(pinged, 1);
  assert.equal(closed, 1);
});

test("unmasked client frames are rejected (protocol error)", () => {
  let closed = 0;
  const r = new FrameReader({ text: () => {}, binary: () => {}, ping: () => {}, close: () => closed++ });
  r.push(new Uint8Array([0x81, 0x02, 0x68, 0x69]));
  assert.equal(closed, 1);
});

test("server frames are unmasked with the right length encoding", () => {
  const small = encodeFrame(1, enc("hi"));
  assert.deepEqual(Array.from(small), [0x81, 2, 0x68, 0x69]);
  const mid = encodeFrame(2, new Uint8Array(1000));
  assert.deepEqual(Array.from(mid.subarray(0, 4)), [0x82, 126, 1000 >> 8, 1000 & 255]);
  assert.equal(mid.length, 1004);
  const big = encodeFrame(2, new Uint8Array(70000));
  assert.equal(big[1], 127);
  assert.equal(big.length, 70000 + 10);
});
