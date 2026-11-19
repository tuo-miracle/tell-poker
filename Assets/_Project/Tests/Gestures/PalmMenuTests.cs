using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using NUnit.Framework;
using Poker.Gestures;

namespace Poker.Tests
{
    /// <summary>
    /// 掌心朝上叫出選單（2026-10-14，實機回報「用手叫選單很不靈敏」）：非慣用手掌心朝上停 0.4 s → 手掌上方浮出按鈕，
    /// 另一手食指進按鈕（半徑 3 cm）停 0.15 s 或穿過按鈕面就算按下。也測防誤觸（牌、籌碼、攤牌、捏合、握拳）與追蹤掉幀。
    /// </summary>
    public class PalmMenuTests
    {
        static readonly Vector3 Head = new Vector3(0f, 0.45f, -0.3f);
        /// <summary>左手（非慣用手）抬在身前左側、牌桌上方 18 cm，離牌與籌碼堆都遠。</summary>
        static readonly Vector3 PalmSpot = new Vector3(-0.22f, 0.18f, 0.02f);

        static PalmMenuContext Ctx(bool cards = false, bool chips = false, bool showdown = false, Vector3? head = null, bool noHead = false) =>
            new PalmMenuContext { HoldingCards = cards, HoldingChips = chips, ShowdownFlip = showdown, Head = noHead ? (Vector3?)null : head ?? Head };

        sealed class Result
        {
            public readonly List<string> Log = new List<string>();
            public readonly List<double> Presses = new List<double>();
            public readonly List<(double t, bool visible)> Vis = new List<(double, bool)>();
            public PalmMenu Menu;
            public double FirstVisible = double.NaN;
            public bool EverVisible => !double.IsNaN(FirstVisible);
            public bool VisibleAt(double t) => Vis.Last(v => v.t <= t + 1e-9).visible;
            public string Dump => string.Join("\n", Log);
        }

        static Result Play(SyntheticHands s, Hand palm = Hand.Left, PalmMenuContext? ctx = null, Func<double, PalmMenuContext> ctxAt = null, PalmMenu menu = null)
        {
            var r = new Result();
            var log = new MenuSummonLog { Sink = r.Log.Add };
            r.Menu = menu ?? new PalmMenu(G.Layout, log);
            if (menu != null) menu.Log = log;
            foreach (var f in s.Frames)
            {
                var c = ctxAt != null ? ctxAt(f.Time) : ctx ?? Ctx();
                if (r.Menu.Step(f, palm, c)) r.Presses.Add(f.Time);
                r.Vis.Add((f.Time, r.Menu.Visible));
                if (r.Menu.Visible && double.IsNaN(r.FirstVisible)) r.FirstVisible = f.Time;
            }
            return r;
        }

        static Vector3 ButtonAt(Vector3 palm) => palm + new Vector3(0, PalmMenu.ButtonLift, 0);
        static Vector3 Normal(Vector3 palm) => Vector3.Normalize(Head - ButtonAt(palm));

        // ── 掌心朝上的判定 ──

        [Test] public void PalmUpHeld_ShowsButtonAbovePalm()
        {
            var s = GestureScripts.PalmUp(new SyntheticHands(), Hand.Left, PalmSpot, hold: 0.6);
            var r = Play(s);
            Assert.IsTrue(r.Menu.Visible, r.Dump);
            Assert.Less(Vector3.Distance(r.Menu.ButtonCenter, ButtonAt(PalmSpot)), 0.01f, "按鈕在手掌上方 9 cm");
            Assert.That(PalmMenu.ButtonLift, Is.InRange(0.08f, 0.10f));
        }

        [Test] public void NeedsAboutAHoldOf04Seconds()
        {
            // 抬手 0.5 s（後段才翻到 35° 內），翻到位後還要停 0.4 s
            var s = GestureScripts.PalmUp(new SyntheticHands(), Hand.Left, PalmSpot, hold: 0.6);
            var r = Play(s);
            Assert.IsTrue(r.EverVisible);
            // 平滑翻手 0.5 s：約 0.34 s 時進到 35° 內，再停 0.4 s
            Assert.That(r.FirstVisible, Is.InRange(0.34 + 0.4 - 0.03, 0.34 + 0.4 + 0.05), "要停約 0.4 s 才出現");
            Assert.AreEqual(0.4, PalmMenu.HoldSeconds, 1e-9);
        }

