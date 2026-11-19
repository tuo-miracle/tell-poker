using System.Linq;
using NUnit.Framework;
using Poker.Core;
using Poker.Game;
using Poker.Gestures;
using UnityEngine;
using Hand = Poker.Gestures.Hand;
using NV3 = System.Numerics.Vector3;

namespace Poker.Tests.Game
{
    /// <summary>拿起底牌看（遊戲層）：牌跟著手、牌面朝向玩家的頭，放開蓋回原位；拿牌棄牌照舊；摔牌不再棄牌。</summary>
    public class CardLookTests
    {
        GameTestRig _rig;
        [TearDown] public void TearDown() { _rig?.Dispose(); _rig = null; }

        static GameConfig PlayerFirst => new GameConfig { Opponents = GameConfig.HeadsUp, Seed = 5, FirstButton = 0, MonteCarloTrials = 80 };
        static GameConfig PipFirst => new GameConfig { Opponents = GameConfig.HeadsUp, Seed = 5, FirstButton = 1, MonteCarloTrials = 80 };

        static readonly NV3 LookAt = new NV3(0f, 0.22f, 0.04f);

        /// <summary>捏住牌、抬到 LookAt、停 hold 秒（不放開）。</summary>
        static SyntheticHands GrabAndHold(double hold)
        {
            var s = GestureScripts.GrabCards(new SyntheticHands());
            s.Move(Hand.Right, 0.5, HandTarget.At(SyntheticHands.PalmForPinch(Hand.Right, LookAt)).WithPitch(-25f));
            s.Wait(hold);
            return s;
        }

        static SyntheticHands Release()
        {
            var s = new SyntheticHands();
            s.Set(Hand.Right, SyntheticHands.PalmForPinch(Hand.Right, LookAt), pinch: 1f, pitch: -25f);
            s.Move(Hand.Right, 0.12, HandTarget.Pinch(0f));
            s.Move(Hand.Right, 0.4, HandTarget.At(SyntheticHands.RightRest).WithPitch(0f));
            return s;
        }

        Vector3 RestOf(int i) => _rig.Game.Spots.PlayerCard(i) + Vector3.up * TableProps.CardY;

        void AssertOnTableFaceDown(string why)
        {
            for (int i = 0; i < 2; i++)
            {
                var c = _rig.Game.PlayerCard(i);
                Assert.Less(Vector3.Distance(c.transform.localPosition, RestOf(i)), 0.002f, $"{why}: card {i} back on its spot ({c.transform.localPosition})");
                Assert.Less(Quaternion.Angle(c.transform.localRotation, Quaternion.Euler(0f, c.Yaw, 0f)), 0.5f, $"{why}: card {i} lies flat, face down");
                Assert.IsFalse(c.FaceUp, why);
            }
        }

        [Test]
        public void LiftingTheCards_ShowsThemInFrontOfThePlayer_FacingTheHead_AndReleaseLaysThemBack()
        {
            _rig = new GameTestRig(PlayerFirst);
            Assert.IsTrue(_rig.WaitPlayerTurn());
            _rig.Play(GrabAndHold(1.0));

            Assert.IsTrue(_rig.Gestures.Any(g => g.Kind == GestureKind.CardsLifted), _rig.Dump());
            Assert.IsTrue(_rig.Game.CardsInHand, "cards follow the hand");
            var head = _rig.Camera.transform.position;
            var grip = _rig.Table.transform.TransformPoint(new Vector3(LookAt.X, LookAt.Y, LookAt.Z));
            for (int i = 0; i < 2; i++)
            {
                var c = _rig.Game.PlayerCard(i).transform;
                Assert.Less(Vector3.Distance(c.position, grip), 0.08f, $"card {i} is at the hand ({c.position} vs grip {grip})");
                // 牌面在本地 −Y：要朝向頭
                var faceNormal = -c.up;
                var toHead = (head - c.position).normalized;
                Assert.Greater(Vector3.Dot(faceNormal, toHead), 0.95f, $"card {i} face points at the head");
                // 牌面上緣（+Z）朝上，看起來是正的
                Assert.Greater(Vector3.Dot(c.forward, Vector3.up), 0.3f, $"card {i} upright");
                Assert.AreEqual(0f, _rig.Game.PlayerCard(i).PeekDegrees, 0.01f, "no peek flap while held");
            }
            Assert.IsFalse(_rig.Actions.Any(a => a.seat == 0), "looking is not an action\n" + _rig.Dump());

            _rig.Play(Release());
            Assert.IsTrue(_rig.Gestures.Any(g => g.Kind == GestureKind.CardsReturned), _rig.Dump());
            _rig.Idle(0.6);
            Assert.IsFalse(_rig.Game.CardsInHand);
            AssertOnTableFaceDown("after release");
            Assert.AreEqual(GameFlow.PlayerTurn, _rig.Game.Flow, "still the player's turn");
        }

