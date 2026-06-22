using BovineLabs.Timeline.Grid.Influence.Data;
using NUnit.Framework;
using Unity.Mathematics;

namespace BovineLabs.Timeline.Grid.Influence.Tests
{
    public class GridProjectionTests
    {
        private static int2 LegacyCell(GridBasis basis, float3 position, quaternion rotation, float3 localOffset,
            float cellSize)
        {
            var world = position + math.rotate(rotation, localOffset);
            var projected = basis.ToGridSpace(world);
            return new int2(
                (int)math.floor(projected.x / cellSize),
                (int)math.floor(projected.y / cellSize));
        }

        [Test]
        public void CellMatchesLegacyFloorProjection()
        {
            var basis = new GridBasis(math.up());
            var rng = new Random(0x9e3779b9u);

            for (var i = 0; i < 2048; i++)
            {
                var position = rng.NextFloat3(new float3(-500f), new float3(500f));
                var rotation = rng.NextQuaternionRotation();
                var localOffset = rng.NextFloat3(new float3(-5f), new float3(5f));
                var cellSize = rng.NextFloat(0.0001f, 4f);

                var cellSpace = basis.CellSpace(position, rotation, localOffset, cellSize);
                var cell = GridBasis.Cell(cellSpace);

                Assert.AreEqual(LegacyCell(basis, position, rotation, localOffset, cellSize), cell, $"sample {i}");
            }
        }

        [Test]
        public void CellSpaceFeedsBilinearReuseIdentically()
        {
            var basis = new GridBasis(new float3(0.3f, 0.8f, -0.5f));
            var position = new float3(12.5f, 3f, -7.25f);
            var rotation = quaternion.Euler(0.4f, 1.1f, -0.7f);
            var localOffset = new float3(0.5f, 0f, 1.25f);
            var cellSize = 0.25f;

            var world = position + math.rotate(rotation, localOffset);
            var projected = basis.ToGridSpace(world);
            var legacyCellSpace = new float2(projected.x / cellSize, projected.y / cellSize);

            var cellSpace = basis.CellSpace(position, rotation, localOffset, cellSize);

            Assert.AreEqual(legacyCellSpace.x, cellSpace.x);
            Assert.AreEqual(legacyCellSpace.y, cellSpace.y);
        }

        [Test]
        public void TryScaleWeightZeroRoundsToFalse()
        {
            var shape = InfluenceShape.SolidRect(int2.zero, new int2(2, 2), 1);

            Assert.IsFalse(shape.TryScaleWeight(0.4f, out _));
            Assert.IsTrue(shape.TryScaleWeight(0.6f, out var up));
            Assert.AreEqual(1, up.Weight);
            Assert.IsTrue(shape.TryScaleWeight(-1f, out var neg));
            Assert.AreEqual(-1, neg.Weight);
        }

        [Test]
        public void TryScaleWeightUsesMathRound()
        {
            var shape = InfluenceShape.Disc(int2.zero, 3, 5);

            Assert.IsTrue(shape.TryScaleWeight(0.5f, out var half));
            Assert.AreEqual((int)math.round(5 * 0.5f), half.Weight);
            Assert.AreEqual(ShapeKind.Disc, half.Kind);
            Assert.AreEqual(3, half.DiscRadius);
        }
    }
}
