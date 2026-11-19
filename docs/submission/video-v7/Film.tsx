import React from "react";
import {AbsoluteFill, OffthreadVideo, Sequence, interpolate, spring, staticFile, useCurrentFrame, useVideoConfig} from "remotion";
import {loadFont} from "@remotion/google-fonts/LibreBaskerville";
import {theme} from "./theme";
const {fontFamily: serif} = loadFont();
import edl from "./edl.json";

type Shot = {id: string; frames: number; title?: boolean; end?: boolean; capTop?: boolean; storyTop?: boolean; storyRight?: boolean; story2?: [string, number, number];
  zoom?: [number, number, number]; story?: [string, number, number]; tag?: [string, number, number]; dlg?: [string, string, number, number][]; speed?: number};
const clamp = {extrapolateLeft: "clamp", extrapolateRight: "clamp"} as const;

const useWindow = (from: number, to: number) => {
  const frame = useCurrentFrame();
  const {fps} = useVideoConfig();
  const inP = spring({frame: frame - from * fps, fps, config: theme.spring.smooth, durationInFrames: 14});
  const out = interpolate(frame, [to * fps - 7, to * fps], [1, 0], {...clamp, easing: theme.ease.in});
  return {p: inP, out, show: frame >= from * fps && frame <= to * fps};
};

// 劇情字幕：和遊戲揭曉牌同一套語言，放在畫面上緣
const Story: React.FC<{text: string; from: number; to: number; low?: boolean; right?: boolean}> = ({text, from, to, low, right}) => {
  const {p, out, show} = useWindow(from, to);
  if (!show) return null;
  return <div style={{position: "absolute", left: 0, right: 0, [low ? "bottom" : "top"]: 54, display: "flex", justifyContent: right ? "flex-end" : "center", paddingRight: right ? 70 : 0,
    opacity: p * out, transform: `translateY(${interpolate(p, [0, 1], [low ? 14 : -14, 0])}px)`}}>
    <div style={{fontFamily: serif, fontSize: 44, color: theme.colors.cream, background: theme.colors.ink,
      padding: "14px 36px 16px", borderRadius: 6, border: `2px solid ${theme.colors.brass}`, letterSpacing: "-0.01em", boxShadow: "0 10px 30px rgba(0,0,0,0.35)"}}>{text}</div>
  </div>;
};

// 對白字幕：講者名＋句子，和劇情字幕外觀明顯不同
const Dialogue: React.FC<{who: string; text: string; from: number; to: number; top?: boolean; lift?: number}> = ({who, text, from, to, top, lift = 0}) => {
  const {p, out, show} = useWindow(from, to);
  if (!show) return null;
  return <div style={{position: "absolute", left: 200, right: 200, [top ? "top" : "bottom"]: 58 + lift, textAlign: "center", opacity: Math.min(1, p * 1.4) * out,
    fontFamily: theme.font.sans, fontSize: 38, fontWeight: 600, lineHeight: 1.25, color: theme.colors.white,
    textShadow: "0 2px 3px rgba(0,0,0,0.95), 0 0 16px rgba(0,0,0,0.75)"}}>
    {who ? <span style={{color: theme.colors.brass, fontWeight: 700, marginRight: 14}}>{who}</span> : null}{text}
  </div>;
};

// 訊號標籤：遊戲實際偵測到的手部動作
const Tag: React.FC<{text: string; from: number; to: number}> = ({text, from, to}) => {
  const frame = useCurrentFrame();
  const {fps} = useVideoConfig();
  const {p, out, show} = useWindow(from, to);
  if (!show) return null;
  const w = interpolate(frame, [from * fps, from * fps + 12], [0, 64], {...clamp, easing: theme.ease.out});
  return <div style={{position: "absolute", left: 64, top: 64, display: "flex", alignItems: "center", gap: 16, opacity: p * out,
    transform: `translateX(${interpolate(p, [0, 1], [-20, 0])}px)`}}>
    <div style={{width: w, height: 3, background: theme.colors.brass}}/>
    <div style={{fontFamily: theme.font.sans, fontSize: 30, fontWeight: 600, letterSpacing: "0.01em", color: theme.colors.cream,
      background: theme.colors.ink, padding: "8px 18px", borderRadius: 4}}>{text}</div>
  </div>;
};

