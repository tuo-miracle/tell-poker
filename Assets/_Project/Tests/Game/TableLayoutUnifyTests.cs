using NUnit.Framework;
using Poker.Game;
using Poker.Runtime;
using UnityEngine;
using V3 = System.Numerics.Vector3;

namespace Poker.Tests.Game
{
    public class TableLayoutUnifyTests
    {
        GameObject _go;
        [TearDown] public void TearDown() { if (_go != null) Object.DestroyImmediate(_go); }

        [Test]
        public void TableDimensions_ComeFromTheSharedTableLayout()
        {
            var l = TableDimensions.Layout;
            Assert.AreEqual(l.FeltSize.X, TableDimensions.Width, 1e-6f);
            Assert.AreEqual(l.FeltSize.Y, TableDimensions.Depth, 1e-6f);
            Assert.GreaterOrEqual(l.FeltSize.Y, l.BetZone.MaxZ - 0.001f, "bet zone fits on the felt");
        }

        [Test]
        public void TableView_MarksTheGestureZones_AtLayoutPositions()
        {
            _go = new GameObject("Table");
            _go.AddComponent<TableFrame>();
            _go.AddComponent<TableView>();
            var l = TableDimensions.Layout;
            foreach (var name in new[] { "Felt", "BetLine", "BetZone", "FoldLine", "PotArea", "ChipSpot", "CardSlot_L", "CardSlot_R" })
                Assert.IsNotNull(_go.transform.Find(name), name);
            Assert.AreEqual(l.BetLineZ, _go.transform.Find("BetLine").localPosition.z, 1e-4f);
            Assert.AreEqual(l.FoldLineZ, _go.transform.Find("FoldLine").localPosition.z, 1e-4f);
            Assert.AreEqual(l.ChipStackCenter.X, _go.transform.Find("ChipSpot").localPosition.x, 1e-4f);
            Assert.AreEqual(l.CardLeftCenter.Z, _go.transform.Find("CardSlot_L").localPosition.z, 1e-4f);
            Assert.AreEqual(l.PotCenter.Z, _go.transform.Find("PotArea").localPosition.z, 1e-4f);
            Assert.IsNull(_go.transform.Find("Seat_Left"), "old 4-seat markers are gone");
        }

        [Test]
        public void Seats_PipOpposite_DotFarRight_NothingBesideThePlayer()
        {
            var spots = new TableSpots(TableDimensions.Layout);
            var pip = spots.OpponentSeat;
            Assert.AreEqual(0f, pip.position.x, 0.02f, "Pip straight across");
            Assert.AreEqual(TableDimensions.Depth, pip.position.z, 0.01f, "across the real table (0.83 m felt; was 0.6-0.7 m on the small table)");
            Assert.Greater(Vector3.Dot(pip.rotation * Vector3.forward, Vector3.back), 0.99f, "faces the player");
            var dot = spots.DealerSeat;
            Assert.Greater(dot.position.x, 0.25f, "dealer to the right");
            Assert.Greater(dot.position.z, 0.5f, "dealer on the far side, not beside the player");
            // 坐在長圓桌的半圓上，面向那個半圓的圓心（不是桌子正中央），跟桌心的夾角約 40°
            var toCenter = (new Vector3(0, 0, TableDimensions.Depth * 0.5f) - dot.position).normalized;
            Assert.Greater(Vector3.Dot(dot.rotation * Vector3.forward, toCenter), 0.7f, "dealer faces the table");
        }

        [Test]
        public void Spots_DoNotOverlapThePlayersZones()
        {
            var l = TableDimensions.Layout;
            var s = new TableSpots(l);
            foreach (var p in new[] { s.Board(0), s.Board(4), s.PotPile, s.AiStack, s.AiBet, s.AiCard(0), s.Deck })
            {
                Assert.Greater(p.z, l.BetLineZ + 0.03f, p.ToString());
                Assert.IsTrue(l.InFelt(new V3(p.x, 0f, p.z)), p + " on the (round-ended) felt");
            }
            Assert.Greater(s.Board(1).x - s.Board(0).x, CardView.Width, "board cards don't overlap");
        }

        [Test]
        public void ChipsTossedOffTheBetZone_AreShownInsideIt_AtTheRecognizersDisplayPoint()
        {
            // 2026-10-13：偏短、偏右、丟過頭的落點也算下注；籌碼要畫在下注區內，辨識器在同一點判斷撈回
            var l = TableDimensions.Layout;
            var s = new TableSpots(l);
            foreach (var p in new[] { new Vector3(-0.09f, 0f, 0.94f), new Vector3(0.45f, 0f, 0.51f), new Vector3(0.14f, 0f, 0.22f), new Vector3(0.05f, 0f, 0.42f) })
            {
                var c = s.ClampToBetZone(p);
                var d = l.BetDisplayPoint(new V3(p.x, 0f, p.z));
                Assert.AreEqual(d.X, c.x, 1e-5f, p.ToString());
                Assert.AreEqual(d.Z, c.z, 1e-5f, p.ToString());
                Assert.IsTrue(l.BetZone.Contains(new V3(c.x, 0f, c.z)), "inside the bet zone: " + p);
            }
        }
    }
}
