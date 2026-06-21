using BovineLabs.Timeline.Grid.Influence.Data;
using NUnit.Framework;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Mathematics;

namespace BovineLabs.Timeline.Grid.Influence.Tests
{
    public unsafe class ReaderApiTests
    {
        // SampleBilinear at a cell centre (cell + 0.5) must equal the sharp ReadCell of that cell — the
        // smooth read degenerates to the integer read on grid points.
        [Test]
        public void SampleBilinearAtCellCentreMatchesReadCell()
        {
            var spec = GridSpec.FromPowerOfTwo(3, uint.MaxValue);
            var field = InfluenceField.Create(spec, Allocator.Persistent);
            var stamps = new NativeArray<Stamp>(
                new[] { new Stamp(InfluenceShape.SolidRect(int2.zero, new int2(2, 2), 9), new int2(1, 1)) },
                Allocator.TempJob);
            field.Schedule(stamps, default).Complete();

            var reader = field.AsReader();
            for (var y = -2; y <= 5; y++)
            for (var x = -2; x <= 5; x++)
            {
                var cell = new int2(x, y);
                var centre = new float2(x + 0.5f, y + 0.5f);
                Assert.AreEqual(reader.ReadCell(cell), reader.SampleBilinear(centre), 1e-4f, $"at {cell}");
            }

            stamps.Dispose();
            field.Dispose();
        }

        // Halfway between a filled (9) and an empty (0) cell, the smooth read is the average.
        [Test]
        public void SampleBilinearInterpolatesBetweenCells()
        {
            var spec = GridSpec.FromPowerOfTwo(3, uint.MaxValue);
            var field = InfluenceField.Create(spec, Allocator.Persistent);
            var stamps = new NativeArray<Stamp>(
                new[] { new Stamp(InfluenceShape.SolidRect(int2.zero, new int2(2, 2), 9), new int2(1, 1)) },
                Allocator.TempJob);
            field.Schedule(stamps, default).Complete();

            var reader = field.AsReader();
            Assert.AreEqual(9, reader.ReadCell(new int2(2, 1)));
            Assert.AreEqual(0, reader.ReadCell(new int2(3, 1)));
            // Midpoint between cell (2,1)=9 and (3,1)=0, no y interpolation.
            Assert.AreEqual(4.5f, reader.SampleBilinear(new float2(3.0f, 1.5f)), 1e-4f);

            stamps.Dispose();
            field.Dispose();
        }

        // ChunkView reads must agree with ReadCell across an active chunk (the editor snapshot relies on this).
        [Test]
        public void ChunkViewMatchesReadCell()
        {
            var spec = GridSpec.FromPowerOfTwo(3, uint.MaxValue);
            var field = InfluenceField.Create(spec, Allocator.Persistent);
            var stamps = new NativeArray<Stamp>(
                new[] { new Stamp(InfluenceShape.Disc(int2.zero, 5, 4), new int2(3, 3)) },
                Allocator.TempJob);
            field.Schedule(stamps, default).Complete();

            var reader = field.AsReader();
            var active = field.ActiveSlotsList;
            var coordBySlot = field.CoordBySlotList;
            Assert.Greater(active.Length, 0, "stamp produced no active chunks");

            for (var i = 0; i < active.Length; i++)
            {
                var coord = coordBySlot[active[i]];
                var baseCell = coord * spec.ChunkSize;
                Assert.IsTrue(reader.TryGetChunk(coord, out var view), $"chunk {coord} not resolvable");

                for (var ly = 0; ly < spec.ChunkSize; ly++)
                for (var lx = 0; lx < spec.ChunkSize; lx++)
                {
                    var cell = baseCell + new int2(lx, ly);
                    Assert.AreEqual(reader.ReadCell(cell), view.ReadLocal(new int2(lx, ly)), $"ReadLocal {cell}");
                    Assert.AreEqual(reader.ReadCell(cell), view.ReadWorld(cell), $"ReadWorld {cell}");
                }
            }

            stamps.Dispose();
            field.Dispose();
        }

        // Composite bounds are the union of the layer bounds; emitted span count is positive and within estimate.
        [Test]
        public void CompositeReaderBoundsAndEmit()
        {
            using var builder = new BlobBuilder(Allocator.Temp);
            ref var root = ref builder.ConstructRoot<CompositeShapeBlob>();
            var layers = builder.Allocate(ref root.Layers, 2);
            layers[0] = InfluenceShape.SolidRect(new int2(0, 0), new int2(3, 3), 1);
            layers[1] = InfluenceShape.SolidRect(new int2(5, 5), new int2(2, 2), 1);
            var blob = builder.CreateBlobAssetReference<CompositeShapeBlob>(Allocator.Temp);

            var bounds = CompositeShapeReader.Bounds(ref blob.Value, int2.zero);
            Assert.AreEqual(new int2(0, 0), bounds.Min);
            Assert.AreEqual(new int2(7, 7), bounds.Max);

            var capacity = CompositeShapeReader.EstimateSpanCount(ref blob.Value);
            Assert.Greater(capacity, 0);

            var buffer = new NativeArray<WeightedRect>(capacity, Allocator.Temp);
            var sink = new SpanSink((WeightedRect*)buffer.GetUnsafePtr(), capacity);
            CompositeShapeReader.Emit(ref blob.Value, int2.zero, ref sink);
            sink.SealRemaining();

            Assert.Greater(sink.Count, 0, "composite emitted no spans");
            Assert.LessOrEqual(sink.Count, capacity, "emitted more spans than estimated");

            buffer.Dispose();
            blob.Dispose();
        }
    }
}
