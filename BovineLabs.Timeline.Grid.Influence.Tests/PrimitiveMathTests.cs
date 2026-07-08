using System.Numerics;
using BovineLabs.Timeline.Grid.Influence.Data;
using NUnit.Framework;
using Unity.Mathematics;

namespace BovineLabs.Timeline.Grid.Influence.Tests
{
    public class PrimitiveMathTests
    {
        [Test]
        public void FloorSqrtSatisfiesInvariantOverSmallDomain()
        {
            for (long v = 0; v <= 1_000_000; v++) AssertFloorSqrt(v);
        }

        [Test]
        public void FloorSqrtIsExactNearPerfectSquaresAndAtExtremes()
        {
            for (long k = 1; k < 3_000_000_000L; k += 7_919)
            {
                var square = k * k;
                Assert.AreEqual(k, IntegerMath.FloorSqrt(square), $"sqrt({square})");
                Assert.AreEqual(k - 1, IntegerMath.FloorSqrt(square - 1), $"sqrt({square - 1})");
                Assert.AreEqual(k, IntegerMath.FloorSqrt(square + 1), $"sqrt({square + 1})");
            }

            AssertFloorSqrt(long.MaxValue);
            AssertFloorSqrt(long.MaxValue - 1);
            Assert.AreEqual(0, IntegerMath.FloorSqrt(-12345));
        }

        [Test]
        public void ClampToIntAndSaturatingAddNeverWrap()
        {
            Assert.AreEqual(0, IntegerMath.ClampToInt(-1));
            Assert.AreEqual(int.MaxValue, IntegerMath.ClampToInt((long)int.MaxValue + 1));
            Assert.AreEqual(12345, IntegerMath.ClampToInt(12345));
            Assert.AreEqual(int.MaxValue, IntegerMath.SaturatingAdd(int.MaxValue, int.MaxValue));
            Assert.AreEqual(0, IntegerMath.SaturatingAdd(-5, -10));
            Assert.AreEqual(7, IntegerMath.SaturatingAdd(3, 4));
        }

        [Test]
        public void EstimateSpanCountSaturatesForEnormousShapes()
        {
            var huge = InfluenceShape.Capsule(new int2(int.MinValue / 2, 0), new int2(int.MaxValue / 2, 0),
                int.MaxValue, 1);
            Assert.DoesNotThrow(() => Rasterizer.EstimateSpanCount(huge));
            Assert.AreEqual(int.MaxValue, Rasterizer.EstimateSpanCount(huge));
        }

        [Test]
        public void GridSpecHoldsSafetyInvariantsAndClampsPower()
        {
            for (var power = 1; power <= 8; power++)
            {
                var spec = GridSpec.FromPowerOfTwo(power, 99);
                Assert.AreEqual(1 << power, spec.ChunkSize);
                Assert.AreEqual(spec.ChunkSize + 1, spec.Dimension);
                Assert.AreEqual(0, spec.Stride % 8, "stride not 8-aligned");
                Assert.GreaterOrEqual(spec.Stride, spec.Dimension);
                Assert.Greater(spec.Stride, spec.ChunkSize, "corner writes can overflow into the next chunk");
                Assert.AreEqual(spec.Stride * spec.Dimension, spec.ElementsPerChunk);
                Assert.AreEqual(99u, spec.RetentionFrames);
            }

            Assert.AreEqual(2, GridSpec.FromPowerOfTwo(0, 0).ChunkSize);
            Assert.AreEqual(2, GridSpec.FromPowerOfTwo(-100, 0).ChunkSize);
            Assert.AreEqual(256, GridSpec.FromPowerOfTwo(9, 0).ChunkSize);
            Assert.AreEqual(256, GridSpec.FromPowerOfTwo(1000, 0).ChunkSize);
        }

        [Test]
        public void ChunkMathRoundTripsAcrossNegativeCoordinates()
        {
            for (var log2 = 1; log2 <= 6; log2++)
            {
                var chunkSize = 1 << log2;
                for (var cell = -500; cell <= 500; cell++)
                {
                    var c = new int2(cell, -cell);
                    var coord = ChunkMath.ChunkCoordOf(c, log2);
                    var chunkBase = ChunkMath.ChunkBaseOf(coord, log2);
                    var local = ChunkMath.LocalOf(c, chunkBase);

                    Assert.IsTrue(ChunkMath.ContainsLocal(local, chunkSize),
                        $"local out of range log2 {log2} cell {cell}");
                    Assert.AreEqual(c, chunkBase + local, $"round trip log2 {log2} cell {cell}");
                }
            }
        }

        private static void AssertFloorSqrt(long v)
        {
            long r = IntegerMath.FloorSqrt(v);
            Assert.GreaterOrEqual(r, 0);
            Assert.LessOrEqual((BigInteger)r * r, (BigInteger)v, $"floor too high for {v}");
            Assert.Greater((BigInteger)(r + 1) * (r + 1), (BigInteger)v, $"floor too low for {v}");
        }
    }
}