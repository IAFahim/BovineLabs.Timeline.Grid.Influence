using BovineLabs.Timeline.Grid.Influence.Data;
using NUnit.Framework;
using Unity.Collections;
using Unity.Mathematics;

namespace BovineLabs.Timeline.Grid.Influence.Tests
{
    /// <summary>
    /// Generation/frame-stamping probes: reader freshness gates, exact retention/eviction timing,
    /// double-buffered front/back isolation while a write is in flight, same-tick double-schedule,
    /// tick regression and FrameId coercion/wraparound.
    /// </summary>
    public class AdversarialFrameStampingTests
    {
        [Test]
        public void StaleChunkKeepsItsSlotButReadsZeroThroughACurrentReader()
        {
            var spec = GridSpec.FromPowerOfTwo(2, uint.MaxValue);
            var field = InfluenceField.Create(spec, Allocator.Persistent);

            try
            {
                var tick1 = new NativeArray<Stamp>(new[]
                {
                    new Stamp(InfluenceShape.SolidRect(int2.zero, new int2(1, 1), 5), new int2(1, 1)),
                    new Stamp(InfluenceShape.SolidRect(int2.zero, new int2(1, 1), 9), new int2(100, 100))
                }, Allocator.TempJob);
                field.Schedule(tick1, 1u, default).Complete();
                tick1.Dispose();

                var tick2 = new NativeArray<Stamp>(new[]
                {
                    new Stamp(InfluenceShape.SolidRect(int2.zero, new int2(1, 1), 9), new int2(100, 100))
                }, Allocator.TempJob);
                field.Schedule(tick2, 2u, default).Complete();
                tick2.Dispose();

                // Retention is infinite: the (1,1) chunk must still occupy its slot...
                Assert.AreEqual(2, field.CoordBySlotList.Length, "stale chunk's slot was released despite infinite retention");
                Assert.AreEqual(0, field.FreeSlotsList.Length, "stale chunk was evicted despite infinite retention");

                // ...but freshness is gated on LastWritten == FrameId, not on map presence.
                var reader = field.AsReader();
                Assert.AreEqual(9, reader.ReadCell(new int2(100, 100)), "chunk written this tick must be readable");
                Assert.AreEqual(0, reader.ReadCell(new int2(1, 1)),
                    "chunk not written this tick must read 0 even though its slot and data are still resident");
            }
            finally
            {
                if (field.IsCreated) field.Dispose();
            }
        }

        [Test]
        public void RetentionEvictsAtTheExactTickAndCountsEvictionsExactly()
        {
            var spec = GridSpec.FromPowerOfTwo(2, 1u); // retention: evicted once un-written for > 1 tick
            var field = InfluenceField.Create(spec, Allocator.Persistent);

            try
            {
                var stamps = new NativeArray<Stamp>(new[]
                {
                    new Stamp(InfluenceShape.SolidRect(int2.zero, new int2(1, 1), 1), new int2(0, 0)),
                    new Stamp(InfluenceShape.SolidRect(int2.zero, new int2(1, 1), 2), new int2(100, 100)),
                    new Stamp(InfluenceShape.SolidRect(int2.zero, new int2(1, 1), 3), new int2(-50, 75))
                }, Allocator.TempJob);
                field.Schedule(stamps, 1u, default).Complete();
                stamps.Dispose();
                Assert.AreEqual(3, field.ActiveSlotCount, "precondition: three distinct chunks written at tick 1");

                field.Schedule(default(NativeArray<Stamp>), 2u, default).Complete();
                Assert.AreEqual(0, field.LastStats.ChunksEvicted,
                    "tick 2: chunks written at tick 1 with retention 1 are not yet stale (2 - 1 == retention) and must survive");
                Assert.AreEqual(0, field.FreeSlotsList.Length, "tick 2: nothing may be freed yet");

                field.Schedule(default(NativeArray<Stamp>), 3u, default).Complete();
                Assert.AreEqual(3, field.LastStats.ChunksEvicted,
                    "tick 3: all three chunks are now one tick past retention and every eviction must be counted");
                Assert.AreEqual(3, field.FreeSlotsList.Length, "tick 3: every evicted chunk must return its slot");
                Assert.AreEqual(0, field.ActiveSlotCount, "tick 3: nothing was written");
            }
            finally
            {
                if (field.IsCreated) field.Dispose();
            }
        }

        [Test]
        public void DoubleBuffer_FrontGenerationStaysReadableWhileBackIsBeingWritten()
        {
            var reg = new FieldRegistry();
            reg.Initialize(1, Allocator.Persistent);

            try
            {
                var id = reg.Register(new FieldConfig
                {
                    Key = 42,
                    Name = "advDouble",
                    ChunkPower = 2,
                    RetentionFrames = uint.MaxValue,
                    DoubleBuffered = true
                }, Allocator.Persistent);
                Assert.IsTrue(id.IsValid, "test setup: registration failed");

                ref var pair = ref reg.Slot(id.Value);
                var cell = new int2(3, 3);

                // Tick 1: write the back buffer, then swap — mirrors FieldTickSystem's pump.
                pair.Tick++;
                var s1 = new NativeArray<Stamp>(
                    new[] { new Stamp(InfluenceShape.SolidRect(int2.zero, new int2(1, 1), 7), cell) },
                    Allocator.TempJob);
                pair.Back.Schedule(s1, pair.Tick, default).Complete();
                s1.Dispose();
                pair.Swap();

                var frontReader = reg.Front(id).AsReader(); // generation 1
                Assert.AreEqual(7, frontReader.ReadCell(cell), "precondition: tick-1 front readable after swap");

                // Tick 2: schedule the other physical buffer and leave the job IN FLIGHT.
                pair.Tick++;
                var s2 = new NativeArray<Stamp>(
                    new[] { new Stamp(InfluenceShape.SolidRect(int2.zero, new int2(1, 1), 9), cell) },
                    Allocator.TempJob);
                var writing = pair.Back.Schedule(s2, pair.Tick, default);

                Assert.AreEqual(7, frontReader.ReadCell(cell),
                    "front buffer (previous generation) was disturbed while the back buffer was being written");

                writing.Complete();
                s2.Dispose();
                pair.Swap();

                Assert.AreEqual(9, reg.Front(id).AsReader().ReadCell(cell),
                    "after the swap the front must expose the new generation's value");
                Assert.AreEqual(7, frontReader.ReadCell(cell),
                    "a reader captured from the old front must stay pinned to its own generation's buffer");
            }
            finally
            {
                reg.Dispose();
            }
        }

