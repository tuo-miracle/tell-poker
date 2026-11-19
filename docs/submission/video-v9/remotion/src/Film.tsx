import React from "react";
import {AbsoluteFill, OffthreadVideo, Sequence, interpolate, spring, staticFile, useCurrentFrame, useVideoConfig} from "remotion";
import {loadFont} from "@remotion/google-fonts/LibreBaskerville";
import {theme} from "./theme";
const {fontFamily: serif} = loadFont();
import edl from "./edl.json";
import tracks from "./tracks.json";

type Dlg = [string, string, number, number, boolean?];
type Shot = {id: string; frames: number; title?: boolean; end?: boolean; card?: string; flash?: boolean; meter?: "fast" | "slow"; capTop?: boolean; storyTop?: boolean; storyRight?: boolean; story2?: [string, number, number];
  zoom?: [number, number, number]; story?: [string, number, number]; tag?: [string, number, number]; dlg?: Dlg[]; speed?: number};
type Track = {w: number; h: number; pts: [number, number, number][]};
const clamp = {extrapolateLeft: "clamp", extrapolateRight: "clamp"} as const;

const useWindow = (from: number, to: number) => {
  const frame = useCurrentFrame();
  const {fps} = useVideoConfig();
  const inP = spring({frame: frame - from * fps, fps, config: theme.spring.smooth, durationInFrames: 14});
  const out = interpolate(frame, [to * fps - 7, to * fps], [1, 0], {...clamp, easing: theme.ease.in});
  return {p: inP, out, show: frame >= from * fps && frame <= to * fps};
};

// 逐字進場，*字* 用黃銅色強調
const Words: React.FC<{text: string; start: number; per?: number; rise?: number; hero?: string}> = ({text, start, per = 3, rise = 16, hero = theme.colors.brass}) => {
  const frame = useCurrentFrame();
  const {fps} = useVideoConfig();
  return <>{text.split(" ").map((w, i) => {
    const on = w.startsWith("*");
    const p = spring({frame: frame - start - i * per, fps, config: theme.spring.snappy});
    return <span key={i} style={{display: "inline-block", marginRight: "0.27em", opacity: p, color: on ? hero : undefined,
      transform: `translateY(${interpolate(p, [0, 1], [rise, 0])}px) scale(${interpolate(p, [0, 1], [0.96, 1])})`}}>{w.replace(/\*/g, "")}</span>;
  })}</>;
};

// 快慢計時條：快的一下填滿，慢的一路爬
const Meter: React.FC<{kind: "fast" | "slow"; from: number}> = ({kind, from}) => {
  const frame = useCurrentFrame();
  const {fps} = useVideoConfig();
  const secs = kind === "fast" ? 0.5 : 3.0;
  const w = interpolate(frame, [from * fps, (from + secs) * fps], [0, 100], {...clamp, easing: kind === "fast" ? theme.ease.out : theme.ease.inOut});
  return <div style={{height: 5, marginTop: 12, background: "rgba(246,238,220,0.18)", borderRadius: 3, overflow: "hidden"}}>
    <div style={{width: `${w}%`, height: "100%", background: theme.colors.brass}}/>
  </div>;
};

// 劇情字幕：和遊戲揭曉牌同一套語言，牌框由中間展開、字再逐字進場
const Story: React.FC<{text: string; from: number; to: number; low?: boolean; right?: boolean; meter?: "fast" | "slow"}> = ({text, from, to, low, right, meter}) => {
  const frame = useCurrentFrame();
  const {fps} = useVideoConfig();
  const {p, out, show} = useWindow(from, to);
  if (!show) return null;
  const open = interpolate(frame, [from * fps, from * fps + 10], [50, 0], {...clamp, easing: theme.ease.out});
  const exitY = interpolate(out, [0, 1], [low ? 12 : -12, 0]);
  return <div style={{position: "absolute", left: 0, right: 0, [low ? "bottom" : "top"]: 54, display: "flex", justifyContent: right ? "flex-end" : "center", paddingRight: right ? 70 : 0,
    opacity: Math.min(1, p * 2) * out, transform: `translateY(${exitY}px)`}}>
    <div style={{clipPath: `inset(-40px ${open}% -40px ${open}%)`, fontFamily: serif, fontSize: 44, color: theme.colors.cream, background: theme.colors.ink,
      padding: "14px 36px 16px", borderRadius: 6, border: `2px solid ${theme.colors.brass}`, letterSpacing: "-0.01em", boxShadow: "0 10px 30px rgba(0,0,0,0.35)"}}>
      <Words text={text} start={from * fps + 4}/>
      {meter ? <Meter kind={meter} from={from}/> : null}
    </div>
  </div>;
};

