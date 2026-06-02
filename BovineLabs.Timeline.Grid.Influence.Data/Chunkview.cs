using System.Runtime.CompilerServices;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Mathematics;

namespace BovineLabs.Timeline.Grid.Influence.Data
{
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
            if ((uint)localPos.x >= (uint)ChunkSize || (uint)localPos.y >= (uint)ChunkSize)
            {
                return 0;
            }

            return Field[localPos.y * Stride + localPos.x];
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int ReadWorld(int2 worldPos) => ReadLocal(worldPos - Base);
    }
}