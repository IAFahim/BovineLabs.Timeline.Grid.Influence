using System;
using System.Threading;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Mathematics;

namespace BovineLabs.Timeline.Grid.Influence.Data
{
    public unsafe partial struct InfluenceField : INativeDisposable
    {
        private NativeFlatMap _slotByCoord;
        private NativeList<int2> _coordBySlot;
        private NativeList<uint> _lastWrittenBySlot;
        private NativeList<byte> _nonZeroBySlot;
        private NativeList<int> _freeSlots;
        private NativeList<int> _activeSlots;
        private NativeList<int> _data;
        private NativeList<Stamp> _extractedStamps;

        private NativeList<int> _offsets;
        private NativeList<WeightedRect> _spans;
        private NativeList<int> _stampCount;

        private NativeArray<Stamp> _emptyStamps;
        private NativeArray<int> _emptyInts;
        private NativeArray<int2> _emptyCoords;
        private NativeArray<byte> _emptyBytes;

        private GridSpec _spec;
        private Allocator _allocator;
        private JobHandle _dependency;

        public bool IsCreated => _data.IsCreated;
        public GridSpec Spec => _spec;
        public uint FrameId { get; private set; }

        public JobHandle Dependency => _dependency;
        public int ActiveSlotCount => _activeSlots.Length;

        public static InfluenceField Create(in GridSpec spec, Allocator allocator)
        {
            return new InfluenceField
            {
                _slotByCoord = NativeFlatMap.Create(64, allocator),
                _coordBySlot = new NativeList<int2>(64, allocator),
                _lastWrittenBySlot = new NativeList<uint>(64, allocator),
                _nonZeroBySlot = new NativeList<byte>(64, allocator),
                _freeSlots = new NativeList<int>(64, allocator),
                _activeSlots = new NativeList<int>(64, allocator),
                _data = new NativeList<int>(64 * spec.ElementsPerChunk, allocator),
                _extractedStamps = new NativeList<Stamp>(256, allocator),
                _offsets = new NativeList<int>(64, allocator),
                _spans = new NativeList<WeightedRect>(1024, allocator),
                _stampCount = new NativeList<int>(64, allocator),
                _emptyStamps = new NativeArray<Stamp>(0, allocator),
                _emptyInts = new NativeArray<int>(0, allocator),
                _emptyCoords = new NativeArray<int2>(0, allocator),
                _emptyBytes = new NativeArray<byte>(0, allocator),
                _spec = spec,
                FrameId = 1,
                _allocator = allocator,
                _dependency = default
            };
        }

        public void Complete()
        {
            _dependency.Complete();
            _dependency = default;
        }

        public FieldReader AsReader()
        {
            ThrowIfNotCreated();
            return new FieldReader(
                _slotByCoord.AsReadOnly(),
                _lastWrittenBySlot.AsArray(),
                _data.AsArray(),
                _spec,
                FrameId);
        }

        public struct StencilConfig
        {
            public bool IsActive;
            public NativeArray<int> ActiveSlots;
            public NativeArray<int2> CoordBySlot;
            public NativeArray<int> Data;
            public NativeArray<byte> NonZeroBySlot;
            public NativeFlatMap.ReadOnly SlotByCoord;
            public int DecayPerMille;
            public int SpreadDenominator;
        }

        public int CompleteAndRead(int2 cell)
        {
            Complete();
            return AsReader().ReadCell(cell);
        }

        public JobHandle Schedule(NativeParallelMultiHashMap<int, Stamp>.ReadOnly stampsMap, int slotIndex,
            JobHandle dependsOn, StencilConfig stencil = default)
        {
            ThrowIfNotCreated();
            var combined = JobHandle.CombineDependencies(_dependency, dependsOn);
            NormalizeStencil(ref stencil);

            var prepare = new PrepareSlotsFromMapJob
            {
                StampsMap = stampsMap,
                SlotIndex = slotIndex,
                ExtractedStamps = _extractedStamps,
                Helper = NewHelper(stencil)
            }.Schedule(combined);

            return ScheduleCore(prepare, _extractedStamps.AsDeferredJobArray(), stencil);
        }

        public JobHandle Schedule(NativeArray<Stamp> stamps, JobHandle dependsOn, StencilConfig stencil = default)
        {
            ThrowIfNotCreated();
            var combined = JobHandle.CombineDependencies(_dependency, dependsOn);
            NormalizeStencil(ref stencil);

            if (!stamps.IsCreated)
                stamps = _emptyStamps;

            var prepare = new PrepareSlotsFromArrayJob
            {
                Stamps = stamps,
                Helper = NewHelper(stencil)
            }.Schedule(combined);

            return ScheduleCore(prepare, stamps, stencil);
        }

