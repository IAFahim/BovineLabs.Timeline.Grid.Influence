using BovineLabs.Timeline.Grid.Influence.Data;
using NUnit.Framework;
using Unity.Collections;
using Unity.Mathematics;

namespace BovineLabs.Timeline.Grid.Influence.Tests
{
    public class FieldLifecycleTests
    {
        [Test]
        public void EmptyScheduleAdvancesFrameAndReadsZero()
        {
            var field = InfluenceField.Create(GridSpec.FromPowerOfTwo(2, uint.MaxValue), Allocator.Persistent);
            var before = field.FrameId;
            field.Schedule(default, default).Complete();

            Assert.AreEqual(before + 1, field.FrameId);
            Assert.AreEqual(0, field.AsReader().ReadCell(new int2(3, 7)));
            field.Dispose();
        }

        [Test]
        public void ReaderAndCompleteAndReadAgree()
        {
            var spec = GridSpec.FromPowerOfTwo(3, uint.MaxValue);
            var field = InfluenceField.Create(spec, Allocator.Persistent);
            var stamps =
                new NativeArray<Stamp>(new[] { new Stamp(InfluenceShape.Disc(int2.zero, 4, 5), new int2(2, 2)) },
                    Allocator.TempJob);
            field.Schedule(stamps, default).Complete();

            var reader = field.AsReader();
            for (var x = -6; x <= 10; x++)
            for (var y = -6; y <= 10; y++)
                Assert.AreEqual(reader.ReadCell(new int2(x, y)), field.CompleteAndRead(new int2(x, y)));

            stamps.Dispose();
            field.Dispose();
        }

        [Test]
        public void ChunkWrittenLastFrameReadsStaleAsZero()
        {
            var field = InfluenceField.Create(GridSpec.FromPowerOfTwo(2, uint.MaxValue), Allocator.Persistent);
            var stamps =
                new NativeArray<Stamp>(
                    new[] { new Stamp(InfluenceShape.SolidRect(int2.zero, new int2(2, 2), 9), new int2(1, 1)) },
                    Allocator.TempJob);

            field.Schedule(stamps, default).Complete();
            Assert.AreEqual(9, field.AsReader().ReadCell(new int2(1, 1)));

            field.Schedule(default, default).Complete();
            Assert.AreEqual(0, field.AsReader().ReadCell(new int2(1, 1)));

            stamps.Dispose();
            field.Dispose();
        }

        [Test]
        public void StaleChunkSlotIsEvictedAndReused()
        {
            var field = InfluenceField.Create(GridSpec.FromPowerOfTwo(2, 1), Allocator.Persistent);

            var farA = new NativeArray<Stamp>(
                new[] { new Stamp(InfluenceShape.SolidRect(int2.zero, new int2(1, 1), 1), new int2(0, 0)) },
                Allocator.TempJob);
            field.Schedule(farA, default).Complete();
            var slotsAfterFirst = field.CoordBySlotList.Length;

            field.Schedule(default, default).Complete();
            field.Schedule(default, default).Complete();

            Assert.Greater(field.FreeSlotsList.Length, 0, "slot was not evicted");

            var farB = new NativeArray<Stamp>(
                new[] { new Stamp(InfluenceShape.SolidRect(int2.zero, new int2(1, 1), 2), new int2(1000, 1000)) },
                Allocator.TempJob);
            field.Schedule(farB, default).Complete();

            Assert.AreEqual(slotsAfterFirst, field.CoordBySlotList.Length, "slot was not reused");
            Assert.AreEqual(2, field.AsReader().ReadCell(new int2(1000, 1000)));

            farA.Dispose();
            farB.Dispose();
            field.Dispose();
        }

        [Test]
        public void FrameWraparoundResetsStalenessWithoutFalsePositives()
        {
            var field = InfluenceField.Create(GridSpec.FromPowerOfTwo(2, uint.MaxValue), Allocator.Persistent);
            field.OverrideFrameId(uint.MaxValue - 1u);

            var first = new NativeArray<Stamp>(
                new[] { new Stamp(InfluenceShape.SolidRect(int2.zero, new int2(1, 1), 7), new int2(0, 0)) },
                Allocator.TempJob);
            field.Schedule(first, default).Complete();
            Assert.AreEqual(uint.MaxValue, field.FrameId);
            Assert.AreEqual(7, field.AsReader().ReadCell(new int2(0, 0)));

            var second =
                new NativeArray<Stamp>(
                    new[] { new Stamp(InfluenceShape.SolidRect(int2.zero, new int2(1, 1), 3), new int2(1000, 1000)) },
                    Allocator.TempJob);
            field.Schedule(second, default).Complete();

            Assert.AreEqual(1u, field.FrameId, "wraparound did not reset frame");
            Assert.AreEqual(0, field.AsReader().ReadCell(new int2(0, 0)), "stale chunk leaked across wraparound");
            Assert.AreEqual(3, field.AsReader().ReadCell(new int2(1000, 1000)));

            first.Dispose();
            second.Dispose();
            field.Dispose();
        }

        [Test]
        public void DisposeIsIdempotentAndMarksUncreated()
        {
            var field = InfluenceField.Create(GridSpec.FromPowerOfTwo(2, uint.MaxValue), Allocator.Persistent);
            Assert.IsTrue(field.IsCreated);
            field.Dispose();
            Assert.IsFalse(field.IsCreated);
            Assert.DoesNotThrow(() => field.Dispose());
        }
    }
}