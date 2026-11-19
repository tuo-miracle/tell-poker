using System;
using System.Collections.Generic;
using Poker.AI;
using Poker.Core;
using Poker.Game;
using Poker.Gestures;
using Poker.Runtime;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Poker.Tests.Game
{
    /// <summary>
    /// 測試用牌桌：TableFrame 放在原點、頭在玩家座位上方、GameController 由測試手動 Step（不靠 Update）。
    /// 手部資料用 SyntheticHands 產生，時間接在目前的時鐘後面。
    /// </summary>
    public sealed class GameTestRig : IDisposable
    {
        public const float Dt = 1f / 72f;
        public readonly GameObject Root;
        public readonly TableFrame Table;
        public readonly Camera Camera;
        public readonly GameController Game;
        public readonly List<(int seat, PlayerCommand cmd)> Actions = new List<(int, PlayerCommand)>();
        public readonly List<InvalidReason> Invalids = new List<InvalidReason>();
        public readonly List<(PlayerSignals s, Poker.AI.PerformanceCue cue)> Signals = new List<(PlayerSignals, Poker.AI.PerformanceCue)>();
        public readonly List<GestureEvent> Gestures = new List<GestureEvent>();
        /// <summary>這個牌桌用的進度存檔（暫存目錄，Dispose 時刪掉）。</summary>
        public readonly string ProgressPath;
        double _handsTime;
        HandsFrame _idle;

        public GameTestRig(GameConfig config = null, Action<GameController> configure = null)
        {
            Root = new GameObject("TestTable");
            Table = Root.AddComponent<TableFrame>();
            Table.Place(new Pose(Vector3.zero, Quaternion.identity), TablePlacementSource.EnvironmentRaycast);
            Root.AddComponent<TableView>();

            var camGo = new GameObject("Head");
            Camera = camGo.AddComponent<Camera>();
            Camera.nearClipPlane = 0.02f;
            Camera.fieldOfView = 80f;
            LookFromSeat(new Vector3(0f, 0.18f, 0.55f));

            var go = new GameObject("Game");
            Game = go.AddComponent<GameController>();
            Game.AutoStep = false;
            Game.Table = Table;
            Game.Head = Camera.transform;
            Game.Config = config ?? new GameConfig { MonteCarloTrials = 80 };
            Game.AiThinkScale = 0.25f;
            // 預設不跑教學、存檔寫到暫存目錄（不碰真的 persistentDataPath）
            ProgressPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "tell_rig_" + System.Guid.NewGuid().ToString("N") + ".json");
            Game.ProgressPath = ProgressPath;
            Game.TutorialEnabled = false;
            configure?.Invoke(Game);
            Game.Init();
            Game.ActionApplied += (s, c) => Actions.Add((s, c));
            Game.InvalidAction += r => Invalids.Add(r);
            Game.SignalsSent += (s, c) => Signals.Add((s, c));
            Game.GestureSeen += e => Gestures.Add(e);
            _idle = new SyntheticHands().Wait(0.05).Frames[^1];
        }

        /// <summary>頭放在玩家座位（桌緣後 30 cm、桌面上 45 cm），看向牌桌上的某一點。</summary>
        public void LookFromSeat(Vector3 tableTarget)
        {
            Camera.transform.position = new Vector3(0f, 0.45f, -0.3f);
            Camera.transform.rotation = Quaternion.LookRotation(tableTarget - Camera.transform.position);
        }

        public void LookAt(Vector3 world)
        {
            Camera.transform.rotation = Quaternion.LookRotation(world - Camera.transform.position);
        }

        /// <summary>空手（兩手放在桌邊）跑 seconds 秒。</summary>
        public void Idle(double seconds)
        {
            int n = Mathf.CeilToInt((float)(seconds / Dt));
            for (int i = 0; i < n; i++) StepIdle();
        }

        /// <summary>沒有手（頭盔放著、手放下）跑 seconds 秒：兩手都沒追蹤到。</summary>
        public void IdleNoHands(double seconds)
        {
            var f = new HandsFrame();
            int n = Mathf.CeilToInt((float)(seconds / Dt));
            for (int i = 0; i < n; i++)
            {
                _handsTime += Dt;
                f.Time = _handsTime;
                Step(f);
            }
        }

        public void StepIdle()
        {
            _handsTime += Dt;
            _idle.Time = _handsTime;
            Step(_idle);
        }

        /// <summary>
        /// 跑一幀。先等背景 AI 決策算完（像實機：一幀 14 ms 裡決策早就算完）：測試桌同步地跑，1 秒模擬時間只花幾毫秒，
        /// 不等的話「決策何時算完」取決於機器忙不忙（2026-10-13 全套 PlayMode 偶發：盯著 Vex 的訊號還在排隊，IsGazed 為 false）。
        /// </summary>
        void Step(HandsFrame f)
        {
            if (!Game.WaitForAiDecision(10000)) throw new TimeoutException("AI decision still running after 10 s");
            Game.Step(Dt, f);
        }

        /// <summary>播放一段合成手勢（時間接在目前時鐘之後）。</summary>
        public void Play(SyntheticHands s)
        {
            double t0 = _handsTime;
            foreach (var f in s.Frames)
            {
                f.Time = t0 + f.Time + Dt;
                _handsTime = f.Time;
                Step(f);
            }
        }

        /// <summary>空轉到條件成立（最多 maxSeconds 秒）。</summary>
        public bool IdleUntil(Func<bool> cond, double maxSeconds = 30)
        {
            int n = Mathf.CeilToInt((float)(maxSeconds / Dt));
            for (int i = 0; i < n; i++)
            {
                if (cond()) return true;
                StepIdle();
            }
            return cond();
        }

        public bool WaitPlayerTurn(double maxSeconds = 30) => IdleUntil(() => Game.Flow == GameFlow.PlayerTurn, maxSeconds);

        public string Dump() =>
            "flow=" + Game.Flow + " street=" + Game.Session.State.Street + " toAct=" + Game.Session.State.ToAct + "\n" +
            "actions: " + string.Join(", ", Actions.ConvertAll(a => a.seat + ":" + a.cmd.GetType().Name)) + "\n" +
            "invalid: " + string.Join(", ", Invalids) + "\n" +
            "gestures: " + string.Join(" | ", Gestures.FindAll(g => g.Kind != GestureKind.PeekChanged).ConvertAll(g => g.ToString()));

        public void Dispose()
        {
            if (Game != null) Object.DestroyImmediate(Game.gameObject);
            if (Camera != null) Object.DestroyImmediate(Camera.gameObject);
            Object.DestroyImmediate(Root);
            foreach (var f in new[] { ProgressPath, ProgressPath + ".tmp", ProgressPath + ".corrupt" })
                if (System.IO.File.Exists(f)) System.IO.File.Delete(f);
        }
    }
}