        private JobHandle ScheduleCore(JobHandle prepare, NativeArray<Stamp> stamps, in StencilConfig stencil)
        {
            var rasterize = new RasterizeJob
            {
                StampCount = _stampCount.AsDeferredJobArray(),
                Stamps = stamps,
                Offsets = _offsets.AsDeferredJobArray(),
                Spans = _spans.AsDeferredJobArray()
            }.Schedule(_stampCount, 1, prepare);

            var clear = new ClearJob
            {
                ActiveSlots = _activeSlots.AsDeferredJobArray(),
                Data = _data.AsDeferredJobArray(),
                ElementsPerChunk = _spec.ElementsPerChunk
            }.Schedule(_activeSlots, 1, prepare);

            var scatter = new ScatterJob
            {
                StampCount = _stampCount.AsDeferredJobArray(),
                Offsets = _offsets.AsDeferredJobArray(),
                Spans = _spans.AsDeferredJobArray(),
                SlotByCoord = _slotByCoord.AsReadOnly(),
                Data = _data.AsDeferredJobArray(),
                Spec = _spec
            }.Schedule(_stampCount, 1, JobHandle.CombineDependencies(rasterize, clear));

            var resolve = new ResolveJob
            {
                ActiveSlots = _activeSlots.AsDeferredJobArray(),
                CoordBySlot = _coordBySlot.AsDeferredJobArray(),
                Data = _data.AsDeferredJobArray(),
                NonZeroBySlot = _nonZeroBySlot.AsDeferredJobArray(),
                Spec = _spec,
                HasStencil = stencil.IsActive,
                StencilData = stencil.Data,
                StencilSlotByCoord = stencil.SlotByCoord,
                DecayPerMille = stencil.DecayPerMille,
                SpreadDenominator = stencil.SpreadDenominator
            }.Schedule(_activeSlots, 1, scatter);

            _dependency = resolve;
            return resolve;
        }

        private PrepareSlotsHelper NewHelper(in StencilConfig stencil)
        {
            var resetFrame = FrameId == uint.MaxValue;
            FrameId = resetFrame ? 1u : FrameId + 1u;

            return new PrepareSlotsHelper
            {
                Offsets = _offsets,
                Spans = _spans,
                StampCount = _stampCount,
                SlotByCoord = _slotByCoord,
                FreeSlots = _freeSlots,
                CoordBySlot = _coordBySlot,
                LastWrittenBySlot = _lastWrittenBySlot,
                NonZeroBySlot = _nonZeroBySlot,
                ActiveSlots = _activeSlots,
                Data = _data,
                Spec = _spec,
                FrameId = FrameId,
                ResetFrame = resetFrame,
                RetentionFrames = _spec.RetentionFrames,
                HasStencil = stencil.IsActive,
                StencilActiveSlots = stencil.ActiveSlots,
                StencilCoordBySlot = stencil.CoordBySlot,
                StencilData = stencil.Data,
                StencilNonZeroBySlot = stencil.NonZeroBySlot,
                DecayPerMille = stencil.DecayPerMille,
                SpreadDenominator = stencil.SpreadDenominator
            };
        }

        private void NormalizeStencil(ref StencilConfig stencil)
        {
            if (stencil.IsActive)
                return;

            stencil.ActiveSlots = _emptyInts;
            stencil.CoordBySlot = _emptyCoords;
            stencil.Data = _emptyInts;
            stencil.NonZeroBySlot = _emptyBytes;
        }

        public void Dispose()
        {
            if (!IsCreated) return;
            Complete();
            _slotByCoord.Dispose();
            _coordBySlot.Dispose();
            _lastWrittenBySlot.Dispose();
            _nonZeroBySlot.Dispose();
            _freeSlots.Dispose();
            _activeSlots.Dispose();
            _data.Dispose();
            if (_extractedStamps.IsCreated) _extractedStamps.Dispose();
            _offsets.Dispose();
            _spans.Dispose();
            _stampCount.Dispose();
            _emptyStamps.Dispose();
            _emptyInts.Dispose();
            _emptyCoords.Dispose();
            _emptyBytes.Dispose();
            this = default;
        }

