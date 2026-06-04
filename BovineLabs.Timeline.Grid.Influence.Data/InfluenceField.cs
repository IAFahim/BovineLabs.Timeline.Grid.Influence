using System;
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
            JobHandle.CombineDependencies(_dependency, dependsOn).Complete();
            _dependency = default;

            AdvanceFrame();
            EvictStale();
            _activeSlots.Clear();

            int stampCount = stamps.IsCreated ? stamps.Length : 0;
            if (stampCount == 0)
            {
                _dependency = default;
                return default;
            }

            NativeArray<int> offsets = new NativeArray<int>(stampCount + 1, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
            int totalSpans = PrepareSlotsAndOffsets(stamps, offsets);

            if (_activeSlots.Length == 0 || totalSpans == 0)
            {
                offsets.Dispose();
                _dependency = default;
                return default;
            }

            NativeArray<WeightedRect> spans = new NativeArray<WeightedRect>(totalSpans, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);

            JobHandle rasterize = new RasterizeJob
            {
                Stamps = stamps,
                Offsets = offsets,
                Spans = spans
            }.Schedule(stampCount, 8);

            JobHandle clear = new ClearJob
            {
                ActiveSlots = _activeSlots.AsArray(),
                Data = _data.AsArray(),
                ElementsPerChunk = _spec.ElementsPerChunk
            }.Schedule(_activeSlots.Length, 1);

            JobHandle scatter = new ScatterJob
            {
                Spans = spans,
                SlotByCoord = _slotByCoord.AsReadOnly(),
                Data = _data.AsArray(),
                Spec = _spec
            }.Schedule(JobHandle.CombineDependencies(rasterize, clear));

            JobHandle resolve = new ResolveJob
            {
                ActiveSlots = _activeSlots.AsArray(),
                Data = _data.AsArray(),
                Spec = _spec
            }.Schedule(_activeSlots.Length, 1, scatter);

            JobHandle cleanup = JobHandle.CombineDependencies(spans.Dispose(resolve), offsets.Dispose(rasterize));
            _dependency = cleanup;
            return cleanup;
        }

        public void Dispose()
        {
            if (!IsCreated)
            {
                return;
            }

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
            if (!IsCreated)
            {
                return inputDeps;
            }

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

        int PrepareSlotsAndOffsets(NativeArray<Stamp> stamps, NativeArray<int> offsets)
        {
            long running = 0;
            for (int i = 0; i < stamps.Length; i++)
            {
                offsets[i] = IntegerMath.ClampToInt(running);
                InfluenceShape shape = stamps[i].Shape;
                running += Rasterizer.EstimateSpanCount(shape);
                ActivateBounds(Rasterizer.Bounds(shape, stamps[i].Origin));
            }

            offsets[stamps.Length] = IntegerMath.ClampToInt(running);
            return offsets[stamps.Length];
        }

        void ActivateBounds(in CellRect bounds)
        {
            if (bounds.IsEmpty)
            {
                return;
            }

            int cx0 = bounds.Min.x >> _spec.Log2;
            int cy0 = bounds.Min.y >> _spec.Log2;
            int cx1 = (bounds.Max.x - 1) >> _spec.Log2;
            int cy1 = (bounds.Max.y - 1) >> _spec.Log2;

            for (int cy = cy0; cy <= cy1; cy++)
            {
                for (int cx = cx0; cx <= cx1; cx++)
                {
                    Activate(new int2(cx, cy));
                }
            }
        }

        void Activate(int2 coord)
        {
            int slot = EnsureSlot(coord);
            if (_lastWrittenBySlot[slot] == _frameId)
            {
                return;
            }

            _lastWrittenBySlot[slot] = _frameId;
            _activeSlots.Add(slot);
        }

        int EnsureSlot(int2 coord)
        {
            if (_slotByCoord.TryGetValue(coord, out int existing))
            {
                return existing;
            }

            int slot;
            if (_freeSlots.Length > 0)
            {
                slot = _freeSlots[_freeSlots.Length - 1];
                _freeSlots.RemoveAtSwapBack(_freeSlots.Length - 1);
                _coordBySlot[slot] = coord;
                _lastWrittenBySlot[slot] = 0;
            }
            else
            {
                slot = _coordBySlot.Length;
                _coordBySlot.Add(coord);
                _lastWrittenBySlot.Add(0);
                _data.ResizeUninitialized(_data.Length + _spec.ElementsPerChunk);
            }

            _slotByCoord.Add(coord, slot);
            return slot;
        }

        void AdvanceFrame()
        {
            if (_frameId == uint.MaxValue)
            {
                for (int i = 0; i < _lastWrittenBySlot.Length; i++)
                {
                    _lastWrittenBySlot[i] = 0;
                }

                _frameId = 1;
                return;
            }

            _frameId++;
        }

        void EvictStale()
        {
            if (_spec.RetentionFrames == uint.MaxValue)
            {
                return;
            }

            for (int slot = 0; slot < _lastWrittenBySlot.Length; slot++)
            {
                uint last = _lastWrittenBySlot[slot];
                if (last == 0 || _frameId - last <= _spec.RetentionFrames)
                {
                    continue;
                }

                _slotByCoord.Remove(_coordBySlot[slot]);
                _lastWrittenBySlot[slot] = 0;
                _freeSlots.Add(slot);
            }
        }

        void ThrowIfNotCreated()
        {
            if (!_data.IsCreated)
            {
                throw new ObjectDisposedException(nameof(InfluenceField));
            }
        }

        [BurstCompile]
        struct RasterizeJob : IJobParallelFor
        {
            [ReadOnly] public NativeArray<Stamp> Stamps;
            [ReadOnly] public NativeArray<int> Offsets;
            [NativeDisableParallelForRestriction] public NativeArray<WeightedRect> Spans;

            public void Execute(int index)
            {
                int start = Offsets[index];
                int capacity = Offsets[index + 1] - start;
                SpanSink sink = new SpanSink((WeightedRect*)Spans.GetUnsafePtr() + start, capacity);
                Rasterizer.Emit(Stamps[index], ref sink);
                sink.SealRemaining();
            }
        }

        [BurstCompile]
        struct ClearJob : IJobParallelFor
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
        struct ScatterJob : IJob
        {
            [ReadOnly] public NativeArray<WeightedRect> Spans;
            [ReadOnly] public NativeFlatMap.ReadOnly SlotByCoord;
            public NativeArray<int> Data;
            public GridSpec Spec;

            public void Execute()
            {
                int* data = (int*)Data.GetUnsafePtr();
                int log2 = Spec.Log2;
                int chunkSize = Spec.ChunkSize;
                int stride = Spec.Stride;
                int elements = Spec.ElementsPerChunk;

                for (int i = 0; i < Spans.Length; i++)
                {
                    WeightedRect span = Spans[i];
                    if (span.IsEmpty)
                    {
                        continue;
                    }

                    CellRect bounds = span.Bounds;
                    int weight = span.Weight;
                    int cx0 = bounds.Min.x >> log2;
                    int cy0 = bounds.Min.y >> log2;
                    int cx1 = (bounds.Max.x - 1) >> log2;
                    int cy1 = (bounds.Max.y - 1) >> log2;

                    for (int cy = cy0; cy <= cy1; cy++)
                    {
                        for (int cx = cx0; cx <= cx1; cx++)
                        {
                            if (!SlotByCoord.TryGetValue(new int2(cx, cy), out int slot))
                            {
                                continue;
                            }

                            int baseX = cx << log2;
                            int baseY = cy << log2;
                            int lx0 = math.max(bounds.Min.x, baseX) - baseX;
                            int ly0 = math.max(bounds.Min.y, baseY) - baseY;
                            int lx1 = math.min(bounds.Max.x, baseX + chunkSize) - baseX;
                            int ly1 = math.min(bounds.Max.y, baseY + chunkSize) - baseY;

                            if (lx0 >= lx1 || ly0 >= ly1)
                            {
                                continue;
                            }

                            int* field = data + slot * elements;
                            field[ly0 * stride + lx0] += weight;
                            field[ly0 * stride + lx1] -= weight;
                            field[ly1 * stride + lx0] -= weight;
                            field[ly1 * stride + lx1] += weight;
                        }
                    }
                }
            }
        }

        [BurstCompile]
        struct ResolveJob : IJobParallelFor
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