// 對白字幕：講者名＋句子；關鍵台詞放大，帶回彈
const Dialogue: React.FC<{who: string; text: string; from: number; to: number; top?: boolean; lift?: number; big?: boolean}> = ({who, text, from, to, top, lift = 0, big}) => {
  const frame = useCurrentFrame();
  const {fps} = useVideoConfig();
  const {p, out, show} = useWindow(from, to);
  if (!show) return null;
  const pop = spring({frame: frame - from * fps, fps, config: {damping: 11, stiffness: 180}});
  const scale = big ? interpolate(pop, [0, 1], [1.35, 1]) : 1;
  return <div style={{position: "absolute", left: 160, right: 160, [top ? "top" : "bottom"]: 58 + lift, textAlign: "center", opacity: Math.min(1, p * 1.6) * out,
    transform: `scale(${scale})`, fontFamily: big ? serif : theme.font.sans, fontSize: big ? 76 : 38, fontWeight: big ? 400 : 600, lineHeight: 1.25, color: big ? theme.colors.cream : theme.colors.white,
    textShadow: big ? "0 4px 6px rgba(0,0,0,0.95), 0 0 34px rgba(0,0,0,0.85)" : "0 2px 3px rgba(0,0,0,0.95), 0 0 16px rgba(0,0,0,0.75)"}}>
    {who ? <span style={{color: theme.colors.brass, fontFamily: theme.font.sans, fontSize: 38, fontWeight: 700, marginRight: 18, display: big ? "block" : "inline"}}>{who}</span> : null}
    {big ? text : <Words text={text} start={from * fps} per={1.5} rise={8} hero={theme.colors.white}/>}
  </div>;
};

// 訊號標籤：遊戲實際偵測到的手部動作，脈衝點＋畫出的線＋字
const Tag: React.FC<{text: string; from: number; to: number}> = ({text, from, to}) => {
  const frame = useCurrentFrame();
  const {fps} = useVideoConfig();
  const {p, out, show} = useWindow(from, to);
  if (!show) return null;
  const f = from * fps;
  const w = interpolate(frame, [f, f + 12], [0, 64], {...clamp, easing: theme.ease.out});
  const pulse = ((frame - f) % 30) / 30;
  return <div style={{position: "absolute", left: 64, top: 64, display: "flex", alignItems: "center", opacity: out, transform: `translateX(${interpolate(p, [0, 1], [-20, 0])}px)`}}>
    <div style={{position: "relative", width: 16, height: 16, marginRight: 12}}>
      <div style={{position: "absolute", inset: 0, borderRadius: 8, background: theme.colors.brass, transform: `scale(${p})`}}/>
      <div style={{position: "absolute", inset: 0, borderRadius: 8, border: `2px solid ${theme.colors.brass}`, opacity: 1 - pulse, transform: `scale(${1 + pulse * 2.4})`}}/>
    </div>
    <div style={{width: w, height: 3, background: theme.colors.brass, marginRight: 14}}/>
    <div style={{fontFamily: theme.font.sans, fontSize: 30, fontWeight: 600, letterSpacing: "0.01em", color: theme.colors.cream, background: theme.colors.ink, padding: "8px 18px", borderRadius: 4, opacity: p}}>
      <Words text={text} start={f + 5} per={2} rise={8} hero={theme.colors.cream}/>
    </div>
  </div>;
};

