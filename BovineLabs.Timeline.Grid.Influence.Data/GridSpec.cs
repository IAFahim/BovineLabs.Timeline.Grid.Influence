using System.Runtime.CompilerServices;
using Unity.Mathematics;

namespace BovineLabs.Timeline.Grid.Influence.Data
{
    public readonly struct GridSpec
    {
        public readonly int Log2;
        public readonly int ChunkSize;
        public readonly int Dimension;
        public readonly int Stride;
        public readonly int ElementsPerChunk;
        public readonly uint RetentionFrames;

        private const int SeamColumns = 1;

        private GridSpec(int log2, int chunkSize, int dimension, int stride, int elementsPerChunk, uint retentionFrames)
        {
            Log2 = log2;
            ChunkSize = chunkSize;
            Dimension = dimension;
            Stride = stride;
            ElementsPerChunk = elementsPerChunk;
            RetentionFrames = retentionFrames;
        }

        public static GridSpec FromPowerOfTwo(int chunkSizePowerOfTwo, uint retentionFrames, int strideAlignment = 8)
        {
            var log2 = math.clamp(chunkSizePowerOfTwo, 1, 8);
            var chunkSize = 1 << log2;
            var dimension = chunkSize + SeamColumns;
            var alignment = math.ceilpow2(math.max(1, strideAlignment));
            var alignMask = alignment - 1;
            var stride = (dimension + alignMask) & ~alignMask;
            var elementsPerChunk = stride * dimension;
            return new GridSpec(log2, chunkSize, dimension, stride, elementsPerChunk, retentionFrames);
        }
    }

    public static class ChunkMath
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int2 ChunkCoordOf(int2 cell, int log2)
        {
            return new int2(cell.x >> log2, cell.y >> log2);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int2 ChunkBaseOf(int2 coord, int log2)
        {
            return new int2(coord.x << log2, coord.y << log2);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ChunkRange ChunkRangeOf(in CellRect bounds, int log2)
        {
            return new ChunkRange(
                new int2(bounds.Min.x >> log2, bounds.Min.y >> log2),
                new int2((bounds.Max.x - 1) >> log2, (bounds.Max.y - 1) >> log2));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int2 LocalOf(int2 cell, int2 chunkBase)
        {
            return cell - chunkBase;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int DataIndex(int slot, int localX, int localY, in GridSpec spec)
        {
            return slot * spec.ElementsPerChunk + localY * spec.Stride + localX;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool ContainsLocal(int2 local, int chunkSize)
        {
            return (uint)local.x < (uint)chunkSize && (uint)local.y < (uint)chunkSize;
        }
    }
}