        public JobHandle Dispose(JobHandle inputDeps)
        {
            if (!IsCreated) return inputDeps;
            var handle = JobHandle.CombineDependencies(inputDeps, _dependency);
            handle = _slotByCoord.Dispose(handle);
            handle = _coordBySlot.Dispose(handle);
            handle = _lastWrittenBySlot.Dispose(handle);
            handle = _nonZeroBySlot.Dispose(handle);
            handle = _freeSlots.Dispose(handle);
            handle = _activeSlots.Dispose(handle);
            handle = _data.Dispose(handle);
            if (_extractedStamps.IsCreated) handle = _extractedStamps.Dispose(handle);
            handle = _offsets.Dispose(handle);
            handle = _spans.Dispose(handle);
            handle = _stampCount.Dispose(handle);
            handle = _emptyStamps.Dispose(handle);
            handle = _emptyInts.Dispose(handle);
            handle = _emptyCoords.Dispose(handle);
            handle = _emptyBytes.Dispose(handle);
            this = default;
            return handle;
        }

        private void ThrowIfNotCreated()
        {
            if (!_data.IsCreated) throw new ObjectDisposedException(nameof(InfluenceField));
        }

        [BurstCompile]
        private struct RasterizeJob : IJobParallelForDefer
        {
            [ReadOnly] public NativeArray<int> StampCount;
            [ReadOnly] public NativeArray<Stamp> Stamps;
            [ReadOnly] public NativeArray<int> Offsets;
            [NativeDisableParallelForRestriction] public NativeArray<WeightedRect> Spans;

            public void Execute(int index)
            {
                var sink = new SpanSink((WeightedRect*)Spans.GetUnsafePtr() + Offsets[index],
                    Offsets[index + 1] - Offsets[index]);
                Rasterizer.Emit(Stamps[index], ref sink);
                sink.SealRemaining();
            }
        }

        [BurstCompile]
        private struct ClearJob : IJobParallelForDefer
        {
            [ReadOnly] public NativeArray<int> ActiveSlots;
            [NativeDisableParallelForRestriction] public NativeArray<int> Data;
            public int ElementsPerChunk;

            public void Execute(int index)
            {
                var field = (int*)Data.GetUnsafePtr() + ActiveSlots[index] * ElementsPerChunk;
                UnsafeUtility.MemClear(field, (long)ElementsPerChunk * sizeof(int));
            }
        }

        [BurstCompile]
        private struct ScatterJob : IJobParallelForDefer
        {
            [ReadOnly] public NativeArray<int> StampCount;
            [ReadOnly] public NativeArray<int> Offsets;
            [ReadOnly] public NativeArray<WeightedRect> Spans;
            [ReadOnly] public NativeFlatMap.ReadOnly SlotByCoord;
            [NativeDisableParallelForRestriction] public NativeArray<int> Data;
            public GridSpec Spec;

            public void Execute(int index)
            {
                var data = (int*)Data.GetUnsafePtr();
                var log2 = Spec.Log2;
                var chunkSize = Spec.ChunkSize;
                var stride = Spec.Stride;
                var elements = Spec.ElementsPerChunk;

                var startSpan = Offsets[index];
                var endSpan = Offsets[index + 1];

                for (var i = startSpan; i < endSpan; i++)
                {
                    var span = Spans[i];
                    if (span.IsEmpty) continue;

                    var bounds = span.Bounds;
                    var weight = span.Weight;
                    var chunks = ChunkMath.ChunkRangeOf(bounds, log2);

                    for (var cy = chunks.Min.y; cy <= chunks.Max.y; cy++)
                    for (var cx = chunks.Min.x; cx <= chunks.Max.x; cx++)
                    {
                        if (!SlotByCoord.TryGetValue(new int2(cx, cy), out var slot)) continue;

                        var baseX = cx << log2;
                        var baseY = cy << log2;
                        var lx0 = math.max(bounds.Min.x, baseX) - baseX;
                        var ly0 = math.max(bounds.Min.y, baseY) - baseY;
                        var lx1 = math.min(bounds.Max.x, baseX + chunkSize) - baseX;
                        var ly1 = math.min(bounds.Max.y, baseY + chunkSize) - baseY;

                        if (lx0 >= lx1 || ly0 >= ly1) continue;

                        var field = data + slot * elements;
                        Interlocked.Add(ref field[ly0 * stride + lx0], weight);
                        Interlocked.Add(ref field[ly0 * stride + lx1], -weight);
                        Interlocked.Add(ref field[ly1 * stride + lx0], -weight);
                        Interlocked.Add(ref field[ly1 * stride + lx1], weight);
                    }
                }
            }
        }

