// 公開網頁（不需要 token）：GET /privacy（隱私權政策，Meta 上架要求的 Privacy Policy URL）與 GET /（簡短首頁）。
// 內容來自 docs/privacy.md（內嵌在 privacy-content.ts，見 scripts/sync-privacy.ts）。

import { renderMarkdown } from "./markdown.ts";
import { PRIVACY_MARKDOWN } from "./privacy-content.ts";

const STYLE = `body{margin:0;background:#fafaf7;color:#1d1d1b;font:16px/1.6 system-ui,-apple-system,"Segoe UI",sans-serif}
main{max-width:760px;margin:0 auto;padding:32px 16px 64px}h1{font-size:1.9em;line-height:1.2}h2{margin-top:2em;border-top:1px solid #ddd;padding-top:1em}
table{border-collapse:collapse;width:100%;display:block;overflow-x:auto}th,td{border:1px solid #ccc;padding:6px 8px;text-align:left;vertical-align:top}
code{background:#eee;padding:0 4px;border-radius:3px}a{color:#0a58ca}
@media (prefers-color-scheme:dark){body{background:#151514;color:#e8e8e3}h2{border-color:#333}th,td{border-color:#444}code{background:#2a2a28}a{color:#7fb0ff}}`;

function doc(title: string, body: string): string {
  return `<!doctype html>\n<html lang="en"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width, initial-scale=1">` +
    `<title>${title}</title><style>${STYLE}</style></head>\n<body><main>\n${body}\n</main></body></html>\n`;
}

const PRIVACY_HTML = doc("Tell — Privacy Policy", renderMarkdown(PRIVACY_MARKDOWN));
const LANDING_HTML = doc("Tell", `<h1>Tell</h1>\n<p>Mixed reality Texas Hold'em for Meta Quest: read the AI opponents' tells while they read your real hands.</p>\n` +
  `<p>This server relays the optional in-game voice chat. It has no web interface.</p>\n<p><a href="/privacy">Privacy Policy</a></p>`);

const PAGES: Record<string, string> = { "/": LANDING_HTML, "/privacy": PRIVACY_HTML, "/privacy/": PRIVACY_HTML };

/** 是網頁路徑就回 Response，否則 null（交給 API 路由）。 */
export function servePage(req: Request, pathname: string): Response | null {
  const html = PAGES[pathname];
  if (html === undefined) return null;
  if (req.method !== "GET" && req.method !== "HEAD") return new Response("GET only", { status: 405, headers: { allow: "GET, HEAD" } });
  return new Response(req.method === "HEAD" ? null : html, {
    status: 200,
    headers: { "content-type": "text/html; charset=utf-8", "cache-control": "public, max-age=300" },
  });
}
