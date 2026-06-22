using BovineLabs.Timeline.Grid.Influence.Data;
using BovineLabs.Timeline.Grid.Influence.Data.Flows;
using NUnit.Framework;
using Unity.Collections;
using Unity.Mathematics;

namespace BovineLabs.Timeline.Grid.Influence.Tests
{
    public class FlowFieldTests
    {
        [Test]
        public void BakedCacheMatchesInlineAscentOverActiveChunks()
        {
            var spec = GridSpec.FromPowerOfTwo(3, uint.MaxValue);
            var field = InfluenceField.Create(spec, Allocator.Persistent);
            var stamps = new NativeArray<Stamp>(
                new[] { new Stamp(InfluenceShape.Disc(int2.zero, 6, 7), new int2(3, 3)) },
                Allocator.TempJob);
            field.Schedule(stamps, default).Complete();

            var flow = FlowField.Create(Allocator.Persistent);
            flow.Resolve(ref field, default).Complete();

            var fieldReader = field.AsReader();
            var flowReader = flow.AsReader(ref field);

            var sawNonZero = false;
            var active = field.ActiveSlotsList;
            var coordBySlot = field.CoordBySlotList;
            for (var i = 0; i < active.Length; i++)
            {
                var slot = active[i];
                var baseCell = coordBySlot[slot] * spec.ChunkSize;
                for (var ly = 0; ly < spec.ChunkSize; ly++)
                for (var lx = 0; lx < spec.ChunkSize; lx++)
                {
                    var cell = baseCell + new int2(lx, ly);
                    var expected = FieldGradient.Ascent(fieldReader, cell);
                    var actual = flowReader.Sample(cell);
                    Assert.AreEqual(expected, actual, $"flow cache mismatch at {cell}");
                    sawNonZero |= math.any(expected != int2.zero);
                }
            }

            Assert.IsTrue(sawNonZero, "test is vacuous: stamp produced no gradient");

            stamps.Dispose();
            flow.Dispose();
            field.Dispose();
        }

        [Test]
        public void SampleReturnsZeroForUnwrittenChunk()
        {
            var spec = GridSpec.FromPowerOfTwo(2, uint.MaxValue);
            var field = InfluenceField.Create(spec, Allocator.Persistent);
            var stamps = new NativeArray<Stamp>(
                new[] { new Stamp(InfluenceShape.SolidRect(int2.zero, new int2(2, 2), 9), new int2(1, 1)) },
                Allocator.TempJob);
            field.Schedule(stamps, default).Complete();

            var flow = FlowField.Create(Allocator.Persistent);
            flow.Resolve(ref field, default).Complete();

            Assert.AreEqual(int2.zero, flow.AsReader(ref field).Sample(new int2(10_000, 10_000)));

            stamps.Dispose();
            flow.Dispose();
            field.Dispose();
        }
    }
}