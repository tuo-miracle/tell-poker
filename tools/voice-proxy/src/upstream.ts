// 連到 Gemini Live 的 WebSocket client（Node 24 與 Cloudflare Workers 都有內建 WebSocket）。

import type { OpenUpstream } from "./session.ts";

/** 連到 Gemini Live：Node 24／Workers 內建的 WebSocket client。 */
export const openUpstream: OpenUpstream = (url, ev) => {
  const ws = new WebSocket(url);
  ws.binaryType = "arraybuffer";
  ws.onopen = () => ev.open();
  ws.onmessage = m => ev.message(typeof m.data === "string" ? m.data : new TextDecoder().decode(m.data as ArrayBuffer));
  ws.onclose = c => ev.close(c.code, c.reason);
  ws.onerror = () => { /* 之後一定會 onclose */ };
  return { send: d => { if (ws.readyState === 1) ws.send(d); }, close: () => { try { ws.close(); } catch {} } };
};
