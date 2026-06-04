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
        NativeFlatMap _slotByCoord;
        NativeList<int2> _coordBySlot;
        NativeList<uint> _lastWrittenBySlot;
        NativeList<int> _freeSlots;
        NativeList<int> _activeSlots;
        NativeList<int> _data;

        GridSpec _spec;
        uint _frameId;
        Allocator _allocator;
        JobHandle _dependency;

        public bool IsCreated => _data.IsCreated;
        public GridSpec Spec => _spec;
        public uint FrameId => _frameId;
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
                _spec = spec,
                _frameId = 1,
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
                (int*)_data.GetUnsafePtr(),
                _spec,
                _frameId);
        }

        public int CompleteAndRead(int2 cell)
        {
            Complete();
            return AsReader().ReadCell(cell);
        }

        public JobHandle Schedule(NativeArray<Stamp> stamps, JobHandle dependsOn)
        {
            ThrowIfNotCreated();
            JobHandle combined = JobHandle.CombineDependencies(_dependency, dependsOn);

            bool resetFrame = false;
            if (_frameId == uint.MaxValue)
            {
                resetFrame = true;
                _frameId = 1;
            }
            else
            {
                _frameId++;
            }

            bool disposeStamps = false;
            if (!stamps.IsCreated)
            {
                stamps = new NativeArray<Stamp>(0, Allocator.TempJob);
                disposeStamps = true;
            }

            NativeList<int> offsets = new NativeList<int>(1, Allocator.TempJob);
            NativeList<WeightedRect> spans = new NativeList<WeightedRect>(1, Allocator.TempJob);
            NativeList<int> stampCount = new NativeList<int>(1, Allocator.TempJob);

            JobHandle prepare = new PrepareSlotsAndOffsetsJob
            {
                Stamps = stamps,
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
                FrameId = _frameId,
                ResetFrame = resetFrame,
                RetentionFrames = _spec.RetentionFrames
            }.Schedule(combined);

            JobHandle rasterize = new RasterizeJob
            {
                StampCount = stampCount.AsDeferredJobArray(),
                Stamps = stamps,
                Offsets = offsets.AsDeferredJobArray(),
                Spans = spans.AsDeferredJobArray()
            }.Schedule(stampCount, 1, prepare);

            JobHandle clear = new ClearJob
            {
                ActiveSlots = _activeSlots.AsDeferredJobArray(),
                Data = _data.AsDeferredJobArray(),
                ElementsPerChunk = _spec.ElementsPerChunk
            }.Schedule(_activeSlots, 1, prepare);

            JobHandle scatter = new ScatterJob
            {
                StampCount = stampCount.AsDeferredJobArray(),
                Offsets = offsets.AsDeferredJobArray(),
                Spans = spans.AsDeferredJobArray(),
                SlotByCoord = _slotByCoord.AsReadOnly(),
                Data = _data.AsDeferredJobArray(),
                Spec = _spec
            }.Schedule(stampCount, 1, JobHandle.CombineDependencies(rasterize, clear));

            JobHandle resolve = new ResolveJob
            {
                ActiveSlots = _activeSlots.AsDeferredJobArray(),
                Data = _data.AsDeferredJobArray(),
                Spec = _spec
            }.Schedule(_activeSlots, 1, scatter);

            JobHandle cleanup = JobHandle.CombineDependencies(spans.Dispose(resolve), offsets.Dispose(resolve));
            cleanup = JobHandle.CombineDependencies(cleanup, stampCount.Dispose(resolve));
            if (disposeStamps)
            {
                cleanup = stamps.Dispose(cleanup);
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
            this = default;
        }

        public JobHandle Dispose(JobHandle inputDeps)
        {
            if (!IsCreated) return inputDeps;
            JobHandle handle = JobHandle.CombineDependencies(inputDeps, _dependency);
            handle = _slotByCoord.Dispose(handle);
            handle = _coordBySlot.Dispose(handle);
            handle = _lastWrittenBySlot.Dispose(handle);
            handle = _freeSlots.Dispose(handle);
            handle = _activeSlots.Dispose(handle);
            handle = _data.Dispose(handle);
            this = default;
            return handle;
        }

        void AdvanceFrame()
        {
            if (_frameId == uint.MaxValue)
            {
                for (int i = 0; i < _lastWrittenBySlot.Length; i++) _lastWrittenBySlot[i] = 0;
                _frameId = 1;
                return;
            }
            _frameId++;
        }

        void EvictStale()
        {
            if (_spec.RetentionFrames == uint.MaxValue) return;
            for (int slot = 0; slot < _lastWrittenBySlot.Length; slot++)
            {
                uint last = _lastWrittenBySlot[slot];
                if (last == 0 || _frameId - last <= _spec.RetentionFrames) continue;
                _slotByCoord.Remove(_coordBySlot[slot]);
                _lastWrittenBySlot[slot] = 0;
                _freeSlots.Add(slot);
            }
        }

        void ThrowIfNotCreated()
        {
            if (!_data.IsCreated) throw new ObjectDisposedException(nameof(InfluenceField));
        }

        [BurstCompile]
        struct RasterizeJob : IJobParallelForDefer
        {
            [ReadOnly] public NativeArray<int> StampCount;
            [ReadOnly] public NativeArray<Stamp> Stamps;
            [ReadOnly] public NativeArray<int> Offsets;
            [NativeDisableParallelForRestriction] public NativeArray<WeightedRect> Spans;

            public void Execute(int index)
            {
                SpanSink sink = new SpanSink((WeightedRect*)Spans.GetUnsafePtr() + Offsets[index], Offsets[index + 1] - Offsets[index]);
                Rasterizer.Emit(Stamps[index], ref sink);
                sink.SealRemaining();
            }
        }

        [BurstCompile]
        struct ClearJob : IJobParallelForDefer
        {
            [ReadOnly] public NativeArray<int> ActiveSlots;
            [NativeDisableParallelForRestriction] public NativeArray<int> Data;
            public int ElementsPerChunk;

            public void Execute(int index)
            {
                int* field = (int*)Data.GetUnsafePtr() + ActiveSlots[index] * ElementsPerChunk;
                UnsafeUtility.MemClear(field, (long)ElementsPerChunk * sizeof(int));
            }
        }

        [BurstCompile]
        struct ScatterJob : IJobParallelForDefer
        {
            [ReadOnly] public NativeArray<int> StampCount;
            [ReadOnly] public NativeArray<int> Offsets;
            [ReadOnly] public NativeArray<WeightedRect> Spans;
            [ReadOnly] public NativeFlatMap.ReadOnly SlotByCoord;
            [NativeDisableParallelForRestriction] public NativeArray<int> Data;
            public GridSpec Spec;

            public void Execute(int index)
            {
                int* data = (int*)Data.GetUnsafePtr();
                int log2 = Spec.Log2;
                int chunkSize = Spec.ChunkSize;
                int stride = Spec.Stride;
                int elements = Spec.ElementsPerChunk;

                int startSpan = Offsets[index];
                int endSpan = Offsets[index + 1];

                for (int i = startSpan; i < endSpan; i++)
                {
                    WeightedRect span = Spans[i];
                    if (span.IsEmpty) continue;

                    CellRect bounds = span.Bounds;
                    int weight = span.Weight;
                    int cx0 = bounds.Min.x >> log2;
                    int cy0 = bounds.Min.y >> log2;
                    int cx1 = (bounds.Max.x - 1) >> log2;
                    int cy1 = (bounds.Max.y - 1) >> log2;

                    for (int cy = cy0; cy <= cy1; cy++)
                    for (int cx = cx0; cx <= cx1; cx++)
                    {
                        if (!SlotByCoord.TryGetValue(new int2(cx, cy), out int slot)) continue;

                        int baseX = cx << log2;
                        int baseY = cy << log2;
                        int lx0 = math.max(bounds.Min.x, baseX) - baseX;
                        int ly0 = math.max(bounds.Min.y, baseY) - baseY;
                        int lx1 = math.min(bounds.Max.x, baseX + chunkSize) - baseX;
                        int ly1 = math.min(bounds.Max.y, baseY + chunkSize) - baseY;

                        if (lx0 >= lx1 || ly0 >= ly1) continue;

                        int* field = data + slot * elements;
                        Interlocked.Add(ref field[ly0 * stride + lx0], weight);
                        Interlocked.Add(ref field[ly0 * stride + lx1], -weight);
                        Interlocked.Add(ref field[ly1 * stride + lx0], -weight);
                        Interlocked.Add(ref field[ly1 * stride + lx1], weight);
                    }
                }
            }
        }

        [BurstCompile]
        struct ResolveJob : IJobParallelForDefer
        {
            [ReadOnly] public NativeArray<int> ActiveSlots;
            [NativeDisableParallelForRestriction] public NativeArray<int> Data;
            public GridSpec Spec;

            public void Execute(int index)
            {
                int* field = (int*)Data.GetUnsafePtr() + ActiveSlots[index] * Spec.ElementsPerChunk;
                PrefixSum.Run(field, Spec);
            }
        }
    }
}
