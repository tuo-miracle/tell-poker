# Release-channel checklist — Tell

Goal: before **11/10**, have a signed release build in a release channel named **"Competition"**, with an invite URL that judges can use. Keep it unchanged and available until the winner announcement (about **12/11**).

Legend: **[DEV]** = the developer must do it personally (account, legal, payment, identity). **[PREP]** = we can prepare it in the repo. **[BOTH]** = we prepare it, the developer submits it.

Official sources used:
- Competition rules: <https://start-developer-competition-26.devpost.com/rules>
- Release channels: <https://developers.meta.com/horizon/resources/publish-release-channels/>
- Add a custom channel: <https://developers.meta.com/horizon/resources/publish-release-channels-add-custom/>
- Add users and invite URL: <https://developers.meta.com/horizon/resources/publish-release-channels-add-users/>
- Upload overview: <https://developers.meta.com/horizon/resources/publish-upload-overview/>
- Quest technical requirements (VRCs): <https://developers.meta.com/horizon/resources/publish-quest-req/>
- Manifest requirements: <https://developers.meta.com/horizon/resources/publish-mobile-manifest/>
- Privacy VRCs: <https://developers.meta.com/horizon/design/safetyprivacy/>
- Privacy policy requirements: <https://developers.meta.com/horizon/policy/privacy-policy/>
- Developer Data Use Policy: <https://developers.meta.com/horizon/policy/data-use/>
- Data Use Checkup: <https://developers.meta.com/horizon/resources/publish-data-use/>
- Store asset guidelines: <https://developers.meta.com/horizon/resources/asset-guidelines/>
- Content guidelines: <https://developers.meta.com/horizon/policy/content-guidelines/>
- Age groups: <https://developers.meta.com/horizon/policy/age-groups/>

## A. Accounts and legal — [DEV] only

- [ ] Meta account with **Developer Access** enabled and a developer **organization** created in the Meta Horizon Developer Dashboard (rules §2(c)).
- [ ] **Meta VR Start membership** confirmed ("Start Member" flair on the developer forum, per the FAQ) before you submit (rules §2(d)).
- [ ] Organization verification and accepting the developer agreements in the dashboard (only the account owner can do this).
- [ ] Devpost account, joined the hackathon, and submission form filled in (`docs/submission/devpost.md`).
- [ ] Decide on the **contact email** for the privacy policy and store listing, and replace `<CONTACT_EMAIL>` in `docs/privacy.md` (then run `node scripts/sync-privacy.ts` in `tools/voice-proxy` and redeploy the relay).
- [ ] Confirm that the API plans allow this use: **Gemini API** (paid tier or not; this affects whether Google may use the data, see open questions) and **ElevenLabs** (commercial rights for the pre-recorded lines on your plan).
- [ ] Keep the **release keystore** and its passwords somewhere safe outside the repo (see D). If it is lost, you cannot update the app.

## B. Create the app and the "Competition" channel — [DEV] (in the dashboard)

1. Developer Dashboard → **Create New App** → platform **Meta Quest (Horizon OS)**. App name: **Tell**.
2. Copy the App ID (we don't use the Platform SDK, so it doesn't need to go into the build).
3. **Distribution → Release Channels → Create Release Channel**. Name it exactly **`Competition`** (rules: "a new release channel named “Competition”"). "Channel names can't be changed later."
4. Upload the build to **Competition** (see E).
5. **Release Channels → click "Competition" → Channel Settings → Channel Invite → check "Grant access to users by URL"**, choose **automatic approval**, and **Save Changes**. Copy the URL into Devpost.
   - The invite URL page "is disabled 90 days after the latest build upload to the channel". An upload around 11/10 stays valid past about 12/11. Re-uploading resets the 90 days, but **don't upload after 11/18 12:00 PT** (judging freeze).
   - Default limit is 200 users (it can be raised to 2,500, but that is permanent and triggers a manual review on future Data Use Checkups). The judges' team size is unknown; 200 is probably enough.
   - "Changing the URL will break any previously shared invite URLs." Never press "change" after submitting.
6. Test the invite URL yourself from a **second Meta account** on a headset: join → install → launch, with no developer mode on that headset.

## C. Store listing data the dashboard asks for

A release channel doesn't need store review, but the dashboard may require some fields before builds can be distributed. Fill them in anyway, because they are needed for a public launch.

