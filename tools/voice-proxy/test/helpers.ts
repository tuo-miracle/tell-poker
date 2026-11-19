// 測試共用：合成 WAV、假狀態
import { pcm16ToWav, bytesToBase64 } from "../src/wav.ts";
import type { PublicState } from "../src/facts.ts";
import { RateLimiter, type LimiterOptions } from "../src/ratelimit.ts";

/** 測試用限流器：預設很寬、時間停在 0。 */
export function limiter(o: Partial<LimiterOptions> = {}): RateLimiter {
  return new RateLimiter({ capacity: 100, refillPerSec: 1, deviceDailyCap: 0, globalDailyCap: 0, now: () => 0, ...o });
}

export const DEV_A = "0f8fad5b-d9cb-469f-a165-70867728950e";
export const DEV_B = "7c9e6679-7425-40de-944b-e07fc1f90ae7";

export function silentWavBase64(seconds: number, sampleRate = 16000): string {
  const pcm = new Uint8Array(Math.round(seconds * sampleRate) * 2);
  return bytesToBase64(pcm16ToWav(pcm, sampleRate));
}

export function sampleState(): PublicState {
  return {
    phase: "betting",
    hand: 4,
    street: "flop",
    board: ["Ah", "7d", "2c"],
    blinds: { small: 5, big: 10 },
    pot: 60,
    toAct: "you",
    button: "rocco",
    youToCall: 20,
    seats: [
      { name: "you", stack: 950, bet: 0, status: "active" },
      { name: "pip", stack: 980, bet: 0, status: "folded" },
      { name: "rocco", stack: 870, bet: 20, status: "active" },
      { name: "vex", stack: 1000, bet: 0, status: "active" },
    ],
    actions: [
      { street: "preflop", who: "rocco", action: "raise", amount: 30 },
      { street: "flop", who: "rocco", action: "bet", amount: 20 },
    ],
  };
}
