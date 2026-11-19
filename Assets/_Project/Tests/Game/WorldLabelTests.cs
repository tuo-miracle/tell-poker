using NUnit.Framework;
using Poker.Game;
using UnityEngine;

namespace Poker.Tests.Game
{
    public class WorldLabelTests
    {
        GameObject _root;
        [SetUp] public void SetUp() => _root = new GameObject("root");
        [TearDown] public void TearDown() => Object.DestroyImmediate(_root);

        [Test]
        public void Label_HasALightPlateBehindDarkText_SizedToTheText()
        {
            var l = new WorldLabel(_root.transform, "L", 0.004f, Color.black);
            l.Show("Was bluffing", Vector3.zero);
            Assert.IsTrue(l.Plate.activeSelf);
            var size = l.Plate.transform.localScale;
            Assert.Greater(size.x, size.y * 3f, "wide plate for a long line");
            Assert.Greater(size.y, 0.02f, "readable height (about 2.5 cm line)");
            l.Show("30", Vector3.zero);
            Assert.Less(l.Plate.transform.localScale.x, size.x * 0.5f, "plate shrinks with text");
            l.Hide();
            Assert.IsFalse(l.Go.activeSelf);
        }
    }
}
