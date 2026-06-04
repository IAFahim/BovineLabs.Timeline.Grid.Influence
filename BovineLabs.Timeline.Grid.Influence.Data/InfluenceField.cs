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
        private NativeList<int> _freeSlots;
        private NativeList<int> _activeSlots;
        private NativeList<int> _data;
        private NativeList<Stamp> _extractedStamps;

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
                _freeSlots = new NativeList<int>(64, allocator),
                _activeSlots = new NativeList<int>(64, allocator),
                _data = new NativeList<int>(64 * spec.ElementsPerChunk, allocator),
                _extractedStamps = new NativeList<Stamp>(256, allocator),
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

            var resetFrame = false;
            if (FrameId == uint.MaxValue)
            {
                resetFrame = true;
                FrameId = 1;
            }
            else
            {
                FrameId++;
            }

            var disposeStencil = false;
            if (!stencil.IsActive)
            {
                disposeStencil = true;
                stencil.ActiveSlots = new NativeArray<int>(0, Allocator.TempJob);
                stencil.CoordBySlot = new NativeArray<int2>(0, Allocator.TempJob);
                stencil.Data = new NativeArray<int>(0, Allocator.TempJob);
            }

            var offsets = new NativeList<int>(1, Allocator.TempJob);
            var spans = new NativeList<WeightedRect>(1, Allocator.TempJob);
            var stampCount = new NativeList<int>(1, Allocator.TempJob);

            var prepare = new PrepareSlotsFromMapJob
            {
                StampsMap = stampsMap,
                SlotIndex = slotIndex,
                ExtractedStamps = _extractedStamps,
                Helper = new PrepareSlotsHelper
                {
                    Offsets = offsets,
                    Spans = spans,
                    StampCount = stampCount,
                    SlotByCoord = _slotByCoord,
                    FreeSlots = _freeSlots,
                    CoordBySlot = _coordBySlot,
                    LastWrittenBySlot = _lastWrittenBySlot,
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
                    DecayPerMille = stencil.DecayPerMille,
                    SpreadDenominator = stencil.SpreadDenominator
                }
            }.Schedule(combined);

            var rasterize = new RasterizeJob
            {
                StampCount = stampCount.AsDeferredJobArray(),
                Stamps = _extractedStamps.AsDeferredJobArray(),
                Offsets = offsets.AsDeferredJobArray(),
                Spans = spans.AsDeferredJobArray()
            }.Schedule(stampCount, 1, prepare);

            var clear = new ClearJob
            {
                ActiveSlots = _activeSlots.AsDeferredJobArray(),
                Data = _data.AsDeferredJobArray(),
                ElementsPerChunk = _spec.ElementsPerChunk
            }.Schedule(_activeSlots, 1, prepare);

            var scatter = new ScatterJob
            {
                StampCount = stampCount.AsDeferredJobArray(),
                Offsets = offsets.AsDeferredJobArray(),
                Spans = spans.AsDeferredJobArray(),
                SlotByCoord = _slotByCoord.AsReadOnly(),
                Data = _data.AsDeferredJobArray(),
                Spec = _spec
            }.Schedule(stampCount, 1, JobHandle.CombineDependencies(rasterize, clear));

            var resolve = new ResolveJob
            {
                ActiveSlots = _activeSlots.AsDeferredJobArray(),
                CoordBySlot = _coordBySlot.AsDeferredJobArray(),
                Data = _data.AsDeferredJobArray(),
                Spec = _spec,
                HasStencil = stencil.IsActive,
                StencilData = stencil.Data,
                StencilSlotByCoord = stencil.SlotByCoord,
                DecayPerMille = stencil.DecayPerMille,
                SpreadDenominator = stencil.SpreadDenominator
            }.Schedule(_activeSlots, 1, scatter);

            var cleanup = JobHandle.CombineDependencies(spans.Dispose(resolve), offsets.Dispose(resolve));
            cleanup = JobHandle.CombineDependencies(cleanup, stampCount.Dispose(resolve));
            if (disposeStencil)
            {
                cleanup = stencil.ActiveSlots.Dispose(cleanup);
                cleanup = stencil.CoordBySlot.Dispose(cleanup);
                cleanup = stencil.Data.Dispose(cleanup);
            }

            _dependency = cleanup;
            return cleanup;
        }

        public JobHandle Schedule(NativeArray<Stamp> stamps, JobHandle dependsOn, StencilConfig stencil = default)
        {
            ThrowIfNotCreated();
            var combined = JobHandle.CombineDependencies(_dependency, dependsOn);

            var resetFrame = false;
            if (FrameId == uint.MaxValue)
            {
                resetFrame = true;
                FrameId = 1;
            }
            else
            {
                FrameId++;
            }

            var disposeStamps = false;
            if (!stamps.IsCreated)
            {
                stamps = new NativeArray<Stamp>(0, Allocator.TempJob);
                disposeStamps = true;
            }

            var disposeStencil = false;
            if (!stencil.IsActive)
            {
                disposeStencil = true;
                stencil.ActiveSlots = new NativeArray<int>(0, Allocator.TempJob);
                stencil.CoordBySlot = new NativeArray<int2>(0, Allocator.TempJob);
                stencil.Data = new NativeArray<int>(0, Allocator.TempJob);
            }

            var offsets = new NativeList<int>(1, Allocator.TempJob);
            var spans = new NativeList<WeightedRect>(1, Allocator.TempJob);
            var stampCount = new NativeList<int>(1, Allocator.TempJob);

            var localStamps = stamps;

            var prepare = new PrepareSlotsFromArrayJob
            {
                Stamps = localStamps,
                Helper = new PrepareSlotsHelper
                {
                    Offsets = offsets,
                    Spans = spans,
                    StampCount = stampCount,
                    SlotByCoord = _slotByCoord,
                    FreeSlots = _freeSlots,
                    CoordBySlot = _coordBySlot,
                    LastWrittenBySlot = _lastWrittenBySlot,
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
                    DecayPerMille = stencil.DecayPerMille,
                    SpreadDenominator = stencil.SpreadDenominator
                }
            }.Schedule(combined);

            var rasterize = new RasterizeJob
            {
                StampCount = stampCount.AsDeferredJobArray(),
                Stamps = localStamps,
                Offsets = offsets.AsDeferredJobArray(),
                Spans = spans.AsDeferredJobArray()
            }.Schedule(stampCount, 1, prepare);

            var clear = new ClearJob
            {
                ActiveSlots = _activeSlots.AsDeferredJobArray(),
                Data = _data.AsDeferredJobArray(),
                ElementsPerChunk = _spec.ElementsPerChunk
            }.Schedule(_activeSlots, 1, prepare);

            var scatter = new ScatterJob
            {
                StampCount = stampCount.AsDeferredJobArray(),
                Offsets = offsets.AsDeferredJobArray(),
                Spans = spans.AsDeferredJobArray(),
                SlotByCoord = _slotByCoord.AsReadOnly(),
                Data = _data.AsDeferredJobArray(),
                Spec = _spec
            }.Schedule(stampCount, 1, JobHandle.CombineDependencies(rasterize, clear));

            var resolve = new ResolveJob
            {
                ActiveSlots = _activeSlots.AsDeferredJobArray(),
                CoordBySlot = _coordBySlot.AsDeferredJobArray(),
                Data = _data.AsDeferredJobArray(),
                Spec = _spec,
                HasStencil = stencil.IsActive,
                StencilData = stencil.Data,
                StencilSlotByCoord = stencil.SlotByCoord,
                DecayPerMille = stencil.DecayPerMille,
                SpreadDenominator = stencil.SpreadDenominator
            }.Schedule(_activeSlots, 1, scatter);

            var cleanup = JobHandle.CombineDependencies(spans.Dispose(resolve), offsets.Dispose(resolve));
            cleanup = JobHandle.CombineDependencies(cleanup, stampCount.Dispose(resolve));
            if (disposeStamps) cleanup = localStamps.Dispose(cleanup);
            if (disposeStencil)
            {
                cleanup = stencil.ActiveSlots.Dispose(cleanup);
                cleanup = stencil.CoordBySlot.Dispose(cleanup);
                cleanup = stencil.Data.Dispose(cleanup);
            }

            _dependency = cleanup;
            return cleanup;
        }

        public void Dispose()
        {
            if (!IsCreated) return;
            Complete();
            _slotByCoord.Dispose();
            _coordBySlot.Dispose();
            _lastWrittenBySlot.Dispose();
            _freeSlots.Dispose();
            _activeSlots.Dispose();
            _data.Dispose();
            if (_extractedStamps.IsCreated) _extractedStamps.Dispose();
            this = default;
        }

        public JobHandle Dispose(JobHandle inputDeps)
        {
            if (!IsCreated) return inputDeps;
            var handle = JobHandle.CombineDependencies(inputDeps, _dependency);
            handle = _slotByCoord.Dispose(handle);
            handle = _coordBySlot.Dispose(handle);
            handle = _lastWrittenBySlot.Dispose(handle);
            handle = _freeSlots.Dispose(handle);
            handle = _activeSlots.Dispose(handle);
            handle = _data.Dispose(handle);
            if (_extractedStamps.IsCreated) handle = _extractedStamps.Dispose(handle);
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
            public GridSpec Spec;

            public bool HasStencil;
            [ReadOnly] public NativeArray<int> StencilData;
            public NativeFlatMap.ReadOnly StencilSlotByCoord;
            public int DecayPerMille;
            public int SpreadDenominator;

            public void Execute(int index)
            {
                var slot = ActiveSlots[index];
                var coord = CoordBySlot[slot];
                var baseIndex = slot * Spec.ElementsPerChunk;
                var field = (int*)Data.GetUnsafePtr() + baseIndex;

                PrefixSum.Run(field, Spec);

                if (HasStencil) ApplyStencil(coord, field);
            }

            private void ApplyStencil(int2 coord, int* field)
            {
                var chunkSize = Spec.ChunkSize;
                var stride = Spec.Stride;

                var stencilSelfBase = -1;
                if (StencilSlotByCoord.TryGetValue(coord, out var stencilSelfSlot))
                    stencilSelfBase = stencilSelfSlot * Spec.ElementsPerChunk;

                var stencilPtr = (int*)StencilData.GetUnsafeReadOnlyPtr();

                for (var y = 0; y < chunkSize; y++)
                for (var x = 0; x < chunkSize; x++)
                {
                    var self = 0;
                    if (stencilSelfBase >= 0) self = stencilPtr[stencilSelfBase + y * stride + x];

                    var incoming =
                        SpreadAt(coord, x - 1, y, stride, stencilPtr) +
                        SpreadAt(coord, x + 1, y, stride, stencilPtr) +
                        SpreadAt(coord, x, y - 1, stride, stencilPtr) +
                        SpreadAt(coord, x, y + 1, stride, stencilPtr);

                    var retained = 0;
                    if (self != 0)
                    {
                        var vp = self - (int)((long)self * DecayPerMille / 1000);
                        if (vp != 0)
                        {
                            var q = vp / SpreadDenominator;
                            retained = vp - 4 * q;
                        }
                    }

                    field[y * stride + x] += retained + incoming;
                }
            }

            private int SpreadAt(int2 chunkCoord, int localX, int localY, int stride, int* stencilPtr)
            {
                var chunkSize = Spec.ChunkSize;
                var coord = chunkCoord;

                if ((uint)localX < (uint)chunkSize && (uint)localY < (uint)chunkSize)
                {
                    if (StencilSlotByCoord.TryGetValue(coord, out var s))
                    {
                        var v = stencilPtr[s * Spec.ElementsPerChunk + localY * stride + localX];
                        if (v == 0) return 0;
                        var vp = v - (int)((long)v * DecayPerMille / 1000);
                        return vp == 0 ? 0 : vp / SpreadDenominator;
                    }

                    return 0;
                }

                if (localX < 0)
                {
                    coord.x -= 1;
                    localX += chunkSize;
                }
                else if (localX >= chunkSize)
                {
                    coord.x += 1;
                    localX -= chunkSize;
                }

                if (localY < 0)
                {
                    coord.y -= 1;
                    localY += chunkSize;
                }
                else if (localY >= chunkSize)
                {
                    coord.y += 1;
                    localY -= chunkSize;
                }

                if (StencilSlotByCoord.TryGetValue(coord, out var slot))
                {
                    var v = stencilPtr[slot * Spec.ElementsPerChunk + localY * stride + localX];
                    if (v == 0) return 0;
                    var vp = v - (int)((long)v * DecayPerMille / 1000);
                    return vp == 0 ? 0 : vp / SpreadDenominator;
                }

                return 0;
            }
        }
    }
}