        [Test] public void TooShortAHold_DoesNotShow_AndLogsHoldShort()
        {
            var s = GestureScripts.PalmUp(new SyntheticHands(), Hand.Left, PalmSpot, hold: 0.15, raise: 0.25);
            s.Move(Hand.Left, 0.3, HandTarget.Roll(0));
            s.Wait(0.3);
            var r = Play(s);
            Assert.IsFalse(r.EverVisible);
            Assert.That(r.Log.Any(l => l.StartsWith("[UX] menu near-miss: hold-short")), r.Dump);
        }

        [TestCase(0f, true)]
        [TestCase(25f, true)]
        [TestCase(32f, true)]
        [TestCase(40f, false)]
        [TestCase(60f, false)]
        public void PalmAngle_Within35DegreesOfUp(float tilt, bool shows)
        {
            var s = GestureScripts.PalmUp(new SyntheticHands(), Hand.Left, PalmSpot, hold: 0.8, tilt: tilt);
            var r = Play(s);
            Assert.AreEqual(shows, r.EverVisible, $"tilt {tilt}°\n{r.Dump}");
            if (!shows && tilt <= 60f) Assert.That(r.Log.Any(l => l.Contains("near-miss: palm-angle")), r.Dump);
        }

        [Test] public void Hysteresis_OnceShown_ToleratesTiltUpTo50Degrees()
        {
            var s = GestureScripts.PalmUp(new SyntheticHands(), Hand.Left, PalmSpot, hold: 0.6);
            s.Move(Hand.Left, 0.2, HandTarget.Roll(-(180f - 45f)));
            s.Wait(0.6);
            var r = Play(s);
            Assert.IsTrue(r.Menu.Visible, "45°：已出現就不收（進 35°、出 50°）");
        }

        [Test] public void TurningAway_HidesAfter03Seconds_NotBefore()
        {
            var s = GestureScripts.PalmUp(new SyntheticHands(), Hand.Left, PalmSpot, hold: 0.6);
            double turn = s.Time;
            s.Move(Hand.Left, 0.05, HandTarget.Roll(0), Ease.Linear);   // 掌心翻回朝下
            s.Wait(0.6);
            var r = Play(s);
            Assert.IsTrue(r.VisibleAt(turn + 0.05 + 0.2), "轉開 0.2 s 還在（短暫抖動不收）");
            Assert.IsFalse(r.VisibleAt(turn + 0.05 + 0.4), "轉開 > 0.3 s 收起");
            Assert.AreEqual(0.3, PalmMenu.HideDelay, 1e-9);
        }

        [Test] public void FollowsThePalmSmoothly()
        {
            var s = GestureScripts.PalmUp(new SyntheticHands(), Hand.Left, PalmSpot, hold: 0.6);
            var moved = PalmSpot + new Vector3(0.06f, 0.03f, 0.04f);
            s.Move(Hand.Left, 0.3, HandTarget.At(moved));
            s.Wait(0.3);
            var menu = new PalmMenu(G.Layout);
            float maxJump = 0f; Vector3? prev = null;
            foreach (var f in s.Frames)
            {
                menu.Step(f, Hand.Left, Ctx());
                if (menu.Visible && prev.HasValue) maxJump = Math.Max(maxJump, Vector3.Distance(prev.Value, menu.ButtonCenter));
                prev = menu.Visible ? menu.ButtonCenter : (Vector3?)null;
            }
            Assert.IsTrue(menu.Visible);
            Assert.Less(Vector3.Distance(menu.ButtonCenter, ButtonAt(moved)), 0.005f, "跟到新位置");
            Assert.Less(maxJump, 0.01f, "每幀移動 < 1 cm（平滑）");
        }

