import { test } from "node:test";
import assert from "node:assert/strict";
import { createServer } from "node:http";
import { attachLive } from "../src/node-live.ts";

// 用 Node 24 內建的 WebSocket client 連真的 http server，驗證握手、分幀與 token header。
test("upgrade on /live hands text and binary to the session and streams its replies back", async () => {
  const seen: { token: string | null; device: string | null; texts: string[]; bins: number[] } = { token: null, device: null, texts: [], bins: [] };
  const server = createServer((_q, r) => { r.writeHead(404); r.end(); });
  attachLive(server, (link, token, device) => {
    seen.token = token;
    seen.device = device;
    return {
      text(s) { seen.texts.push(s); if (JSON.parse(s).type === "end") { link.sendText('{"type":"reply","text":"hi"}'); link.sendBinary(new Uint8Array(70000).fill(9)); link.sendText('{"type":"done"}'); link.close(); } },
      binary(b) { seen.bins.push(b.length); },
      closed() {},
    };
  });
  await new Promise<void>(r => server.listen(0, "127.0.0.1", () => r()));
  const port = (server.address() as any).port;
  // 瀏覽器式 WebSocket 不能設 header：token 改放 query（Unity 的 ClientWebSocket 用 header）
  const ws = new WebSocket(`ws://127.0.0.1:${port}/live?token=tok&device=0f8fad5b-d9cb-469f-a165-70867728950e`);
  ws.binaryType = "arraybuffer";
  const got: (string | number)[] = [];
  const closed = new Promise<void>(r => { ws.onclose = () => r(); });
  ws.onmessage = ev => got.push(typeof ev.data === "string" ? ev.data : (ev.data as ArrayBuffer).byteLength);
  await new Promise<void>(r => { ws.onopen = () => r(); });
  ws.send('{"type":"start"}');
  ws.send(new Uint8Array(3200));
  ws.send('{"type":"end"}');
  await closed;
  server.close();
  assert.equal(seen.token, "tok");
  assert.equal(seen.device, "0f8fad5b-d9cb-469f-a165-70867728950e", "device id from the query string");
  assert.deepEqual(seen.texts, ['{"type":"start"}', '{"type":"end"}']);
  assert.deepEqual(seen.bins, [3200]);
  assert.deepEqual(got, ['{"type":"reply","text":"hi"}', 70000, '{"type":"done"}']);
});

test("non-/live upgrades are refused", async () => {
  const server = createServer();
  attachLive(server, () => { throw new Error("should not be called"); });
  await new Promise<void>(r => server.listen(0, "127.0.0.1", () => r()));
  const port = (server.address() as any).port;
  const ws = new WebSocket(`ws://127.0.0.1:${port}/other`);
  const outcome = await new Promise<string>(r => { ws.onopen = () => r("open"); ws.onerror = () => r("error"); });
  server.close();
  assert.equal(outcome, "error");
});

test("device id: x-voice-device header wins, missing gives null", async () => {
  const seen: (string | null)[] = [];
  const server = createServer();
  attachLive(server, (link, _token, device) => { seen.push(device); link.close(); return { text() {}, binary() {}, closed() {} }; });
  await new Promise<void>(r => server.listen(0, "127.0.0.1", () => r()));
  const port = (server.address() as any).port;
  const once = (url: string, headers?: Record<string, string>) => new Promise<void>(r => {
    // Node 24 的 WebSocket 可以帶 headers（undici 擴充）
    const ws = new (WebSocket as any)(url, headers ? { headers } : undefined);
    ws.onclose = () => r(); ws.onerror = () => r();
  });
  await once(`ws://127.0.0.1:${port}/live?token=tok`);
  await once(`ws://127.0.0.1:${port}/live?device=aaaa`, { "x-voice-device": "bbbb" });
  server.close();
  assert.deepEqual(seen, [null, "bbbb"]);
});
