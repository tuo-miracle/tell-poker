using System.Globalization;
using Poker.Gestures;
using UnityEngine;

namespace Poker.Game
{
    /// <summary>
    /// 有沒有人在（2026-10-14 實機遙測：頭盔放著，教學看牌那一步 Dot 輪流重講與給建議講了 2 小時，一隻手都沒追蹤到）：
    /// - 兩手都追蹤不到 ≥ NoHandsPauseSeconds 秒、或 App 失焦 → 暫停（IdlePaused）：教學不計時、不給提示、不算卡住；
    ///   手回來（或重新取得焦點）→ 這一步從 0 重新計時，提示重講一次（不是整套重來）。
    /// - 正式牌局：手不在（HandsAway：追蹤不到超過 HandsGoneSeconds 秒、或失焦）時即時台詞的「想了 3／7／12 秒」不講、
    ///   備援按鈕的「卡住」不計時。
    /// log：[Tutorial] paused (no hands|unfocused)／[Tutorial] resumed（教學中）；[Playtest] idle_pause reason=…／idle_resume after=…（一直記）。
    /// </summary>
    public sealed partial class GameController
    {
        /// <summary>兩手都追蹤不到這麼久就暫停（頭盔放著、手放下）。</summary>
        public const float NoHandsPauseSeconds = 5f;
        /// <summary>即時台詞與備援按鈕：手追蹤不到超過這麼久就當作不在（容忍一下下的追蹤閃斷）。</summary>
        public const float HandsGoneSeconds = 1f;
        /// <summary>回來時重講提示，但這麼久內剛講過就不重講（避免兩句連著講）。</summary>
        public const float ResumeReplayAfter = 8f;
        /// <summary>手在牌桌附近（牌桌座標，公尺）才算「玩家在動」：叫醒安靜的教學提示。</summary>
        static readonly Vector3 NearTableMin = new Vector3(-1.0f, -0.3f, -0.6f), NearTableMax = new Vector3(1.0f, 0.6f, 1.3f);

        /// <summary>App 有焦點（OnApplicationFocus／OnApplicationPause；Editor 不跟，測試直接設）。</summary>
        public bool AppFocused { get; set; } = true;
        /// <summary>沒有人在：沒有手 ≥ NoHandsPauseSeconds 秒或失焦。</summary>
        public bool IdlePaused { get; private set; }
        /// <summary>手不在（或失焦）：即時台詞的發呆訊號、備援按鈕的卡住計時不算。</summary>
        public bool HandsAway => !AppFocused || Now - _handsSeenAt > HandsGoneSeconds;

        double _handsSeenAt, _idlePausedAt;

        void OnApplicationFocus(bool focus)
        {
            if (!Application.isEditor) AppFocused = focus;
        }

        void OnApplicationPause(bool paused)
        {
            if (!Application.isEditor) AppFocused = !paused;
        }

        /// <summary>每幀最先跑：記下最後一次看到手的時間，進出暫停。</summary>
        void TickPresence(HandsFrame frame)
        {
            if (frame != null && (frame.Left.Tracked || frame.Right.Tracked)) _handsSeenAt = Now;
            string why = !AppFocused ? "unfocused" : Now - _handsSeenAt >= NoHandsPauseSeconds ? "no hands" : null;
            if (why != null && !IdlePaused)
            {
                IdlePaused = true;
                _idlePausedAt = Now;
                if (TutorialRunning) Debug.Log("[Tutorial] paused (" + why + ")");
                Playtest.Event("idle_pause", "reason=" + why.Replace(' ', '_') + " where=" + (TutorialRunning ? "tutorial" : "game"));
                Ghost?.Hide();
            }
            else if (why == null && IdlePaused)
            {
                IdlePaused = false;
                Playtest.Event("idle_resume", "after=" + (Now - _idlePausedAt).ToString("0.0", CultureInfo.InvariantCulture) + " where=" + (TutorialRunning ? "tutorial" : "game"));
                if (TutorialRunning) ResumeTutorial();
            }
        }

        /// <summary>回來了：這一步從 0 計時（最多再一輪提示），這一步已經提示過而且現在做得到就重講一次。</summary>
        void ResumeTutorial()
        {
            Debug.Log("[Tutorial] resumed");
            Tutorial.RestartIdle();
            var step = Tutorial.Step;
            if (!_tutorialPrompted || _promptedStep != step || !TutorialStepReady(step) || Now - _lastReminder < ResumeReplayAfter) return;
            Voice.Say(Tutorial.Prompt(step), Now, "tutorial", force: true);
            Props.ShowTutorialGlow(Tutorial.GlowFor(step));
            _lastReminder = Now;
        }

        static bool NearTable(Vector3 p) =>
            p.x >= NearTableMin.x && p.x <= NearTableMax.x && p.y >= NearTableMin.y && p.y <= NearTableMax.y && p.z >= NearTableMin.z && p.z <= NearTableMax.z;

        /// <summary>即時台詞裡「玩家在發呆」的訊號（想了 3／7／12 秒）：手不在時不講。</summary>
        static bool IsIdleCue(Poker.AI.LiveCue c) => c == Poker.AI.LiveCue.Think3 || c == Poker.AI.LiveCue.Think7 || c == Poker.AI.LiveCue.Think12;
    }
}