| Item | Spec (asset guidelines) | Who |
|---|---|---|
| **Privacy Policy URL** | `https://vr-poker-api.elebymax.com/privacy` (public, HTTPS, trusted CA, served by the relay: `GET /privacy`). Required: VRC.Quest.Privacy.1–4. | [PREP] done, [DEV] pastes it in the Additional Details page |
| Icon | 512×512 px, 24-bit PNG, solid with no transparency, square corners | [PREP] |
| Spatialized icon | 180×180 px, background image + transparent foreground, 18dp padding, no shadow | [PREP] |
| Hero cover | 3000×900 px (10:3), 24-bit PNG, title in the safe area | [PREP] |
| Cover landscape | 2560×1440 px (16:9), 24-bit PNG | [PREP] |
| Cover square | 1440×1440 px, 24-bit PNG | [PREP] |
| Cover portrait | 1008×1440 px (7:10), 24-bit PNG | [PREP] |
| Mini landscape | 1080×360 px (3:1), 24-bit PNG | [PREP] |
| Logo (optional) | up to 9000×1440 px, 32-bit PNG with transparency | [PREP] |
| Screenshots | **5 unique**, 2560×1440 px, 24-bit PNG, real in-app content, no marketing text or badges | [PREP] from headset captures |
| Trailer (optional) | 16:9, 1080p–2K, MP4/H.264/AAC, **30 s – 2 min**, real in-app content, Meta Quest hardware only | [PREP] cut from the competition video |
| Short and long description, genre (Card / Casino), "Hands" input, Seated play mode, Mixed reality | — | [PREP] text from devpost.md |
| Supported devices | Quest 3, Quest 3S (judged on these); Quest 2/Pro optional | [DEV] |

Notes: covers must not contain pricing, banners or extra text. The title must be the most prominent text. No third-party logos (Gemini and ElevenLabs logos must not appear in any asset).

## D. Content rating, age group, permissions and data use

- [ ] **[DEV] IARC rating:** in Submission Info → Content Ratings, the dashboard redirects you to the IARC questionnaire. It is free and the result is instant. Answer honestly: **simulated gambling = yes** (poker with virtual chips; no real money, no purchases, no cash-out). **User interaction / users can communicate = no** (no other people; characters are AI). Mild language: characters trash-talk at a PG level (the relay filters replies). The questionnaire decides the actual result; we can't predict it reliably (simulated gambling is often rated Teen/12+ or higher).
- [ ] **[DEV] Age group self-certification:** choose **Teens and Adults (13+)**. The policy says the age group is separate from IARC and is verified by Meta. Content Guidelines prohibit only "Real money gambling or … the ability to redeem or cash out in-game currencies or objects". Tell complies. Gambling-themed content may lead reviewers to expect a higher rating; choose 18+ only if IARC returns an adult rating (open question).
- [ ] **Data Use Checkup:** "A Data Use Checkup (DUC) is a requirement for any app that uses Platform SDK features". **Tell uses no Platform SDK features** (no `Oculus.Platform`; packages are only Meta XR Core, Interaction (OVR) and MRUK 207). So a DUC is **not required**. If the dashboard still shows one, complete it with "no features". VRC.Quest.Privacy.5 ("Team and app must clear data protection checks") still applies at the organization level.
- [ ] **Permissions justification** (VRC.Quest.Security.2: "minimum number of permissions required"). Prepare this text for review notes:

| Permission (manifest) | Why |
|---|---|
| `android.permission.RECORD_AUDIO` | Optional voice chat with the characters. Requested at runtime **only after** the player turns on Settings → Voice chat and agrees to the consent screen. The game is fully playable if it is denied. |
| `android.permission.INTERNET` | Sends voice chat audio to our relay (HTTPS/WSS). Nothing else uses the network. |
| `com.oculus.permission.HAND_TRACKING` | All gameplay is bare-hand gestures. |
| `com.oculus.permission.USE_SCENE` | Finds the real table (MRUK) to place the poker table on it. |
| `com.oculus.permission.USE_ANCHOR_API` | Anchors the table to the room. **Remove it if the persistent-anchor feature does not ship.** |

- [ ] **Privacy policy** content checked against Meta's requirements: what data, how processed, purposes, and how to request deletion; public and HTTPS (<https://developers.meta.com/horizon/policy/privacy-policy/>). Data Use Policy: microphone data counts as Device User Data, and sharing it with a third party needs the user's express consent (the in-app consent screen). Keep "proof of that direction or consent": the consent text in `WristMenuView.VoiceConsentText` plus the stored `VoiceConsent` flag. Never sell the data.

## E. Build, signing and upload — [BOTH]

- [ ] **[DEV] Create a release keystore** once (Unity → Project Settings → Player → Publishing Settings → Keystore Manager, or `keytool -genkeypair -v -keystore tell-release.keystore -alias tell -keyalg RSA -keysize 2048 -validity 10000`). Store it **outside the repo**, for example `~/keys/`, with the passwords in a password manager. The first upload binds the app to that key.
- [ ] **[PREP] Build with the custom keystore** (`androidUseCustomKeystore` is currently `0`, which means the debug keystore). Add a `ProjectConfigurator`/`BuildScripts` option that reads the keystore path and passwords from environment variables, never committed. APK signature scheme **v2** is required (VRC.Quest.Packaging.2). Verify with `apksigner verify --verbose Builds/Quest/TellPoker.apk` (it should show "Verified using v2 scheme: true").
- [ ] **[PREP] Version:** `bundleVersion` (versionName) `1.0` → `0.9.0` (or `1.0.0`). **`AndroidBundleVersionCode` must increase on every upload** (currently `1`). Set it from the build script or bump it by hand before each upload.
- [ ] Upload with **Meta Quest Developer Hub** (App Distribution → Upload) or the **Oculus Platform Utility**: `ovr-platform-util upload-quest-build --app_id <APP_ID> --app_secret <SECRET> --apk Builds/Quest/TellPoker.apk --channel Competition`. The app secret is **[DEV]**: never commit it.
- [ ] "All uploaded apps, even alpha builds, must meet the release packaging requirements". Check the VRCs below before uploading.