// 黏在遊戲揭曉牌上的聚光框（逐格模板追蹤，放在影片同一層所以會跟著推近）
const Spot: React.FC<{track: Track}> = ({track}) => {
  const frame = useCurrentFrame();
  const {fps} = useVideoConfig();
  const pts = track.pts;
  const pt = pts[Math.min(frame, pts.length - 1)];
  const first = pts.findIndex((q) => q[2]);
  if (first < 0 || frame < first || !pt[2]) return null;
  const p = spring({frame: frame - first, fps, config: theme.spring.snappy});
  const pad = 1.0 + (1 - p) * 2.2;
  const W = track.w + pad, H = track.h + pad * 1.78;
  const c = (s: React.CSSProperties) => <div style={{position: "absolute", width: 34, height: 34, borderColor: theme.colors.brass, borderStyle: "solid", borderWidth: 0, ...s}}/>;
  return <div style={{position: "absolute", left: `${pt[0] - W / 2}%`, top: `${pt[1] - H / 2}%`, width: `${W}%`, height: `${H}%`, opacity: p, borderRadius: 8, boxShadow: "0 0 140px 2000px rgba(3,12,8,0.42)"}}>
    {c({left: 0, top: 0, borderLeftWidth: 4, borderTopWidth: 4})}{c({right: 0, top: 0, borderRightWidth: 4, borderTopWidth: 4})}
    {c({left: 0, bottom: 0, borderLeftWidth: 4, borderBottomWidth: 4})}{c({right: 0, bottom: 0, borderRightWidth: 4, borderBottomWidth: 4})}
  </div>;
};

// 翻牌：牌背轉到牌面，帶回彈與一道掃光
const FlipCard: React.FC<{delay: number; w: number; h: number; children: React.ReactNode}> = ({delay, w, h, children}) => {
  const frame = useCurrentFrame();
  const {fps} = useVideoConfig();
  const p = spring({frame: frame - delay, fps, config: {damping: 13, stiffness: 85}});
  const rot = interpolate(p, [0, 1], [180, 0]);
  const float = Math.sin((frame + delay * 7) / 26) * 3;
  const glint = interpolate(frame, [delay + 22, delay + 40], [-60, 160], {...clamp, easing: theme.ease.inOut});
  const face: React.CSSProperties = {position: "absolute", inset: 0, borderRadius: w * 0.07, backfaceVisibility: "hidden", overflow: "hidden", boxShadow: "0 18px 40px rgba(0,0,0,0.55)"};
  return <div style={{width: w, height: h, perspective: 1400, opacity: Math.min(1, p * 4), transform: `translateY(${interpolate(p, [0, 1], [46, 0]) + float}px)`}}>
    <div style={{position: "relative", width: "100%", height: "100%", transformStyle: "preserve-3d", transform: `rotateY(${rot}deg)`}}>
      <div style={{...face, background: theme.colors.cream, border: `3px solid ${theme.colors.brass}`, display: "flex", alignItems: "center", justifyContent: "center", color: theme.colors.feltDeep, fontFamily: serif}}>
        {children}
        <div style={{position: "absolute", inset: 0, background: `linear-gradient(105deg, transparent ${glint - 12}%, rgba(255,255,255,0.75) ${glint}%, transparent ${glint + 12}%)`}}/>
      </div>
      <div style={{...face, transform: "rotateY(180deg)", background: theme.colors.felt, border: `3px solid ${theme.colors.brass}`,
        backgroundImage: `repeating-linear-gradient(45deg, ${theme.colors.brass}55 0 2px, transparent 2px 14px), repeating-linear-gradient(-45deg, ${theme.colors.brass}55 0 2px, transparent 2px 14px)`}}/>
    </div>
  </div>;
};

const TellCards: React.FC<{start: number; w: number; h: number; size: number}> = ({start, w, h, size}) => <div style={{display: "flex", gap: 22}}>
  {"TELL".split("").map((ch, i) => <FlipCard key={i} delay={start + i * 6} w={w} h={h}><span style={{fontSize: size, lineHeight: 1}}>{ch}</span></FlipCard>)}
</div>;