        [Test]
        public void SameTickDoubleSchedule_SecondStampsCoverageMustBeVisible()
        {
            // Contract under test: after a completed Schedule, every cell covered by a supplied,
            // non-dropped stamp reflects that stamp's contribution. Scheduling twice with the SAME
            // tick leaves chunks already stamped this tick out of ActiveSlots (PrepareSlotsJob.cs
            // Activate early-out), so they are neither cleared nor prefix-sum resolved while
            // ScatterJob still writes raw corner deltas into their resolved data.
            var spec = GridSpec.FromPowerOfTwo(2, uint.MaxValue);
            var field = InfluenceField.Create(spec, Allocator.Persistent);

            try
            {
                var first = new NativeArray<Stamp>(
                    new[] { new Stamp(InfluenceShape.SolidRect(int2.zero, new int2(1, 1), 7), int2.zero) },
                    Allocator.TempJob);
                field.Schedule(first, 1u, default).Complete();
                first.Dispose();

                var second = new NativeArray<Stamp>(
                    new[] { new Stamp(InfluenceShape.SolidRect(int2.zero, new int2(2, 2), 11), int2.zero) },
                    Allocator.TempJob);
                field.Schedule(second, 1u, default).Complete();
                second.Dispose();

                Assert.AreEqual(11, field.AsReader().ReadCell(new int2(1, 1)),
                    "cell covered only by the second same-tick stamp must read its weight; a same-tick " +
                    "re-schedule left the chunk unresolved (corner deltas written into resolved data)");
            }
            finally
            {
                if (field.IsCreated) field.Dispose();
            }
        }

        [Test]
        public void TickRegressionEvictsEveryChunkAndRecyclesSlots()
        {
            var spec = GridSpec.FromPowerOfTwo(2, uint.MaxValue);
            var field = InfluenceField.Create(spec, Allocator.Persistent);

            try
            {
                var s1 = new NativeArray<Stamp>(
                    new[] { new Stamp(InfluenceShape.SolidRect(int2.zero, new int2(1, 1), 7), int2.zero) },
                    Allocator.TempJob);
                field.Schedule(s1, 10u, default).Complete();
                s1.Dispose();
                Assert.AreEqual(7, field.AsReader().ReadCell(int2.zero), "precondition: tick-10 write readable");

                var s2 = new NativeArray<Stamp>(
                    new[] { new Stamp(InfluenceShape.SolidRect(int2.zero, new int2(1, 1), 9), new int2(100, 100)) },
                    Allocator.TempJob);
                field.Schedule(s2, 5u, default).Complete();
                s2.Dispose();

                Assert.AreEqual(5u, field.FrameId, "a regressed tick must become the new FrameId");
                Assert.AreEqual(1, field.LastStats.ChunksEvicted,
                    "tick regression must evict every previously written chunk, exactly once each");
                var reader = field.AsReader();
                Assert.AreEqual(9, reader.ReadCell(new int2(100, 100)), "post-regression write must be readable");
                Assert.AreEqual(0, reader.ReadCell(int2.zero), "pre-regression data must not leak past a tick reset");
                Assert.AreEqual(1, field.CoordBySlotList.Length,
                    "the evicted slot must be recycled for the new chunk, not leaked");
            }
            finally
            {
                if (field.IsCreated) field.Dispose();
            }
        }

        [Test]
        public void ZeroTickIsCoercedToOneAndFrameIdWraparoundResetsTheField()
        {
            var spec = GridSpec.FromPowerOfTwo(2, uint.MaxValue);
            var field = InfluenceField.Create(spec, Allocator.Persistent);

            try
            {
                // Tick 0 is reserved (LastWrittenBySlot uses 0 as the free sentinel) and must coerce to 1.
                var s1 = new NativeArray<Stamp>(
                    new[] { new Stamp(InfluenceShape.SolidRect(int2.zero, new int2(1, 1), 3), int2.zero) },
                    Allocator.TempJob);
                field.Schedule(s1, 0u, default).Complete();
                s1.Dispose();
                Assert.AreEqual(1u, field.FrameId, "tick 0 must be coerced to 1, never colliding with the free sentinel");
                Assert.AreEqual(3, field.AsReader().ReadCell(int2.zero), "write under coerced tick must be readable");

                // Auto-increment from uint.MaxValue must reset to 1 and evict everything.
                field.OverrideFrameId(uint.MaxValue);
                field.Schedule(default(NativeArray<Stamp>), default).Complete();
                Assert.AreEqual(1u, field.FrameId, "FrameId wraparound must reset to 1, not to 0");
                Assert.AreEqual(1, field.LastStats.ChunksEvicted,
                    "wraparound reset must evict all resident chunks so stale LastWritten stamps cannot alias new ticks");
                Assert.AreEqual(0, field.AsReader().ReadCell(int2.zero),
                    "pre-wraparound data must not be readable after the reset");
            }
            finally
            {
                if (field.IsCreated) field.Dispose();
            }
        }
    }
}
