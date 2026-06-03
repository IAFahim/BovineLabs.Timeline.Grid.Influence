using BovineLabs.Timeline.Grid.Influence.Data;
using NUnit.Framework;
using Unity.Mathematics;

namespace BovineLabs.Timeline.Grid.Influence.Tests
{
    public class RasterizerOracleTests
    {
        static readonly int[] ChunkPowers = { 1, 2, 3, 6 };

        [Test]
        public void SingleDiscRadiusOneFormsExactPlus()
        {
            AssertExactNonZeroSet(
                new Stamp(InfluenceShape.Disc(int2.zero, 1, 1), int2.zero),
                new[] { new int2(0, 0), new int2(1, 0), new int2(-1, 0), new int2(0, 1), new int2(0, -1) });
        }

        [Test]
        public void SingleDiscRadiusTwoMatchesExactMembership()
        {
            var expected = new System.Collections.Generic.List<int2>();
            for (int y = -2; y <= 2; y++)
            {
                for (int x = -2; x <= 2; x++)
                {
                    if (x * x + y * y <= 4)
                    {
                        expected.Add(new int2(x, y));
                    }
                }
            }

            AssertExactNonZeroSet(new Stamp(InfluenceShape.Disc(int2.zero, 2, 1), int2.zero), expected.ToArray());
        }

        [Test]
        public void DegenerateCapsuleEqualsDisc()
        {
            AssertSceneMatchesOracleEverywhere(new[]
            {
                new Stamp(InfluenceShape.Capsule(new int2(3, -4), new int2(3, -4), 5, 2), new int2(7, 11))
            });

            var capsule = InfluenceTestHarness.Run(GridSpec.FromPowerOfTwo(3, uint.MaxValue),
                new[] { new Stamp(InfluenceShape.Capsule(new int2(0, 0), new int2(0, 0), 5, 1), int2.zero) },
                new int2(-8, -8), new int2(16, 16));
            var disc = InfluenceTestHarness.Run(GridSpec.FromPowerOfTwo(3, uint.MaxValue),
                new[] { new Stamp(InfluenceShape.Disc(int2.zero, 5, 1), int2.zero) },
                new int2(-8, -8), new int2(16, 16));

            CollectionAssert.AreEqual(disc, capsule);
        }

        [Test]
        public void CapsuleIsSymmetricUnderEndpointSwap()
        {
            for (int seed = 1; seed <= 64; seed++)
            {
                var rng = new Random((uint)seed);
                int2 a = rng.NextInt2(new int2(-6, -6), new int2(7, 7));
                int2 b = rng.NextInt2(new int2(-6, -6), new int2(7, 7));
                int radius = rng.NextInt(0, 9);
                var spec = GridSpec.FromPowerOfTwo(2, uint.MaxValue);

                var forward = new[] { new Stamp(InfluenceShape.Capsule(a, b, radius, 3), int2.zero) };
                var reverse = new[] { new Stamp(InfluenceShape.Capsule(b, a, radius, 3), int2.zero) };
                var (min, size) = InfluenceTestHarness.PaddedBox(forward, spec, 2);

                CollectionAssert.AreEqual(
                    InfluenceTestHarness.Run(spec, forward, min, size),
                    InfluenceTestHarness.Run(spec, reverse, min, size),
                    $"Capsule swap asymmetry seed {seed}");
            }
        }

        [Test]
        public void FuzzedScenesMatchOracleAcrossAllChunkSizes()
        {
            for (int seed = 1; seed <= 300; seed++)
            {
                var rng = new Random((uint)(seed * 2654435761u | 1u));
                int stampCount = rng.NextInt(1, 24);
                var stamps = new Stamp[stampCount];
                for (int i = 0; i < stampCount; i++)
                {
                    stamps[i] = InfluenceTestHarness.RandomStamp(ref rng);
                }

                int[,] reference = null;
                foreach (int power in ChunkPowers)
                {
                    var spec = GridSpec.FromPowerOfTwo(power, uint.MaxValue);
                    var (min, size) = InfluenceTestHarness.PaddedBox(stamps, spec, 2);
                    long[,] oracle = InfluenceTestHarness.Oracle(stamps, min, size);
                    int[,] field = InfluenceTestHarness.Run(spec, stamps, min, size);

                    AssertMatch(field, oracle, min, size, seed, power);

                    if (power == ChunkPowers[0])
                    {
                        reference = field;
                    }
                    else
                    {
                        var (refMin, refSize) = InfluenceTestHarness.PaddedBox(stamps, GridSpec.FromPowerOfTwo(ChunkPowers[0], uint.MaxValue), 2);
                        AssertChunkInvariance(reference, refMin, refSize, field, min, size, seed, power);
                    }
                }
            }
        }

        static void AssertMatch(int[,] field, long[,] oracle, int2 min, int2 size, int seed, int power)
        {
            for (int x = 0; x < size.x; x++)
            {
                for (int y = 0; y < size.y; y++)
                {
                    long expected = oracle[x, y];
                    Assert.That(expected, Is.InRange((long)int.MinValue, (long)int.MaxValue),
                        $"Oracle exceeded int range seed {seed} power {power} cell ({min.x + x},{min.y + y})");
                    Assert.AreEqual((int)expected, field[x, y],
                        $"Mismatch seed {seed} power {power} cell ({min.x + x},{min.y + y})");
                }
            }
        }

        static void AssertChunkInvariance(int[,] a, int2 aMin, int2 aSize, int[,] b, int2 bMin, int2 bSize, int seed, int power)
        {
            int2 lo = math.max(aMin, bMin);
            int2 hi = math.min(aMin + aSize, bMin + bSize);
            for (int cx = lo.x; cx < hi.x; cx++)
            {
                for (int cy = lo.y; cy < hi.y; cy++)
                {
                    Assert.AreEqual(a[cx - aMin.x, cy - aMin.y], b[cx - bMin.x, cy - bMin.y],
                        $"Chunk-size divergence seed {seed} power {power} cell ({cx},{cy})");
                }
            }
        }

        static void AssertExactNonZeroSet(Stamp stamp, int2[] expectedNonZero)
        {
            var spec = GridSpec.FromPowerOfTwo(2, uint.MaxValue);
            var stamps = new[] { stamp };
            var (min, size) = InfluenceTestHarness.PaddedBox(stamps, spec, 2);
            int[,] field = InfluenceTestHarness.Run(spec, stamps, min, size);

            var expected = new System.Collections.Generic.HashSet<int2>(expectedNonZero);
            for (int x = 0; x < size.x; x++)
            {
                for (int y = 0; y < size.y; y++)
                {
                    int2 cell = new int2(min.x + x, min.y + y);
                    bool shouldBeSet = expected.Contains(cell);
                    Assert.AreEqual(shouldBeSet ? stamp.Shape.Weight : 0, field[x, y], $"cell ({cell.x},{cell.y})");
                }
            }
        }

        static void AssertSceneMatchesOracleEverywhere(Stamp[] stamps)
        {
            var spec = GridSpec.FromPowerOfTwo(3, uint.MaxValue);
            var (min, size) = InfluenceTestHarness.PaddedBox(stamps, spec, 2);
            AssertMatch(InfluenceTestHarness.Run(spec, stamps, min, size), InfluenceTestHarness.Oracle(stamps, min, size), min, size, 0, 3);
        }
    }
}
