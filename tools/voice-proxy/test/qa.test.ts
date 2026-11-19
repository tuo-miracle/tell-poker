import { test } from "node:test";
import assert from "node:assert/strict";
import {
  normalizeWords, compareText, numberToWords, mp3Duration, padMp3, durationFlags, checkClip,
  buildTranscribeRequest, parseTranscript, qaKey, verifyClips, transcribe, type ClipInput,
} from "../bake/qa.ts";

test("normalize: case, punctuation, dashes and ellipses are ignored", () => {
  assert.deepEqual(normalizeWords("Let's peek — cup your hand... Lift!"), ["let", "us", "peek", "cup", "your", "hand", "lift"]);
  assert.deepEqual(normalizeWords("all-in"), ["all", "in"]);
});

test("normalize: contractions expand the same way on both sides", () => {
  assert.deepEqual(normalizeWords("Can't check. Don't pull. I'll go. You're up. I'm in. We've won."),
    ["can", "not", "check", "do", "not", "pull", "i", "will", "go", "you", "are", "up", "i", "am", "in", "we", "have", "won"]);
  assert.deepEqual(normalizeWords("Can’t"), ["can", "not"], "curly apostrophe");
  assert.deepEqual(normalizeWords("cannot"), ["can", "not"]);
});

test("numbers and number words are equivalent", () => {
  assert.equal(numberToWords(0), "zero");
  assert.equal(numberToWords(15), "fifteen");
  assert.equal(numberToWords(42), "forty two");
  assert.equal(numberToWords(1200), "one thousand two hundred");
  assert.equal(numberToWords(2000000), "two million");
  assert.deepEqual(normalizeWords("4 times, 1,000 chips, 50%"), ["four", "times", "one", "thousand", "chips", "fifty", "percent"]);
  assert.ok(compareText("You looked at your cards four times.", "You looked at your cards 4 times.").ok);
});

test("filler spellings are unified; hm runs collapse", () => {
  assert.deepEqual(normalizeWords("Umm, uhh... Hmmm. Mm-hmm"), ["um", "uh", "hm"]);
  assert.ok(compareText("Hmm. Hmm hmm hmm.", "Hmm, hmm-hmm.").ok);
  assert.ok(compareText("Oh! Um, I'll... put some in.", "Oh, um, I'll put some in.").ok);
  assert.ok(compareText("Okay.", "OK.").ok);
});

test("the Quest bug: extra mumbled words are flagged as extras", () => {
  const r = compareText("Can't check here. Call or fold.", "Can't check here, uh, try or need it. Call or fold.");
  assert.equal(r.ok, false);
  assert.deepEqual(r.extra, ["uh", "try", "or", "need", "it"]);
  assert.deepEqual(r.missing, []);
  assert.ok(r.fillers >= 1);
});

test("fillers the text does not contain are extras, fillers it does contain are fine", () => {
  assert.equal(compareText("Nice hand.", "Um, nice hand.").ok, false);
  assert.ok(compareText("Um, sorry, I didn't catch that.", "Um, sorry, I didn't catch that.").ok);
});

test("missing words and substitutions are reported", () => {
  const m = compareText("Pinch both cards and lift them up.", "Pinch both cards and lift up.");
  assert.equal(m.ok, false);
  assert.deepEqual(m.missing, ["them"]);
  const s = compareText("Three on the board.", "Tree on the board.");
  assert.equal(s.ok, false);
  assert.deepEqual(s.substituted, [{ want: "three", heard: "tree" }]);
});

test("mumbling markers count as extras", () => {
  const r = compareText("Bet.", "Bet [mumbling]");
  assert.equal(r.ok, false);
  assert.deepEqual(r.extra, ["[mumble]"]);
});

test("character names: common transcription spellings are accepted", () => {
  assert.ok(compareText("It's Vex's turn.", "It's Vecks's turn.").ok);
  assert.ok(compareText("Rocco eats again!", "Rocko eats again!").ok);
});

