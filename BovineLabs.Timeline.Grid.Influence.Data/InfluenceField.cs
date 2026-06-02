using System;
using System.Runtime.CompilerServices;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Mathematics;

namespace BovineLabs.Timeline.Grid.Influence.Data
{
    public unsafe struct InfluenceField : INativeDisposable
    {
        NativeParallelHashMap<int2, int> _slotByCoord;
        NativeList<int2> _coordBySlot;
        NativeList<uint> _lastWrittenFrameBySlot;
        NativeList<int> _freeSlots;
        NativeList<int> _activeSlots;
        NativeList<int> _data;

        int _chunkSize;
        int _log2;
        int _stride;
        int _dimension;
        int _elementsPerChunk;
        uint _frameId;
        uint _retentionFrames;
        Allocator _allocator;
        JobHandle _lastScheduled;

        public bool IsCreated
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _data.IsCreated;
        }

        public JobHandle LastScheduled
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _lastScheduled;
        }

        public int ChunkSize => _chunkSize;
        public int Log2 => _log2;
        public int Stride => _stride;
        public int Dimension => _dimension;
        public uint FrameId => _frameId;

        public NativeArray<int> ActiveSlots => _activeSlots.AsArray();
        public NativeArray<int2> CoordBySlot => _coordBySlot.AsArray();
        public NativeArray<int> Data => _data.AsArray();

        public static InfluenceField Create(int chunkSize, uint retentionFrames, Allocator allocator)
        {
            if (chunkSize <= 0 || (chunkSize & (chunkSize - 1)) != 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(chunkSize), chunkSize, "Chunk size must be a positive power of two.");
            }

            int dimension = chunkSize + 1;
            int stride = (dimension + 7) & ~7;

            return new InfluenceField
            {
                _slotByCoord = new NativeParallelHashMap<int2, int>(64, allocator),
                _coordBySlot = new NativeList<int2>(64, allocator),
                _lastWrittenFrameBySlot = new NativeList<uint>(64, allocator),
                _freeSlots = new NativeList<int>(64, allocator),
                _activeSlots = new NativeList<int>(64, allocator),
                _data = new NativeList<int>(64 * stride * dimension, allocator),
                _chunkSize = chunkSize,
                _log2 = math.tzcnt(chunkSize),
                _stride = stride,
                _dimension = dimension,
                _elementsPerChunk = stride * dimension,
                _frameId = 1,
                _retentionFrames = retentionFrames,
                _allocator = allocator,
                _lastScheduled = default
            };
        }

        public void Complete()
        {
            _lastScheduled.Complete();
            _lastScheduled = default;
        }

        public JobHandle ScheduleBatched(NativeArray<Stamp> stamps, JobHandle dependsOn = default)
        {
            ThrowIfNotCreated();
            Complete();
            dependsOn.Complete();

            AdvanceFrame();
            EvictStale();

            _activeSlots.Clear();
            int rectCapacity = PrepareSlotsAndCountRects(stamps);

            if (_activeSlots.Length == 0)
            {
                _lastScheduled = default;
                return default;
            }

            NativeList<WorldRect> rects = new NativeList<WorldRect>(math.max(1, rectCapacity), _allocator);

            JobHandle rasterize = new RasterizeJob
            {
                Stamps = stamps,
                Rects = rects
            }.Schedule();

            JobHandle clear = new ClearJob
            {
                ActiveSlots = _activeSlots.AsArray(),
                Data = _data.AsArray(),
                ElementsPerChunk = _elementsPerChunk
            }.Schedule(_activeSlots.Length, 1);

            JobHandle scatter = new ScatterJob
            {
                Rects = rects.AsDeferredJobArray(),
                SlotByCoord = _slotByCoord,
                Data = _data.AsArray(),
                Log2 = _log2,
                ChunkSize = _chunkSize,
                Stride = _stride,
                ElementsPerChunk = _elementsPerChunk
            }.Schedule(JobHandle.CombineDependencies(rasterize, clear));

            JobHandle resolve = new ResolveJob
            {
                ActiveSlots = _activeSlots.AsArray(),
                Data = _data.AsArray(),
                Stride = _stride,
                Dimension = _dimension,
                ElementsPerChunk = _elementsPerChunk
            }.Schedule(_activeSlots.Length, 1, scatter);

            _lastScheduled = rects.Dispose(resolve);
            return _lastScheduled;
        }

        public int Read(int2 cell)
        {
            ThrowIfNotCreated();
            Complete();

            int2 coord = new int2(cell.x >> _log2, cell.y >> _log2);
            if (!_slotByCoord.TryGetValue(coord, out int slot)) return 0;
            if (_lastWrittenFrameBySlot[slot] != _frameId) return 0;

            int lx = cell.x - (coord.x << _log2);
            int ly = cell.y - (coord.y << _log2);
            if ((uint)lx >= (uint)_chunkSize || (uint)ly >= (uint)_chunkSize) return 0;

            return _data[slot * _elementsPerChunk + ly * _stride + lx];
        }

        public ChunkView GetChunkView(int2 coord)
        {
            ThrowIfNotCreated();
            Complete();

            if (!_slotByCoord.TryGetValue(coord, out int slot)) return default;
            if (_lastWrittenFrameBySlot[slot] != _frameId) return default;

            int* field = (int*)_data.GetUnsafePtr() + slot * _elementsPerChunk;
            return new ChunkView
            {
                Field = field,
                Base = new int2(coord.x << _log2, coord.y << _log2),
                Stride = _stride,
                ChunkSize = _chunkSize
            };
        }

        public void Dispose()
        {
            if (!IsCreated) return;
            Complete();

            if (_slotByCoord.IsCreated) _slotByCoord.Dispose();
            if (_coordBySlot.IsCreated) _coordBySlot.Dispose();
            if (_lastWrittenFrameBySlot.IsCreated) _lastWrittenFrameBySlot.Dispose();
            if (_freeSlots.IsCreated) _freeSlots.Dispose();
            if (_activeSlots.IsCreated) _activeSlots.Dispose();
            if (_data.IsCreated) _data.Dispose();

            this = default;
        }

        public JobHandle Dispose(JobHandle inputDeps)
        {
            if (!IsCreated) return inputDeps;

            JobHandle handle = JobHandle.CombineDependencies(inputDeps, _lastScheduled);
            handle = _slotByCoord.Dispose(handle);
            handle = _coordBySlot.Dispose(handle);
            handle = _lastWrittenFrameBySlot.Dispose(handle);
            handle = _freeSlots.Dispose(handle);
            handle = _activeSlots.Dispose(handle);
            handle = _data.Dispose(handle);

            this = default;
            return handle;
        }

        int PrepareSlotsAndCountRects(NativeArray<Stamp> stamps)
        {
            long rectCapacity = 0;

            for (int i = 0; i < stamps.Length; i++)
            {
                Stamp stamp = stamps[i];
                rectCapacity += Rasterizer.EstimateRectCount(stamp);
                ActivateBounds(Rasterizer.Bounds(stamp.Shape, stamp.Origin));
            }

            return rectCapacity > int.MaxValue ? int.MaxValue : (int)rectCapacity;
        }

        void ActivateBounds(AlignedRect bounds)
        {
            if (bounds.IsEmpty) return;

            int cx0 = bounds.Min.x >> _log2;
            int cy0 = bounds.Min.y >> _log2;
            int cx1 = (bounds.Max.x - 1) >> _log2;
            int cy1 = (bounds.Max.y - 1) >> _log2;

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
            if (_lastWrittenFrameBySlot[slot] == _frameId) return;

            _lastWrittenFrameBySlot[slot] = _frameId;
            _activeSlots.Add(slot);
        }

        int EnsureSlot(int2 coord)
        {
            if (_slotByCoord.TryGetValue(coord, out int existing)) return existing;

            int slot;
            if (_freeSlots.Length > 0)
            {
                slot = _freeSlots[_freeSlots.Length - 1];
                _freeSlots.RemoveAtSwapBack(_freeSlots.Length - 1);
                _coordBySlot[slot] = coord;
                _lastWrittenFrameBySlot[slot] = 0;
            }
            else
            {
                slot = _coordBySlot.Length;
                _coordBySlot.Add(coord);
                _lastWrittenFrameBySlot.Add(0);
                _data.ResizeUninitialized(_data.Length + _elementsPerChunk);
            }

            _slotByCoord.Add(coord, slot);
            return slot;
        }

        void AdvanceFrame()
        {
            if (_frameId == uint.MaxValue)
            {
                for (int i = 0; i < _lastWrittenFrameBySlot.Length; i++)
                {
                    _lastWrittenFrameBySlot[i] = 0;
                }

                _frameId = 1;
                return;
            }

            _frameId++;
        }

        void EvictStale()
        {
            if (_retentionFrames == uint.MaxValue) return;

            for (int slot = 0; slot < _lastWrittenFrameBySlot.Length; slot++)
            {
                uint last = _lastWrittenFrameBySlot[slot];
                if (last == 0) continue;
                if (_frameId - last <= _retentionFrames) continue;

                _slotByCoord.Remove(_coordBySlot[slot]);
                _lastWrittenFrameBySlot[slot] = 0;
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

        [BurstCompile(FloatMode = FloatMode.Fast, OptimizeFor = OptimizeFor.Performance)]
        struct RasterizeJob : IJob
        {
            [ReadOnly] public NativeArray<Stamp> Stamps;
            public NativeList<WorldRect> Rects;

            public void Execute()
            {
                for (int i = 0; i < Stamps.Length; i++)
                {
                    Rasterizer.Emit(Stamps[i], ref Rects);
                }
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
                int slot = ActiveSlots[index];
                int* field = (int*)Data.GetUnsafePtr() + slot * ElementsPerChunk;
                UnsafeUtility.MemClear(field, (long)ElementsPerChunk * sizeof(int));
            }
        }

        [BurstCompile(FloatMode = FloatMode.Fast, OptimizeFor = OptimizeFor.Performance)]
        struct ScatterJob : IJob
        {
            [ReadOnly] public NativeArray<WorldRect> Rects;
            [ReadOnly] public NativeParallelHashMap<int2, int> SlotByCoord;
            public NativeArray<int> Data;
            public int Log2;
            public int ChunkSize;
            public int Stride;
            public int ElementsPerChunk;

            public void Execute()
            {
                int* data = (int*)Data.GetUnsafePtr();

                for (int i = 0; i < Rects.Length; i++)
                {
                    AlignedRect bounds = Rects[i].Bounds;
                    if (bounds.IsEmpty) continue;

                    int weight = Rects[i].Weight;
                    int cx0 = bounds.Min.x >> Log2;
                    int cy0 = bounds.Min.y >> Log2;
                    int cx1 = (bounds.Max.x - 1) >> Log2;
                    int cy1 = (bounds.Max.y - 1) >> Log2;

                    for (int cy = cy0; cy <= cy1; cy++)
                    {
                        for (int cx = cx0; cx <= cx1; cx++)
                        {
                            if (!SlotByCoord.TryGetValue(new int2(cx, cy), out int slot)) continue;

                            int baseX = cx << Log2;
                            int baseY = cy << Log2;
                            int lx0 = math.max(bounds.Min.x, baseX) - baseX;
                            int ly0 = math.max(bounds.Min.y, baseY) - baseY;
                            int lx1 = math.min(bounds.Max.x, baseX + ChunkSize) - baseX;
                            int ly1 = math.min(bounds.Max.y, baseY + ChunkSize) - baseY;

                            if (lx0 >= lx1 || ly0 >= ly1) continue;

                            int* field = data + slot * ElementsPerChunk;
                            field[ly0 * Stride + lx0] += weight;
                            field[ly0 * Stride + lx1] -= weight;
                            field[ly1 * Stride + lx0] -= weight;
                            field[ly1 * Stride + lx1] += weight;
                        }
                    }
                }
            }
        }

        [BurstCompile(FloatMode = FloatMode.Fast, OptimizeFor = OptimizeFor.Performance)]
        struct ResolveJob : IJobParallelFor
        {
            [ReadOnly] public NativeArray<int> ActiveSlots;
            [NativeDisableParallelForRestriction] public NativeArray<int> Data;
            public int Stride;
            public int Dimension;
            public int ElementsPerChunk;

            public void Execute(int index)
            {
                int slot = ActiveSlots[index];
                int* field = (int*)Data.GetUnsafePtr() + slot * ElementsPerChunk;
                PrefixSumResolve.Run(field, Stride, Dimension);
            }
        }
    }
}