        [Test]
        public void Looking_CountsAsPeekTime_ButTheAiNeverGetsCardValues()
        {
            _rig = new GameTestRig(PipFirst, g => g.AiOverride = st => new Call(1));
            Assert.IsTrue(_rig.WaitPlayerTurn());
            _rig.Play(GrabAndHold(2.0));
            _rig.Play(Release());
            _rig.Idle(0.4);
            _rig.Play(GestureScripts.Knock(new SyntheticHands(), Hand.Right, GestureScripts.KnockSpot(Hand.Right)));
            Assert.IsTrue(_rig.Actions.Any(a => a.seat == 0 && a.cmd is Check), _rig.Dump());
            var sent = _rig.Signals.Last(x => x.s.Time > 0 && x.s.GazeSeat < 0).s;
            Assert.GreaterOrEqual(sent.PeekSeconds, 1.5f, "holding the cards up counts as looking at them");

            // AI 只拿到肢體訊號：訊號與手勢事件裡都沒有任何牌
            foreach (var f in typeof(Poker.AI.PlayerSignals).GetFields())
                Assert.IsFalse(MentionsCards(f.FieldType), "PlayerSignals." + f.Name);
            foreach (var f in typeof(GestureEvent).GetFields())
                Assert.IsFalse(MentionsCards(f.FieldType), "GestureEvent." + f.Name);
        }

        static bool MentionsCards(System.Type t) =>
            t == typeof(Card) || (t.IsArray && MentionsCards(t.GetElementType())) || (t.IsGenericType && t.GetGenericArguments().Any(MentionsCards));

        [Test]
        public void PickUpAndCarryToTheMuck_Folds_AndTheCardsEndUpInTheMuck()
        {
            _rig = new GameTestRig(PlayerFirst);
            Assert.IsTrue(_rig.WaitPlayerTurn());
            _rig.Play(GestureScripts.CarryCardsToMuck(new SyntheticHands()));
            Assert.IsTrue(_rig.Actions.Any(a => a.seat == 0 && a.cmd is Fold), _rig.Dump());
            _rig.Idle(1.0);
            Assert.IsFalse(_rig.Game.CardsInHand);
            for (int i = 0; i < 2; i++)
            {
                var c = _rig.Game.PlayerCard(i);
                var p = c.transform.localPosition;
                Assert.Less(Vector2.Distance(new Vector2(p.x, p.z), new Vector2(_rig.Game.Spots.Muck.x, _rig.Game.Spots.Muck.z)), 0.03f, $"card {i} in the muck ({p})");
                Assert.Less(Quaternion.Angle(c.transform.localRotation, Quaternion.Euler(0f, c.Yaw, 0f)), 0.5f, $"card {i} lies flat");
            }

            // 棄掉的牌拿不起來
            int lifted = _rig.Gestures.Count(g => g.Kind == GestureKind.CardsLifted);
            _rig.Play(GestureScripts.LiftCards(new SyntheticHands()));
            Assert.AreEqual(lifted, _rig.Gestures.Count(g => g.Kind == GestureKind.CardsLifted), "folded cards cannot be picked up");
            Assert.IsFalse(_rig.Game.CardsInHand);
        }

        [Test]
        public void HoldingTheCardsUpAtShowdown_DoesNotReveal_TimeoutFlipLaysThemDownFirst()
        {
            _rig = new GameTestRig(PipFirst, g => g.AiOverride = st => Engine.GetLegalActions(st, 1).CanCheck ? (PlayerCommand)new Check(1) : new Call(1));
            Assert.IsTrue(_rig.IdleUntil(() => _rig.Game.Session.HandsStarted == 1, 1), "first hand dealt");
            for (int guard = 0; guard < 8; guard++)
            {
                Assert.IsTrue(_rig.IdleUntil(() => _rig.Game.Flow == GameFlow.PlayerTurn || _rig.Game.Session.IsHandOver, 20), "stalled");
                if (_rig.Game.Session.IsHandOver) break;
                _rig.Play(GestureScripts.Knock(new SyntheticHands(), Hand.Right, GestureScripts.KnockSpot(Hand.Right)));
            }
            Assert.IsTrue(_rig.IdleUntil(() => _rig.Game.Flow == GameFlow.ShowdownWait, 5), "showdown waits for the flip");
            _rig.Game.ShowdownFlipTimeout = 2.5f;
            _rig.Play(GrabAndHold(1.0));
            Assert.IsTrue(_rig.Game.CardsInHand, _rig.Dump());
            Assert.IsFalse(_rig.Game.PlayerCard(0).FaceUp, "holding up is private, not a reveal");
            Assert.IsFalse(_rig.Gestures.Any(g => g.Kind == GestureKind.ShowdownFlip));
            Assert.AreEqual(GameFlow.ShowdownWait, _rig.Game.Flow);

            // 還拿著時翻牌逾時：先蓋回桌上再翻開（翻開是攤牌規則，不是因為舉著）
            _rig.Play(GrabAndHoldContinue(2.5));
            Assert.IsTrue(_rig.IdleUntil(() => _rig.Game.Flow == GameFlow.HandOver, 15), _rig.Dump());
            Assert.IsFalse(_rig.Game.CardsInHand);
            for (int i = 0; i < 2; i++)
            {
                var c = _rig.Game.PlayerCard(i);
                Assert.IsTrue(c.FaceUp, "revealed by the showdown timeout");
                Assert.Less(Vector3.Distance(c.transform.localPosition, RestOf(i)), 0.002f, $"card {i} flipped on its spot");
            }
        }

