using System;
using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Mathematics;
using static Unity.Burst.Intrinsics.X86;

namespace Influence
{
    public unsafe struct InfluenceField : IDisposable
    {
        NativeParallelHashMap<int2, int> _index;
        NativeList<ChunkSlot> _slots;
        int _chunkSize;
        int _log2;
        int _stride;
        int _dimension;
        Allocator _allocator;

        struct ChunkSlot
        {
            public int2 Coord;
            public IntPtr Field;
        }

        public static InfluenceField Create(int chunkSizePowerOfTwo, Allocator allocator)
        {
            int log2 = IntegerMath.FloorLog2PowerOfTwo(chunkSizePowerOfTwo);
            int dimension = chunkSizePowerOfTwo + 1;
            int stride = (dimension + 7) & ~7;
            return new InfluenceField
            {
                _index = new NativeParallelHashMap<int2, int>(64, allocator),
                _slots = new NativeList<ChunkSlot>(64, allocator),
                _chunkSize = chunkSizePowerOfTwo,
                _log2 = log2,
                _stride = stride,
                _dimension = dimension,
                _allocator = allocator
            };
        }

        public void BuildBatched(NativeArray<Stamp> stamps)
        {
            NativeList<WorldRect> rects = new NativeList<WorldRect>(256, Allocator.TempJob);
            new RasterizeJob { Stamps = stamps, Rects = rects }.Run();

            for (int i = 0; i < stamps.Length; i++)
                EnsureBounds(Rasterizer.Bounds(stamps[i].Shape, stamps[i].Origin));

            ClearActive();

            new ScatterJob
            {
                Rects = rects.AsArray(),
                Index = _index,
                Slots = _slots.AsArray(),
                ChunkSize = _chunkSize,
                Log2 = _log2,
                Stride = _stride
            }.Run();

            new ResolveJob
            {
                Slots = _slots.AsArray(),
                Stride = _stride,
                Dimension = _dimension
            }.Schedule(_slots.Length, 1).Complete();

            rects.Dispose();
        }

        public void AddImmediate(Stamp stamp)
        {
            EnsureBounds(Rasterizer.Bounds(stamp.Shape, stamp.Origin));
            NativeList<WorldRect> rects = new NativeList<WorldRect>(64, Allocator.Temp);
            Rasterizer.Emit(stamp, ref rects);
            for (int i = 0; i < rects.Length; i++) BroadcastRect(rects[i]);
            rects.Dispose();
        }

        public void RemoveImmediate(Stamp stamp) => AddImmediate(stamp.Negated());

        public int Read(int2 cell)
        {
            int2 coord = new int2(cell.x >> _log2, cell.y >> _log2);
            if (!_index.TryGetValue(coord, out int slot)) return 0;
            int baseX = coord.x << _log2;
            int baseY = coord.y << _log2;
            int* field = (int*)_slots[slot].Field.ToPointer();
            return field[(cell.y - baseY) * _stride + (cell.x - baseX)];
        }

        public void Dispose()
        {
            if (_slots.IsCreated)
            {
                for (int i = 0; i < _slots.Length; i++)
                {
                    void* p = (void*)_slots[i].Field;
                    if (p != null) UnsafeUtility.Free(p, _allocator);
                }
                _slots.Dispose();
            }
            if (_index.IsCreated) _index.Dispose();
        }

        int EnsureSlot(int2 coord)
        {
            if (_index.TryGetValue(coord, out int existing)) return existing;
            long bytes = (long)_stride * _dimension * sizeof(int);
            void* ptr = UnsafeUtility.Malloc(bytes, 32, _allocator);
            UnsafeUtility.MemClear(ptr, bytes);
            int index = _slots.Length;
            _slots.Add(new ChunkSlot { Coord = coord, Field = (IntPtr)ptr });
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

        void ClearActive()
        {
            long bytes = (long)_stride * _dimension * sizeof(int);
            for (int i = 0; i < _slots.Length; i++)
                UnsafeUtility.MemClear((void*)_slots[i].Field, bytes);
        }

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
                if (!_index.TryGetValue(new int2(cx, cy), out int slot)) continue;
                int baseX = cx << _log2;
                int baseY = cy << _log2;
                int lx0 = math.max(bounds.Min.x, baseX) - baseX;
                int ly0 = math.max(bounds.Min.y, baseY) - baseY;
                int lx1 = math.min(bounds.Max.x, baseX + _chunkSize) - baseX;
                int ly1 = math.min(bounds.Max.y, baseY + _chunkSize) - baseY;
                if (lx0 >= lx1 || ly0 >= ly1) continue;

                int* field = (int*)_slots[slot].Field.ToPointer();
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

        [BurstCompile]
        struct RasterizeJob : IJob
        {
            [ReadOnly] public NativeArray<Stamp> Stamps;
            public NativeList<WorldRect> Rects;

            public void Execute()
            {
                for (int i = 0; i < Stamps.Length; i++) Rasterizer.Emit(Stamps[i], ref Rects);
            }
        }

        [BurstCompile]
        struct ScatterJob : IJob
        {
            [ReadOnly] public NativeArray<WorldRect> Rects;
            [ReadOnly] public NativeParallelHashMap<int2, int> Index;
            [ReadOnly] public NativeArray<ChunkSlot> Slots;
            public int ChunkSize;
            public int Log2;
            public int Stride;

            public void Execute()
            {
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
                        if (!Index.TryGetValue(new int2(cx, cy), out int slot)) continue;
                        int baseX = cx << Log2;
                        int baseY = cy << Log2;
                        int lx0 = math.max(bounds.Min.x, baseX) - baseX;
                        int ly0 = math.max(bounds.Min.y, baseY) - baseY;
                        int lx1 = math.min(bounds.Max.x, baseX + ChunkSize) - baseX;
                        int ly1 = math.min(bounds.Max.y, baseY + ChunkSize) - baseY;
                        if (lx0 >= lx1 || ly0 >= ly1) continue;

                        int* field = (int*)Slots[slot].Field.ToPointer();
                        field[ly0 * Stride + lx0] += weight;
                        field[ly0 * Stride + lx1] -= weight;
                        field[ly1 * Stride + lx0] -= weight;
                        field[ly1 * Stride + lx1] += weight;
                    }
                }
            }
        }

        [BurstCompile]
        struct ResolveJob : IJobParallelFor
        {
            [ReadOnly] public NativeArray<ChunkSlot> Slots;
            public int Stride;
            public int Dimension;

            public void Execute(int index)
                => PrefixSumResolve.Run((int*)Slots[index].Field.ToPointer(), Stride, Dimension);
        }
    }
}