// 絨布底：緩慢流動的光暈和飄動的花色，不用純色背景
const Felt: React.FC = () => {
  const frame = useCurrentFrame();
  const d1 = Math.sin(frame / 40) * 70, d2 = Math.cos(frame / 52) * 60;
  const suits = [["♠", 12, 24, 0.5], ["♥", 84, 20, 0.8], ["♦", 16, 76, 0.7], ["♣", 88, 72, 0.45]] as const;
  return <AbsoluteFill style={{background: theme.colors.feltDeep, overflow: "hidden"}}>
    <div style={{position: "absolute", width: 1500, height: 1500, borderRadius: "50%", left: 210 + d1, top: -330 + d2, background: `radial-gradient(circle, ${theme.colors.felt} 0%, transparent 66%)`}}/>
    {suits.map(([s, x, y, sp], i) => <div key={i} style={{position: "absolute", left: `${x}%`, top: `${y + Math.sin(frame / 30 + i) * 1.2}%`, fontSize: 120, color: theme.colors.brass, opacity: 0.13,
      transform: `translate(-50%, -50%) translateX(${frame * sp}px) rotate(${(i % 2 ? 1 : -1) * (10 + frame * 0.12)}deg)`}}>{s}</div>)}
  </AbsoluteFill>;
};

// 章節動畫卡：一張牌翻開，接著逐字打出這一段的主張
// 章節字卡的發牌：五張牌發成扇形；flips 是各張牌翻開的秒數（null＝不翻），faces 是翻開的花色；second 是中間那張再翻一次（說謊：亮出來的不是真的）
const RED = "#8E2A2A";
type Deal = {flips: (number | null)[]; faces: string[]; scan?: boolean; second?: {at: number; face: string}};
const DEALS: Record<string, Deal> = {
  reads: {flips: [null, null, 1.5, null, null], faces: ["", "", "♠", "", ""], scan: true},
  lie: {flips: [null, null, 0.9, null, null], faces: ["", "", "♥", "", ""], second: {at: 2.0, face: "♠"}},
  read: {flips: [null, 1.0, 1.35, 1.7, null], faces: ["", "♦", "♣", "♥", ""]},
};
const FanCard: React.FC<{i: number; deal: Deal; w: number; h: number}> = ({i, deal, w, h}) => {
  const frame = useCurrentFrame();
  const {fps} = useVideoConfig();
  const k = i - 2;
  const enter = spring({frame: frame - (0.1 + i * 0.13) * fps, fps, config: {damping: 15, stiffness: 120}});
  const at = deal.flips[i];
  const p1 = at === null ? 0 : spring({frame: frame - at * fps, fps, config: {damping: 14, stiffness: 110}});
  const p2 = deal.second && i === 2 ? spring({frame: frame - deal.second.at * fps, fps, config: {damping: 14, stiffness: 110}}) : 0;
  const rot = 180 * (1 - p1) + 360 * p2;
  const face = deal.second && p2 > 0.5 && i === 2 ? deal.second.face : deal.faces[i];
  // 讀牌：一道掃過去，掃到的牌抬起來再放下
  const scanAt = 0.95 + i * 0.14;
  const lift = deal.scan ? interpolate(frame, [scanAt * fps, (scanAt + 0.16) * fps, (scanAt + 0.42) * fps], [0, 1, 0], {...clamp, easing: theme.ease.inOut}) : 0;
  const shown = Math.max(p1 * (1 - p2), p2);
  const x = interpolate(enter, [0, 1], [0, k * (w * 0.74)]);
  const y = interpolate(enter, [0, 1], [460, Math.abs(k) * 15]) - lift * 34 - shown * 22 + Math.sin(frame / 24 + i) * 2;
  const tilt = interpolate(enter, [0, 1], [k * 22, k * 6]);
  const box: React.CSSProperties = {position: "absolute", inset: 0, borderRadius: w * 0.07, backfaceVisibility: "hidden", border: `3px solid ${theme.colors.brass}`, boxShadow: "0 14px 30px rgba(0,0,0,0.55)"};
  return <div style={{position: "absolute", left: "50%", bottom: 8, width: w, height: h, marginLeft: -w / 2, perspective: 1200, zIndex: i === 2 ? 5 : 5 - Math.abs(k), opacity: Math.min(1, enter * 5),
    transform: `translate(${x}px, ${y}px) rotate(${tilt}deg) scale(${1 + shown * 0.1})`}}>
    <div style={{position: "relative", width: "100%", height: "100%", transformStyle: "preserve-3d", transform: `rotateY(${rot}deg)`}}>
      <div style={{...box, background: theme.colors.cream, display: "flex", alignItems: "center", justifyContent: "center", fontFamily: serif, fontSize: w * 0.62, color: face === "♥" || face === "♦" ? RED : theme.colors.feltDeep}}>{face}</div>
      <div style={{...box, transform: "rotateY(180deg)", background: theme.colors.felt,
        backgroundImage: `repeating-linear-gradient(45deg, ${theme.colors.brass}55 0 2px, transparent 2px 13px), repeating-linear-gradient(-45deg, ${theme.colors.brass}55 0 2px, transparent 2px 13px)`}}/>
    </div>
  </div>;
};
// All in：三柱籌碼落下，再一起推成一大疊
const ChipPile: React.FC = () => {
  const frame = useCurrentFrame();
  const {fps} = useVideoConfig();
  const W = 136, T = 21, N = 4;
  const merge = spring({frame: frame - 1.9 * fps, fps, config: {damping: 16, stiffness: 95}});
  const order = [1, 0, 2];
  return <>{order.flatMap((col, oi) => Array.from({length: N}, (_, n) => {
    const drop = spring({frame: frame - (0.15 + (oi * N + n) * 0.09) * fps, fps, config: {damping: 11, stiffness: 150}});
    const baseX = (col - 1) * 186;
    const x = interpolate(merge, [0, 1], [baseX, 0]);
    const stackAt = n + oi * N * merge;
    const y = -stackAt * T + interpolate(drop, [0, 1], [-640, 0]);
    return <div key={`${col}-${n}`} style={{position: "absolute", left: "50%", bottom: 34, width: W, height: W * 0.36, marginLeft: -W / 2, borderRadius: "50%", opacity: Math.min(1, drop * 5), zIndex: oi * N + n,
      transform: `translate(${x}px, ${y}px)`, background: "#A8322E", border: `3px dashed ${theme.colors.cream}`, boxShadow: `0 7px 0 #6F1F1C, 0 14px 22px rgba(0,0,0,0.45)`}}/>;
  }))}</>;
};
const ChapterCard: React.FC<{text: string}> = ({text}) => {
  const frame = useCurrentFrame();
  const {fps, durationInFrames} = useVideoConfig();
  const allIn = /All/.test(text);
  const deal = /lie/.test(text) ? DEALS.lie : /them/.test(text) ? DEALS.read : DEALS.reads;
  const push = interpolate(frame, [0, durationInFrames], [1, 1.035], {...clamp, easing: theme.ease.out});
  const rule = spring({frame: frame - 1.45 * fps, fps, config: theme.spring.smooth});
  // 退場比進場快；Wipe 蓋上之前字先收
  const out = interpolate(frame, [durationInFrames - 0.55 * fps, durationInFrames - 0.2 * fps], [1, 0], {...clamp, easing: theme.ease.in});
  return <AbsoluteFill>
    <Felt/>
    <AbsoluteFill style={{justifyContent: "center", alignItems: "center", transform: `scale(${push})`}}>
      <div style={{position: "relative", width: 1000, height: 300, opacity: out, transform: `translateY(${interpolate(out, [0, 1], [26, 0])}px)`}}>
        {allIn ? <ChipPile/> : [0, 1, 2, 3, 4].map((i) => <FanCard key={i} i={i} deal={deal} w={128} h={178}/>)}
      </div>
      <div style={{fontFamily: serif, fontSize: 112, color: theme.colors.cream, marginTop: 40, letterSpacing: "-0.015em", textShadow: "0 6px 30px rgba(0,0,0,0.5)", opacity: out, transform: `translateY(${interpolate(out, [0, 1], [-16, 0])}px)`}}>
        <Words text={text} start={1.15 * fps} per={4} rise={34}/>
      </div>
      <div style={{width: interpolate(rule, [0, 1], [0, 460]) * out, height: 3, background: theme.colors.brass, marginTop: 22}}/>
    </AbsoluteFill>
  </AbsoluteFill>;
};
const Title: React.FC = () => {
  const frame = useCurrentFrame();
  const {fps, durationInFrames} = useVideoConfig();
  const a = spring({frame: frame - 2, fps, config: theme.spring.smooth});
  const b = spring({frame: frame - 36, fps, config: theme.spring.smooth});
  const out = interpolate(frame, [durationInFrames - 8, durationInFrames - 1], [1, 0], {...clamp, easing: theme.ease.in});
  return <AbsoluteFill style={{justifyContent: "center", alignItems: "center", opacity: out}}>
    <AbsoluteFill style={{background: "radial-gradient(ellipse at center, rgba(4,16,11,0.8) 0%, rgba(4,16,11,0.55) 50%, rgba(4,16,11,0.2) 100%)", opacity: a}}/>
    <TellCards start={6} w={150} h={210} size={124}/>
    <div style={{width: interpolate(b, [0, 1], [0, 420]), height: 3, background: theme.colors.brass, margin: "32px 0 24px"}}/>
    <div style={{fontFamily: serif, fontSize: 44, color: theme.colors.cream, textShadow: "0 3px 14px rgba(0,0,0,0.8)"}}>
      <Words text="AI poker players who *read* your real hands." start={36} per={2}/>
    </div>
  </AbsoluteFill>;
};