        [Test] public void FingersCurledIntoAFist_DoesNotShow()
        {
            var r = Play(GestureScripts.PalmUp(new SyntheticHands(), Hand.Left, PalmSpot, hold: 0.8, curl: 1f));
            Assert.IsFalse(r.EverVisible);
            Assert.That(r.Log.Any(l => l.Contains("near-miss: fingers-curled")), r.Dump);
        }

        [Test] public void RelaxedSlightlyCurledFingers_StillShow()
        {
            var r = Play(GestureScripts.PalmUp(new SyntheticHands(), Hand.Left, PalmSpot, hold: 0.8, curl: 0.3f));
            Assert.IsTrue(r.EverVisible, r.Dump);
        }

        [Test] public void Pinching_DoesNotShow_NeverRequiresAPinch()
        {
            // 掌心朝上＋捏合接近 Meta 系統選單手勢：不出現，也不要求捏合
            var r = Play(GestureScripts.PalmUp(new SyntheticHands(), Hand.Left, PalmSpot, hold: 0.8, pinch: 0.8f));
            Assert.IsFalse(r.EverVisible);
            Assert.That(r.Log.Any(l => l.Contains("near-miss: pinching")), r.Dump);

            var s = GestureScripts.PalmUp(new SyntheticHands(), Hand.Left, PalmSpot, hold: 0.8);
            foreach (var f in s.Frames) f.Left.IndexPinching = true;   // 平台的「正在捏合」也算
            Assert.IsFalse(Play(s).EverVisible);
        }

        [Test] public void PinchWhileShown_Hides()
        {
            var s = GestureScripts.PalmUp(new SyntheticHands(), Hand.Left, PalmSpot, hold: 0.6);
            s.Move(Hand.Left, 0.1, HandTarget.Pinch(0.9f));
            s.Wait(0.5);
            Assert.IsFalse(Play(s).Menu.Visible);
        }

        [Test] public void PalmFacingTheHead_DoesNotShow()
        {
            // 頭就在手掌正上方：掌心朝上＝朝著頭（系統選單手勢的姿勢），不出現
            var above = PalmSpot + new Vector3(0f, 0.35f, 0.02f);
            var r = Play(GestureScripts.PalmUp(new SyntheticHands(), Hand.Left, PalmSpot, hold: 0.8), ctx: Ctx(head: above));
            Assert.IsFalse(r.EverVisible);
            Assert.That(r.Log.Any(l => l.Contains("near-miss: facing-head")), r.Dump);
            Assert.IsTrue(Play(GestureScripts.PalmUp(new SyntheticHands(), Hand.Left, PalmSpot, hold: 0.8), ctx: Ctx(noHead: true)).EverVisible,
                "不知道頭在哪就只看朝上");
        }

        [Test] public void PalmUpNearTheTable_DoesNotShow()
        {
            var low = new Vector3(PalmSpot.X, 0.04f, PalmSpot.Z);
            var r = Play(GestureScripts.PalmUp(new SyntheticHands(), Hand.Left, low, hold: 0.8));
            Assert.IsFalse(r.EverVisible);
            Assert.That(r.Log.Any(l => l.Contains("near-miss: too-low")), r.Dump);
        }

        [Test] public void WorksForTheRightHandToo()
        {
            var spot = new Vector3(0.22f, 0.18f, 0.0f);   // 右手在右側、籌碼堆內側上方 18 cm
            Assert.IsTrue(Play(GestureScripts.PalmUp(new SyntheticHands(), Hand.Right, spot, hold: 0.6), palm: Hand.Right).EverVisible);
        }

        [Test] public void OnlyTheButtonHandSummons()
        {
            var spot = new Vector3(0.22f, 0.18f, 0.0f);
            Assert.IsFalse(Play(GestureScripts.PalmUp(new SyntheticHands(), Hand.Right, spot, hold: 0.8), palm: Hand.Left).EverVisible);
        }

        // ── 防誤觸：牌、籌碼、攤牌、拿著東西 ──

