// 最小 WebSocket 伺服器端分幀（RFC 6455）：只給 node-server.ts 用（零依賴；Workers 上用內建的 WebSocketPair）。
// 支援：遮罩的 client frame、分段訊息、ping／close；送出的 frame 不遮罩。

import { createHash } from "node:crypto";

export function acceptKey(key: string): string {
  return createHash("sha1").update(key + "258EAFA5-E914-47DA-95CA-C5AB0DC85B11").digest("base64");
}

export function encodeFrame(opcode: number, payload: Uint8Array): Uint8Array {
  const n = payload.length;
  const head = n < 126 ? 2 : n < 65536 ? 4 : 10;
  const out = new Uint8Array(head + n);
  out[0] = 0x80 | opcode;
  if (n < 126) out[1] = n;
  else if (n < 65536) { out[1] = 126; out[2] = n >> 8; out[3] = n & 255; }
  else { out[1] = 127; new DataView(out.buffer).setBigUint64(2, BigInt(n)); }
  out.set(payload, head);
  return out;
}

export interface FrameHandlers {
  text(s: string): void;
  binary(b: Uint8Array): void;
  ping(payload: Uint8Array): void;
  close(): void;
}

const MAX_MESSAGE = 4 * 1024 * 1024;

export class FrameReader {
  private buf = new Uint8Array(0);
  private parts: Uint8Array[] = [];
  private partOp = 0;
  private dead = false;
  private readonly h: FrameHandlers;
  constructor(h: FrameHandlers) { this.h = h; }

  push(chunk: Uint8Array): void {
    if (this.dead) return;
    const b = new Uint8Array(this.buf.length + chunk.length);
    b.set(this.buf); b.set(chunk, this.buf.length);
    this.buf = b;
    for (;;) {
      if (this.buf.length < 2) return;
      const fin = (this.buf[0] & 0x80) !== 0;
      const op = this.buf[0] & 0x0f;
      const masked = (this.buf[1] & 0x80) !== 0;
      let len = this.buf[1] & 0x7f;
      let p = 2;
      if (len === 126) { if (this.buf.length < 4) return; len = (this.buf[2] << 8) | this.buf[3]; p = 4; }
      else if (len === 127) {
        if (this.buf.length < 10) return;
        len = Number(new DataView(this.buf.buffer, this.buf.byteOffset).getBigUint64(2)); p = 10;
      }
      if (!masked || len > MAX_MESSAGE) { this.fail(); return; } // client frame 一定要遮罩
      if (this.buf.length < p + 4 + len) return;
      const mask = this.buf.subarray(p, p + 4);
      const payload = new Uint8Array(len);
      for (let i = 0; i < len; i++) payload[i] = this.buf[p + 4 + i] ^ mask[i & 3];
      this.buf = this.buf.slice(p + 4 + len);
      this.frame(fin, op, payload);
      if (this.dead) return;
    }
  }

  private frame(fin: boolean, op: number, payload: Uint8Array) {
    if (op === 8) { this.dead = true; this.h.close(); return; }
    if (op === 9) { this.h.ping(payload); return; }
    if (op === 10) return;
    if (op === 1 || op === 2) { this.partOp = op; this.parts = [payload]; }
    else if (op === 0 && this.partOp) this.parts.push(payload);
    else { this.fail(); return; }
    if (!fin) return;
    const total = this.parts.reduce((n, x) => n + x.length, 0);
    const msg = new Uint8Array(total);
    let at = 0;
    for (const x of this.parts) { msg.set(x, at); at += x.length; }
    const kind = this.partOp;
    this.parts = []; this.partOp = 0;
    if (kind === 1) this.h.text(new TextDecoder().decode(msg));
    else this.h.binary(msg);
  }

  private fail() { this.dead = true; this.h.close(); }
}