test("short fragments are compared exactly", () => {
  assert.ok(compareText("to call", "To call.").ok);
  assert.equal(compareText("seventy", "seventeen").ok, false);
  assert.equal(compareText("and", "and uh").ok, false);
  assert.equal(compareText("wins in", "wins").ok, false);
});

// MPEG-2 Layer III, 22.05 kHz, 32 kbps, mono：每幀 576 取樣、72*32000/22050 = 104 位元組
function fakeMp3(frames: number, id3 = false): Uint8Array {
  const parts: number[] = [];
  if (id3) parts.push(0x49, 0x44, 0x33, 4, 0, 0, 0, 0, 0, 10, ...new Array(10).fill(0));
  for (let i = 0; i < frames; i++) { parts.push(0xff, 0xf3, 0x40, 0xc4); for (let k = 4; k < 104; k++) parts.push(0); }
  return new Uint8Array(parts);
}

test("mp3 duration counts frames (skipping an ID3 tag)", () => {
  assert.ok(Math.abs(mp3Duration(fakeMp3(100)) - 100 * 576 / 22050) < 1e-6);
  assert.ok(Math.abs(mp3Duration(fakeMp3(50, true)) - 50 * 576 / 22050) < 1e-6);
  assert.equal(mp3Duration(new Uint8Array([1, 2, 3])), 0);
});

test("duration far above the voice's median seconds per word is flagged", () => {
  const clips = [
    ...Array.from({ length: 10 }, (_, i) => ({ id: "dot.ok." + i, voice: "dot", text: "one two three four five six", seconds: 2.0 })),
    { id: "dot.slow", voice: "dot", text: "Can't check here. Call or fold.", seconds: 6.5 },
    { id: "dot.num.zero", voice: "dot", text: "zero", seconds: 0.6 },
  ];
  const f = durationFlags(clips);
  assert.deepEqual([...f.keys()], ["dot.slow"]);
  assert.match(f.get("dot.slow")!, /s\/word/);
});

test("checkClip combines word comparison and duration", () => {
  const ok = checkClip({ text: "Call or fold.", transcript: "Call or fold." });
  assert.equal(ok.ok, true);
  const bad = checkClip({ text: "Call or fold.", transcript: "Call or, uh, fold." });
  assert.equal(bad.ok, false);
  assert.match(bad.issues.join(" "), /extra: uh/);
  const slow = checkClip({ text: "Call or fold.", transcript: "Call or fold.", durationFlag: "4.0 s/word" });
  assert.equal(slow.ok, false);
  assert.match(slow.issues.join(" "), /duration/);
});

test("transcribe request: audio inline, temperature 0, no intended text leaked", () => {
  const req = buildTranscribeRequest("QUJD");
  const s = JSON.stringify(req);
  assert.equal(req.generationConfig.temperature, 0);
  assert.equal(req.contents[0].parts[0].inlineData.mimeType, "audio/mpeg");
  assert.equal(req.contents[0].parts[0].inlineData.data, "QUJD");
  assert.match(s, /verbatim/i);
  assert.match(s, /filler/i);
  assert.equal(parseTranscript({ candidates: [{ content: { parts: [{ text: '{"transcript":"Call or fold."}' }] } }] }), "Call or fold.");
  assert.throws(() => parseTranscript({}), /no candidate/);
});

test("cache key changes with audio bytes or text", () => {
  const a = qaKey(new Uint8Array([1, 2]), "Hi.");
  assert.notEqual(a, qaKey(new Uint8Array([1, 3]), "Hi."));
  assert.notEqual(a, qaKey(new Uint8Array([1, 2]), "Hi!"));
  assert.equal(a, qaKey(new Uint8Array([1, 2]), "Hi."));
});