        [Test] public void PalmUpOverOwnCards_IsBlocked()
        {
            // 翻腕咪牌翻到 180°、攤牌翻牌都在牌上方掌心朝上
            var r = Play(GestureScripts.RollPeek(new SyntheticHands(), Hand.Left, roll: 180f, hold: 1.0));
            Assert.IsFalse(r.EverVisible, r.Dump);
            Assert.That(r.Log.Any(l => l.Contains("near-miss: blocked-by-cards")), r.Dump);
            var over = G.Cards + new Vector3(0.1f, 0.10f, -0.05f);   // 離牌 11 cm、高 10 cm
            Assert.IsFalse(Play(GestureScripts.PalmUp(new SyntheticHands(), Hand.Left, over, hold: 0.8)).EverVisible);
        }

        [Test] public void PalmUpHighAboveTheCards_IsAllowed()
        {
            var high = G.Cards + new Vector3(-0.05f, 0.22f, -0.05f);
            Assert.IsTrue(Play(GestureScripts.PalmUp(new SyntheticHands(), Hand.Left, high, hold: 0.8)).EverVisible, "牌上方 22 cm 不是在翻牌");
        }

        [Test] public void PalmUpOverOwnChips_IsBlocked()
        {
            var over = G.Stack + new Vector3(0.0f, 0.08f, -0.04f);
            var r = Play(GestureScripts.PalmUp(new SyntheticHands(), Hand.Right, over, hold: 0.8), palm: Hand.Right);
            Assert.IsFalse(r.EverVisible);
            Assert.That(r.Log.Any(l => l.Contains("near-miss: blocked-by-chips")), r.Dump);
        }

        [TestCase(true, false, false, "blocked-holding")]
        [TestCase(false, true, false, "blocked-holding")]
        [TestCase(false, false, true, "blocked-showdown")]
        public void GameStateBlocks(bool cards, bool chips, bool showdown, string reason)
        {
            var r = Play(GestureScripts.PalmUp(new SyntheticHands(), Hand.Left, PalmSpot, hold: 0.8), ctx: Ctx(cards, chips, showdown));
            Assert.IsFalse(r.EverVisible);
            Assert.That(r.Log.Any(l => l.Contains("near-miss: " + reason)), r.Dump);
        }

        [Test] public void ShowdownStartingWhileShown_Hides()
        {
            var s = GestureScripts.PalmUp(new SyntheticHands(), Hand.Left, PalmSpot, hold: 0.6);
            double at = s.Time;
            s.Wait(0.6);
            var r = Play(s, ctxAt: t => Ctx(showdown: t >= at));
            Assert.IsFalse(r.Menu.Visible);
        }

        [Test] public void GameGestures_NeverSummon()
        {
            foreach (var h in new[] { Hand.Left, Hand.Right })
            {
                var scripts = new (string name, SyntheticHands s, PalmMenuContext ctx)[]
                {
                    ("peek", GestureScripts.Peek(new SyntheticHands(), h), Ctx()),
                    ("rollpeek135", GestureScripts.RollPeek(new SyntheticHands(), h), Ctx()),
                    ("rollpeek180", GestureScripts.RollPeek(new SyntheticHands(), h, roll: 180f, hold: 1.2), Ctx()),
                    ("flip", GestureScripts.Flip(new SyntheticHands(), h).Wait(0.8), Ctx()),
                    ("flip-showdown", GestureScripts.Flip(new SyntheticHands(), h, height: 0.15f).Wait(0.8), Ctx(showdown: true)),
                    ("chips", GestureScripts.PickChips(new SyntheticHands(), h), Ctx(chips: true)),
                    ("knock", GestureScripts.Knock(new SyntheticHands(), h, GestureScripts.KnockSpot(h)), Ctx()),
                    ("fold", GestureScripts.FoldPush(new SyntheticHands(), h), Ctx()),
                    ("allin", GestureScripts.AllIn(new SyntheticHands()), Ctx()),
                    ("lookcards", GestureScripts.LiftCards(GestureScripts.GrabCards(new SyntheticHands(), h), h), Ctx(cards: true)),
                };
                foreach (var (name, s, ctx) in scripts)
                    Assert.IsFalse(Play(s, palm: h, ctx: ctx).EverVisible, $"{name} ({h}) 不該叫出選單");
            }
        }

        // ── 按下：停留或穿過按鈕面 ──