## F. Build changes needed before the release build — [PREP]

| # | Change | Where | Why |
|---|---|---|---|
| 1 | **Disable cleartext HTTP — done 2026-10-14:** `ProjectConfigurator` now sets `InsecureHttpOption.NotAllowed` (and `ProjectSettings.asset` is committed with it). Dev-only opt-in for the local relay: `TELL_ALLOW_HTTP=1 VOICE_URL=http://127.0.0.1:8787 tools/build-quest.sh`; `build-quest.sh` refuses an `http://` VOICE_URL without it. **Release build: do not set `TELL_ALLOW_HTTP`.** | `Assets/_Project/Editor/Build/ProjectConfigurator.cs` (`HttpOptionFor`, test `ProjectConfiguratorTests`) | Security. No plain HTTP in the release build. |
| 2 | **Dev tools off:** build **without** `TELL_DEV_TOOLS=1` (no RecorderPanel or HandDebug in the scene; `MainSceneGameTests` asserts this). Hand recordings must not be written on judges' headsets. | build command | Privacy policy says hand data is never stored |
| 3 | **Non-debuggable release:** `BuildOptions.None` (not Development) gives `android:debuggable` false. Verify with `aapt dump badging` / `apkanalyzer manifest debuggable`. | `BuildScripts.cs` | Manifest requirement |
| 4 | **Version bump:** versionName plus a monotonically increasing versionCode | ProjectSettings / configurator | Upload rejects a repeated code |
| 5 | **Release signing** (custom keystore from env vars) | configurator / build script | See E |
| 6 | **Company name:** `DefaultCompany` → your developer or org name | ProjectSettings `companyName` | Shows in the Android metadata and the save path |
| 7 | **Supported devices:** the manifest lists `quest|quest2|questpro|quest3|quest3s|stanley`. Decide whether to drop `quest` (Quest 1, unsupported by SDK 207) and confirm what `stanley` is. Judges use Quest 3/3S. | `Assets/Plugins/Android/AndroidManifest.xml` | Avoid offering the app to devices it can't run on |
| 8 | **Remove `USE_ANCHOR_API`** if persistent anchors don't ship | manifest | Minimum permissions (VRC.Quest.Security.2) |
| 9 | **Voice client token:** `build-quest.sh` writes `.secrets/voice-client.token` into `Resources/VoiceClient.json`, so it ships inside the APK and can be extracted. That is acceptable for the competition only because of the relay's caps. Since 2026-10-14 the relay limits **per random install ID** (6 burst, 1 per 5 s, 80 turns per UTC day per headset) with a global backstop of 3000 turns/day (`VOICE_DAILY_CAP`), so judges no longer share one bucket. Check the ElevenLabs credit budget against `VOICE_DAILY_CAP` before deploying, watch `GET /stats` (needs the token) during judging, and rotate the token after judging. | `tools/build-quest.sh`, `deploy/vr-poker-api/docker-compose.yml` | Abuse protection vs. judges being rate-limited |
| 10 | **Fill in `<CONTACT_EMAIL>`** in `docs/privacy.md` → `node scripts/sync-privacy.ts` → test → `tools/deploy-voice-api.sh` | relay | VRC.Quest.Privacy.4 (deletion path must work) |
| 11 | Already compliant, just verify: `installLocation="auto"`, `com.oculus.intent.category.VR`, `android.hardware.vr.headtracking required=true`, `excludeFromRecents=true`, `targetSdkVersion 34` (required for apps created after March 1, 2026), IL2CPP **ARM64** only (VRC.Quest.Packaging.6), APK < 1 GB (VRC.Quest.Packaging.5), `allowBackup=false`. | manifest / ProjectSettings | Packaging VRCs |

## G. Final pre-submit verification (on a headset that is not your dev headset, if possible)

- [ ] Install via the **invite URL** on a second account. The app launches, passthrough works, and the table snaps to an untested table.
- [ ] Full tutorial plus one game, controller-free, at 60 fps or more (OVR Metrics).
- [ ] Voice chat: consent screen → mic prompt → reply. With Wi-Fi off, the game still plays (offline lines).
- [ ] `https://vr-poker-api.elebymax.com/privacy` and `/` load on a phone. `/health` returns ok.
- [ ] The relay stays up and **unchanged** from 11/18 until about 12/11 (the judging freeze applies to "the state of the Entry"). Have uptime monitoring or at least a daily check.
- [ ] Devpost: release channel URL, YouTube link (public), description, Track **Gaming**, Division **New Experience**, target launch date. Submit **before 11/10** (your hard stop). The official deadline is 11/18 12:00 PT.
