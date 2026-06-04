using BovineLabs.Timeline.Grid.Influence.Data;
using NUnit.Framework;
using Unity.Mathematics;

namespace BovineLabs.Timeline.Grid.Influence.Tests
{
    public class FieldAlgebraTests
    {
        private static readonly GridSpec Spec = GridSpec.FromPowerOfTwo(2, uint.MaxValue);

        [Test]
        public void SuperpositionHoldsForArbitraryScenes()
        {
            for (var seed = 1; seed <= 120; seed++)
            {
                var rng = new Random((uint)((seed * 3266489917u) | 1u));
                var left = NewScene(ref rng, rng.NextInt(1, 8));
                var right = NewScene(ref rng, rng.NextInt(1, 8));
                var combined = Concat(left, right);
                var (min, size) = InfluenceTestHarness.PaddedBox(combined, Spec, 2);

                var fl = InfluenceTestHarness.Run(Spec, left, min, size);
                var fr = InfluenceTestHarness.Run(Spec, right, min, size);
                var fc = InfluenceTestHarness.Run(Spec, combined, min, size);

                for (var x = 0; x < size.x; x++)
                for (var y = 0; y < size.y; y++)
                    Assert.AreEqual(fl[x, y] + fr[x, y], fc[x, y], $"superposition seed {seed} ({x},{y})");
            }
        }

        [Test]
        public void StampAndItsNegationCancelToZero()
        {
            for (var seed = 1; seed <= 200; seed++)
            {
                var rng = new Random((uint)((seed * 668265263u) | 1u));
                var stamp = InfluenceTestHarness.RandomStamp(ref rng);
                var scene = new[] { stamp, stamp.Negated() };
                var (min, size) = InfluenceTestHarness.PaddedBox(scene, Spec, 2);
                var field = InfluenceTestHarness.Run(Spec, scene, min, size);

                foreach (var value in field) Assert.AreEqual(0, value, $"cancellation seed {seed}");
            }
        }

        [Test]
        public void TranslationShiftsFieldExactly()
        {
            for (var seed = 1; seed <= 120; seed++)
            {
                var rng = new Random((uint)((seed * 374761393u) | 1u));
                var stamp = InfluenceTestHarness.RandomStamp(ref rng);
                var delta = rng.NextInt2(new int2(-20, -20), new int2(21, 21));
                var shifted = new[] { new Stamp(stamp.Shape, stamp.Origin + delta) };
                var baseline = new[] { stamp };

                var (min, size) = InfluenceTestHarness.PaddedBox(baseline, Spec, 2);
                var f0 = InfluenceTestHarness.Run(Spec, baseline, min, size);
                var f1 = InfluenceTestHarness.Run(Spec, shifted, min + delta, size);

                CollectionAssert.AreEqual(f0, f1, $"translation seed {seed}");
            }
        }

        [Test]
        public void WeightScalesLinearly()
        {
            for (var seed = 1; seed <= 120; seed++)
            {
                var rng = new Random((uint)((seed * 2246822519u) | 1u));
                var once = InfluenceTestHarness.RandomStamp(ref rng);
                var doubled = new[] { new Stamp(once.Shape.WithWeight(once.Shape.Weight * 2), once.Origin) };
                var twice = new[] { once, once };
                var (min, size) = InfluenceTestHarness.PaddedBox(twice, Spec, 2);

                CollectionAssert.AreEqual(
                    InfluenceTestHarness.Run(Spec, doubled, min, size),
                    InfluenceTestHarness.Run(Spec, twice, min, size),
                    $"weight linearity seed {seed}");
            }
        }

        [Test]
        public void ResultIsIndependentOfStampOrder()
        {
            for (var seed = 1; seed <= 120; seed++)
            {
                var rng = new Random((uint)((seed * 3266489917u) | 1u));
                var scene = NewScene(ref rng, rng.NextInt(2, 20));
                var shuffled = (Stamp[])scene.Clone();
                for (var i = shuffled.Length - 1; i > 0; i--)
                {
                    var j = rng.NextInt(0, i + 1);
                    (shuffled[i], shuffled[j]) = (shuffled[j], shuffled[i]);
                }

                var (min, size) = InfluenceTestHarness.PaddedBox(scene, Spec, 2);
                CollectionAssert.AreEqual(
                    InfluenceTestHarness.Run(Spec, scene, min, size),
                    InfluenceTestHarness.Run(Spec, shuffled, min, size),
                    $"order independence seed {seed}");
            }
        }

        [Test]
        public void RepeatedRunsAreDeterministic()
        {
            var rng = new Random(0xC0FFEEu);
            var scene = NewScene(ref rng, 32);
            var (min, size) = InfluenceTestHarness.PaddedBox(scene, Spec, 2);

            var first = InfluenceTestHarness.Run(Spec, scene, min, size);
            for (var i = 0; i < 8; i++)
                CollectionAssert.AreEqual(first, InfluenceTestHarness.Run(Spec, scene, min, size),
                    $"determinism run {i}");
        }

        private static Stamp[] NewScene(ref Random rng, int count)
        {
            var stamps = new Stamp[count];
            for (var i = 0; i < count; i++) stamps[i] = InfluenceTestHarness.RandomStamp(ref rng);

            return stamps;
        }

        private static Stamp[] Concat(Stamp[] a, Stamp[] b)
        {
            var result = new Stamp[a.Length + b.Length];
            a.CopyTo(result, 0);
            b.CopyTo(result, a.Length);
            return result;
        }
    }
}