function geminiFake(answers: Record<string, string[]>, opts: { fail?: number; judge?: boolean } = {}) {
  let calls = 0, judged = 0, fail = opts.fail ?? 0;
  const fetch = async (_u: string | URL | Request, init?: RequestInit) => {
    calls++;
    if (fail > 0) { fail--; return new Response("busy", { status: 503 }); }
    const body = JSON.parse(String(init?.body));
    const prompt = body.contents[0].parts[1].text as string;
    if (/script for this clip/i.test(prompt)) {
      judged++;
      return Response.json({ candidates: [{ content: { parts: [{ text: JSON.stringify({ match: opts.judge ?? true, differences: opts.judge === false ? "says part" : "" }) }] } }] });
    }
    const key = body.contents[0].parts[0].inlineData.data as string;
    const list = answers[key];
    const t = list.length > 1 ? list.shift()! : list[0];
    return Response.json({ candidates: [{ content: { parts: [{ text: JSON.stringify({ transcript: t }) }] } }] });
  };
  return { fetch, calls: () => calls, judged: () => judged };
}
const b64 = (s: string) => Buffer.from(s).toString("base64");
const clip = (id: string, text: string, audio: string): ClipInput => ({ id, voice: "dot", text, bytes: new TextEncoder().encode(audio) });

test("verifyClips: transcribes, caches, and a second run calls nothing", async () => {
  const g = geminiFake({ [b64("A")]: ["Call or fold."], [b64("B")]: ["Can't check here, uh, try or need it. Call or fold."] });
  const cache = {};
  const clips = [clip("dot.a", "Call or fold.", "A"), clip("dot.b", "Can't check here. Call or fold.", "B")];
  const r = await verifyClips({ clips, fetch: g.fetch, apiKey: "K", model: "m", cache, retryDelayMs: 1 });
  assert.equal(r.results.find(x => x.id === "dot.a")!.ok, true);
  const b = r.results.find(x => x.id === "dot.b")!;
  assert.equal(b.ok, false);
  const used = g.calls();
  assert.equal(used, 3, "bad clip is transcribed twice (second opinion)");
  const again = await verifyClips({ clips, fetch: g.fetch, apiKey: "K", model: "m", cache, retryDelayMs: 1 });
  assert.equal(g.calls(), used, "cached");
  assert.deepEqual(again.results.map(x => x.ok), [true, false]);
});

test("verifyClips: a fail that the second transcription does not confirm passes", async () => {
  const g = geminiFake({ [b64("A")]: ["Call or old.", "Call or fold."] });
  const r = await verifyClips({ clips: [clip("dot.a", "Call or fold.", "A")], fetch: g.fetch, apiKey: "K", model: "m", cache: {}, retryDelayMs: 1 });
  assert.equal(r.results[0].ok, true);
  assert.equal(r.results[0].transcripts.length, 2);
});

test("verifyClips: retries when Gemini is busy", async () => {
  const g = geminiFake({ [b64("A")]: ["Call or fold."] }, { fail: 2 });
  const r = await verifyClips({ clips: [clip("dot.a", "Call or fold.", "A")], fetch: g.fetch, apiKey: "K", model: "m", cache: {}, retryDelayMs: 1 });
  assert.equal(r.results[0].ok, true);
  assert.equal(g.calls(), 3);
});

test("hesitation sounds are interchangeable when the text has one", () => {
  assert.ok(compareText("Eh, let's see where this goes.", "Ah, let's see where this goes.").ok);
  assert.ok(compareText("Oh! Um, I'll... put some in.", "Oh! Uh, I'll put some in.").ok);
  assert.equal(compareText("Let's see.", "Ah, let's see.").ok, false);
});

test("padMp3 adds silent frames before and after (short clips transcribe much better with silence around them)", () => {
  const src = fakeMp3(10, true);
  for (let i = 0; i < 10; i++) src[20 + i * 104 + 10] = 7; // 原本的幀有內容
  const out = padMp3(src, 0.5);
  const frame = 576 / 22050, padFrames = Math.ceil(0.5 / frame);
  assert.ok(Math.abs(mp3Duration(out) - (10 + 2 * padFrames) * frame) < 1e-6);
  assert.equal(out[0], 0xff, "ID3 tag dropped, starts with a frame");
  assert.equal(out[4], 0, "silent frame has empty side info");
  assert.deepEqual(padMp3(new Uint8Array([1, 2, 3]), 0.5), new Uint8Array([1, 2, 3]), "not an mp3: unchanged");
});

