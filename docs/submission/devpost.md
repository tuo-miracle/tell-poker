# Devpost submission draft — Tell

> Draft for the Meta VR Start Developer Competition 2026 submission form. Fields in `<ANGLE_BRACKETS>` must be filled in by the developer.
> Numbers marked *(verify)* come from development notes and must be re-checked on the final build before submitting.

## Form fields

| Field | Value |
|---|---|
| Submission name | **Tell** |
| Tagline (max 140 characters) | `Bare-hand poker on your real desk. Read the AI's tells — they read your real hands. Fake one and fool them.` (107 characters) |
| Track | **Gaming** |
| Division | **New Experience** (all code written from September 24, 2026; repository history starts 2026-10-12) |
| Release channel link | `<INVITE_URL from the "Competition" release channel>` |
| Video | `<YouTube or Vimeo URL, public>` (storyboard: `docs/submission/video.md`) |
| Target launch date | `<TARGET_LAUNCH_DATE>` (suggestion: Q2 2027 on the Meta Horizon Store, free) |
| Team member | Max Lai — solo developer (design, code, AI, audio direction) — `<CONTACT_EMAIL>` |
| Optional: how hand interactions are implemented | See "Hand interactions" below |

### Elevator pitch (one paragraph)

Tell puts a Texas Hold'em table on your real desk with three cartoon AI opponents and a dealer. There are no controllers and no buttons: you peek at your cards, pinch and toss chips, tap the table to check and push your stack all in with your bare hands. The game is about reading people, in both directions. Look at an opponent for a second and their tells get louder. They watch your hands too: when you hesitate, peek twice, or reach for your chips and pull back, Rocco says so out loud and plays differently. Because they read you, you can lie to them. Reach slowly on purpose, let Rocco think you are weak, and watch him call your monster hand: "You played me!" Fool them too often and they stop trusting that tell.

## Story sections (about 500 words, the suggested length)

### Inspiration

The best part of poker isn't the cards. It's the moment you catch someone's tell, or realize they've caught yours. VR poker so far has been poker with controllers: you click to check and grab chips with a trigger, and nobody at the table is looking at you. Quest 3 can see your hands and your real desk, so I wanted a poker game where your body is the input *and* the information. The AI opponents should notice what your real hands do, say it out loud, and be fooled when you act.

### What it does

- **Your desk becomes the table.** Tell finds your real table with scene understanding, snaps the felt to it and calibrates with both hands. Tapping to check, sliding chips and pushing all in all happen on a surface you can actually touch. If there is no table, a virtual one appears.
- **Every action is a bare-hand gesture:** lift a card corner to peek, pick up both cards to look, pinch chips (the count grows as you lift), toss or slide them in to bet, double-tap to check, push or drop your cards on the muck to fold, push with both hands to go all in.
- **You read them.** Look at Pip, Rocco or Vex for about a second and their acting gets bigger. Pip is honest and nervous, Rocco is loud, and Vex often fakes it.
- **They read you, live.** While you think, the opponents watch your real hands: long thinks, re-peeks, reaching for chips and pulling back, hovering over your cards. They comment as it happens ("Changed your mind? I saw that.") and shift their decisions within a cap. After each hand a reveal card shows what they read ("Rocco read: long think → weak hand").
- **Fake a tell.** Hesitate on purpose with a monster hand and Rocco calls your "bluff". Opponents remember which tells fooled them and trust them less.
- **Talk to them (optional).** Look at a character and speak English. They answer in their own voice with real table facts (pot, stacks, what to call) and banter in character. They never see your cards.
- An ~8-minute game against three opponents, a dealer who teaches the first hands by demonstration, persistent stats and a per-opponent record.

### How we built it

