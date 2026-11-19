using System.Linq;
using System.Numerics;
using NUnit.Framework;
using Poker.Gestures;

namespace Poker.Tests
{
    /// <summary>
    /// 攤牌亮牌（2026-10-13 開發者 Quest 3 回饋，s8.log 7445–7450 s）：教學翻牌那一步連續 4 次把牌拿起來往前丟到中間，
    /// 每次都被當成棄牌（攤牌時被擋下）。攤牌時「拿起來往前放」就是亮牌的自然動作。
    /// 規則：攤牌輪到玩家時，所有棄牌類動作（推、拖、拿到棄牌堆、拿著牌往前放開或丟）以及拿著牌在自己牌位前方任何地方放開＝ShowdownFlip，
    /// 牌在放開處附近翻開（夾在玩家前方的桌面上）；原本的翻牌手勢照舊。放回自己的牌位、在臉前放開＝放回，不亮牌。
    /// 不是攤牌時一切照舊（往前丟＝棄牌）。
    /// </summary>
    public class ShowdownRevealTests
    {
        static TableContext Showdown => G.Ctx(phase: TablePhase.Showdown, canCheck: false);
        static TableContext River => G.Ctx(phase: TablePhase.River, canCheck: false);

        /// <summary>s8.log 的 4 次：捏住牌抬起（CardsLifted 位置）→ 往前放開（CardsReturned 位置＝捏合點）。</summary>
        static readonly (Vector3 lift, Vector3 release)[] LogS8 =
        {
            (new Vector3(-0.01f, 0.08f, 0.10f), new Vector3(0.18f, 0.11f, 0.33f)),
            (new Vector3(-0.02f, 0.08f, 0.08f), new Vector3(0.19f, 0.12f, 0.33f)),
            (new Vector3(-0.02f, 0.09f, 0.09f), new Vector3(0.20f, 0.13f, 0.33f)),
            (new Vector3(-0.03f, 0.08f, 0.07f), new Vector3(0.19f, 0.18f, 0.31f)),
        };

        /// <summary>捏住牌、0.2 s 抬到 lift，花 travel 秒移到 release（捏合點）放開（log 每次從抬起到放開約 0.3 s）。</summary>
        static SyntheticHands LiftThenRelease(Vector3 lift, Vector3 release, double hold = 0, double travel = 0.28)
        {
            var h = Hand.Right;
            var s = GestureScripts.GrabCards(new SyntheticHands(), h);
            s.Move(h, 0.2, HandTarget.At(SyntheticHands.PalmForPinch(h, lift)));
            if (hold > 0) s.Wait(hold);
            s.Move(h, travel, HandTarget.At(SyntheticHands.PalmForPinch(h, release)));
            s.Move(h, 0.12, HandTarget.Pinch(0f));
            s.Move(h, 0.3, HandTarget.At(s.Palm(h) + new Vector3(0, 0.06f, 0)));
            s.Move(h, 0.4, HandTarget.At(SyntheticHands.RightRest));
            s.Wait(0.5);
            return s;
        }

        static float Flat(Vector3 a, Vector3 b) => Vector2.Distance(new Vector2(a.X, a.Z), new Vector2(b.X, b.Z));

        static void AssertRevealedOnce(System.Collections.Generic.List<GestureEvent> ev, string why)
        {
            Assert.That(ev.Count(GestureKind.ShowdownFlip), Is.EqualTo(1), why + "\n" + ev.Dump());
            Assert.That(ev.Count(GestureKind.FoldPushed), Is.Zero, why + "\n" + ev.Dump());
            Assert.That(ev.Where(e => e.IsAction).All(e => e.Kind == GestureKind.ShowdownFlip), Is.True, why + "\n" + ev.Dump());
        }

