// Node 本機用：http server 的 upgrade → /live WebSocket（ws.ts 分幀）→ session（createLiveSession）。
// token：Unity 的 ClientWebSocket 帶 x-voice-token header；不能設 header 的 client 可以用 ?token=。
// 裝置 id（限流用）：?device=（Unity）或 x-voice-device header。

import type { Server, IncomingMessage } from "node:http";
import type { Duplex } from "node:stream";
import { acceptKey, encodeFrame, FrameReader } from "./ws.ts";
import type { ClientLink } from "./session.ts";

export interface LiveSessionLike { text(s: string): void; binary(b: Uint8Array): void; closed(): void }

export function attachLive(server: Server, open: (link: ClientLink, token: string | null, device: string | null) => LiveSessionLike): void {
  server.on("upgrade", (req: IncomingMessage, socket: Duplex, head: Buffer) => {
    const url = new URL(req.url ?? "/", "http://local");
    const key = req.headers["sec-websocket-key"];
    if (url.pathname !== "/live" || typeof key !== "string" || String(req.headers.upgrade).toLowerCase() !== "websocket") {
      socket.end("HTTP/1.1 404 Not Found\r\nConnection: close\r\n\r\n");
      return;
    }
    socket.write(`HTTP/1.1 101 Switching Protocols\r\nUpgrade: websocket\r\nConnection: Upgrade\r\nSec-WebSocket-Accept: ${acceptKey(key)}\r\n\r\n`);
    if ("setNoDelay" in socket) (socket as any).setNoDelay(true);
    let gone = false;
    const write = (op: number, b: Uint8Array) => { if (!gone) socket.write(encodeFrame(op, b)); };
    const link: ClientLink = {
      sendText: s => write(1, new TextEncoder().encode(s)),
      sendBinary: b => write(2, b),
      close: () => { if (gone) return; write(8, new Uint8Array([0x03, 0xe8])); gone = true; socket.end(); },
    };
    const header = req.headers["x-voice-token"];
    const token = typeof header === "string" && header ? header : url.searchParams.get("token");
    const devHeader = req.headers["x-voice-device"];
    const device = typeof devHeader === "string" && devHeader ? devHeader : url.searchParams.get("device") || null;
    const session = open(link, token, device);
    const reader = new FrameReader({
      text: s => session.text(s),
      binary: b => session.binary(b),
      ping: p => write(10, p),
      close: () => { session.closed(); link.close(); },
    });
    if (head && head.length) reader.push(new Uint8Array(head));
    socket.on("data", (c: Buffer) => reader.push(new Uint8Array(c.buffer, c.byteOffset, c.length)));
    socket.on("close", () => { gone = true; session.closed(); });
    socket.on("error", () => { gone = true; session.closed(); });
  });
}
