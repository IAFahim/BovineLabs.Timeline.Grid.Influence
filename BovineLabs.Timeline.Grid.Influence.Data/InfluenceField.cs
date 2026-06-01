using System;
using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Mathematics;
using static Unity.Burst.Intrinsics.X86;

namespace BovineLabs.Timeline.Grid.Influence.Data
{

    public unsafe struct InfluenceField : INativeDisposable
    {
        NativeParallelHashMap<int2, int> _index;
        NativeList<ChunkSlot> _slots;
        NativeList<int> _activeChunks;

        int _chunkSize;
        int _log2;
        int _stride;
        int _dimension;
        uint _frameId;
        AllocatorManager.AllocatorHandle _allocator;
        JobHandle _lastScheduledHandle;

        internal struct ChunkSlot
        {
            public int2 Coord;

            [NativeDisableUnsafePtrRestriction]
            public int* Field;

            public uint LastWrittenFrame;
        }

        public bool IsCreated => _slots.IsCreated;
        public JobHandle LastScheduledHandle => _lastScheduledHandle;

        public static InfluenceField Create(int chunkSizePowerOfTwo, AllocatorManager.AllocatorHandle allocator)
        {
            if (chunkSizePowerOfTwo <= 0 || (chunkSizePowerOfTwo & (chunkSizePowerOfTwo - 1)) != 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(chunkSizePowerOfTwo),
                    chunkSizePowerOfTwo,
                    "Chunk size must be a positive power of two.");
            }

            int dimension = chunkSizePowerOfTwo + 1;

            int stride = (dimension + 7) & ~7;

            return new InfluenceField
            {
                _index = new NativeParallelHashMap<int2, int>(64, allocator),
                _slots = new NativeList<ChunkSlot>(64, allocator),
                _activeChunks = new NativeList<int>(64, allocator),
                _chunkSize = chunkSizePowerOfTwo,
                _log2 = math.tzcnt(chunkSizePowerOfTwo),
                _stride = stride,
                _dimension = dimension,
                _frameId = 1,
                _allocator = allocator,
                _lastScheduledHandle = default
            };
        }
        
        public void Complete()
        {
            _lastScheduledHandle.Complete();
            _lastScheduledHandle = default;
        }
        
        public JobHandle ScheduleBatched(NativeArray<Stamp> stamps, JobHandle dependsOn = default)
        {
            ThrowIfNotCreated();

            Complete();

            dependsOn.Complete();

            AdvanceFrame();

            long rectCapacityLong = 0;
            for (int i = 0; i < stamps.Length; i++)
            {
                EnsureBounds(Rasterizer.Bounds(stamps[i].Shape, stamps[i].Origin));
                rectCapacityLong += Rasterizer.EstimateRectCount(stamps[i]);

                if (rectCapacityLong > int.MaxValue)
                {
                    throw new InvalidOperationException("Rasterized rect count exceeds NativeList capacity.");
                }
            }

            _activeChunks.Clear();
            if (_activeChunks.Capacity < _slots.Length)
            {
                _activeChunks.SetCapacity(_slots.Length);
            }

            int rectCapacity = math.max(1, (int)rectCapacityLong);
            NativeList<WorldRect> rects = new NativeList<WorldRect>(rectCapacity, _allocator);

            var rasterJob = new RasterizeJob
            {
                Stamps = stamps,
                Rects = rects
            };

            var rasterHandle = rasterJob.Schedule();

            var scatterJob = new ScatterJob
            {
                Rects = rects.AsDeferredJobArray(),
                Index = _index,
                Slots = _slots,
                ActiveChunks = _activeChunks,
                ChunkSize = _chunkSize,
                Log2 = _log2,
                Stride = _stride,
                Dimension = _dimension,
                FrameId = _frameId
            };

            var scatterHandle = scatterJob.Schedule(rasterHandle);

            var resolveJob = new ResolveJob
            {
                Slots = _slots.AsArray(),
                ActiveChunks = _activeChunks.AsDeferredJobArray(),
                Stride = _stride,
                Dimension = _dimension
            };

            var resolveHandle = resolveJob.Schedule(_activeChunks, 1, scatterHandle);
            _lastScheduledHandle = rects.Dispose(resolveHandle);
            return _lastScheduledHandle;
        }
        
