// docs/privacy.md → src/privacy-content.ts（中繼部署時只同步 tools/voice-proxy，所以內容要內嵌；Workers 也讀不到檔案）。
// 改了 docs/privacy.md 就跑：node scripts/sync-privacy.ts（test/pages.test.ts 會檢查兩邊一致）。
import { readFileSync, writeFileSync } from "node:fs";

const md = readFileSync(new URL("../../../docs/privacy.md", import.meta.url), "utf8");
const out = new URL("../src/privacy-content.ts", import.meta.url);
writeFileSync(out, "// 自動產生：node scripts/sync-privacy.ts（來源 docs/privacy.md）。不要手改。\n" +
  `export const PRIVACY_MARKDOWN = ${JSON.stringify(md)};\n`);
console.log(`privacy-content.ts updated (${md.length} chars)`);