        static void AssertOnTableInFront(Vector3 p, string why)
        {
            var l = G.Layout;
            Assert.That(p.Y, Is.EqualTo(l.TableY).Within(1e-4f), why + " on the table surface " + p);
            Assert.That(p.Z, Is.GreaterThan(l.CardsRect.MaxZ), why + " in front of the player's own card spot " + p);
            Assert.That(p.Z, Is.LessThanOrEqualTo(l.BetLineZ), why + " still on the player's side " + p);
            Assert.That(System.Math.Abs(p.X), Is.LessThanOrEqualTo(l.FeltSize.X * 0.5f - 0.15f), why + " not off the side " + p);
        }

        // ── 攤牌：棄牌類動作都是亮牌 ──

        [Test]
        public void Log_s8_four_forward_tosses_at_showdown_reveal_instead_of_folding()
        {
            foreach (var (lift, release) in LogS8)
            {
                var ev = G.Run(LiftThenRelease(lift, release), Showdown);
                AssertRevealedOnce(ev, release.ToString());
                var flip = ev.First(GestureKind.ShowdownFlip);
                AssertOnTableInFront(flip.Position, release.ToString());
                Assert.That(Flat(flip.Position, release), Is.LessThan(0.05f), "cards land near where they were released " + release + " → " + flip.Position);
                Assert.That(ev.Count(GestureKind.CardsReturned), Is.EqualTo(1), "hold ends\n" + ev.Dump());
            }
        }

        [Test]
        public void Log_s8_same_tosses_outside_showdown_still_fold()
        {
            foreach (var (lift, release) in LogS8)
            {
                var ev = G.Run(LiftThenRelease(lift, release), River);
                Assert.That(ev.Count(GestureKind.FoldPushed), Is.EqualTo(1), release + "\n" + ev.Dump());
                Assert.That(ev.Count(GestureKind.ShowdownFlip), Is.Zero, release + "\n" + ev.Dump());
            }
        }

        [Test]
        public void Push_slide_carry_and_carry_arc_at_showdown_reveal()
        {
            var cases = new (string name, SyntheticHands s)[]
            {
                ("push", GestureScripts.FoldPush(new SyntheticHands())),
                ("slide", GestureScripts.FoldSlide(new SyntheticHands())),
                ("carry arc", GestureScripts.FoldCarryArc(new SyntheticHands())),
                ("carry cards to muck", GestureScripts.CarryCardsToMuck(new SyntheticHands())),
                ("carry cards to muck (left)", GestureScripts.CarryCardsToMuck(new SyntheticHands(), Hand.Left)),
            };
            foreach (var (name, s) in cases)
            {
                var ev = G.Run(s, Showdown);
                AssertRevealedOnce(ev, name);
                AssertOnTableInFront(ev.First(GestureKind.ShowdownFlip).Position, name);
                // 同一個動作不是攤牌時照舊是棄牌
                Assert.That(G.Run(s, River).Count(GestureKind.FoldPushed), Is.EqualTo(1), name + " folds outside showdown");
            }
        }

        [Test]
        public void Looked_then_released_forward_anywhere_reveals_even_high()
        {
            // 看過（停 1.5 s）再往前伸到 30 cm 高放開：不是攤牌時高處放開＝放回；攤牌時往前放開就是亮牌
            var high = LiftThenRelease(new Vector3(0f, 0.25f, 0.06f), new Vector3(0.05f, 0.30f, 0.30f), hold: 1.5, travel: 0.4);
            AssertRevealedOnce(G.Run(high, Showdown), "high forward release");
            Assert.That(G.Run(high, River).AnyAction(), Is.False, "outside showdown a high release returns the cards");
            // 牌前緣前方不遠（還沒到棄牌線）停住放開
            var near = LiftThenRelease(new Vector3(0f, 0.20f, 0.04f), new Vector3(-0.05f, 0.10f, 0.24f), hold: 1.0);
            AssertRevealedOnce(G.Run(near, Showdown), "just in front of the own cards");
        }

