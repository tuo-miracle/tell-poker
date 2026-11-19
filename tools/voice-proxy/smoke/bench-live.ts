// 手動量測（不進 CI）：Gemini Live API（BidiGenerateContent WebSocket）邊講邊送音訊，講完到回話的延遲。
//   node smoke/bench-live.ts [model] [--runs N] [--modality TEXT|AUDIO]
// 音訊以真實速度分 100 ms 一塊送出（模擬玩家正在講），關掉伺服器端 VAD，最後送 activityEnd；
// 計時從 activityEnd（＝我們的 VAD 判定講完）算起：第一段文字、整個回合完成。連線＋setup 另計（可以在盯著角色時就先連）。

import { execFileSync } from "node:child_process";
import { dirname, join, resolve } from "node:path";
import { fileURLToPath } from "node:url";
import { loadKeys } from "../src/keys.ts";
import { systemPrompt } from "../src/characters.ts";
import { parseWav, bytesToBase64 } from "../src/wav.ts";
import { QUESTIONS, STATE, DIRECTIVES, questionWav, stats } from "./questions.ts";

const here = dirname(fileURLToPath(import.meta.url));
const checkout = resolve(here, "../../..");
let main: string | null = null;
try { main = dirname(execFileSync("git", ["-C", checkout, "rev-parse", "--path-format=absolute", "--git-common-dir"], { encoding: "utf8" }).trim()); } catch {}
const keys = loadKeys(process.env, [process.env.VOICE_SECRETS_DIR, join(checkout, ".secrets"), main && join(main, ".secrets")].filter((d): d is string => !!d));

const args = process.argv.slice(2);
const opt = (k: string, d: string) => { const i = args.indexOf(k); if (i < 0) return d; const v = args[i + 1]; args.splice(i, 2); return v; };
const runs = Number(opt("--runs", "1"));
const modality = opt("--modality", "TEXT");
const thinkingOpt = opt("--thinking", "");
const useTool = args.includes("--tool");
if (useTool) args.splice(args.indexOf("--tool"), 1);
const model = args[0] ?? "gemini-3.8-live";

// 結構化回話改成 function call（Live 的原生音訊模型不支援 TEXT 輸出）：模型不開口，只呼叫 reply()
const REPLY_FN = {
  name: "reply",
  description: "Deliver the character's reply. Always call this exactly once per player utterance instead of speaking.",
  parameters: {
    type: "OBJECT",
    properties: {
      transcript: { type: "STRING" }, speaker: { type: "STRING", enum: ["pip", "rocco", "vex", "dot"] },
      intent: { type: "STRING", enum: ["on_topic", "off_topic", "banter", "ignore"] },
      fact_kind: { type: "STRING", enum: ["none", "pot", "stack", "to_call", "blinds", "turn"] }, fact_seat: { type: "STRING" },
      text: { type: "STRING" }, emotion: { type: "STRING" },
    },
    required: ["transcript", "speaker", "intent", "fact_kind", "text", "emotion"],
  },
};

const sleep = (ms: number) => new Promise(r => setTimeout(r, ms));

function instruction(addressee: string) {
  return systemPrompt() + "\n\nOutput: reply with ONLY one JSON object, no markdown: " +
    '{"transcript":"...","speaker":"pip|rocco|vex|dot","intent":"on_topic|off_topic|banter|ignore","fact":{"kind":"pot|stack|to_call|blinds|turn","seat":"NAME or null"} or null,"text":"...","emotion":"..."}' +
    `\n\nAddressee: ${addressee} (the character the player is looking at)\nGame state (public only): ${JSON.stringify(STATE)}\nActing directives: ${JSON.stringify(DIRECTIVES)}`;
}

