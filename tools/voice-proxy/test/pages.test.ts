import { test } from "node:test";
import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { createHandler, type HandlerConfig } from "../src/handler.ts";
import { limiter } from "./helpers.ts";
import { renderMarkdown } from "../src/markdown.ts";
import { PRIVACY_MARKDOWN } from "../src/privacy-content.ts";

function handler() {
  const cfg: HandlerConfig = {
    geminiKey: "G", elevenKey: "E", tokens: ["tok"], model: "m", ttsModel: "t", ttsSampleRate: 22050, maxAudioSec: 10,
    fetch: async () => new Response("unexpected upstream call", { status: 500 }),
    now: () => 0, limiter: limiter({ capacity: 1 }), log: () => {},
  };
  return createHandler(cfg);
}

const get = (path: string, method = "GET") => new Request("http://x" + path, { method });

test("GET /privacy serves the privacy policy as HTML without a token", async () => {
  const res = await handler()(get("/privacy"));
  assert.equal(res.status, 200);
  assert.match(res.headers.get("content-type") ?? "", /^text\/html; charset=utf-8/);
  const html = await res.text();
  assert.match(html, /^<!doctype html>/i);
  assert.match(html, /<title>Tell — Privacy Policy<\/title>/);
  for (const h of ["What we collect and why", "Who receives your voice data", "What we store", "Your choices", "Deleting your data", "Contact"])
    assert.ok(html.includes(`>${h}</h2>`), `has heading ${h}`);
  assert.ok(html.includes("Google (Gemini API)") && html.includes("ElevenLabs"), "names the third parties");
});

test("GET /privacy/ and HEAD /privacy also work", async () => {
  assert.equal((await handler()(get("/privacy/"))).status, 200);
  const head = await handler()(get("/privacy", "HEAD"));
  assert.equal(head.status, 200);
  assert.equal(await head.text(), "");
});

test("GET / is a short landing page linking to the privacy policy", async () => {
  const res = await handler()(get("/"));
  assert.equal(res.status, 200);
  assert.match(res.headers.get("content-type") ?? "", /^text\/html/);
  const html = await res.text();
  assert.ok(html.includes(`href="/privacy"`));
  assert.ok(html.includes("Tell"));
});

test("pages only answer GET/HEAD", async () => {
  assert.equal((await handler()(get("/privacy", "POST"))).status, 405);
});

test("existing routes are unchanged", async () => {
  assert.equal((await handler()(get("/health"))).status, 200);
  assert.equal((await handler()(get("/nope"))).status, 404);
  assert.equal((await handler()(new Request("http://x/talk", { method: "POST", body: "{}" }))).status, 401);
});

test("embedded policy matches docs/privacy.md (run: node scripts/sync-privacy.ts)", () => {
  const doc = readFileSync(new URL("../../../docs/privacy.md", import.meta.url), "utf8");
  assert.equal(PRIVACY_MARKDOWN, doc);
});

test("markdown: headings, paragraphs, lists, inline styles, links", () => {
  const html = renderMarkdown("# Title\n\nSome **bold** and `code` with a [link](https://a.b/c).\nSecond line.\n\n- one\n- two **x**\n\n### Sub");
  assert.equal(html,
    "<h1>Title</h1>\n<p>Some <strong>bold</strong> and <code>code</code> with a <a href=\"https://a.b/c\">link</a>. Second line.</p>\n" +
    "<ul>\n<li>one</li>\n<li>two <strong>x</strong></li>\n</ul>\n<h3>Sub</h3>");
});

test("markdown: tables", () => {
  const html = renderMarkdown("| A | B |\n|---|---|\n| **x** | y |");
  assert.equal(html, "<table>\n<thead><tr><th>A</th><th>B</th></tr></thead>\n<tbody>\n<tr><td><strong>x</strong></td><td>y</td></tr>\n</tbody>\n</table>");
});

test("markdown: escapes HTML and placeholders like <CONTACT_EMAIL>", () => {
  assert.equal(renderMarkdown("Mail <CONTACT_EMAIL> & <script>x</script>"), "<p>Mail &lt;CONTACT_EMAIL&gt; &amp; &lt;script&gt;x&lt;/script&gt;</p>");
});

test("markdown: only http(s) and relative links become anchors", () => {
  assert.equal(renderMarkdown("[a](javascript:alert(1))"), "<p>[a](javascript:alert(1))</p>");
  assert.equal(renderMarkdown("[a](/privacy)"), "<p><a href=\"/privacy\">a</a></p>");
});
