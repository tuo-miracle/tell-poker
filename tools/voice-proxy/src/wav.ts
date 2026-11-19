// WAV（PCM 16-bit）工具：讀標頭檢查長度、把 ElevenLabs 的裸 PCM 包成 WAV、base64 轉換（Workers 與 Node 都可用）。

export interface WavInfo {
  sampleRate: number;
  channels: number;
  bitsPerSample: number;
  dataOffset: number;
  dataLength: number;
  durationSec: number;
}

function ascii(b: Uint8Array, at: number, n: number): string {
  let s = "";
  for (let i = 0; i < n; i++) s += String.fromCharCode(b[at + i]);
  return s;
}

export function parseWav(bytes: Uint8Array): WavInfo {
  if (bytes.length < 12 || ascii(bytes, 0, 4) !== "RIFF" || ascii(bytes, 8, 4) !== "WAVE") throw new Error("not a RIFF/WAVE file");
  const dv = new DataView(bytes.buffer, bytes.byteOffset, bytes.byteLength);
  let p = 12;
  let fmt: { channels: number; sampleRate: number; bits: number; format: number } | null = null;
  while (p + 8 <= bytes.length) {
    const id = ascii(bytes, p, 4);
    const size = dv.getUint32(p + 4, true);
    const body = p + 8;
    if (id === "fmt ") {
      if (size < 16 || body + 16 > bytes.length) throw new Error("bad fmt chunk");
      fmt = { format: dv.getUint16(body, true), channels: dv.getUint16(body + 2, true), sampleRate: dv.getUint32(body + 4, true), bits: dv.getUint16(body + 14, true) };
    } else if (id === "data") {
      if (!fmt) throw new Error("data before fmt");
      if (fmt.format !== 1) throw new Error("only PCM WAV is supported");
      const dataLength = Math.min(size, bytes.length - body);
      const byteRate = fmt.sampleRate * fmt.channels * (fmt.bits / 8);
      if (!(byteRate > 0)) throw new Error("bad byte rate");
      return { sampleRate: fmt.sampleRate, channels: fmt.channels, bitsPerSample: fmt.bits, dataOffset: body, dataLength, durationSec: dataLength / byteRate };
    }
    p = body + size + (size & 1);
  }
  throw new Error("no data chunk");
}

export function pcm16ToWav(pcm: Uint8Array, sampleRate: number, channels = 1): Uint8Array {
  const out = new Uint8Array(44 + pcm.length);
  const dv = new DataView(out.buffer);
  const w = (at: number, s: string) => { for (let i = 0; i < s.length; i++) out[at + i] = s.charCodeAt(i); };
  w(0, "RIFF"); dv.setUint32(4, 36 + pcm.length, true); w(8, "WAVE");
  w(12, "fmt "); dv.setUint32(16, 16, true); dv.setUint16(20, 1, true); dv.setUint16(22, channels, true);
  dv.setUint32(24, sampleRate, true); dv.setUint32(28, sampleRate * channels * 2, true);
  dv.setUint16(32, channels * 2, true); dv.setUint16(34, 16, true);
  w(36, "data"); dv.setUint32(40, pcm.length, true);
  out.set(pcm, 44);
  return out;
}

export function base64ToBytes(b64: string): Uint8Array {
  const bin = atob(b64);
  const out = new Uint8Array(bin.length);
  for (let i = 0; i < bin.length; i++) out[i] = bin.charCodeAt(i);
  return out;
}

export function bytesToBase64(bytes: Uint8Array): string {
  let s = "";
  const chunk = 0x8000;
  for (let i = 0; i < bytes.length; i += chunk) s += String.fromCharCode(...bytes.subarray(i, i + chunk));
  return btoa(s);
}