        [Test] public void Dwell_015Seconds_InsideTheButton_Presses()
        {
            var s = GestureScripts.PalmUp(new SyntheticHands(), Hand.Left, PalmSpot, hold: 0.6);
            var c = ButtonAt(PalmSpot); var n = Normal(PalmSpot);
            // 從頭的那一側靠近、停在按鈕前 1.5 cm（球內、沒穿過按鈕面）
            GestureScripts.IndexTo(s, Hand.Right, c + n * 0.015f, approach: 0.3, hold: 0.3, from: c + n * 0.12f);
            var r = Play(s);
            Assert.AreEqual(1, r.Presses.Count, r.Dump);
            Assert.AreEqual(0.15, PalmMenu.PressDwell, 1e-9);
            Assert.AreEqual(0.03f, PalmMenu.ButtonRadius, 1e-6f);
        }

        [Test] public void BriefTouchShorterThanDwell_DoesNotPress_AndLogsFingerMissed()
        {
            var s = GestureScripts.PalmUp(new SyntheticHands(), Hand.Left, PalmSpot, hold: 0.6);
            var c = ButtonAt(PalmSpot); var n = Normal(PalmSpot);
            s.Move(Hand.Right, 0.01, HandTarget.At(GestureScripts.PalmForIndexTip(Hand.Right, c + n * 0.12f)));
            s.Move(Hand.Right, 0.08, HandTarget.At(GestureScripts.PalmForIndexTip(Hand.Right, c + n * 0.015f)), Ease.Linear);
            s.Wait(0.03);
            s.Move(Hand.Right, 0.08, HandTarget.At(GestureScripts.PalmForIndexTip(Hand.Right, c + n * 0.15f)), Ease.Linear);
            s.Wait(0.3);
            var r = Play(s);
            Assert.AreEqual(0, r.Presses.Count, r.Dump);
            Assert.That(r.Log.Any(l => l.StartsWith("[UX] menu near-miss: finger-missed") && l.Contains("closest=")), r.Dump);
        }

        [Test] public void FastPokeThroughTheButtonPlane_Presses()
        {
            var s = GestureScripts.PalmUp(new SyntheticHands(), Hand.Left, PalmSpot, hold: 0.6);
            var c = ButtonAt(PalmSpot); var n = Normal(PalmSpot);
            s.Move(Hand.Right, 0.01, HandTarget.At(GestureScripts.PalmForIndexTip(Hand.Right, c + n * 0.06f)));
            s.Move(Hand.Right, 0.05, HandTarget.At(GestureScripts.PalmForIndexTip(Hand.Right, c - n * 0.06f)), Ease.Linear);   // 0.05 s 穿過
            s.Wait(0.2);
            var r = Play(s);
            Assert.AreEqual(1, r.Presses.Count, "穿過按鈕面就算（不用停）\n" + r.Dump);
        }

        [Test] public void PokeThroughThePlaneOutsideTheDisk_DoesNotPress()
        {
            var s = GestureScripts.PalmUp(new SyntheticHands(), Hand.Left, PalmSpot, hold: 0.6);
            var c = ButtonAt(PalmSpot); var n = Normal(PalmSpot);
            var side = Vector3.Normalize(Vector3.Cross(n, Vector3.UnitY)) * 0.045f;   // 偏 4.5 cm
            s.Move(Hand.Right, 0.01, HandTarget.At(GestureScripts.PalmForIndexTip(Hand.Right, c + side + n * 0.06f)));
            s.Move(Hand.Right, 0.05, HandTarget.At(GestureScripts.PalmForIndexTip(Hand.Right, c + side - n * 0.06f)), Ease.Linear);
            s.Wait(0.2);
            Assert.AreEqual(0, Play(s).Presses.Count);
        }

        [Test] public void NoArmingFromAboveNeeded_ApproachFromBelowOrSideWorks()
        {
            foreach (var dir in new[] { -Vector3.UnitY, Vector3.UnitX, -Vector3.UnitX, Vector3.UnitZ })
            {
                var s = GestureScripts.PalmUp(new SyntheticHands(), Hand.Left, PalmSpot, hold: 0.6);
                var c = ButtonAt(PalmSpot);
                GestureScripts.IndexTo(s, Hand.Right, c, approach: 0.3, hold: 0.3, from: c + dir * 0.1f);
                Assert.AreEqual(1, Play(s).Presses.Count, "from " + dir);
            }
        }