        public void AddImmediate(Stamp stamp)
        {
            ThrowIfNotCreated();
            Complete();

            int capacity = math.max(1, Rasterizer.EstimateRectCount(stamp));
            EnsureBounds(Rasterizer.Bounds(stamp.Shape, stamp.Origin));

            NativeList<WorldRect> rects = new NativeList<WorldRect>(capacity, Allocator.Temp);
            Rasterizer.Emit(stamp, ref rects);

            for (int i = 0; i < rects.Length; i++)
            {
                BroadcastRect(rects[i]);
            }

            rects.Dispose();
        }

        public void RemoveImmediate(Stamp stamp) => AddImmediate(stamp.Negated());

        public int Read(int2 cell)
        {
            ThrowIfNotCreated();
            Complete();

            int2 coord = new int2(cell.x >> _log2, cell.y >> _log2);
            if (!_index.TryGetValue(coord, out int slot))
            {
                return 0;
            }

            ChunkSlot chunk = _slots[slot];
            if (chunk.LastWrittenFrame != _frameId)
            {
                return 0;
            }

            int baseX = coord.x << _log2;
            int baseY = coord.y << _log2;
            int lx = cell.x - baseX;
            int ly = cell.y - baseY;

            if ((uint)lx >= (uint)_chunkSize || (uint)ly >= (uint)_chunkSize)
            {
                return 0;
            }

            return chunk.Field[ly * _stride + lx];
        }
        
        public ChunkView GetChunkView(int2 coord)
        {
            ThrowIfNotCreated();
            Complete();

            if (!_index.TryGetValue(coord, out int slot))
            {
                return default;
            }

            ChunkSlot chunk = _slots[slot];
            if (chunk.LastWrittenFrame != _frameId)
            {
                return default;
            }

            return new ChunkView
            {
                Field = chunk.Field,
                Base = new int2(coord.x << _log2, coord.y << _log2),
                Stride = _stride,
                ChunkSize = _chunkSize
            };
        }

        public void Dispose()
        {
            if (!_slots.IsCreated && !_activeChunks.IsCreated && !_index.IsCreated)
            {
                return;
            }

            Complete();

            if (_slots.IsCreated)
            {
                for (int i = 0; i < _slots.Length; i++)
                {
                    int* field = _slots[i].Field;
                    if (field != null)
                    {
                        AllocatorManager.Free(_allocator, field);
                    }
                }

                _slots.Dispose();
            }

            if (_activeChunks.IsCreated)
            {
                _activeChunks.Dispose();
            }

            if (_index.IsCreated)
            {
                _index.Dispose();
            }

            this = default;
        }

        public JobHandle Dispose(JobHandle inputDeps)
        {
            JobHandle dependency = JobHandle.CombineDependencies(inputDeps, _lastScheduledHandle);

            if (!_slots.IsCreated && !_activeChunks.IsCreated && !_index.IsCreated)
            {
                return dependency;
            }

            JobHandle handle = dependency;

            if (_slots.IsCreated)
            {
                var freeFieldsJob = new FreeSlotFieldsJob
                {
                    Slots = _slots.AsArray(),
                    Allocator = _allocator
                };

                handle = freeFieldsJob.Schedule(handle);
                handle = _slots.Dispose(handle);
            }

            if (_activeChunks.IsCreated)
            {
                handle = _activeChunks.Dispose(handle);
            }

            if (_index.IsCreated)
            {
                handle = _index.Dispose(handle);
            }

            this = default;
            return handle;
        }

        void ThrowIfNotCreated()
        {
            if (!_slots.IsCreated)
            {
                throw new ObjectDisposedException(nameof(InfluenceField));
            }
        }