test("transcribe sends the clip padded with silence", async () => {
  let sent = "";
  const fetch = async (_u: string | URL | Request, init?: RequestInit) => {
    sent = JSON.parse(String(init?.body)).contents[0].parts[0].inlineData.data;
    return Response.json({ candidates: [{ content: { parts: [{ text: '{"transcript":"one"}' }] } }] });
  };
  const src = fakeMp3(20);
  assert.equal(await transcribe(fetch, { apiKey: "K", model: "m" }, src), "one");
  const got = new Uint8Array(Buffer.from(sent, "base64"));
  assert.ok(mp3Duration(got) > mp3Duration(src) + 1.5, "about 0.8 s each side");
});

test("homophones the ear cannot tell apart are accepted", () => {
  assert.ok(compareText("tells read", "tells Reed").ok);
  assert.ok(compareText("two", "To.").ok);
  assert.ok(compareText("four", "for").ok);
  assert.equal(compareText("two", "three").ok, false);
});

test("substitution-only mismatches get a guided check and pass as warnings", async () => {
  const g = geminiFake({ [b64("A")]: ["Pod goes to Pip."] }, { judge: true });
  const cache = {};
  const r = await verifyClips({ clips: [clip("dot.pot.pip", "Pot goes to Pip!", "A")], fetch: g.fetch, apiKey: "K", model: "m", cache, retryDelayMs: 1 });
  assert.equal(r.results[0].ok, true);
  assert.equal(r.results[0].warn, true);
  assert.match(r.results[0].note!, /pot→pod/);
  assert.equal(g.judged(), 1);
  await verifyClips({ clips: [clip("dot.pot.pip", "Pot goes to Pip!", "A")], fetch: g.fetch, apiKey: "K", model: "m", cache, retryDelayMs: 1 });
  assert.equal(g.judged(), 1, "judge verdict is cached");
});

test("guided check saying mismatch fails the clip", async () => {
  const g = geminiFake({ [b64("A")]: ["Pod goes to Pip."] }, { judge: false });
  const r = await verifyClips({ clips: [clip("dot.pot.pip", "Pot goes to Pip!", "A")], fetch: g.fetch, apiKey: "K", model: "m", cache: {}, retryDelayMs: 1 });
  assert.equal(r.results[0].ok, false);
  assert.match(r.results[0].issues.join(" "), /guided check: says part/);
});

test("extra or missing words never get the guided check", async () => {
  const g = geminiFake({ [b64("A")]: ["Can't check here, uh, try or need it. Call or fold."], [b64("B")]: ["Vex bet twenty. Pinch your chips."] });
  const r = await verifyClips({ clips: [clip("dot.a", "Can't check here. Call or fold.", "A"), clip("dot.b", "Vex bet 20. Pinch your chips and toss them in to call.", "B")], fetch: g.fetch, apiKey: "K", model: "m", cache: {}, retryDelayMs: 1 });
  assert.deepEqual(r.results.map(x => x.ok), [false, false]);
  assert.equal(g.judged(), 0);
});

test("judgeable: substitutions or one missing short word, never extras", async () => {
  const { judgeable } = await import("../bake/qa.ts");
  assert.equal(judgeable(compareText("Pot goes to Pip!", "Pod goes to Pip.")), true);
  assert.equal(judgeable(compareText("Somebody's scared.", "Somebody scared.")), true);
  assert.equal(judgeable(compareText("Call or fold.", "Call or, uh, fold.")), false);
  assert.equal(judgeable(compareText("Toss them in to call.", "Toss them.")), false);
  assert.equal(judgeable(compareText("Call.", "Call.")), false);
});

test("duration uses the spoken word count (hm runs are not collapsed there)", () => {
  const clips = [
    ...Array.from({ length: 10 }, (_, i) => ({ id: "pip.ok." + i, voice: "pip", text: "one two three four five six", seconds: 2.2 })),
    { id: "pip.unsure.01", voice: "pip", text: "Hmm. Hmm hmm hmm.", seconds: 1.8 },
  ];
  assert.equal(durationFlags(clips).size, 0);
});