        [Test]
        public void Forward_toss_landing_is_clamped_onto_the_table_in_front_of_the_player()
        {
            // 丟過頭、往側邊丟：牌落點夾在玩家前方的桌面上
            foreach (var release in new[] { new Vector3(0.40f, 0.10f, 0.60f), new Vector3(-0.42f, 0.12f, 0.25f) })
            {
                var ev = G.Run(LiftThenRelease(new Vector3(0f, 0.10f, 0.10f), release, travel: 0.4), Showdown);
                AssertRevealedOnce(ev, release.ToString());
                AssertOnTableInFront(ev.First(GestureKind.ShowdownFlip).Position, release.ToString());
            }
        }

        [Test]
        public void Classic_flip_still_reveals_in_place()
        {
            var ev = G.Run(GestureScripts.Flip(new SyntheticHands()), Showdown);
            Assert.That(ev.Count(GestureKind.ShowdownFlip), Is.EqualTo(1), ev.Dump());
            Assert.That(ev.First(GestureKind.ShowdownFlip).Position, Is.EqualTo(default(Vector3)), "flipped on the own spot (no landing point)");
        }

        // ── 攤牌：放回自己的牌位不亮牌 ──

        [Test]
        public void At_showdown_looking_and_putting_the_cards_back_on_their_spot_does_not_reveal()
        {
            // 放回原位＝「我再看一眼」，不是亮牌；亮牌要往前放、或翻牌（不翻也會在逾時後自動翻開，教學中由提示帶著做）
            var cases = new (string name, SyntheticHands s)[]
            {
                ("back on the spot", GestureScripts.LiftCards(new SyntheticHands(), hold: 0.8, releaseAt: GestureScripts.CardGrip)),
                ("log 624 put back", LiftThenRelease(new Vector3(0f, 0.20f, 0.04f), new Vector3(0.01f, 0.068f, 0.11f), hold: 3.0, travel: 0.5)),
                ("in front of the face", GestureScripts.LiftCards(new SyntheticHands(), hold: 1.0)),
                ("front edge of the own cards", LiftThenRelease(new Vector3(0f, 0.20f, 0.04f), new Vector3(0.02f, 0.06f, 0.19f), hold: 0.5)),
            };
            // 日常雜動「拿起來、就近放下」（RobustnessStatsTests.Fidget）：在牌前緣捏起、往前 7 cm 放下，不算亮牌
            var nearby = new SyntheticHands();
            GestureScripts.GrabCards(nearby, Hand.Right, G.Cards + new Vector3(0f, 0.01f, 0.03f));
            nearby.Move(Hand.Right, 0.4, HandTarget.At(SyntheticHands.PalmForPinch(Hand.Right, G.Cards + new Vector3(0.02f, 0.10f, 0.08f))));
            nearby.Move(Hand.Right, 0.4, HandTarget.At(SyntheticHands.PalmForPinch(Hand.Right, G.Cards + new Vector3(0.02f, 0.03f, 0.10f))));
            nearby.Move(Hand.Right, 0.12, HandTarget.Pinch(0f));
            nearby.Move(Hand.Right, 0.3, HandTarget.At(nearby.Palm(Hand.Right) + new Vector3(0, 0.07f, 0)));
            nearby.Wait(0.5);
            cases = cases.Append(("set down 7 cm ahead of the grab", nearby)).ToArray();
            foreach (var (name, s) in cases)
            {
                var ev = G.Run(s, Showdown);
                Assert.That(ev.AnyAction(), Is.False, name + "\n" + ev.Dump());
                Assert.That(ev.Count(GestureKind.CardsReturned), Is.EqualTo(1), name + "\n" + ev.Dump());
            }
        }

        [Test]
        public void Not_the_players_showdown_nothing_happens()
        {
            // 攤牌但不是輪到玩家亮牌（例：已棄牌看別人攤牌）：往前放開只是放回
            var off = G.Ctx(turn: false, canCheck: false, phase: TablePhase.Showdown);
            var (lift, release) = LogS8[0];
            var ev = G.Run(LiftThenRelease(lift, release), off);
            Assert.That(ev.AnyAction(), Is.False, ev.Dump());
            Assert.That(G.Run(GestureScripts.FoldPush(new SyntheticHands()), off).AnyAction(), Is.False);
        }
    }
}