const EndCard: React.FC = () => {
  const frame = useCurrentFrame();
  const {fps, durationInFrames} = useVideoConfig();
  const a = spring({frame: frame - 34, fps, config: theme.spring.smooth});
  const c = spring({frame: frame - 62, fps, config: theme.spring.smooth});
  const out = interpolate(frame, [durationInFrames - 12, durationInFrames - 1], [1, 0], {...clamp, easing: theme.ease.in});
  return <AbsoluteFill style={{opacity: out, background: "#000"}}>
    <Felt/>
    <AbsoluteFill style={{justifyContent: "center", alignItems: "center"}}>
      <TellCards start={6} w={170} h={238} size={140}/>
      <div style={{width: interpolate(a, [0, 1], [0, 460]), height: 3, background: theme.colors.brass, margin: "34px 0 26px"}}/>
      <div style={{fontFamily: serif, fontSize: 52, color: theme.colors.cream}}><Words text="Read them. *Fool* them." start={36} per={3}/></div>
      <div style={{fontFamily: theme.font.sans, fontSize: 30, fontWeight: 500, color: theme.colors.brass, marginTop: 36, letterSpacing: "0.02em", opacity: c, transform: `translateY(${interpolate(c, [0, 1], [10, 0])}px)`}}>Meta Quest 3 / 3S  ·  Hands only  ·  Mixed reality</div>
      <div style={{position: "absolute", bottom: 26, fontFamily: theme.font.sans, fontSize: 18, color: theme.colors.cream, opacity: c * 0.7}}>Music: "Hep Cats" by Kevin MacLeod (incompetech.com), CC BY 4.0</div>
    </AbsoluteFill>
  </AbsoluteFill>;
};

