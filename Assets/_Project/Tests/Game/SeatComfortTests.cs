using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Poker.Characters;
using Poker.Game;
using Poker.Runtime;
using UnityEngine;
using UnityEngine.TestTools;

namespace Poker.Tests.Game
{
    /// <summary>
    /// 預設座位（4 人桌、真實尺寸桌的 5 人局）坐起來舒不舒服（2026-10-14 實機回饋）：Pip 坐太近、佔掉左邊一大塊視野；Rocco 的籌碼堆擠到 Pip 的手臂。
    /// 從坐著的眼睛（桌面上 45 cm、桌緣後 30 cm）量：每個角色的頭至少 MinHeadDistance 遠；
    /// 每個 AI 的籌碼堆、荷官的牌堆離別人的手與前臂（水平）至少 MinStackClearance；
    /// 而且仍守 SeatingPlanner 的規則（身體在 ±60° 內、相鄰身體至少 Spacing）。
    /// </summary>
    public class SeatComfortTests
    {
        public const float MinHeadDistance = 1.12f;
        public const float MinStackClearance = 0.12f;
        static readonly Vector3 Eye = SeatingPlanner.DefaultHead;

        GameTestRig _rig;

        [TearDown]
        public void TearDown()
        {
            _rig?.Dispose();
            _rig = null;
        }

        [UnityTest]
        public IEnumerator DefaultSeats_NobodyCrowdsThePlayer_OrAnotherPlayersChips([Values(false, true)] bool fiveHanded)
        {
            var cfg = new GameConfig { Seed = 12, FirstButton = 0, MonteCarloTrials = 60 };
            if (fiveHanded) cfg.Opponents = GameConfig.FiveHanded;
            _rig = new GameTestRig(cfg);
            yield return null;
            var game = _rig.Game;
            _rig.Idle(0.5);   // 角色坐好、手放上桌
            var t = game.Table.transform;
            var problems = new List<string>();
            var log = new System.Text.StringBuilder();

            var people = new List<(string name, int seat, CharacterPerformer c)>();
            for (int s = 1; s < game.Spots.SeatCount; s++) if (game.Character(s) != null) people.Add((game.Character(s).name, s, game.Character(s)));
            if (game.Dealer != null) people.Add(("Dealer", -1, game.Dealer));
            Assert.AreEqual(cfg.Opponents.Length + 1, people.Count, "AIs and the dealer");

            foreach (var (name, seat, c) in people)
            {
                var head = t.InverseTransformPoint(c.Rig.Head.position);
                float d = Vector3.Distance(Eye, head);
                float yaw = Mathf.Atan2(head.x - Eye.x, head.z - Eye.z) * Mathf.Rad2Deg;
                log.AppendLine($"{name,-20} head {head:F2} dist {d:0.00} m yaw {yaw:0.0}°");
                if (d < MinHeadDistance) problems.Add($"{name}: head {d:0.00} m from the eye (< {MinHeadDistance})");
            }

            // 籌碼堆 vs 別人的手、前臂（水平距離）
            foreach (var (name, seat, _) in people)
            {
                if (seat < 1) continue;
                var stack = game.Spots.StackOf(seat);
                foreach (var (other, oseat, oc) in people)
                {
                    if (oseat == seat) continue;
                    var parts = new[] { oc.Rig.HandL, oc.Rig.HandR, oc.Rig.ForeArmL, oc.Rig.ForeArmR };
                    foreach (var p in parts)
                    {
                        var q = t.InverseTransformPoint(p.position);
                        float dx = new Vector2(q.x - stack.x, q.z - stack.z).magnitude;
                        if (dx < MinStackClearance) problems.Add($"{name}'s stack {stack:F2} is {dx:0.000} m from {other}'s {p.name} {q:F2}");
                        log.AppendLine($"  {name} stack {stack:F2} ↔ {other} {p.name} {dx:0.000} m");
                    }
                }
            }

            // 籌碼堆也不能擠到底池
            foreach (var (name, seat, _) in people)
            {
                if (seat < 1) continue;
                var stack = game.Spots.StackOf(seat);
                var pot = game.Spots.PotPile;
                float dp = new Vector2(stack.x - pot.x, stack.z - pot.z).magnitude;
                log.AppendLine($"  {name} stack ↔ pot {dp:0.000} m");
                if (dp < 0.10f) problems.Add($"{name}'s stack {stack:F2} is {dp:0.000} m from the pot {pot:F2}");
            }

            // 荷官的牌堆 vs 其他人的手、前臂
            var deck = game.Spots.Deck;
            foreach (var (other, oseat, oc) in people)
            {
                if (oseat < 1) continue;
                foreach (var p in new[] { oc.Rig.HandL, oc.Rig.HandR, oc.Rig.ForeArmL, oc.Rig.ForeArmR })
                {
                    var q = t.InverseTransformPoint(p.position);
                    float dx = new Vector2(q.x - deck.x, q.z - deck.z).magnitude;
                    log.AppendLine($"  deck {deck:F2} ↔ {other} {p.name} {dx:0.000} m");
                    if (dx < MinStackClearance) problems.Add($"dealer's deck {deck:F2} is {dx:0.000} m from {other}'s {p.name} {q:F2}");
                }
            }

            // SeatingPlanner 的規則
            var bodies = people.Select(p => (p.name, body: SeatingPlanner.BodyOf(p.seat >= 1 ? game.Spots.SeatPose(p.seat) : game.Spots.DealerSeat))).ToList();
            foreach (var (name, body) in bodies)
                if (!SeatingPlanner.InView(Eye, body)) problems.Add($"{name}: body {body:F2} outside ±{SeatingPlanner.MaxYaw}°");
            for (int i = 0; i < bodies.Count; i++)
                for (int j = i + 1; j < bodies.Count; j++)
                {
                    float d = Vector3.Distance(bodies[i].body, bodies[j].body);
                    if (d < SeatingPlanner.Spacing - 1e-3f) problems.Add($"{bodies[i].name} and {bodies[j].name}: bodies {d:0.00} m apart (< {SeatingPlanner.Spacing})");
                }

            Debug.Log("[Seats]\n" + log);
            Assert.IsEmpty(problems, string.Join("\n", problems));
        }
    }
}
