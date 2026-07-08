using BovineLabs.Timeline.Grid.Influence.Data;
using NUnit.Framework;
using Unity.Collections;
using Unity.Mathematics;

namespace BovineLabs.Timeline.Grid.Influence.Tests
{
    public class FieldRetentionTests
    {
        [Test]
        public void DoubleBufferedRetentionMatchesConfig()
        {
            // A double-buffered field schedules each physical buffer every OTHER game frame. With the shared
            // game tick (TODO-13) a buffer's FrameId equals the game tick, so RetentionFrames is measured in
            // game frames rather than in per-buffer schedules (which would double it). This drives a single
            // field with the tick overload to model one buffer's every-other-frame cadence.
            var spec = GridSpec.FromPowerOfTwo(2, 4); // retention = 4 game ticks
            var field = InfluenceField.Create(spec, Allocator.Persistent);

            try
            {
                // Written at game tick 2 (this buffer runs on even ticks).
                var stamp = new NativeArray<Stamp>(
                    new[] { new Stamp(InfluenceShape.SolidRect(int2.zero, new int2(1, 1), 5), int2.zero) },
                    Allocator.TempJob);
                field.Schedule(stamp, 2u, default).Complete();
                stamp.Dispose();
                Assert.Greater(field.ActiveSlotCount, 0);

                var freedAtTick = -1;
                for (var tick = 4u; tick <= 12u; tick += 2u)
                {
                    field.Schedule(default(NativeArray<Stamp>), tick, default).Complete();
                    if (field.ActiveSlotCount == 0 && field.FreeSlotsList.Length > 0)
                    {
                        freedAtTick = (int)tick;
                        break;
                    }
                }

                // Written at tick 2, retention 4: evicted at the first tick where tick - 2 > 4 => tick 8.
                // Without the shared tick, FrameId would advance once per schedule and eviction would slip to ~tick 12.
                Assert.AreEqual(8, freedAtTick, "double-buffered retention did not track game ticks");
            }
            finally
            {
                if (field.IsCreated) field.Dispose();
            }
        }
    }
}