        void AdvanceFrame()
        {
            if (_frameId == uint.MaxValue)
            {
                for (int i = 0; i < _slots.Length; i++)
                {
                    ChunkSlot slot = _slots[i];
                    slot.LastWrittenFrame = 0;
                    _slots[i] = slot;
                }

                _frameId = 1;
                return;
            }

            _frameId++;
        }

        int EnsureSlot(int2 coord)
        {
            if (_index.TryGetValue(coord, out int existing))
            {
                return existing;
            }

            int items = _stride * _dimension;
            long bytes = (long)items * sizeof(int);

            int* ptr = (int*)AllocatorManager.Allocate(_allocator, sizeof(int), 32, items);
            UnsafeUtility.MemClear(ptr, bytes);

            int index = _slots.Length;
            _slots.Add(new ChunkSlot
            {
                Coord = coord,
                Field = ptr,
                LastWrittenFrame = 0
            });

            _index.Add(coord, index);
            return index;
        }

        void EnsureBounds(AlignedRect bounds)
        {
            if (bounds.IsEmpty)
            {
                return;
            }

            int cx0 = bounds.Min.x >> _log2;
            int cy0 = bounds.Min.y >> _log2;
            int cx1 = (bounds.Max.x - 1) >> _log2;
            int cy1 = (bounds.Max.y - 1) >> _log2;

            for (int cy = cy0; cy <= cy1; cy++)
            {
                for (int cx = cx0; cx <= cx1; cx++)
                {
                    EnsureSlot(new int2(cx, cy));
                }
            }
        }

        void ActivateChunkForCurrentFrame(ChunkSlot* slotPtr)
        {
            if (slotPtr->LastWrittenFrame == _frameId)
            {
                return;
            }

            UnsafeUtility.MemClear(slotPtr->Field, ChunkBytes);
            slotPtr->LastWrittenFrame = _frameId;
        }

        long ChunkBytes => (long)_stride * _dimension * sizeof(int);
        