        /// <summary>Pip 先動、一路過牌到攤牌，等玩家亮牌（逾時拉長，不讓自動翻牌干擾）。</summary>
        void ReachShowdown()
        {
            _rig = new GameTestRig(PipFirst, g => g.AiOverride = st => Engine.GetLegalActions(st, 1).CanCheck ? (PlayerCommand)new Check(1) : new Call(1));
            Assert.IsTrue(_rig.IdleUntil(() => _rig.Game.Session.HandsStarted == 1, 1), "first hand dealt");
            for (int guard = 0; guard < 8; guard++)
            {
                Assert.IsTrue(_rig.IdleUntil(() => _rig.Game.Flow == GameFlow.PlayerTurn || _rig.Game.Session.IsHandOver, 20), "stalled");
                if (_rig.Game.Session.IsHandOver) break;
                _rig.Play(GestureScripts.Knock(new SyntheticHands(), Hand.Right, GestureScripts.KnockSpot(Hand.Right)));
            }
            Assert.IsTrue(_rig.IdleUntil(() => _rig.Game.Flow == GameFlow.ShowdownWait, 5), "showdown waits for the flip");
            _rig.Game.ShowdownFlipTimeout = 30f;
        }

        /// <summary>s8.log 7445 s：捏住牌抬起（−0.01, 0.08, 0.10），0.28 s 往前移到 release 放開。</summary>
        static SyntheticHands TossForward(NV3 release)
        {
            var s = GestureScripts.GrabCards(new SyntheticHands());
            s.Move(Hand.Right, 0.2, HandTarget.At(SyntheticHands.PalmForPinch(Hand.Right, new NV3(-0.01f, 0.08f, 0.10f))));
            s.Move(Hand.Right, 0.28, HandTarget.At(SyntheticHands.PalmForPinch(Hand.Right, release)));
            s.Move(Hand.Right, 0.12, HandTarget.Pinch(0f));
            s.Move(Hand.Right, 0.3, HandTarget.At(s.Palm(Hand.Right) + new NV3(0, 0.06f, 0)));
            s.Move(Hand.Right, 0.4, HandTarget.At(SyntheticHands.RightRest));
            return s;
        }

        [Test]
        public void AtShowdown_TossingTheCardsForward_RevealsThemFaceUp_WhereTheyWereReleased()
        {
            ReachShowdown();
            var release = new NV3(0.18f, 0.11f, 0.33f);
            _rig.Play(TossForward(release));
            Assert.IsFalse(_rig.Gestures.Any(g => g.Kind == GestureKind.FoldPushed), "never a fold at showdown\n" + _rig.Dump());
            var flip = _rig.Gestures.LastOrDefault(g => g.Kind == GestureKind.ShowdownFlip);
            Assert.AreEqual(GestureKind.ShowdownFlip, flip.Kind, _rig.Dump());
            _rig.Idle(0.6);
            Assert.IsFalse(_rig.Game.CardsInHand);
            var spot = new Vector3(flip.Position.X, 0f, flip.Position.Z);
            Assert.Less(Vector2.Distance(new Vector2(spot.x, spot.z), new Vector2(release.X, release.Z)), 0.05f, "landing near the release " + spot);
            var center = Vector3.zero;
            for (int i = 0; i < 2; i++)
            {
                var c = _rig.Game.PlayerCard(i);
                Assert.IsTrue(c.FaceUp, $"card {i} revealed");
                Assert.Less(Quaternion.Angle(c.transform.localRotation, Quaternion.Euler(0f, c.Yaw, 0f) * Quaternion.AngleAxis(180f, Vector3.forward)), 0.5f, $"card {i} lies flat, face up");
                Assert.AreEqual(TableProps.CardY, c.transform.localPosition.y, 1e-4f, $"card {i} on the felt");
                center += c.transform.localPosition * 0.5f;
            }
            Assert.Less(Vector2.Distance(new Vector2(center.x, center.z), new Vector2(spot.x, spot.z)), 0.005f, "cards turned over where they were put, not back on the own spot " + center);
            Assert.Greater(Vector3.Distance(_rig.Game.PlayerCard(0).transform.localPosition, _rig.Game.PlayerCard(1).transform.localPosition), CardView.Width * 0.9f, "side by side");
            Assert.IsTrue(_rig.IdleUntil(() => _rig.Game.Flow == GameFlow.HandOver, 15), _rig.Dump());
            Assert.IsFalse(_rig.Actions.Any(a => a.seat == 0 && a.cmd is Fold));
        }

