using BovineLabs.Timeline.Grid.Influence.Data;
using NUnit.Framework;
using Unity.Collections;
using Unity.Mathematics;

namespace BovineLabs.Timeline.Grid.Influence.Tests
{
    public class BudgetTests
    {
        [Test]
        public void HugeStampRespectsChunkBudget()
        {
            var spec = GridSpec.FromPowerOfTwo(4, uint.MaxValue);
            var field = InfluenceField.Create(spec, Allocator.Persistent);

            try
            {
                var stamps = new NativeArray<Stamp>(
                    new[] { new Stamp(InfluenceShape.Disc(int2.zero, 100_000, 5), int2.zero) },
                    Allocator.TempJob);
                field.Schedule(stamps, default).Complete();
                stamps.Dispose();

                Assert.LessOrEqual(field.ActiveSlotCount, PrepareSlotsHelper.MaxChunksPerSchedule);
                Assert.AreEqual(0, field.ActiveSlotCount, "the oversized stamp must be dropped, not activated");

                var stats = field.LastStats;
                Assert.AreEqual(1, stats.StampsIn);
                Assert.AreEqual(1, stats.StampsDroppedChunkBudget, "expected one chunk-budget drop");
                Assert.AreEqual(0, stats.ChunksActivated);
            }
            finally
            {
                if (field.IsCreated) field.Dispose();
            }
        }

        [Test]
        public void SpanBudgetOverflow_DropsDeterministicallyAndCounts()
        {
            const int count = 200;
            const int radius = 20;
            const int spacing = 64;

            var (values1, stats1) = RunOverBudget(false, count, radius, spacing);
            var (values2, stats2) = RunOverBudget(true, count, radius, spacing);

            CollectionAssert.AreEqual(values1, values2,
                "drop selection differed between insertion orders — sort is not deterministic");

            Assert.AreEqual(stats1.StampsIn, stats2.StampsIn);
            Assert.AreEqual(stats1.StampsDroppedSpanBudget, stats2.StampsDroppedSpanBudget);
            Assert.AreEqual(stats1.StampsDroppedChunkBudget, stats2.StampsDroppedChunkBudget);

            Assert.Greater(stats1.StampsDroppedSpanBudget + stats1.StampsDroppedChunkBudget, 0,
                "test did not exceed any budget; increase stamp count");
        }

        private static (int[] values, FieldFrameStats stats) RunOverBudget(bool reverse, int count, int radius,
            int spacing)
        {
            var spec = GridSpec.FromPowerOfTwo(2, uint.MaxValue);
            var field = InfluenceField.Create(spec, Allocator.Persistent);
            var map = new NativeParallelMultiHashMap<int, Stamp>(count + 4, Allocator.TempJob);

            try
            {
                for (var k = 0; k < count; k++)
                {
                    var i = reverse ? count - 1 - k : k;
                    var weight = 3 + (i % 7);
                    map.Add(0, new Stamp(InfluenceShape.Disc(int2.zero, radius, weight), new int2(i * spacing, 0)));
                }

                field.Schedule(map.AsReadOnly(), 0, default).Complete();

                var reader = field.AsReader();
                var values = new int[count];
                for (var i = 0; i < count; i++)
                    values[i] = reader.ReadCell(new int2(i * spacing, 0));

                return (values, field.LastStats);
            }
            finally
            {
                map.Dispose();
                field.Dispose();
            }
        }
    }
}
