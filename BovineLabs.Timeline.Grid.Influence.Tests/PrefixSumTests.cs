using BovineLabs.Timeline.Grid.Influence.Data;
using NUnit.Framework;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;

namespace BovineLabs.Timeline.Grid.Influence.Tests
{
    public class PrefixSumTests
    {
        [Test]
        public unsafe void RunMatchesNaiveTwoDimensionalInclusiveScan()
        {
            int[] powers = { 1, 2, 3, 5, 8 };
            foreach (int power in powers)
            {
                var spec = GridSpec.FromPowerOfTwo(power, uint.MaxValue);
                for (int seed = 1; seed <= 40; seed++)
                {
                    var rng = new Unity.Mathematics.Random((uint)(seed * 2654435761u | 1u));
                    var buffer = new NativeArray<int>(spec.ElementsPerChunk, Allocator.Temp, NativeArrayOptions.ClearMemory);
                    var reference = new long[spec.Dimension, spec.Dimension];

                    for (int y = 0; y < spec.Dimension; y++)
                    {
                        for (int x = 0; x < spec.Dimension; x++)
                        {
                            int value = rng.NextInt(-50, 51);
                            buffer[y * spec.Stride + x] = value;
                            reference[x, y] = value;
                        }
                    }

                    for (int y = 0; y < spec.Dimension; y++)
                    {
                        for (int x = 0; x < spec.Dimension; x++)
                        {
                            long left = x > 0 ? reference[x - 1, y] : 0;
                            long below = y > 0 ? reference[x, y - 1] : 0;
                            long diag = x > 0 && y > 0 ? reference[x - 1, y - 1] : 0;
                            reference[x, y] += left + below - diag;
                        }
                    }

                    PrefixSum.Run((int*)buffer.GetUnsafePtr(), spec);

                    for (int y = 0; y < spec.Dimension; y++)
                    {
                        for (int x = 0; x < spec.Dimension; x++)
                        {
                            Assert.AreEqual(reference[x, y], buffer[y * spec.Stride + x],
                                $"power {power} seed {seed} cell ({x},{y})");
                        }
                    }

                    buffer.Dispose();
                }
            }
        }

        [Test]
        public unsafe void DifferenceArrayCornersResolveToConstantRect()
        {
            var spec = GridSpec.FromPowerOfTwo(4, uint.MaxValue);
            var buffer = new NativeArray<int>(spec.ElementsPerChunk, Allocator.Temp, NativeArrayOptions.ClearMemory);

            int x0 = 3, y0 = 2, x1 = 11, y1 = 9, weight = 6;
            int* field = (int*)buffer.GetUnsafePtr();
            field[y0 * spec.Stride + x0] += weight;
            field[y0 * spec.Stride + x1] -= weight;
            field[y1 * spec.Stride + x0] -= weight;
            field[y1 * spec.Stride + x1] += weight;

            PrefixSum.Run(field, spec);

            for (int y = 0; y < spec.ChunkSize; y++)
            {
                for (int x = 0; x < spec.ChunkSize; x++)
                {
                    bool inside = x >= x0 && x < x1 && y >= y0 && y < y1;
                    Assert.AreEqual(inside ? weight : 0, field[y * spec.Stride + x], $"cell ({x},{y})");
                }
            }

            buffer.Dispose();
        }
    }
}
