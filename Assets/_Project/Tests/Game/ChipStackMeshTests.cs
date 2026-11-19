using NUnit.Framework;
using Poker.Game;

namespace Poker.Tests.Game
{
    public class ChipStackMeshTests
    {
        [Test]
        public void Empty_HasNoVertices()
        {
            Assert.AreEqual(0, ChipStackMesh.Get(0).vertexCount);
        }

        [Test]
        public void SingleColumn_HeightIsChipCountTimesThickness()
        {
            var m = ChipStackMesh.Get(7);
            Assert.AreEqual(7 * ChipStackMesh.Thickness, m.bounds.size.y, 1e-4f);
            Assert.AreEqual(ChipStackMesh.Radius * 2, m.bounds.size.x, 2e-3f);
        }

        [Test]
        public void LargeStacks_SplitIntoColumns_NoTallerThanMax()
        {
            var m = ChipStackMesh.Get(100);
            Assert.AreEqual(ChipStackMesh.MaxPerColumn * ChipStackMesh.Thickness, m.bounds.size.y, 1e-4f);
            Assert.AreEqual(5, ChipStackMesh.Columns(100));
            Assert.AreEqual(3, ChipStackMesh.Columns(41));
            Assert.Greater(m.bounds.size.x, ChipStackMesh.Radius * 4);
        }

        [Test]
        public void Meshes_AreCachedPerCount()
        {
            Assert.AreSame(ChipStackMesh.Get(12), ChipStackMesh.Get(12));
        }

        [Test]
        public void OddChip_IsVisibleAsExtraColumn()
        {
            // 98 枚加一個零頭（面額不足一枚）：顯示多一枚小籌碼
            var plain = ChipStackMesh.Get(5);
            var odd = ChipStackMesh.Get(5, true);
            Assert.Greater(odd.vertexCount, plain.vertexCount);
        }
    }
}
