using BovineLabs.Timeline.Grid.Influence.Data;
using NUnit.Framework;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Mathematics;

namespace BovineLabs.Timeline.Grid.Influence.Tests
{
    public class PrefixSumTests
    {
        [Test]
        public unsafe void RunMatchesNaiveTwoDimensionalInclusiveScan()
        {
            int[] powers = { 1, 2, 3, 5, 8 };
            foreach (var power in powers)
            {
                var spec = GridSpec.FromPowerOfTwo(power, uint.MaxValue);
                for (var seed = 1; seed <= 40; seed++)
                {
                    var rng = new Random((uint)((seed * 2654435761u) | 1u));
                    var buffer = new NativeArray<int>(spec.ElementsPerChunk, Allocator.Temp);
                    var reference = new long[spec.Dimension, spec.Dimension];

                    for (var y = 0; y < spec.Dimension; y++)
                    for (var x = 0; x < spec.Dimension; x++)
                    {
                        var value = rng.NextInt(-50, 51);
                        buffer[y * spec.Stride + x] = value;
                        reference[x, y] = value;
                    }

                    for (var y = 0; y < spec.Dimension; y++)
                    for (var x = 0; x < spec.Dimension; x++)
                    {
                        var left = x > 0 ? reference[x - 1, y] : 0;
                        var below = y > 0 ? reference[x, y - 1] : 0;
                        var diag = x > 0 && y > 0 ? reference[x - 1, y - 1] : 0;
                        reference[x, y] += left + below - diag;
                    }

                    PrefixSum.Run((int*)buffer.GetUnsafePtr(), spec);

                    for (var y = 0; y < spec.Dimension; y++)
                    for (var x = 0; x < spec.Dimension; x++)
                        Assert.AreEqual(reference[x, y], buffer[y * spec.Stride + x],
                            $"power {power} seed {seed} cell ({x},{y})");

                    buffer.Dispose();
                }
            }
        }

        [Test]
        public unsafe void DifferenceArrayCornersResolveToConstantRect()
        {
            var spec = GridSpec.FromPowerOfTwo(4, uint.MaxValue);
            var buffer = new NativeArray<int>(spec.ElementsPerChunk, Allocator.Temp);

            int x0 = 3, y0 = 2, x1 = 11, y1 = 9, weight = 6;
            var field = (int*)buffer.GetUnsafePtr();
            field[y0 * spec.Stride + x0] += weight;
            field[y0 * spec.Stride + x1] -= weight;
            field[y1 * spec.Stride + x0] -= weight;
            field[y1 * spec.Stride + x1] += weight;

            PrefixSum.Run(field, spec);

            for (var y = 0; y < spec.ChunkSize; y++)
            for (var x = 0; x < spec.ChunkSize; x++)
            {
                var inside = x >= x0 && x < x1 && y >= y0 && y < y1;
                Assert.AreEqual(inside ? weight : 0, field[y * spec.Stride + x], $"cell ({x},{y})");
            }

            buffer.Dispose();
        }
    }
}