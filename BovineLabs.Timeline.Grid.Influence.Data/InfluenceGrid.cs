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

        public void BeginFrame()
        {
            ActiveChunks.Clear();
            FrameId.Value++;

            if (FrameId.Value == 0) // overflow
            {
                ResetFrameIdsAfterOverflow();
            }

            EvictInactiveChunks();
        }

        public int GetOrCreateChunkSlot(int2 coord, int elementsPerChunk)
        {
            if (ChunkIndex.TryGetValue(coord, out int slotIdx)) return slotIdx;

            if (FreeSlots.Length > 0)
            {
                int lastFree = FreeSlots.Length - 1;
                slotIdx = FreeSlots[lastFree];
                FreeSlots.RemoveAtSwapBack(lastFree);

                ChunkCoords[slotIdx] = coord;
                ChunkLastWrittenFrame[slotIdx] = 0;
            }
            else
            {
                slotIdx = ChunkCoords.Length;
                ChunkCoords.Add(coord);
                ChunkLastWrittenFrame.Add(0);
                ChunkData.ResizeUninitialized(ChunkData.Length + elementsPerChunk);
            }

            ChunkIndex.Add(coord, slotIdx);
            return slotIdx;
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
                    Base = new int2(
                        IntegerMath.ShiftLeftSaturating(coord.x, Log2),
                        IntegerMath.ShiftLeftSaturating(coord.y, Log2)),
                    Stride = Stride,
                    ChunkSize = ChunkSize
                };
            }
        }

        private void EvictInactiveChunks()
        {
            if (ChunkRetentionFrames == uint.MaxValue) return;

            uint currentFrame = FrameId.Value;
            for (int slotIdx = 0; slotIdx < ChunkLastWrittenFrame.Length; slotIdx++)
            {
                uint lastWrittenFrame = ChunkLastWrittenFrame[slotIdx];
                if (lastWrittenFrame == 0) continue;
                if (currentFrame - lastWrittenFrame <= ChunkRetentionFrames) continue;

                ChunkIndex.Remove(ChunkCoords[slotIdx]);
                ChunkLastWrittenFrame[slotIdx] = 0;
                FreeSlots.Add(slotIdx);
            }
        }

        private void ResetFrameIdsAfterOverflow()
        {
            ChunkIndex.Clear();
            FreeSlots.Clear();

            for (int i = 0; i < ChunkLastWrittenFrame.Length; i++)
            {
                ChunkLastWrittenFrame[i] = 0;
                FreeSlots.Add(i);
            }

            FrameId.Value = 1;
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
