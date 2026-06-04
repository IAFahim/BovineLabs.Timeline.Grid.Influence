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
                var retained = 0;
                if (self != 0)
                {
                    var vp = self - (int)((long)self * decay / 1000);
                    if (vp != 0) retained = vp - 4 * (vp / spreadDenom);
                }

                back[x, y] = retained + incoming;
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
            var v = grid[x, y];
            if (v == 0) return 0;
            var vp = v - (int)((long)v * decay / 1000);
            return vp == 0 ? 0 : vp / denom;
        }

        [Test]
        public void IntegratedStencil_SpreadsAcrossChunkBoundaries_IdenticalToOracle()
        {
            var chunkPower = 4; // 16x16
            var spec = GridSpec.FromPowerOfTwo(chunkPower, uint.MaxValue);

            var front = InfluenceField.Create(spec, Allocator.Persistent);
            var back = InfluenceField.Create(spec, Allocator.Persistent);

            // Central stamp that borders chunk edge
            var initialStamps = new[]
            {
                new Stamp(InfluenceShape.SolidRect(int2.zero, new int2(2, 2), 1000),
                    new int2(15, 15)) // straddles chunk 0,0 and 1,1
            };

            // Box enclosing area we care about: 4x4 chunks = 64x64 cells
            var boxMin = new int2(-16, -16);
            var boxSize = new int2(64, 64);

            var oracleGrid = new int[boxSize.x, boxSize.y];

            var steps = 10;
            var decay = 30;
            var spreadDenom = 5;

            var array = new NativeArray<Stamp>(initialStamps, Allocator.TempJob);
            front.Schedule(array, default).Complete();
            array.Dispose();

            oracleGrid = StepOracle(oracleGrid, initialStamps, boxMin, decay, spreadDenom);

            for (var step = 0; step < steps; step++)
            {
                // Verify Front == Oracle before step
                var reader = front.AsReader();
                for (var x = 0; x < boxSize.x; x++)
                for (var y = 0; y < boxSize.y; y++)
                {
                    var cell = new int2(boxMin.x + x, boxMin.y + y);
                    var actual = reader.ReadCell(cell);
                    var expected = oracleGrid[x, y];
                    Assert.AreEqual(expected, actual, $"Mismatch at step {step}, cell {cell}");
                }

                // Advance
                var stencil = new InfluenceField.StencilConfig
                {
                    IsActive = true,
                    ActiveSlots = front.ActiveSlotsDeferred,
                    CoordBySlot = front.CoordBySlotDeferred,
                    Data = front.DataDeferred,
                    SlotByCoord = front.SlotByCoordReadOnly,
                    DecayPerMille = decay,
                    SpreadDenominator = spreadDenom
                };

                // Empty stamps for subsequent steps
                var emptyStamps = new NativeArray<Stamp>(0, Allocator.TempJob);
                back.Schedule(emptyStamps, default, stencil).Complete();
                emptyStamps.Dispose();

                oracleGrid = StepOracle(oracleGrid, new Stamp[0], boxMin, decay, spreadDenom);

                // Swap
                var temp = front;
                front = back;
                back = temp;
            }

            front.Dispose();
            back.Dispose();
        }
    }
}