        void BroadcastRect(WorldRect rect)
        {
            AlignedRect bounds = rect.Bounds;
            if (bounds.IsEmpty)
            {
                return;
            }

            int weight = rect.Weight;
            int cx0 = bounds.Min.x >> _log2;
            int cy0 = bounds.Min.y >> _log2;
            int cx1 = (bounds.Max.x - 1) >> _log2;
            int cy1 = (bounds.Max.y - 1) >> _log2;

            bool avx2 = Avx2.IsAvx2Supported;
            v256 wide = default;
            if (avx2)
            {
                wide = Avx.mm256_set1_epi32(weight);
            }

            for (int cy = cy0; cy <= cy1; cy++)
            {
                for (int cx = cx0; cx <= cx1; cx++)
                {
                    if (!_index.TryGetValue(new int2(cx, cy), out int slotIdx))
                    {
                        continue;
                    }

                    ChunkSlot* slotPtr = (ChunkSlot*)_slots.GetUnsafePtr() + slotIdx;
                    ActivateChunkForCurrentFrame(slotPtr);

                    int baseX = cx << _log2;
                    int baseY = cy << _log2;
                    int lx0 = math.max(bounds.Min.x, baseX) - baseX;
                    int ly0 = math.max(bounds.Min.y, baseY) - baseY;
                    int lx1 = math.min(bounds.Max.x, baseX + _chunkSize) - baseX;
                    int ly1 = math.min(bounds.Max.y, baseY + _chunkSize) - baseY;

                    if (lx0 >= lx1 || ly0 >= ly1)
                    {
                        continue;
                    }

                    int* field = slotPtr->Field;
                    int count = lx1 - lx0;

                    for (int ly = ly0; ly < ly1; ly++)
                    {
                        int* row = field + ly * _stride + lx0;
                        int x = 0;

                        if (avx2)
                        {
                            for (; x <= count - 8; x += 8)
                            {
                                v256 current = Avx.mm256_loadu_si256(row + x);
                                Avx.mm256_storeu_si256(row + x, Avx2.mm256_add_epi32(current, wide));
                            }
                        }

                        for (; x < count; x++)
                        {
                            row[x] += weight;
                        }
                    }
                }
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

        [BurstCompile(FloatMode = FloatMode.Fast, OptimizeFor = OptimizeFor.Performance)]
        struct ScatterJob : IJob
        {
            [ReadOnly] public NativeArray<WorldRect> Rects;
            [ReadOnly] public NativeParallelHashMap<int2, int> Index;

            public NativeList<ChunkSlot> Slots;
            public NativeList<int> ActiveChunks;

            public int ChunkSize;
            public int Log2;
            public int Stride;
            public int Dimension;
            public uint FrameId;

            public void Execute()
            {
                long chunkBytes = (long)Stride * Dimension * sizeof(int);

                for (int i = 0; i < Rects.Length; i++)
                {
                    AlignedRect bounds = Rects[i].Bounds;
                    if (bounds.IsEmpty)
                    {
                        continue;
                    }

                    int weight = Rects[i].Weight;
                    int cx0 = bounds.Min.x >> Log2;
                    int cy0 = bounds.Min.y >> Log2;
                    int cx1 = (bounds.Max.x - 1) >> Log2;
                    int cy1 = (bounds.Max.y - 1) >> Log2;

                    for (int cy = cy0; cy <= cy1; cy++)
                    {
                        for (int cx = cx0; cx <= cx1; cx++)
                        {
                            if (!Index.TryGetValue(new int2(cx, cy), out int slotIdx))
                            {
                                continue;
                            }

                            int baseX = cx << Log2;
                            int baseY = cy << Log2;
                            int lx0 = math.max(bounds.Min.x, baseX) - baseX;
                            int ly0 = math.max(bounds.Min.y, baseY) - baseY;
                            int lx1 = math.min(bounds.Max.x, baseX + ChunkSize) - baseX;
                            int ly1 = math.min(bounds.Max.y, baseY + ChunkSize) - baseY;

                            if (lx0 >= lx1 || ly0 >= ly1)
                            {
                                continue;
                            }

                            ChunkSlot* slotPtr = (ChunkSlot*)Slots.GetUnsafePtr() + slotIdx;
                            if (slotPtr->LastWrittenFrame != FrameId)
                            {
                                UnsafeUtility.MemClear(slotPtr->Field, chunkBytes);
                                slotPtr->LastWrittenFrame = FrameId;
                                ActiveChunks.AddNoResize(slotIdx);
                            }

                            int* field = slotPtr->Field;
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
        struct ResolveJob : IJobParallelForDefer
        {
            [ReadOnly] public NativeArray<ChunkSlot> Slots;
            [ReadOnly] public NativeArray<int> ActiveChunks;
            public int Stride;
            public int Dimension;

            public void Execute(int index)
            {
                int slotIdx = ActiveChunks[index];
                PrefixSumResolve.Run(Slots[slotIdx].Field, Stride, Dimension);
            }
        }

        [BurstCompile]
        struct FreeSlotFieldsJob : IJob
        {
            [ReadOnly] public NativeArray<ChunkSlot> Slots;
            public AllocatorManager.AllocatorHandle Allocator;

            public void Execute()
            {
                for (int i = 0; i < Slots.Length; i++)
                {
                    int* field = Slots[i].Field;
                    if (field != null)
                    {
                        AllocatorManager.Free(Allocator, field);
                    }
                }
            }
        }
    }

    public unsafe struct ChunkView
    {
        [NativeDisableUnsafePtrRestriction]
        public int* Field;

        public int2 Base;
        public int Stride;
        public int ChunkSize;

        public bool IsValid => Field != null;

        public bool ContainsWorld(int2 worldPos)
        {
            int2 local = worldPos - Base;
            return (uint)local.x < (uint)ChunkSize && (uint)local.y < (uint)ChunkSize;
        }

        public int ReadLocal(int2 localPos)
        {
            if ((uint)localPos.x >= (uint)ChunkSize || (uint)localPos.y >= (uint)ChunkSize)
            {
                return 0;
            }

            return Field[localPos.y * Stride + localPos.x];
        }

        public int ReadWorld(int2 worldPos)
        {
            int2 local = worldPos - Base;
            return ReadLocal(local);
        }
    }
}