async function once(q: (typeof QUESTIONS)[number]) {
  const wav = await questionWav(q, keys.eleven);
  const info = parseWav(wav);
  const pcm = wav.subarray(info.dataOffset, info.dataOffset + info.dataLength);
  const url = `wss://generativelanguage.googleapis.com/ws/google.ai.generativelanguage.v1beta.GenerativeService.BidiGenerateContent?key=${encodeURIComponent(keys.gemini)}`;
  const t0 = performance.now();
  const ws = new WebSocket(url);
  let setupAt = 0, endAt = 0, firstAt = 0, doneAt = 0, text = "", err = "", transcriptOut = "", inTranscript = "";
  const done = new Promise<void>(res => {
    ws.onmessage = async ev => {
      const s = typeof ev.data === "string" ? ev.data : await (ev.data as Blob).text();
      const m = JSON.parse(s);
      if (m.setupComplete) { setupAt = performance.now(); return; }
      if (m.toolCall) {
        if (!firstAt) firstAt = performance.now();
        text += JSON.stringify(m.toolCall.functionCalls?.map((f: any) => f.args));
        doneAt = performance.now(); res(); ws.close(); return;
      }
      const sc = m.serverContent;
      if (sc) {
        for (const p of sc.modelTurn?.parts ?? []) {
          if (p.text && !p.thought) { if (!firstAt) firstAt = performance.now(); text += p.text; }
          if (p.inlineData && !firstAt) firstAt = performance.now();
        }
        if (sc.outputTranscription?.text) transcriptOut += sc.outputTranscription.text;
        if (sc.inputTranscription?.text) inTranscript += sc.inputTranscription.text;
        if (sc.turnComplete) { doneAt = performance.now(); res(); ws.close(); }
      }
    };
    ws.onclose = ev => { if (!doneAt) { err = `closed ${ev.code} ${ev.reason}`; res(); } };
    ws.onerror = () => { err ||= "ws error"; };
  });
  await new Promise<void>((res, rej) => { ws.onopen = () => res(); setTimeout(() => rej(new Error("connect timeout")), 8000); });
  const gen: any = { responseModalities: [modality] };
  if (thinkingOpt === "budget0") gen.thinkingConfig = { thinkingBudget: 0 };
  else if (thinkingOpt) gen.thinkingConfig = { thinkingLevel: thinkingOpt };
  const setup: any = {
    model: "models/" + model, generationConfig: gen,
    systemInstruction: { parts: [{ text: instruction(q.addressee) }] },
    realtimeInputConfig: { automaticActivityDetection: { disabled: true } },
  };
  if (modality === "AUDIO") setup.outputAudioTranscription = {};
  if (useTool) {
    setup.tools = [{ functionDeclarations: [REPLY_FN] }];
    setup.systemInstruction.parts[0].text += "\n\nNever speak out loud. For every player utterance, call the reply function exactly once with the fields above (fact_kind none when there is no fact).";
  }
  ws.send(JSON.stringify({ setup }));
  while (!setupAt && !err && ws.readyState === 1) await sleep(10);
  if (err || !setupAt) { await done; return { err: err || "no setup", connect: 0 }; }
  const connect = setupAt - t0;
  ws.send(JSON.stringify({ realtimeInput: { activityStart: {} } }));
  const chunk = 3200; // 100 ms @ 16 kHz 16-bit
  const start = performance.now();
  for (let i = 0, k = 0; i < pcm.length; i += chunk, k++) {
    ws.send(JSON.stringify({ realtimeInput: { audio: { data: bytesToBase64(pcm.subarray(i, i + chunk)), mimeType: "audio/pcm;rate=16000" } } }));
    const due = start + (k + 1) * 100;
    const wait = due - performance.now();
    if (wait > 0) await sleep(wait);
  }
  ws.send(JSON.stringify({ realtimeInput: { activityEnd: {} } }));
  endAt = performance.now();
  await Promise.race([done, sleep(15000)]);
  return { connect, first: firstAt ? firstAt - endAt : NaN, done: doneAt ? doneAt - endAt : NaN, text: text || transcriptOut, err, inTranscript };
}

const rows: { first: number; done: number; connect: number }[] = [];
for (let r = 0; r < runs; r++) for (const q of QUESTIONS) {
  let x: any;
  try { x = await once(q); } catch (e) { x = { err: (e as Error).message }; }
  if (x.err && !x.text) { console.log(`${model} | ${q.say} | ERROR ${x.err}`); if (/closed 1007|closed 1008|not found|not supported/i.test(x.err)) process.exit(1); continue; }
  rows.push({ first: x.first, done: x.done, connect: x.connect });
  console.log(`${model} | connect=${Math.round(x.connect)} first=${Math.round(x.first)} done=${Math.round(x.done)} | ${q.say} | ${x.text.replace(/\s+/g, " ").slice(0, 260)}`);
}
if (rows.length) {
  const f = (k: "first" | "done" | "connect") => { const s = stats(rows.map(r => r[k]).filter(Number.isFinite)); return `${s.median}/${s.p90}`; };
  console.log(`SUMMARY ${model} ${modality} n=${rows.length} median/p90 connect=${f("connect")} first(after end)=${f("first")} done(after end)=${f("done")}`);
}