        [BurstCompile]
        private struct ResolveJob : IJobParallelForDefer
        {
            [ReadOnly] public NativeArray<int> ActiveSlots;
            [ReadOnly] public NativeArray<int2> CoordBySlot;
            [NativeDisableParallelForRestriction] public NativeArray<int> Data;
            [NativeDisableParallelForRestriction] public NativeArray<byte> NonZeroBySlot;
            public GridSpec Spec;

            public bool HasStencil;
            [ReadOnly] public NativeArray<int> StencilData;
            public NativeFlatMap.ReadOnly StencilSlotByCoord;
            public int DecayPerMille;
            public int SpreadDenominator;

            public void Execute(int index)
            {
                var slot = ActiveSlots[index];
                var field = (int*)Data.GetUnsafePtr() + slot * Spec.ElementsPerChunk;

                PrefixSum.Run(field, Spec);

                if (HasStencil) ApplyStencil(CoordBySlot[slot], field);

                NonZeroBySlot[slot] = AnyNonZero(field);
            }

            private byte AnyNonZero(int* field)
            {
                var chunkSize = Spec.ChunkSize;
                var stride = Spec.Stride;
                var acc = 0;

                for (var y = 0; y < chunkSize; y++)
                {
                    var row = field + y * stride;
                    for (var x = 0; x < chunkSize; x++) acc |= row[x];
                }

                return acc != 0 ? (byte)1 : (byte)0;
            }

            private void ApplyStencil(int2 coord, int* field)
            {
                var chunkSize = Spec.ChunkSize;
                var stride = Spec.Stride;
                var haloStride = chunkSize + 2;

                var outflowHalo = new NativeArray<int>(haloStride * haloStride, Allocator.Temp);
                var halo = (int*)outflowHalo.GetUnsafePtr();

                if (StencilSlotByCoord.TryGetValue(coord, out var selfSlot))
                {
                    var self = (int*)StencilData.GetUnsafeReadOnlyPtr() + selfSlot * Spec.ElementsPerChunk;
                    for (var y = 0; y < chunkSize; y++)
                    {
                        var source = self + y * stride;
                        var haloRow = halo + (y + 1) * haloStride + 1;
                        var target = field + y * stride;
                        for (var x = 0; x < chunkSize; x++)
                        {
                            var kept = IntegerMath.DecayKeep(source[x], DecayPerMille);
                            var spread = kept / SpreadDenominator;
                            haloRow[x] = spread;
                            target[x] += kept - 4 * spread;
                        }
                    }
                }

                FillHaloColumn(new int2(coord.x - 1, coord.y), chunkSize - 1, 0, halo, haloStride);
                FillHaloColumn(new int2(coord.x + 1, coord.y), 0, chunkSize + 1, halo, haloStride);
                FillHaloRow(new int2(coord.x, coord.y - 1), chunkSize - 1, 0, halo, haloStride);
                FillHaloRow(new int2(coord.x, coord.y + 1), 0, chunkSize + 1, halo, haloStride);

                for (var y = 0; y < chunkSize; y++)
                {
                    var target = field + y * stride;
                    var center = halo + (y + 1) * haloStride + 1;
                    var below = center - haloStride;
                    var above = center + haloStride;
                    for (var x = 0; x < chunkSize; x++)
                        target[x] += center[x - 1] + center[x + 1] + below[x] + above[x];
                }

                outflowHalo.Dispose();
            }

            private void FillHaloColumn(int2 coord, int sourceX, int haloX, int* halo, int haloStride)
            {
                if (!StencilSlotByCoord.TryGetValue(coord, out var slot)) return;

                var source = (int*)StencilData.GetUnsafeReadOnlyPtr() + slot * Spec.ElementsPerChunk;
                for (var y = 0; y < Spec.ChunkSize; y++)
                    halo[(y + 1) * haloStride + haloX] =
                        IntegerMath.Outflow(source[y * Spec.Stride + sourceX], DecayPerMille, SpreadDenominator);
            }

            private void FillHaloRow(int2 coord, int sourceY, int haloY, int* halo, int haloStride)
            {
                if (!StencilSlotByCoord.TryGetValue(coord, out var slot)) return;

                var source = (int*)StencilData.GetUnsafeReadOnlyPtr() + slot * Spec.ElementsPerChunk +
                             sourceY * Spec.Stride;
                for (var x = 0; x < Spec.ChunkSize; x++)
                    halo[haloY * haloStride + (x + 1)] =
                        IntegerMath.Outflow(source[x], DecayPerMille, SpreadDenominator);
            }
        }
    }
}
