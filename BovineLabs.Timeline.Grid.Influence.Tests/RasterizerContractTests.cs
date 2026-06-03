using BovineLabs.Timeline.Grid.Influence.Data;
using NUnit.Framework;
using Unity.Mathematics;

namespace BovineLabs.Timeline.Grid.Influence.Tests
{
    public class RasterizerContractTests
    {
        [Test]
        public void EstimateSpanCountIsAlwaysAnUpperBoundAndNeverDropsSpans()
        {
            for (int seed = 1; seed <= 4000; seed++)
            {
                var rng = new Random((uint)(seed * 747796405u | 1u));
                Stamp stamp = InfluenceTestHarness.RandomStamp(ref rng);
                int estimate = Rasterizer.EstimateSpanCount(stamp.Shape);

                InfluenceTestHarness.Emit(stamp, estimate, out int exactCapacityCount);
                InfluenceTestHarness.Emit(stamp, estimate + 4096, out int generousCount);

                Assert.LessOrEqual(generousCount, estimate,
                    $"Emit produced more spans than estimated seed {seed} kind {stamp.Shape.Kind}");
                Assert.AreEqual(generousCount, exactCapacityCount,
                    $"Exact-capacity sink dropped spans seed {seed} kind {stamp.Shape.Kind}");
            }
        }

        [Test]
        public void EveryEmittedSpanIsContainedWithinBounds()
        {
            for (int seed = 1; seed <= 4000; seed++)
            {
                var rng = new Random((uint)(seed * 2246822519u | 1u));
                Stamp stamp = InfluenceTestHarness.RandomStamp(ref rng);
                CellRect bounds = Rasterizer.Bounds(stamp.Shape, stamp.Origin);
                WeightedRect[] spans = InfluenceTestHarness.Emit(stamp, Rasterizer.EstimateSpanCount(stamp.Shape), out _);

                foreach (WeightedRect span in spans)
                {
                    if (span.IsEmpty)
                    {
                        continue;
                    }

                    Assert.IsFalse(bounds.IsEmpty, $"Non-empty span under empty bounds seed {seed}");
                    Assert.GreaterOrEqual(span.Bounds.Min.x, bounds.Min.x, $"span left seed {seed}");
                    Assert.GreaterOrEqual(span.Bounds.Min.y, bounds.Min.y, $"span bottom seed {seed}");
                    Assert.LessOrEqual(span.Bounds.Max.x, bounds.Max.x, $"span right seed {seed}");
                    Assert.LessOrEqual(span.Bounds.Max.y, bounds.Max.y, $"span top seed {seed}");
                }
            }
        }

        [Test]
        public void DegenerateShapesEmitNothing()
        {
            Stamp[] degenerate =
            {
                new(InfluenceShape.SolidRect(int2.zero, new int2(0, 5), 1), int2.zero),
                new(InfluenceShape.SolidRect(int2.zero, new int2(5, -3), 1), int2.zero),
                new(InfluenceShape.RectShell(int2.zero, new int2(5, 5), 0, 1), int2.zero),
                new(InfluenceShape.Disc(int2.zero, -1, 1), int2.zero),
                new(InfluenceShape.Annulus(int2.zero, 5, 5, 1), int2.zero),
                new(InfluenceShape.Annulus(int2.zero, -1, -2, 1), int2.zero),
                new(InfluenceShape.Capsule(int2.zero, new int2(3, 3), -1, 1), int2.zero)
            };

            foreach (Stamp stamp in degenerate)
            {
                InfluenceTestHarness.Emit(stamp, math.max(1, Rasterizer.EstimateSpanCount(stamp.Shape)), out int count);
                foreach (WeightedRect span in InfluenceTestHarness.Emit(stamp, math.max(1, count), out _))
                {
                    Assert.IsTrue(span.IsEmpty, $"Degenerate {stamp.Shape.Kind} emitted coverage");
                }
            }
        }

        [Test]
        public void ShellThickerThanHalfBecomesSolid()
        {
            var stamps = new[] { new Stamp(InfluenceShape.RectShell(int2.zero, new int2(6, 6), 5, 1), int2.zero) };
            var spec = GridSpec.FromPowerOfTwo(3, uint.MaxValue);
            var (min, size) = InfluenceTestHarness.PaddedBox(stamps, spec, 2);
            int[,] field = InfluenceTestHarness.Run(spec, stamps, min, size);

            for (int x = 0; x < 6; x++)
            {
                for (int y = 0; y < 6; y++)
                {
                    Assert.AreEqual(1, field[x - min.x, y - min.y], $"solid shell cell ({x},{y})");
                }
            }
        }
    }
}
