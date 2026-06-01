using System;
using BovineLabs.Timeline.Grid.Influence.Data;
using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Mathematics;
using static Unity.Burst.Intrinsics.X86;

namespace BovineLabs.Timeline.Grid.Influence
{
    public unsafe struct InfluenceField : INativeDisposable
    {
        NativeParallelHashMap<int2, int> _index;
        NativeList<ChunkSlot> _slots;
        NativeList<int> _activeChunks; // Tracks which chunks were touched this frame
        
        int _chunkSize;
        int _log2;
        int _stride;
        int _dimension;
        uint _frameId;
        AllocatorManager.AllocatorHandle _allocator;

        internal struct ChunkSlot
        {
            public int2 Coord;
            [NativeDisableUnsafePtrRestriction] 
            public int* Field;
            public uint LastWrittenFrame;
        }

        public static InfluenceField Create(int chunkSizePowerOfTwo, AllocatorManager.AllocatorHandle allocator)
        {
            // HARDWARE GEM: Trailing zero count replaces the while loop for O(1) Log2
            int log2 = math.tzcnt(chunkSizePowerOfTwo);
            int dimension = chunkSizePowerOfTwo + 1;
            int stride = (dimension + 7) & ~7; // 32-byte aligned for AVX2
            
            return new InfluenceField
            {
                _index = new NativeParallelHashMap<int2, int>(64, allocator),
                _slots = new NativeList<ChunkSlot>(64, allocator),
                _activeChunks = new NativeList<int>(64, allocator),
                _chunkSize = chunkSizePowerOfTwo,
                _log2 = log2,
                _stride = stride,
                _dimension = dimension,
                _frameId = 1,
                _allocator = allocator
            };
        }

