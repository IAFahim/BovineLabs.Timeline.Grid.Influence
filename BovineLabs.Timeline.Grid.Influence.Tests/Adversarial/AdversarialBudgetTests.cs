using BovineLabs.Timeline.Grid.Influence.Data;
using NUnit.Framework;
using Unity.Collections;
using Unity.Mathematics;

namespace BovineLabs.Timeline.Grid.Influence.Tests
{
    /// <summary>
    /// Exact accounting probes of the per-schedule budgets in PrepareSlotsHelper: chunk clamp hit
    /// exactly / one over, deterministic drop selection, and precise span-budget arithmetic
    /// including non-sticky acceptance after a drop.
    /// </summary>
    public class AdversarialBudgetTests
    {
        [Test]
        public void ChunkBudget_ExactlyAtClamp_ActivatesEverythingWithoutDrops()
        {
            Assert.AreEqual(1 << 14, PrepareSlotsHelper.MaxChunksPerSchedule,
                "test geometry assumes MaxChunksPerSchedule == 16384; update the rect sizes below");

            var r = RunChunkBudget(false);

            Assert.AreEqual(2, r.stats.StampsIn, "StampsIn wrong");
            Assert.AreEqual(0, r.stats.StampsDroppedChunkBudget,
                "a schedule that lands exactly on the chunk clamp must not drop anything");
            Assert.AreEqual(0, r.stats.StampsDroppedSpanBudget, "unexpected span-budget drop");
            Assert.AreEqual(PrepareSlotsHelper.MaxChunksPerSchedule, r.stats.ChunksActivated,
                "ChunksActivated must equal the exact chunk count of the accepted stamps");
            Assert.AreEqual(PrepareSlotsHelper.MaxChunksPerSchedule, r.activeSlots,
                "ActiveSlotCount must equal the exact chunk count at the clamp");
            Assert.AreEqual(3, r.insideBig, "cell inside the clamp-filling rect lost its weight");
            Assert.AreEqual(4, r.second, "cell of the budget-completing 1x1 stamp lost its weight");
        }

        [Test]
        public void ChunkBudget_OneChunkOverClamp_DropsExactlyTheOverflowingStamp()
        {
            Assert.AreEqual(1 << 14, PrepareSlotsHelper.MaxChunksPerSchedule,
                "test geometry assumes MaxChunksPerSchedule == 16384; update the rect sizes below");

            var r = RunChunkBudget(true);

            Assert.AreEqual(3, r.stats.StampsIn, "StampsIn wrong");
            Assert.AreEqual(1, r.stats.StampsDroppedChunkBudget,
                "exactly one stamp exceeds the chunk clamp and exactly one drop must be counted");
            Assert.AreEqual(0, r.stats.StampsDroppedSpanBudget, "unexpected span-budget drop");
            Assert.AreEqual(PrepareSlotsHelper.MaxChunksPerSchedule, r.stats.ChunksActivated,
                "the dropped stamp must not activate chunks");
            Assert.AreEqual(0, r.third, "the chunk-budget-dropped stamp must contribute nothing to the field");
            Assert.AreEqual(3, r.insideBig, "accepted stamp corrupted by the drop");
            Assert.AreEqual(4, r.second, "accepted stamp corrupted by the drop");
        }

        [Test]
        public void ChunkBudget_DropSelectionIsIdenticalAcrossTwoIdenticalRuns()
        {
            var a = RunChunkBudget(true);
            var b = RunChunkBudget(true);

            Assert.AreEqual(a.stats.StampsIn, b.stats.StampsIn, "StampsIn differed between identical runs");
            Assert.AreEqual(a.stats.StampsDroppedSpanBudget, b.stats.StampsDroppedSpanBudget,
                "span drops differed between identical runs");
            Assert.AreEqual(a.stats.StampsDroppedChunkBudget, b.stats.StampsDroppedChunkBudget,
                "chunk drops differed between identical runs");
            Assert.AreEqual(a.stats.ChunksActivated, b.stats.ChunksActivated,
                "ChunksActivated differed between identical runs");
            Assert.AreEqual(a.activeSlots, b.activeSlots, "ActiveSlotCount differed between identical runs");
            Assert.AreEqual((a.insideBig, a.second, a.third), (b.insideBig, b.second, b.third),
                "resolved values differed between identical runs — drop ordering is not deterministic");
        }