- **Unity 6.3 (URP) with Meta XR SDK 207:** hand tracking, passthrough, MR Utility Kit table detection, Interaction SDK hand integration, and head-gaze targeting (Quest 3/3S has no eye tracking). *(If the persistent-anchor task in tasks/todo.md ships, add: "the table position is remembered with spatial anchors.")*
- **A custom gesture recognizer** in pure C#, built on hand poses in table space. Every gesture is regression-tested by replaying hand recordings captured on the headset (132 recordings; 90% replay accuracy on the current set *(verify)*).
- **A poker engine** with side pots and TDA rules, an exhaustively verified 7-card evaluator, and seeded shuffles.
- **AI opponents:** a preflop chart, Monte Carlo equity after the flop, and personality parameters. A tell system turns each opponent's true state into acting cues, and a reliability setting decides how often the acting is honest. Live "table talk" is driven by timestamped signals from your hands.
- **Reading you is capped (±0.15 equity) and learns.** In headless self-play (6 seeds × 20,000 hands), faking tells against non-learning AIs wins +25.4 BB/100. Once the AIs learn, that falls to −4.0 *(verify)*. You can fool them, but not forever.
- **Voice:** a TypeScript relay streams speech to Gemini Live, checks every number in the reply against the real table state, and streams ElevenLabs audio back. Typical time from end of speech to first audio is about 0.7 s on our test bench *(verify on Quest)*. All 358 fixed lines are pre-recorded and checked by an automatic listening test, so the game works fully offline.

### Challenges we ran into

- **Hands at the table edge:** hand tracking gets noisy near the desk surface. We replaced fingertip rules with palm position, wrist velocity and intent-based thresholds, all tunable in the headset.
- **A tell must be legible but not exploitable.** We capped the effect, added a separate trust score for each tell, and confirmed with self-play that honest play and tell-faking both stay balanced.
- **Speech latency.** The first version took about 2.5 s to reply. Streaming into Gemini Live with a function-call reply, followed by streaming TTS, cut it to well under a second on the bench.
- **Teaching poker without a wall of text:** the dealer demonstrates each gesture with ghost hands, waits for you to do it, and gives specific hints when you are close.

### Accomplishments that we're proud of

- The core loop works end to end. You can hear an AI call out your real hesitation, then use that same hesitation to fool it.
- Everything runs on Quest 3 at 72 fps with four characters at the table *(verify on final build)*. Every action is done with bare hands, and the game is fully playable offline.

### What we learned

People read faces. On Quest, a cartoon character reading *your hands* creates that feeling of being watched, and saying the read out loud matters more than its exact accuracy.

### What's next

Harder opponents that model you over many sessions, more characters, tournaments, and multiplayer at the same real table (colocation), with the AIs reading every player. Target: a free release on the Meta Horizon Store.

## Hand interactions (optional field)

The whole game is played with bare hands. No controller is ever needed. Gestures are recognized from palm pose, pinch state and wrist velocity in a table-aligned coordinate frame, so they keep working wherever you sit. The real desk gives physical feedback: you feel the tap when you check and the surface under the chips you slide. Each gesture has an anti-misfire rule: a 1.5 s take-back window on bets, a 1 s all-in countdown that cancels when you pull back, and peeks only you can see. The menu on your wrist is operated with the other hand's index finger.

## Built with

`unity` · `c#` · `meta-xr-sdk-207` · `meta-interaction-sdk` · `mr-utility-kit` · `passthrough` · `hand-tracking` · `openxr` · `urp` · `typescript` · `node.js` · `websocket` · `gemini-live` · `elevenlabs` · `cloudflare` · `docker`

## Prizes targeted

Each entry can win at most one prize (the highest value it qualifies for). Rules: <https://start-developer-competition-26.devpost.com/rules>.

1. **Best New Gaming Experience** (winner $100,000, runner-up $50,000). This is the primary target.
2. **Best Agentic Interaction** ($25,000): "Apps where AI is a core part of the experience—spatial agents, voice-first AI interaction, procedural content, context-aware assistance." Tell's AIs read the player's real hands live and change their play.
3. **Best First Five Minutes** ($25,000): "teaches controller-free interaction without a wall of text, and delivers a payoff before the user takes the headset off." The tutorial ends with the player fooling Rocco.

The form has no field for special awards (they are judged across all entries). The description and video should make the agentic loop and the first-five-minutes payoff obvious.

## Submission requirements checklist (official sources)