        [Test] public void HoldingInside_PressesOnce_ThenCooldownAndExitBeforeNext()
        {
            var s = GestureScripts.PalmUp(new SyntheticHands(), Hand.Left, PalmSpot, hold: 0.6);
            var c = ButtonAt(PalmSpot); var n = Normal(PalmSpot);
            GestureScripts.IndexTo(s, Hand.Right, c + n * 0.01f, approach: 0.2, hold: 1.0, from: c + n * 0.12f);
            GestureScripts.IndexTo(s, Hand.Right, c + n * 0.12f, approach: 0.05, hold: 0.0);
            GestureScripts.IndexTo(s, Hand.Right, c + n * 0.01f, approach: 0.05, hold: 0.3);   // 很快又進來：還在冷卻嗎？
            var r = Play(s);
            Assert.AreEqual(2, r.Presses.Count, r.Dump);
            Assert.GreaterOrEqual(r.Presses[1] - r.Presses[0], PalmMenu.Cooldown);

            var s2 = GestureScripts.PalmUp(new SyntheticHands(), Hand.Left, PalmSpot, hold: 0.6);
            GestureScripts.IndexTo(s2, Hand.Right, c + n * 0.01f, approach: 0.2, hold: 0.2, from: c + n * 0.12f);
            GestureScripts.IndexTo(s2, Hand.Right, c + n * 0.08f, approach: 0.04, hold: 0.0);
            GestureScripts.IndexTo(s2, Hand.Right, c + n * 0.01f, approach: 0.04, hold: 0.2);   // 停滿 0.15 s，但離上一次按下 < 0.6 s
            GestureScripts.IndexTo(s2, Hand.Right, c + n * 0.12f, approach: 0.1, hold: 0.3);
            Assert.AreEqual(1, Play(s2).Presses.Count, "冷卻 0.6 s 內再進來不算");
        }

        [Test] public void HoverHighlightsAndShowsDwellProgress()
        {
            var s = GestureScripts.PalmUp(new SyntheticHands(), Hand.Left, PalmSpot, hold: 0.6);
            var c = ButtonAt(PalmSpot); var n = Normal(PalmSpot);
            GestureScripts.IndexTo(s, Hand.Right, c + n * 0.045f, approach: 0.2, hold: 0.2, from: c + n * 0.15f);
            var menu = new PalmMenu(G.Layout);
            foreach (var f in s.Frames) menu.Step(f, Hand.Left, Ctx());
            Assert.IsTrue(menu.Hovered, "按鈕外 1.5 cm：高亮");
            Assert.AreEqual(0f, menu.Progress, 1e-6f);
            GestureScripts.IndexTo(s, Hand.Right, c + n * 0.01f, approach: 0.02, hold: 0.07);
            menu = new PalmMenu(G.Layout);
            foreach (var f in s.Frames) menu.Step(f, Hand.Left, Ctx());
            Assert.That(menu.Progress, Is.InRange(0.2f, 0.99f), "停留中：進度");
        }

        [Test] public void HiddenButton_CannotBePressed()
        {
            var s = new SyntheticHands();
            var c = ButtonAt(PalmSpot);
            GestureScripts.IndexTo(s, Hand.Right, c, approach: 0.3, hold: 0.5);   // 左手沒翻掌，只是食指到了那個位置
            Assert.AreEqual(0, Play(s).Presses.Count);
        }

        // ── 雙手交疊時的追蹤掉幀（Quest 手疊手會掉追蹤） ──

