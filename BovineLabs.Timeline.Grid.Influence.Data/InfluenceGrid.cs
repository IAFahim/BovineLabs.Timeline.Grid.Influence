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
        public NativeList<int> FreeSlots;
        public NativeReference<uint> FrameId;

        public int ChunkSize;
        public int Log2;
        public int Stride;
        public int Dimension;
        public uint ChunkRetentionFrames;

        public bool IsCreated => ChunkIndex.IsCreated;

        public static InfluenceGrid Create(int chunkSizePowerOfTwo, Allocator allocator)
        {
            return Create(chunkSizePowerOfTwo, 300, allocator);
        }

        public static InfluenceGrid Create(int chunkSizePowerOfTwo, uint chunkRetentionFrames, Allocator allocator)
        {
            if (chunkSizePowerOfTwo < 1 || chunkSizePowerOfTwo > 8)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(chunkSizePowerOfTwo),
                    "Chunk size power must be an exponent in the range [1, 8].");
            }

            int chunkSize = 1 << chunkSizePowerOfTwo;
            int dimension = chunkSize + 1;
            int stride = (dimension + 7) & ~7;

            return new InfluenceGrid
            {
                ChunkIndex = new NativeParallelHashMap<int2, int>(64, allocator),
                ChunkCoords = new NativeList<int2>(64, allocator),
                ChunkLastWrittenFrame = new NativeList<uint>(64, allocator),
                ChunkData = new NativeList<int>(64 * stride * dimension, allocator),
                ActiveChunks = new NativeList<int>(64, allocator),
                FreeSlots = new NativeList<int>(64, allocator),
                FrameId = new NativeReference<uint>(1, allocator),
                ChunkSize = chunkSize,
                Log2 = chunkSizePowerOfTwo,
                Stride = stride,
                Dimension = dimension,
                ChunkRetentionFrames = chunkRetentionFrames
            };
        }

        public void Dispose()
        {
            if (ChunkIndex.IsCreated) ChunkIndex.Dispose();
            if (ChunkCoords.IsCreated) ChunkCoords.Dispose();
            if (ChunkLastWrittenFrame.IsCreated) ChunkLastWrittenFrame.Dispose();
            if (ChunkData.IsCreated) ChunkData.Dispose();
            if (ActiveChunks.IsCreated) ActiveChunks.Dispose();
            if (FreeSlots.IsCreated) FreeSlots.Dispose();
            if (FrameId.IsCreated) FrameId.Dispose();
        }
    }
}