        [Test]
        public void SpanBudget_CountsDropsExactlyAndLaterSmallerStampStillFits()
        {
            Assert.AreEqual(1 << 20, PrepareSlotsHelper.MaxSpansPerSchedule,
                "test arithmetic assumes MaxSpansPerSchedule == 1<<20; recompute the counts below");

            // Disc r=100 estimates exactly 2r+1 = 201 spans and fits inside one power-8 chunk, so the
            // span budget binds long before the chunk budget and only one slot is ever activated.
            const int estimatePerBig = 201;
            const int droppedBig = 4;
            var accepted = PrepareSlotsHelper.MaxSpansPerSchedule / estimatePerBig; // 5216
            var totalBig = accepted + droppedBig;
            var remaining = PrepareSlotsHelper.MaxSpansPerSchedule - accepted * estimatePerBig; // 160
            Assert.Greater(remaining, 81, "test setup: trailing small disc (estimate 81) must fit the remainder");

            var spec = GridSpec.FromPowerOfTwo(8, uint.MaxValue);
            var field = InfluenceField.Create(spec, Allocator.Persistent);

            try
            {
                var stamps = new NativeArray<Stamp>(totalBig + 1, Allocator.TempJob);
                for (var i = 0; i < totalBig; i++)
                    stamps[i] = new Stamp(InfluenceShape.Disc(new int2(128, 128), 100, 1), int2.zero);

                stamps[totalBig] = new Stamp(InfluenceShape.Disc(new int2(128, 128), 40, 1), int2.zero);

                field.Schedule(stamps, default).Complete();
                stamps.Dispose();

                var stats = field.LastStats;
                Assert.AreEqual(totalBig + 1, stats.StampsIn, "StampsIn wrong");
                Assert.AreEqual(droppedBig, stats.StampsDroppedSpanBudget,
                    "span-budget drop count must be exact: budget/estimate leaves precisely four big discs over");
                Assert.AreEqual(0, stats.StampsDroppedChunkBudget, "unexpected chunk-budget drop");
                Assert.AreEqual(1, stats.ChunksActivated,
                    "all stamps share one chunk; ChunksActivated counts actual activations, not per-stamp bounds");
                Assert.AreEqual(1, field.ActiveSlotCount, "exactly one chunk slot must be active");

                // Non-sticky acceptance: the small disc scheduled AFTER the drops still fits the
                // remaining budget, so the center must read accepted-big + 1.
                var center = field.AsReader().ReadCell(new int2(128, 128));
                Assert.AreEqual(accepted + 1, center,
                    "center must accumulate every accepted stamp: span drops are per-stamp, not sticky");
            }
            finally
            {
                if (field.IsCreated) field.Dispose();
            }
        }

        private static (FieldFrameStats stats, int insideBig, int second, int third, int activeSlots)
            RunChunkBudget(bool includeOverflow)
        {
            // chunkSize 2: a (258 x 254) rect at the origin spans 129 x 127 = 16383 chunks; the 1x1
            // stamp far away adds the 16384th, landing exactly on MaxChunksPerSchedule. The optional
            // third 1x1 stamp is one chunk over the clamp and must be dropped.
            var spec = GridSpec.FromPowerOfTwo(1, uint.MaxValue);
            var field = InfluenceField.Create(spec, Allocator.Persistent);

            try
            {
                var count = includeOverflow ? 3 : 2;
                var stamps = new NativeArray<Stamp>(count, Allocator.TempJob);
                stamps[0] = new Stamp(InfluenceShape.SolidRect(int2.zero, new int2(258, 254), 3), int2.zero);
                stamps[1] = new Stamp(InfluenceShape.SolidRect(int2.zero, new int2(1, 1), 4), new int2(10_000, 10_000));
                if (includeOverflow)
                    stamps[2] = new Stamp(InfluenceShape.SolidRect(int2.zero, new int2(1, 1), 9),
                        new int2(20_000, 20_000));

                field.Schedule(stamps, default).Complete();
                stamps.Dispose();

                var reader = field.AsReader();
                return (field.LastStats,
                    reader.ReadCell(new int2(5, 5)),
                    reader.ReadCell(new int2(10_000, 10_000)),
                    reader.ReadCell(new int2(20_000, 20_000)),
                    field.ActiveSlotCount);
            }
            finally
            {
                if (field.IsCreated) field.Dispose();
            }
        }
    }
}
