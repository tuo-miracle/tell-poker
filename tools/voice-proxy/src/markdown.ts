// 極簡 Markdown → HTML（只給 /privacy 用；零依賴，可在 Workers 跑）。
// 支援：# 標題、段落、- 清單、| 表格、**粗體**、`code`、[文字](https://… 或 /…)。其他一律當純文字並跳脫。

function escape(s: string): string {
  return s.replace(/&/g, "&amp;").replace(/</g, "&lt;").replace(/>/g, "&gt;").replace(/"/g, "&quot;");
}

function inline(s: string): string {
  // 反引號內不再處理粗體與連結
  return s.split(/(`[^`]*`)/).map(part => {
    if (part.length >= 2 && part.startsWith("`") && part.endsWith("`")) return `<code>${escape(part.slice(1, -1))}</code>`;
    return escape(part)
      .replace(/\*\*(.+?)\*\*/g, "<strong>$1</strong>")
      .replace(/\[([^\]]+)\]\(((?:https?:\/\/|\/)[^)\s]*)\)/g, `<a href="$2">$1</a>`);
  }).join("");
}

function cells(line: string): string[] {
  return line.trim().replace(/^\|/, "").replace(/\|$/, "").split("|").map(c => c.trim());
}

export function renderMarkdown(md: string): string {
  const out: string[] = [];
  const lines = md.replace(/\r\n/g, "\n").split("\n");
  let para: string[] = [];
  let list: string[] = [];
  const flush = () => {
    if (para.length) { out.push(`<p>${inline(para.join(" "))}</p>`); para = []; }
    if (list.length) { out.push(`<ul>\n${list.map(i => `<li>${inline(i)}</li>`).join("\n")}\n</ul>`); list = []; }
  };
  for (let i = 0; i < lines.length; i++) {
    const line = lines[i];
    const h = /^(#{1,6})\s+(.*)$/.exec(line);
    if (!line.trim()) { flush(); continue; }
    if (h) { flush(); out.push(`<h${h[1].length}>${inline(h[2].trim())}</h${h[1].length}>`); continue; }
    const li = /^[-*]\s+(.*)$/.exec(line);
    if (li) { if (para.length) flush(); list.push(li[1]); continue; }
    if (line.trimStart().startsWith("|")) {
      flush();
      const rows: string[] = [];
      while (i < lines.length && lines[i].trimStart().startsWith("|")) rows.push(lines[i++]);
      i--;
      const head = cells(rows[0]);
      const body = rows.slice(1).filter(r => !/^\s*\|[\s|:-]+\|\s*$/.test(r)).map(cells);
      out.push(`<table>\n<thead><tr>${head.map(c => `<th>${inline(c)}</th>`).join("")}</tr></thead>\n<tbody>\n` +
        body.map(r => `<tr>${r.map(c => `<td>${inline(c)}</td>`).join("")}</tr>`).join("\n") + `\n</tbody>\n</table>`);
      continue;
    }
    if (list.length) flush();
    para.push(line.trim());
  }
  flush();
  return out.join("\n");
}
