using System.Reflection;
using BovineLabs.Timeline.Grid.Influence.Fields;
using NUnit.Framework;

namespace BovineLabs.Timeline.Grid.Influence.Tests
{
    /// <summary>
    /// Probes of the fixed-tick accumulator (FieldTickSystem.ResolveSubSteps, TODO-03): interval-0
    /// behaviour, the 2-sub-step catch-up clamp, the debt clamp, and exact fractional carryover.
    /// The method is private and only touches the _accumulator field, so it is driven through
    /// reflection on a boxed system instance (no SystemState required); deltas use exact binary
    /// fractions so double accumulation is bit-exact and deterministic.
    /// </summary>
    public class AdversarialTickAccumulatorTests
    {
        [Test]
        public void ZeroInterval_AlwaysTicksOnceAndClearsAccumulatedDebt()
        {
            var step = NewPump();

            Assert.AreEqual(2, step(0.25f, 10f), "precondition: build up catch-up debt at a fixed interval");
            Assert.AreEqual(1, step(0f, 0f), "interval 0 must tick exactly once per frame, even with dt 0");
            Assert.AreEqual(0, step(0.25f, 0.2f),
                "an interval-0 frame must reset the accumulator; stale debt leaked into the next fixed-interval frame");
        }

        [Test]
        public void CatchUp_IsClampedToTwoSubStepsAndDebtToOneInterval()
        {
            var step = NewPump();

            Assert.AreEqual(2, step(0.25f, 10f),
                "a huge frame hitch must be clamped to exactly 2 catch-up sub-steps");
            Assert.AreEqual(1, step(0.25f, 0f),
                "leftover debt must be clamped to exactly one interval: the next frame runs one sub-step, not two");
            Assert.AreEqual(0, step(0.25f, 0f),
                "after the clamped debt is consumed no further sub-steps may run — unbounded catch-up debt detected");
        }

        [Test]
        public void FractionalDeltaTime_CarriesExactlyAcrossFrames()
        {
            var step = NewPump();

            Assert.AreEqual(0, step(0.25f, 0.125f), "dt below the interval must produce zero sub-steps");
            Assert.AreEqual(1, step(0.25f, 0.125f), "carried fraction must accumulate to exactly one interval");
            Assert.AreEqual(1, step(0.25f, 0.375f), "1.5 intervals of total time must yield one sub-step plus carry");
            Assert.AreEqual(1, step(0.25f, 0.125f), "the 0.125 carry from the previous frame was lost");
            Assert.AreEqual(0, step(0.25f, 0f), "accumulator must be empty after the carry is consumed");
        }

        private delegate int Pump(float interval, float deltaTime);

        private static Pump NewPump()
        {
            var method = typeof(FieldTickSystem).GetMethod("ResolveSubSteps",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(method,
                "FieldTickSystem.ResolveSubSteps(float, float) was renamed or removed; update this test");

            // Box once so _accumulator persists across invocations, exactly like the live system field.
            object system = default(FieldTickSystem);
            return (interval, dt) => (int)method.Invoke(system, new object[] { interval, dt });
        }
    }
}