        /// <summary>
        /// Schedules the influence map generation across worker threads using Difference Arrays.
        /// Call this once per frame for mass-unit updates.
        /// </summary>
        public JobHandle ScheduleBatched(NativeArray<Stamp> stamps, JobHandle dependsOn = default)
        {
            for (int i = 0; i < stamps.Length; i++)
                EnsureBounds(Rasterizer.Bounds(stamps[i].Shape, stamps[i].Origin));

            _frameId++;
            _activeChunks.Clear();
            _activeChunks.SetCapacity(_slots.Length);

            NativeList<WorldRect> rects = new NativeList<WorldRect>(stamps.Length * 4, _allocator);
            var rasterJob = new RasterizeJob { Stamps = stamps, Rects = rects };
            var rasterHandle = rasterJob.Schedule(dependsOn);

            var scatterJob = new ScatterJob
            {
                Rects = rects.AsDeferredJobArray(),
                Index = _index,
                Slots = _slots,
                ActiveChunks = _activeChunks,
                ChunkSize = _chunkSize,
                Log2 = _log2,
                Stride = _stride,
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

            return rects.Dispose(resolveHandle);
        }

        /// <summary>
        /// Instantly adds a stamp directly to the resolved grid.
        /// Use this ONLY for single events (like an explosion) after the batched resolve has completed.
        /// </summary>
        public void AddImmediate(Stamp stamp)
        {
            EnsureBounds(Rasterizer.Bounds(stamp.Shape, stamp.Origin));
            NativeList<WorldRect> rects = new NativeList<WorldRect>(64, Allocator.Temp);
            Rasterizer.Emit(stamp, ref rects);
            
            for (int i = 0; i < rects.Length; i++) 
                BroadcastRect(rects[i]);
                
            rects.Dispose();
        }

        public void RemoveImmediate(Stamp stamp) => AddImmediate(stamp.Negated());

        public int Read(int2 cell)
        {
            int2 coord = new int2(cell.x >> _log2, cell.y >> _log2);
            if (!_index.TryGetValue(coord, out int slot)) return 0;
            
            int baseX = coord.x << _log2;
            int baseY = coord.y << _log2;
            int* field = _slots[slot].Field;
            
            if (_slots[slot].LastWrittenFrame != _frameId) return 0;
            
            return field[(cell.y - baseY) * _stride + (cell.x - baseX)];
        }

        public ChunkView GetChunkView(int2 coord)
        {
            if (!_index.TryGetValue(coord, out int slot) || _slots[slot].LastWrittenFrame != _frameId)
                return default;

            return new ChunkView
            {
                Field = _slots[slot].Field,
                Base = new int2(coord.x << _log2, coord.y << _log2),
                Stride = _stride
            };
        }

        public void Dispose()
        {
            if (_slots.IsCreated)
            {
                for (int i = 0; i < _slots.Length; i++)
                {
                    if (_slots[i].Field != null) 
                        AllocatorManager.Free(_allocator, _slots[i].Field);
                }
                _slots.Dispose();
            }
            if (_activeChunks.IsCreated) _activeChunks.Dispose();
            if (_index.IsCreated) _index.Dispose();
        }

        public JobHandle Dispose(JobHandle inputDeps)
        {
            Dispose();
            return inputDeps;
        }

        int EnsureSlot(int2 coord)
        {
            if (_index.TryGetValue(coord, out int existing)) return existing;
            
            int items = _stride * _dimension;
            long bytes = (long)items * sizeof(int);
            
            int* ptr = (int*)AllocatorManager.Allocate(_allocator, sizeof(int), 32, items);
            UnsafeUtility.MemClear(ptr, bytes);
            
            int index = _slots.Length;
            _slots.Add(new ChunkSlot { Coord = coord, Field = ptr, LastWrittenFrame = 0 });
            _index.Add(coord, index);
            return index;
        }

        void EnsureBounds(AlignedRect bounds)
        {
            if (bounds.IsEmpty) return;
            int cx0 = bounds.Min.x >> _log2;
            int cy0 = bounds.Min.y >> _log2;
            int cx1 = (bounds.Max.x - 1) >> _log2;
            int cy1 = (bounds.Max.y - 1) >> _log2;
            
            for (int cy = cy0; cy <= cy1; cy++)
                for (int cx = cx0; cx <= cx1; cx++)
                    EnsureSlot(new int2(cx, cy));
        }

        /// <summary>
        /// The dense write path used ONLY for Immediate updates to an already resolved grid.
        /// </summary>
        void BroadcastRect(WorldRect rect)
        {
            AlignedRect bounds = rect.Bounds;
            if (bounds.IsEmpty) return;
            int weight = rect.Weight;
            int cx0 = bounds.Min.x >> _log2;
            int cy0 = bounds.Min.y >> _log2;
            int cx1 = (bounds.Max.x - 1) >> _log2;
            int cy1 = (bounds.Max.y - 1) >> _log2;
            bool avx2 = Avx2.IsAvx2Supported;

            for (int cy = cy0; cy <= cy1; cy++)
            for (int cx = cx0; cx <= cx1; cx++)
            {
                if (!_index.TryGetValue(new int2(cx, cy), out int slotIdx)) continue;
                
                // If the chunk isn't active this frame, we shouldn't dense write into old garbage
                ChunkSlot* slotPtr = (ChunkSlot*)_slots.GetUnsafePtr() + slotIdx;
                if (slotPtr->LastWrittenFrame != _frameId) continue; 
                
                int baseX = cx << _log2;
                int baseY = cy << _log2;
                int lx0 = math.max(bounds.Min.x, baseX) - baseX;
                int ly0 = math.max(bounds.Min.y, baseY) - baseY;
                int lx1 = math.min(bounds.Max.x, baseX + _chunkSize) - baseX;
                int ly1 = math.min(bounds.Max.y, baseY + _chunkSize) - baseY;
                if (lx0 >= lx1 || ly0 >= ly1) continue;

                int* field = slotPtr->Field;
                int count = lx1 - lx0;
                for (int ly = ly0; ly < ly1; ly++)
                {
                    int* row = field + ly * _stride + lx0;
                    int x = 0;
                    if (avx2)
                    {
                        v256 wide = Avx.mm256_set1_epi32(weight);
                        for (; x <= count - 8; x += 8)
                            Avx.mm256_storeu_si256(row + x, Avx2.mm256_add_epi32(Avx.mm256_loadu_si256(row + x), wide));
                    }
                    for (; x < count; x++) row[x] += weight;
                }
            }
        }

        // --- JOBS ---

        [BurstCompile(FloatMode = FloatMode.Fast, OptimizeFor = OptimizeFor.Performance)]
        struct RasterizeJob : IJob
        {
            [ReadOnly] public NativeArray<Stamp> Stamps;
            public NativeList<WorldRect> Rects;

            public void Execute()
            {
                for (int i = 0; i < Stamps.Length; i++) 
                    Rasterizer.Emit(Stamps[i], ref Rects);
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
            public uint FrameId;

            public void Execute()
            {
                long chunkBytes = (long)Stride * (ChunkSize + 1) * sizeof(int);

                for (int i = 0; i < Rects.Length; i++)
                {
                    AlignedRect bounds = Rects[i].Bounds;
                    int weight = Rects[i].Weight;
                    
                    int cx0 = bounds.Min.x >> Log2;
                    int cy0 = bounds.Min.y >> Log2;
                    int cx1 = (bounds.Max.x - 1) >> Log2;
                    int cy1 = (bounds.Max.y - 1) >> Log2;

                    for (int cy = cy0; cy <= cy1; cy++)
                    for (int cx = cx0; cx <= cx1; cx++)
                    {
                        if (!Index.TryGetValue(new int2(cx, cy), out int slotIdx)) continue;
                        
                        int baseX = cx << Log2;
                        int baseY = cy << Log2;
                        int lx0 = math.max(bounds.Min.x, baseX) - baseX;
                        int ly0 = math.max(bounds.Min.y, baseY) - baseY;
                        int lx1 = math.min(bounds.Max.x, baseX + ChunkSize) - baseX;
                        int ly1 = math.min(bounds.Max.y, baseY + ChunkSize) - baseY;
                        
                        if (lx0 >= lx1 || ly0 >= ly1) continue;

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
    }

    public unsafe struct ChunkView
    {
        [NativeDisableUnsafePtrRestriction] 
        public int* Field;
        public int2 Base;
        public int Stride;

        public bool IsValid => Field != null;

        public int ReadLocal(int2 localPos) => Field[localPos.y * Stride + localPos.x];

        public int ReadWorld(int2 worldPos)
        {
            int2 local = worldPos - Base;
            return Field[local.y * Stride + local.x];
        }
    }
}