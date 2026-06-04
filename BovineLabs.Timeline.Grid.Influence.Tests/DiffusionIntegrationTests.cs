using BovineLabs.Timeline.Grid.Influence.Data;
using NUnit.Framework;
using Unity.Collections;
using Unity.Mathematics;

namespace BovineLabs.Timeline.Grid.Influence.Tests
{
    public class DiffusionIntegrationTests
    {
        static int[,] StepOracle(int[,] front, Stamp[] stamps, int2 boxMin, int decay, int spreadDenom)
        {
            int w = front.GetLength(0);
            int h = front.GetLength(1);
            int[,] back = new int[w, h];
            
            for (int x = 0; x < w; x++)
            {
                for (int y = 0; y < h; y++)
                {
                    int self = front[x, y];
                    int incoming = Spread(front, x - 1, y, decay, spreadDenom) +
                                   Spread(front, x + 1, y, decay, spreadDenom) +
                                   Spread(front, x, y - 1, decay, spreadDenom) +
                                   Spread(front, x, y + 1, decay, spreadDenom);
                    int retained = 0;
                    if (self != 0)
                    {
                        int vp = self - (int)((long)self * decay / 1000);
                        if (vp != 0)
                        {
                            retained = vp - 4 * (vp / spreadDenom);
                        }
                    }
                    back[x, y] = retained + incoming;
                }
            }

            for (int s = 0; s < stamps.Length; s++)
            {
                for (int x = 0; x < w; x++)
                {
                    for (int y = 0; y < h; y++)
                    {
                        back[x, y] += (int)InfluenceTestHarness.Contribution(stamps[s].Shape, stamps[s].Origin, new int2(boxMin.x + x, boxMin.y + y));
                    }
                }
            }
            return back;
        }

        static int Spread(int[,] grid, int x, int y, int decay, int denom)
        {
            if (x < 0 || y < 0 || x >= grid.GetLength(0) || y >= grid.GetLength(1)) return 0;
            int v = grid[x, y];
            if (v == 0) return 0;
            int vp = v - (int)((long)v * decay / 1000);
            return vp == 0 ? 0 : vp / denom;
        }

        [Test]
        public void IntegratedStencil_SpreadsAcrossChunkBoundaries_IdenticalToOracle()
        {
            int chunkPower = 4; // 16x16
            var spec = GridSpec.FromPowerOfTwo(chunkPower, uint.MaxValue);
            
            var front = InfluenceField.Create(spec, Allocator.Persistent);
            var back = InfluenceField.Create(spec, Allocator.Persistent);
            
            // Central stamp that borders chunk edge
            var initialStamps = new Stamp[]
            {
                new Stamp(InfluenceShape.SolidRect(int2.zero, new int2(2, 2), 1000), new int2(15, 15)) // straddles chunk 0,0 and 1,1
            };
            
            // Box enclosing area we care about: 4x4 chunks = 64x64 cells
            int2 boxMin = new int2(-16, -16);
            int2 boxSize = new int2(64, 64);
            
            int[,] oracleGrid = new int[boxSize.x, boxSize.y];
            
            int steps = 10;
            int decay = 30;
            int spreadDenom = 5;

            var array = new NativeArray<Stamp>(initialStamps, Allocator.TempJob);
            front.Schedule(array, default).Complete();
            array.Dispose();
            
            oracleGrid = StepOracle(oracleGrid, initialStamps, boxMin, decay, spreadDenom);

            for (int step = 0; step < steps; step++)
            {
                // Verify Front == Oracle before step
                var reader = front.AsReader();
                for (int x = 0; x < boxSize.x; x++)
                {
                    for (int y = 0; y < boxSize.y; y++)
                    {
                        int2 cell = new int2(boxMin.x + x, boxMin.y + y);
                        int actual = reader.ReadCell(cell);
                        int expected = oracleGrid[x, y];
                        Assert.AreEqual(expected, actual, $"Mismatch at step {step}, cell {cell}");
                    }
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