// 只調影片層：統一五段不同燈光的素材，暗角收邊，把視線帶回桌面
const Grade: React.FC = () => <AbsoluteFill style={{pointerEvents: "none"}}>
  <AbsoluteFill style={{backgroundColor: theme.colors.felt, mixBlendMode: "soft-light", opacity: 0.16}}/>
  <AbsoluteFill style={{background: "radial-gradient(ellipse at center, transparent 54%, rgba(0,0,0,0.42) 100%)"}}/>
  <AbsoluteFill style={{background: "linear-gradient(180deg, rgba(0,0,0,0.16), transparent 22%, transparent 76%, rgba(0,0,0,0.24))"}}/>
</AbsoluteFill>;

const Grain: React.FC = () => {
  const frame = useCurrentFrame();
  const noise = `url("data:image/svg+xml,%3Csvg xmlns='http://www.w3.org/2000/svg' width='220' height='220'%3E%3Cfilter id='n'%3E%3CfeTurbulence type='fractalNoise' baseFrequency='0.9' numOctaves='2'/%3E%3C/filter%3E%3Crect width='220' height='220' filter='url(%23n)' opacity='0.5'/%3E%3C/svg%3E")`;
  return <AbsoluteFill style={{pointerEvents: "none", backgroundImage: noise, backgroundSize: "220px", backgroundPosition: `${(frame * 7) % 220}px ${(frame * 13) % 220}px`, opacity: 0.045, mixBlendMode: "overlay"}}/>;
};