        [Test] public void PalmHandTrackingDropsWhileFingerArrives_StillPresses()
        {
            var s = GestureScripts.PalmUp(new SyntheticHands(), Hand.Left, PalmSpot, hold: 0.6);
            var c = ButtonAt(PalmSpot); var n = Normal(PalmSpot);
            GestureScripts.IndexTo(s, Hand.Right, c + n * 0.05f, approach: 0.25, hold: 0.0, from: c + n * 0.15f);
            s.Track(Hand.Left, false);
            GestureScripts.IndexTo(s, Hand.Right, c + n * 0.01f, approach: 0.1, hold: 0.25);   // 掌心那手掉 0.35 s
            s.Track(Hand.Left, true);
            s.Wait(0.2);
            var r = Play(s);
            Assert.AreEqual(1, r.Presses.Count, "按鈕停在最後的位置等 0.5 s\n" + r.Dump);
        }

        [Test] public void FingerHandFlickersMidDwell_StillPresses()
        {
            var s = GestureScripts.PalmUp(new SyntheticHands(), Hand.Left, PalmSpot, hold: 0.6);
            var c = ButtonAt(PalmSpot); var n = Normal(PalmSpot);
            GestureScripts.IndexTo(s, Hand.Right, c + n * 0.01f, approach: 0.2, hold: 0.05, from: c + n * 0.12f);
            s.Track(Hand.Right, false).Wait(0.12).Track(Hand.Right, true);
            s.Wait(0.3);
            var r = Play(s);
            Assert.AreEqual(1, r.Presses.Count, r.Dump);
        }

        [Test] public void BothHandsFlickering_StillPresses()
        {
            var s = GestureScripts.PalmUp(new SyntheticHands(), Hand.Left, PalmSpot, hold: 0.6);
            var c = ButtonAt(PalmSpot); var n = Normal(PalmSpot);
            GestureScripts.IndexTo(s, Hand.Right, c + n * 0.01f, approach: 0.2, hold: 0.0, from: c + n * 0.12f);
            for (int i = 0; i < 8; i++)
            {
                bool off = i % 2 == 0;
                s.Track(i % 4 < 2 ? Hand.Left : Hand.Right, !off);
                s.Wait(0.05);
                s.Track(Hand.Left, true).Track(Hand.Right, true);
                s.Wait(0.03);
            }
            var r = Play(s);
            Assert.AreEqual(1, r.Presses.Count, r.Dump);
        }

        [Test] public void PalmLostTooLong_HidesAndLogsTrackingLost()
        {
            var s = GestureScripts.PalmUp(new SyntheticHands(), Hand.Left, PalmSpot, hold: 0.6);
            var c = ButtonAt(PalmSpot); var n = Normal(PalmSpot);
            GestureScripts.IndexTo(s, Hand.Right, c + n * 0.06f, approach: 0.25, hold: 0.0, from: c + n * 0.15f);
            s.Track(Hand.Left, false);
            s.Wait(0.8);
            var r = Play(s);
            Assert.IsFalse(r.Menu.Visible);
            Assert.AreEqual(0, r.Presses.Count);
            Assert.That(r.Log.Any(l => l.StartsWith("[UX] menu near-miss: tracking-lost palm")), r.Dump);
        }

        [Test] public void FingerLostNearTheButton_LogsTrackingLostFinger()
        {
            var s = GestureScripts.PalmUp(new SyntheticHands(), Hand.Left, PalmSpot, hold: 0.6);
            var c = ButtonAt(PalmSpot); var n = Normal(PalmSpot);
            GestureScripts.IndexTo(s, Hand.Right, c + n * 0.05f, approach: 0.25, hold: 0.0, from: c + n * 0.15f);
            s.Track(Hand.Right, false);
            s.Wait(0.6);
            var r = Play(s);
            Assert.That(r.Log.Any(l => l.StartsWith("[UX] menu near-miss: tracking-lost finger")), r.Dump);
        }

        [Test] public void WithRealisticTrackingNoise_SummonAndPressWork()
        {
            int ok = 0;
            var fails = new List<string>();
            for (int seed = 0; seed < 20; seed++)
            {
                var s = GestureScripts.PalmUp(new SyntheticHands(seed: seed).WithNoise(0.01f, 4f), Hand.Left, PalmSpot, hold: 0.6);
                var c = ButtonAt(PalmSpot); var n = Normal(PalmSpot);
                GestureScripts.IndexTo(s, Hand.Right, c + n * 0.01f, approach: 0.3, hold: 0.5, from: c + n * 0.15f);
                var r = Play(s);
                if (r.Presses.Count == 1) ok++;
                else fails.Add($"seed {seed}: presses={r.Presses.Count} shown={r.EverVisible}\n  " + string.Join("\n  ", r.Log));
            }
            Assert.That(ok, Is.GreaterThanOrEqualTo(19), "1 cm／4° 追蹤誤差下 20 次至少 19 次成功\n" + string.Join("\n", fails));
        }