Sources: Official Rules <https://start-developer-competition-26.devpost.com/rules> (§3–4, §6), Overview "What to submit" <https://start-developer-competition-26.devpost.com/>, FAQ <https://start-developer-competition-26.devpost.com/details/faqs>. Quotes are verbatim.

- [ ] **Deadline:** "Entry period: September 24, 2026 at 10:00:00 AM PT – November 18, 2026 at 12:00:00 PM PT" (Rules). Our own hard stop is 11/10, because the developer is away after that.
- [ ] **Eligibility:** "have an active and valid Meta account with Developer Access enabled" and "Representative or person submitting an Entry must be a member of the Meta VR Start Program by the time of submission" (Rules §2). Check the "Start Member" flair on the developer forum (FAQ).
- [ ] **Build:** "An APK uploaded to the Meta VR Developer Dashboard under a new release channel named “Competition”" (Rules §4) and "Create and submit an Invite URL so judges can access and test your Project" (Overview). FAQ: "APKs will not be considered" (a release-channel link is required).
- [ ] **Availability:** the Project must "remain available until the Winner Announcement" (Rules §4), which is "On or around December 11, 2026". Note that the invite URL page is disabled 90 days after the last upload to the channel (<https://developers.meta.com/horizon/resources/publish-release-channels-add-users/>), so an upload around 11/10 stays valid.
- [ ] **No changes after the deadline:** "Once the Entry Period has ended, you may not make changes or alterations to your submission" (Rules §4). FAQ: do not update the app "during the judging period between November 18 at 12:00PM PST and when winners are announced". **This includes the voice relay: keep it running and unchanged until about 12/11.**
- [ ] **Video:** "should be less than three (3) minutes and showcase footage of your Project as viewed on a Meta Quest device … uploaded to and made publicly visible on YouTube or Vimeo" (Rules §4). "Judges are not required to watch beyond three minutes." Judging criteria: "show real gameplay that honestly represents the experience. Lead with the story and what makes your project distinct. Don't lean on AI-generated video to carry the pitch" (Rules §6).
- [ ] **Text description:** "explains your inspiration, how you built it, future plans for improvement and a target launch date, if not already published" (Rules §4). Overview: "suggested length: 500 words or less". Tagline is limited to "140 characters".
- [ ] **Track and Division selected:** Gaming and New Experience. New Experience means "conceived of and built within the competition window (starting September 24, 2026). No pre-existing codebases" (Rules §4).
- [ ] **Hands-first:** "Fully usable with hands end-to-end … Can someone complete the entire experience without ever pairing a controller?" (Rules §4). Tell passes.
- [ ] **Not a wrapper:** "Thin wrappers around existing platforms or services (YouTube, ChatGPT, etc.) are highly discouraged … remove the third-party service. Is there still a Project?" (Rules §4). Tell passes: voice chat is optional, and the whole game works offline. Say so in the description.
- [ ] **Language:** "Projects must be entirely in the English language or include English subtitles" (Rules §4). All lines are English and subtitled.
- [ ] **Content:** "must follow Meta VR Developer Content Guidelines for 10+, 13+, or 18+" (Rules §4). Meta's Content Guidelines prohibit "Real money gambling or … the ability to redeem or cash out in-game currencies" (<https://developers.meta.com/horizon/policy/content-guidelines/>). Tell uses virtual chips only, with no purchases or cash-out.
- [ ] **No brands or real people:** "Projects must not contain any commercial or corporate advertising (including, without limitation, corporate logos, brand names, and slogans)" and "may not mention or depict any identifiable person other than Entrants" (Rules §4). Keep Gemini and ElevenLabs logos out of the app and video. Naming them in this text and in the privacy policy is factual attribution. Only the developer appears in the video.
- [ ] **Third-party licenses:** "If a Project uses or relies upon any third-party tools, code, or assets, the participant must be authorized to use them" (Rules §4). Check the ElevenLabs plan terms for commercial use of the generated voice lines, and the licenses of any fonts and sounds.
- [ ] **Performance (judging):** "must be performant on Meta Quest hardware (min 60 fps)" and, for passthrough, "holding up in rooms the developer never tested" (Rules §6).
- [ ] **One entry per person;** solo entrant (Rules §2).
