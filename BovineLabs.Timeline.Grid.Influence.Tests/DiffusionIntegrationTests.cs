using BovineLabs.Timeline.Grid.Influence.Data;
using NUnit.Framework;
using Unity.Collections;
using Unity.Mathematics;

namespace BovineLabs.Timeline.Grid.Influence.Tests
{
    public class DiffusionIntegrationTests
    {
        private static int[,] StepOracle(int[,] front, Stamp[] stamps, int2 boxMin, int decay, int spreadDenom)
        {
            var w = front.GetLength(0);
            var h = front.GetLength(1);
            var back = new int[w, h];

            for (var x = 0; x < w; x++)
            for (var y = 0; y < h; y++)
            {
                var self = front[x, y];
                var incoming = Spread(front, x - 1, y, decay, spreadDenom) +
                               Spread(front, x + 1, y, decay, spreadDenom) +
                               Spread(front, x, y - 1, decay, spreadDenom) +
                               Spread(front, x, y + 1, decay, spreadDenom);

                back[x, y] = IntegerMath.DecayKeep(self, decay) -
                             4 * IntegerMath.Outflow(self, decay, spreadDenom) +
                             incoming;
            }

            for (var s = 0; s < stamps.Length; s++)
            for (var x = 0; x < w; x++)
            for (var y = 0; y < h; y++)
                back[x, y] += (int)InfluenceTestHarness.Contribution(stamps[s].Shape, stamps[s].Origin,
                    new int2(boxMin.x + x, boxMin.y + y));

            return back;
        }

        private static int Spread(int[,] grid, int x, int y, int decay, int denom)
        {
            if (x < 0 || y < 0 || x >= grid.GetLength(0) || y >= grid.GetLength(1)) return 0;
            return IntegerMath.Outflow(grid[x, y], decay, denom);
        }

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
        public void IntegratedStencil_SpreadsAcrossChunkBoundaries_IdenticalToOracle()
        {
            var chunkPower = 4;
            var spec = GridSpec.FromPowerOfTwo(chunkPower, uint.MaxValue);

            var front = InfluenceField.Create(spec, Allocator.Persistent);
            var back = InfluenceField.Create(spec, Allocator.Persistent);

            var initialStamps = new[]
            {
                new Stamp(InfluenceShape.SolidRect(int2.zero, new int2(2, 2), 1000), new int2(15, 15))
            };

            var boxMin = new int2(-16, -16);
            var boxSize = new int2(64, 64);

            var oracleGrid = new int[boxSize.x, boxSize.y];

            var steps = 10;
            var decay = 30;
            var spreadDenom = 5;

            try
            {
                var array = new NativeArray<Stamp>(initialStamps, Allocator.TempJob);
                front.Schedule(array, default).Complete();
                array.Dispose();

                oracleGrid = StepOracle(oracleGrid, initialStamps, boxMin, decay, spreadDenom);

                for (var step = 0; step < steps; step++)
                {
                    var reader = front.AsReader();
                    for (var x = 0; x < boxSize.x; x++)
                    for (var y = 0; y < boxSize.y; y++)
                    {
                        var cell = new int2(boxMin.x + x, boxMin.y + y);
                        var actual = reader.ReadCell(cell);
                        var expected = oracleGrid[x, y];
                        Assert.AreEqual(expected, actual, $"Mismatch at step {step}, cell {cell}");
                    }

                    back.Schedule(default, default, StencilOf(ref front, decay, spreadDenom)).Complete();

                    oracleGrid = StepOracle(oracleGrid, new Stamp[0], boxMin, decay, spreadDenom);

                    (front, back) = (back, front);
                }
            }
            finally
            {
                if (front.IsCreated) front.Dispose();
                if (back.IsCreated) back.Dispose();
            }
        }

        [Test]
        public void DiffusionDecaysToZeroAndDeactivatesChunks()
        {
            var spec = GridSpec.FromPowerOfTwo(4, uint.MaxValue);
            var front = InfluenceField.Create(spec, Allocator.Persistent);
            var back = InfluenceField.Create(spec, Allocator.Persistent);

            const int decay = 400;
            const int spreadDenom = 5;

            try
            {
                var stamps = new NativeArray<Stamp>(
                    new[] { new Stamp(InfluenceShape.SolidRect(int2.zero, new int2(2, 2), 1000), new int2(15, 15)) },
                    Allocator.TempJob);
                front.Schedule(stamps, default).Complete();
                stamps.Dispose();

                Assert.Greater(front.ActiveSlotCount, 0);

                var diedAt = -1;
                for (var step = 0; step < 64; step++)
                {
                    back.Schedule(default, default, StencilOf(ref front, decay, spreadDenom)).Complete();
                    (front, back) = (back, front);

                    if (front.ActiveSlotCount == 0)
                    {
                        diedAt = step;
                        break;
                    }
                }

                Assert.GreaterOrEqual(diedAt, 0, "diffusion never decayed to an empty active set");
                Assert.AreEqual(0, front.AsReader().ReadCell(new int2(15, 15)));
                Assert.AreEqual(0, front.AsReader().ReadCell(new int2(16, 16)));
                Assert.AreEqual(0, front.AsReader().ReadCell(new int2(8, 8)));
            }
            finally
            {
                if (front.IsCreated) front.Dispose();
                if (back.IsCreated) back.Dispose();
            }
        }

        [Test]
        public void EvictionDrainsChunksAfterDiffusionDies()
        {
            var spec = GridSpec.FromPowerOfTwo(4, 4);
            var front = InfluenceField.Create(spec, Allocator.Persistent);
            var back = InfluenceField.Create(spec, Allocator.Persistent);

            const int decay = 400;
            const int spreadDenom = 5;

            try
            {
                var stamps = new NativeArray<Stamp>(
                    new[] { new Stamp(InfluenceShape.SolidRect(int2.zero, new int2(2, 2), 1000), new int2(8, 8)) },
                    Allocator.TempJob);
                front.Schedule(stamps, default).Complete();
                stamps.Dispose();

                for (var step = 0; step < 80; step++)
                {
                    back.Schedule(default, default, StencilOf(ref front, decay, spreadDenom)).Complete();
                    (front, back) = (back, front);
                }

                Assert.AreEqual(0, front.ActiveSlotCount);
                Assert.Greater(front.FreeSlotsList.Length + back.FreeSlotsList.Length, 0,
                    "no slots were reclaimed after the field died");
            }
            finally
            {
                if (front.IsCreated) front.Dispose();
                if (back.IsCreated) back.Dispose();
            }
        }
    }
}