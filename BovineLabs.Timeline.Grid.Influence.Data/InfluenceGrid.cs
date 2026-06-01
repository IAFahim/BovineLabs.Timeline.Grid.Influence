using System;
using System.Runtime.CompilerServices;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Mathematics;

namespace BovineLabs.Timeline.Grid.Influence.Data
{
    public struct InfluenceGrid : IDisposable
    {
        public NativeParallelHashMap<int2, int> ChunkIndex;
        public NativeList<int2> ChunkCoords;
        public NativeList<uint> ChunkLastWrittenFrame;
        public NativeList<int> ChunkData;
        public NativeList<int> ActiveChunks;
        public NativeReference<uint> FrameId;

        public int ChunkSize;
        public int Log2;
        public int Stride;
        public int Dimension;

        public bool IsCreated => ChunkIndex.IsCreated;

        public static InfluenceGrid Create(int chunkSizePowerOfTwo, Allocator allocator)
        {
            if (chunkSizePowerOfTwo <= 0 || (chunkSizePowerOfTwo & (chunkSizePowerOfTwo - 1)) != 0)
            {
                throw new ArgumentOutOfRangeException(nameof(chunkSizePowerOfTwo), "Chunk size must be a positive power of two.");
            }

            int dimension = chunkSizePowerOfTwo + 1;
            int stride = (dimension + 7) & ~7;

            return new InfluenceGrid
            {
                ChunkIndex = new NativeParallelHashMap<int2, int>(64, allocator),
                ChunkCoords = new NativeList<int2>(64, allocator),
                ChunkLastWrittenFrame = new NativeList<uint>(64, allocator),
                ChunkData = new NativeList<int>(64 * stride * dimension, allocator),
                ActiveChunks = new NativeList<int>(64, allocator),
                FrameId = new NativeReference<uint>(1, allocator),
                ChunkSize = chunkSizePowerOfTwo,
                Log2 = math.tzcnt(chunkSizePowerOfTwo),
                Stride = stride,
                Dimension = dimension
            };
        }

        public void Dispose()
        {
            if (ChunkIndex.IsCreated) ChunkIndex.Dispose();
            if (ChunkCoords.IsCreated) ChunkCoords.Dispose();
            if (ChunkLastWrittenFrame.IsCreated) ChunkLastWrittenFrame.Dispose();
            if (ChunkData.IsCreated) ChunkData.Dispose();
            if (ActiveChunks.IsCreated) ActiveChunks.Dispose();
            if (FrameId.IsCreated) FrameId.Dispose();
        }

        public ChunkView GetChunkView(int2 coord)
        {
            if (!ChunkIndex.TryGetValue(coord, out int slotIdx)) return default;
            if (ChunkLastWrittenFrame[slotIdx] != FrameId.Value) return default;

            unsafe
            {
                int* field = (int*)ChunkData.GetUnsafePtr() + slotIdx * Stride * Dimension;
                return new ChunkView
                {
                    Field = field,
                    Base = new int2(coord.x << Log2, coord.y << Log2),
                    Stride = Stride,
                    ChunkSize = ChunkSize
                };
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

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool ContainsWorld(int2 worldPos)
        {
            int2 local = worldPos - Base;
            return (uint)local.x < (uint)ChunkSize && (uint)local.y < (uint)ChunkSize;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int ReadLocal(int2 localPos)
        {
            if ((uint)localPos.x >= (uint)ChunkSize || (uint)localPos.y >= (uint)ChunkSize) return 0;
            return Field[localPos.y * Stride + localPos.x];
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int ReadWorld(int2 worldPos) => ReadLocal(worldPos - Base);
    }
}