        // ── 診斷 log ──

        [Test] public void NearMissLog_IsRateLimited_AndCountsSuppressed()
        {
            var lines = new List<string>();
            var log = new MenuSummonLog { Sink = lines.Add };
            log.NearMiss(10.0, "palm-angle", "angle=40deg");
            log.NearMiss(10.5, "too-low", "");
            log.NearMiss(11.0, "too-low", "");
            log.NearMiss(12.0, "pinching", "x=1");
            CollectionAssert.AreEqual(new[]
            {
                "[UX] menu near-miss: palm-angle angle=40deg",
                "[UX] menu near-miss: pinching x=1 (+2 suppressed)",
            }, lines);
            Assert.AreEqual(1.5, log.MinInterval, 1e-9);
        }

        [Test] public void NearMissDetail_ReportsAngleHoldAndHeight()
        {
            var r = Play(GestureScripts.PalmUp(new SyntheticHands(), Hand.Left, PalmSpot, hold: 0.8, tilt: 45f));
            var line = r.Log.First(l => l.Contains("near-miss: palm-angle"));
            StringAssert.Contains("angle=45deg", line);
            StringAssert.Contains("hold=", line);
            StringAssert.Contains("height=18cm", line);
        }

        // ── 真實錄製（Quest 3，132 段 .hands）：一般手勢不會叫出選單；翻牌錄製證明掌心法線慣例對 ──

        static IEnumerable<string> Recordings()
        {
            var dir = RecordingCheck.FindRecordingsDir();
            return dir == null ? Enumerable.Empty<string>() : Directory.GetFiles(dir, "*.hands").OrderBy(f => f, StringComparer.Ordinal);
        }

        [Test] public void RealRecordings_NeverSummonDuringGameGestures()
        {
            var files = Recordings().ToList();
            if (files.Count == 0) Assert.Ignore("沒有錄製檔");
            var shown = new List<string>();
            foreach (var path in files)
            {
                var rec = HandRecording.Load(path);
                var exp = RecordingExpectation.Parse(path, rec.Note);
                bool showdown = exp.Context.Phase == TablePhase.Showdown;
                foreach (var hand in new[] { Hand.Left, Hand.Right })
                {
                    var recog = new GestureRecognizer(G.Layout);
                    var menu = new PalmMenu(G.Layout);
                    foreach (var f in rec.Frames)
                    {
                        recog.Update(f, exp.Context);
                        var ctx = PalmMenuContext.From(recog, showdown, Head);
                        menu.Step(f, hand, ctx);
                        if (menu.Visible) { shown.Add($"{Path.GetFileName(path)} {hand} t={f.Time - rec.Frames[0].Time:0.00}"); break; }
                    }
                }
            }
            Assert.IsEmpty(shown, $"{files.Count} 段 × 2 手：\n" + string.Join("\n", shown));
        }

        [Test] public void RealFlipRecordings_TurnThePalmUp_SoTheNormalConventionHolds()
        {
            var flips = Recordings().Where(f => Path.GetFileName(f).StartsWith("flip")).ToList();
            if (flips.Count == 0) Assert.Ignore("沒有翻牌錄製");
            int up = 0;
            foreach (var path in flips)
            {
                var rec = HandRecording.Load(path);
                float best = 180f;
                foreach (var f in rec.Frames)
                    foreach (var h in new[] { f.Left, f.Right })
                        if (h.Tracked) best = Math.Min(best, PalmMenu.UpAngle(h));
                if (best < 45f) up++;
            }
            Assert.That(up, Is.GreaterThanOrEqualTo(flips.Count * 2 / 3), $"{up}/{flips.Count} 段翻牌時掌心朝上 < 45°");
        }
    }
}
