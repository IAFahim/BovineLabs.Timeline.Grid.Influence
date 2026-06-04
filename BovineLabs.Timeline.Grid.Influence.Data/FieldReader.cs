using System.Runtime.CompilerServices;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Mathematics;

namespace BovineLabs.Timeline.Grid.Influence.Data
{
    public unsafe struct FieldReader
    {
        [ReadOnly] NativeFlatMap.ReadOnly _slotByCoord;
        [ReadOnly] NativeArray<uint> _lastWrittenBySlot;
        [NativeDisableUnsafePtrRestriction] int* _data;
        GridSpec _spec;
        uint _frameId;

        internal FieldReader(
            NativeFlatMap.ReadOnly slotByCoord,
            NativeArray<uint> lastWrittenBySlot,
            int* data,
            in GridSpec spec,
            uint frameId)
        {
            _slotByCoord = slotByCoord;
            _lastWrittenBySlot = lastWrittenBySlot;
            _data = data;
            _spec = spec;
            _frameId = frameId;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int ReadCell(int2 cell)
        {
            int2 coord = ChunkMath.ChunkCoordOf(cell, _spec.Log2);
            if (!_slotByCoord.TryGetValue(coord, out int slot) || _lastWrittenBySlot[slot] != _frameId)
            {
                return 0;
            }

            int2 local = ChunkMath.LocalOf(cell, ChunkMath.ChunkBaseOf(coord, _spec.Log2));
            if (!ChunkMath.ContainsLocal(local, _spec.ChunkSize))
            {
                return 0;
            }

            return _data[ChunkMath.DataIndex(slot, local.x, local.y, _spec)];
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool TryGetChunk(int2 coord, out ChunkView view)
        {
            if (!_slotByCoord.TryGetValue(coord, out int slot) || _lastWrittenBySlot[slot] != _frameId)
            {
                view = default;
                return false;
            }

            view = new ChunkView(_data + slot * _spec.ElementsPerChunk, ChunkMath.ChunkBaseOf(coord, _spec.Log2), _spec);
            return true;
        }
    }

    public unsafe struct ChunkView
    {
        [NativeDisableUnsafePtrRestriction] readonly int* _field;
        readonly int2 _base;
        readonly int _stride;
        readonly int _chunkSize;

        internal ChunkView(int* field, int2 chunkBase, in GridSpec spec)
        {
            _field = field;
            _base = chunkBase;
            _stride = spec.Stride;
            _chunkSize = spec.ChunkSize;
        }

        public bool IsValid => _field != null;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int ReadLocal(int2 local)
        {
            if (!ChunkMath.ContainsLocal(local, _chunkSize))
            {
                return 0;
            }

            return _field[local.y * _stride + local.x];
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int ReadWorld(int2 cell) => ReadLocal(cell - _base);
    }
}
