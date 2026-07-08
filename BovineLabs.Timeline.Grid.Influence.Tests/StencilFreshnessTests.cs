using BovineLabs.Timeline.Grid.Influence.Data;
using NUnit.Framework;
using Unity.Collections;
using Unity.Mathematics;

namespace BovineLabs.Timeline.Grid.Influence.Tests
{
    public class StencilFreshnessTests
    {
        private static InfluenceField.StencilConfig StencilOf(ref InfluenceField source, int decay, int spreadDenom)
        {
            return new InfluenceField.StencilConfig
            {
                IsActive = true,
                ActiveSlots = source.ActiveSlotsDeferred,
                CoordBySlot = source.CoordBySlotDeferred,
                Data = source.DataDeferred,
                NonZeroBySlot = source.NonZeroBySlotDeferred,
                SlotByCoord = source.SlotByCoordReadOnly,
                LastWrittenBySlot = source.LastWrittenBySlotDeferred,
                FrameId = source.FrameId,
                DecayPerMille = decay,
                SpreadDenominator = spreadDenom
            };
        }

        [Test]
        public void StaleHaloDoesNotReinject()
        {
            var spec = GridSpec.FromPowerOfTwo(2, uint.MaxValue); // chunkSize 4
            var front = InfluenceField.Create(spec, Allocator.Persistent);
            var back = InfluenceField.Create(spec, Allocator.Persistent);

            const int decay = 0;
            const int spreadDenom = 4;

            try
            {
                // Fill chunk A (coord 0,0 => world cells 0..3) entirely with 1000.
                var stampA = new NativeArray<Stamp>(
                    new[] { new Stamp(InfluenceShape.SolidRect(int2.zero, new int2(4, 4), 1000), int2.zero) },
                    Allocator.TempJob);
                front.Schedule(stampA, default).Complete();
                stampA.Dispose();
                Assert.AreEqual(1000, front.AsReader().ReadCell(new int2(3, 2)));

                // Empty schedule: chunk A is now stale (LastWritten != FrameId) but still mapped with
                // non-zero data — exactly the "cancelled-to-zero in one buffer" hazard from TODO-05.
                front.Schedule(default, default).Complete();
                Assert.AreEqual(0, front.AsReader().ReadCell(new int2(3, 2)), "stale chunk must read as zero");

                // Diffuse into back: activate chunk B (coord 1,0 => world x 4..7) with an interior stamp only.
                var stampB = new NativeArray<Stamp>(
                    new[] { new Stamp(InfluenceShape.SolidRect(new int2(6, 2), new int2(1, 1), 500), int2.zero) },
                    Allocator.TempJob);
                back.Schedule(stampB, default, StencilOf(ref front, decay, spreadDenom)).Complete();
                stampB.Dispose();

                var reader = back.AsReader();
                Assert.AreEqual(500, reader.ReadCell(new int2(6, 2)), "stamp B should be present");

                // Chunk B's column adjacent to the dead chunk A (world x = 4) must receive no halo inflow.
                for (var y = 0; y < 4; y++)
                    Assert.AreEqual(0, reader.ReadCell(new int2(4, y)),
                        $"stale halo from dead chunk A leaked into chunk B at (4,{y})");
            }
            finally
            {
                if (front.IsCreated) front.Dispose();
                if (back.IsCreated) back.Dispose();
            }
        }
    }
}