        [Test]
        public void AtShowdown_PushingTheCardsForward_RevealsThemInFront()
        {
            ReachShowdown();
            _rig.Play(GestureScripts.FoldPush(new SyntheticHands()));
            Assert.IsFalse(_rig.Gestures.Any(g => g.Kind == GestureKind.FoldPushed), _rig.Dump());
            _rig.Idle(0.6);
            var l = _rig.Game.Spots.Layout;
            for (int i = 0; i < 2; i++)
            {
                var c = _rig.Game.PlayerCard(i);
                Assert.IsTrue(c.FaceUp, $"card {i} revealed\n" + _rig.Dump());
                Assert.Greater(c.transform.localPosition.z, RestOf(i).z + 0.05f, $"card {i} pushed forward ({c.transform.localPosition})");
                Assert.LessOrEqual(c.transform.localPosition.z, l.BetLineZ + CardView.Length * 0.5f, $"card {i} stays on the player's side");
            }
        }

        [Test]
        public void AtShowdown_LookingAndPuttingTheCardsBack_DoesNotReveal()
        {
            ReachShowdown();
            int before = _rig.Gestures.Count;
            _rig.Play(GestureScripts.LiftCards(new SyntheticHands(), hold: 0.8, releaseAt: GestureScripts.CardGrip));
            _rig.Idle(0.6);
            Assert.IsTrue(_rig.Gestures.Skip(before).Any(g => g.Kind == GestureKind.CardsLifted), "looked\n" + _rig.Dump());
            Assert.IsFalse(_rig.Gestures.Skip(before).Any(g => g.IsAction), _rig.Dump());
            AssertOnTableFaceDown("put back at showdown");
            Assert.AreEqual(GameFlow.ShowdownWait, _rig.Game.Flow, "still waiting for the reveal");
        }

        /// <summary>手維持在 LookAt 捏著（接著上一段）。</summary>
        static SyntheticHands GrabAndHoldContinue(double hold)
        {
            var s = new SyntheticHands();
            s.Set(Hand.Right, SyntheticHands.PalmForPinch(Hand.Right, LookAt), pinch: 1f, pitch: -25f);
            s.Wait(hold);
            return s;
        }

        [Test]
        public void SlappingTheCards_NoLongerFolds()
        {
            _rig = new GameTestRig(PlayerFirst);
            Assert.IsTrue(_rig.WaitPlayerTurn());
            _rig.Play(GestureScripts.Slam(new SyntheticHands(), height: 0.32f, duration: 0.13));
            Assert.IsFalse(_rig.Actions.Any(a => a.seat == 0), "slam is not a gesture any more\n" + _rig.Dump());
            Assert.AreEqual(GameFlow.PlayerTurn, _rig.Game.Flow);
            Assert.IsFalse(System.Enum.GetNames(typeof(SfxKind)).Any(n => n.Contains("Slap")), "slam sounds removed");
        }

        [Test]
        public void HeldPose_FacesTheHead_TwoCardsSideBySide()
        {
            var grip = new Vector3(0.02f, 0.2f, 0.05f);
            var head = new Vector3(0f, 0.45f, -0.3f);
            HeldCards.CardPose(0, grip, head, out var p0, out var r0);
            HeldCards.CardPose(1, grip, head, out var p1, out var r1);
            var toHead = (head - grip).normalized;
            Assert.Greater(Vector3.Dot(r0 * Vector3.down, toHead), 0.98f);
            Assert.Greater(Vector3.Dot(r1 * Vector3.down, toHead), 0.98f);
            Assert.Greater(Vector3.Distance(p0, p1), CardView.Width * 0.4f, "cards fanned apart, not stacked");
            Assert.Less(Vector3.Distance((p0 + p1) * 0.5f, grip), CardView.Length, "held near the grip");
            // 左邊那張（第 0 張）在玩家看來偏左
            var right = Vector3.Cross(Vector3.up, -toHead).normalized;
            Assert.Less(Vector3.Dot(p0 - p1, right), 0f);
        }
    }
}