// 章節轉場：黃銅邊的絨布面板掃過，真正的剪接點藏在面板正中央
const Wipe: React.FC = () => {
  const frame = useCurrentFrame();
  const {durationInFrames} = useVideoConfig();
  const x = interpolate(frame, [0, durationInFrames - 1], [-175, 175], {...clamp, easing: theme.ease.inOut});
  return <AbsoluteFill style={{overflow: "hidden", pointerEvents: "none"}}>
    <div style={{position: "absolute", left: "-30%", top: "-10%", width: "160%", height: "120%", transform: `translateX(${x}%) skewX(-14deg)`, background: theme.colors.feltDeep,
      borderLeft: `18px solid ${theme.colors.brass}`, borderRight: `18px solid ${theme.colors.brass}`, filter: "blur(3px)"}}/>
  </AbsoluteFill>;
};

const ShotView: React.FC<{s: Shot}> = ({s}) => {
  const frame = useCurrentFrame();
  const {durationInFrames, fps} = useVideoConfig();
  if (s.end) return <EndCard/>;
  if (s.card) return <ChapterCard text={s.card}/>;
  const z = s.zoom ?? [1.018, 50, 50];
  const push = interpolate(frame, [0, durationInFrames], [z[0] === 1.018 ? 1 : 1 + (z[0] - 1) * 0.6, z[0]], {...clamp, easing: theme.ease.out});
  // 每次切鏡頭有一點微縮回，切點才有力道
  const settle = spring({frame, fps, config: theme.spring.snappy});
  const punch = interpolate(settle, [0, 1], [s.flash ? 1.025 : 1.018, 1]);
  const track = (tracks as unknown as Record<string, Track>)[s.id];
  return <AbsoluteFill style={{background: "#000"}}>
    <AbsoluteFill style={{transform: `scale(${push * punch})`, transformOrigin: `${z[1]}% ${z[2]}%`}}>
      <OffthreadVideo muted src={staticFile(`s${s.id}.mp4`)} style={{width: "100%", height: "100%", objectFit: "cover", filter: "contrast(1.05) saturate(1.07)"}}/>
      {track ? <Spot track={track}/> : null}
    </AbsoluteFill>
    <Grade/>
    
    {s.title ? <Title/> : null}
    {s.story ? <Story text={s.story[0]} from={s.story[1]} to={s.story[2]} low={!s.storyTop} right={s.storyRight} meter={s.meter}/> : null}
    {s.story2 ? <Story text={s.story2[0]} from={s.story2[1]} to={s.story2[2]} low/> : null}
    {s.tag ? <Tag text={s.tag[0]} from={s.tag[1]} to={s.tag[2]}/> : null}
    {(s.dlg ?? []).map((d, i) => <Dialogue key={i} who={d[0]} text={d[1]} from={d[2]} to={d[3]} big={d[4]} top={s.capTop} lift={s.story && !s.storyTop && !s.storyRight ? 96 + (s.meter ? 18 : 0) : 0}/>)}
  </AbsoluteFill>;
};

export const Film: React.FC = () => {
  const shots = edl as unknown as Shot[];
  let at = 0;
  const wipes: number[] = [];
  const views = shots.map((s, i) => {
    const from = at; at += s.frames;
    const next = shots[i + 1];
    if (next && (s.card || next.card || s.id === "03" || next.end)) wipes.push(at);
    return <Sequence key={s.id} from={from} durationInFrames={s.frames}><ShotView s={s}/></Sequence>;
  });
  return <AbsoluteFill style={{background: "#000"}}>
    {views}
    {wipes.map((w) => <Sequence key={w} from={w - 11} durationInFrames={22}><Wipe/></Sequence>)}
    <Grain/>
  </AbsoluteFill>;
};