const Title: React.FC = () => {
  const frame = useCurrentFrame();
  const {fps, durationInFrames} = useVideoConfig();
  const a = spring({frame: frame - 6, fps, config: theme.spring.smooth});
  const b = spring({frame: frame - 32, fps, config: theme.spring.smooth});
  const out = interpolate(frame, [durationInFrames - 8, durationInFrames - 1], [1, 0], clamp);
  return <AbsoluteFill style={{justifyContent: "center", alignItems: "center", opacity: out}}>
    <AbsoluteFill style={{background: "radial-gradient(ellipse at center, rgba(4,16,11,0.72) 0%, rgba(4,16,11,0.45) 48%, rgba(4,16,11,0.15) 100%)", opacity: a}}/>
    <div style={{fontFamily: serif, fontSize: 190, color: theme.colors.cream, letterSpacing: "0.09em", paddingLeft: "0.09em", opacity: a,
      transform: `scale(${interpolate(a, [0, 1], [1.05, 1])})`, textShadow: "0 8px 40px rgba(0,0,0,0.6)"}}>TELL</div>
    <div style={{width: interpolate(a, [0, 1], [0, 340]), height: 3, background: theme.colors.brass, margin: "10px 0 28px"}}/>
    <div style={{fontFamily: serif, fontSize: 44, color: theme.colors.cream, opacity: b, transform: `translateY(${interpolate(b, [0, 1], [14, 0])}px)`,
      textShadow: "0 3px 14px rgba(0,0,0,0.8)"}}>AI poker players who read your real hands.</div>
  </AbsoluteFill>;
};

const EndCard: React.FC = () => {
  const frame = useCurrentFrame();
  const {fps} = useVideoConfig();
  const a = spring({frame: frame - 4, fps, config: theme.spring.smooth});
  const b = spring({frame: frame - 26, fps, config: theme.spring.smooth});
  const c = spring({frame: frame - 48, fps, config: theme.spring.smooth});
  const drift = Math.sin(frame / 40) * 12;
  return <AbsoluteFill style={{background: `radial-gradient(ellipse at ${50 + drift / 8}% 45%, ${theme.colors.felt} 0%, ${theme.colors.feltDeep} 72%)`, justifyContent: "center", alignItems: "center"}}>
    <div style={{fontFamily: serif, fontSize: 210, color: theme.colors.cream, letterSpacing: "0.09em", paddingLeft: "0.09em", opacity: a, transform: `scale(${interpolate(a, [0, 1], [1.04, 1])})`}}>TELL</div>
    <div style={{width: interpolate(a, [0, 1], [0, 380]), height: 3, background: theme.colors.brass, margin: "12px 0 30px"}}/>
    <div style={{fontFamily: serif, fontSize: 52, color: theme.colors.cream, opacity: b, transform: `translateY(${interpolate(b, [0, 1], [12, 0])}px)`}}>Read them. Fool them.</div>
    <div style={{fontFamily: theme.font.sans, fontSize: 30, fontWeight: 500, color: theme.colors.brass, marginTop: 44, letterSpacing: "0.02em", opacity: c}}>Meta Quest 3 / 3S  ·  Hands only  ·  Mixed reality</div>
  </AbsoluteFill>;
};

const ShotView: React.FC<{s: Shot}> = ({s}) => {
  const frame = useCurrentFrame();
  const {durationInFrames} = useVideoConfig();
  if (s.end) return <EndCard/>;
  const z = s.zoom ?? [1.018, 50, 50];
  const push = interpolate(frame, [0, durationInFrames], [z[0] === 1.018 ? 1 : 1 + (z[0] - 1) * 0.6, z[0]], {...clamp, easing: theme.ease.out});
  return <AbsoluteFill style={{background: "#000"}}>
    <AbsoluteFill style={{transform: `scale(${push})`, transformOrigin: `${z[1]}% ${z[2]}%`}}>
      <OffthreadVideo muted src={staticFile(`s${s.id}.mp4`)} style={{width: "100%", height: "100%", objectFit: "cover"}}/>
    </AbsoluteFill>
    {s.title ? <Title/> : null}
    {s.story ? <Story text={s.story[0]} from={s.story[1]} to={s.story[2]} low={!s.storyTop} right={s.storyRight}/> : null}
    {s.story2 ? <Story text={s.story2[0]} from={s.story2[1]} to={s.story2[2]} low/> : null}
    {s.tag ? <Tag text={s.tag[0]} from={s.tag[1]} to={s.tag[2]}/> : null}
    {(s.dlg ?? []).map((d, i) => <Dialogue key={i} who={d[0]} text={d[1]} from={d[2]} to={d[3]} top={s.capTop} lift={s.story && !s.storyTop && !s.storyRight ? 96 : 0}/>)}
  </AbsoluteFill>;
};

export const Film: React.FC = () => {
  let at = 0;
  return <AbsoluteFill style={{background: "#000"}}>
    {(edl as unknown as Shot[]).map((s) => {
      const from = at; at += s.frames;
      return <Sequence key={s.id} from={from} durationInFrames={s.frames}><ShotView s={s}/></Sequence>;
    })}
  </AbsoluteFill>;
};
