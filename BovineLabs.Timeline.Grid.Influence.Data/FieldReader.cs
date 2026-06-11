using System.Runtime.CompilerServices;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Mathematics;

namespace BovineLabs.Timeline.Grid.Influence.Data
{
    public unsafe struct FieldReader
    {
        private const int MemoMiss = -1;

        [ReadOnly] private readonly NativeFlatMap.ReadOnly _slotByCoord;
        [ReadOnly] private NativeArray<uint> _lastWrittenBySlot;
        [ReadOnly] private NativeArray<int> _data;
        private readonly GridSpec _spec;
        private readonly uint _frameId;

        private int2 _memoCoord;
        private int _memoSlot;
        private byte _memoValid;

        internal FieldReader(
            NativeFlatMap.ReadOnly slotByCoord,
            NativeArray<uint> lastWrittenBySlot,
            NativeArray<int> data,
            in GridSpec spec,
            uint frameId)
        {
            _slotByCoord = slotByCoord;
            _lastWrittenBySlot = lastWrittenBySlot;
            _data = data;
            _spec = spec;
            _frameId = frameId;
            _memoCoord = default;
            _memoSlot = MemoMiss;
            _memoValid = 0;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int ReadCell(int2 cell)
        {
            var slot = ResolveSlot(ChunkMath.ChunkCoordOf(cell, _spec.Log2));
            if (slot < 0) return 0;

            var mask = _spec.ChunkSize - 1;
            return _data[ChunkMath.DataIndex(slot, cell.x & mask, cell.y & mask, _spec)];
        }

        public float SampleBilinear(float2 cellSpace)
        {
            var shifted = cellSpace - 0.5f;
            var floored = math.floor(shifted);
            var fraction = shifted - floored;
            var baseCell = (int2)floored;

            float v00 = ReadCell(baseCell);
            float v10 = ReadCell(baseCell + new int2(1, 0));
            float v01 = ReadCell(baseCell + new int2(0, 1));
            float v11 = ReadCell(baseCell + new int2(1, 1));

            return math.lerp(math.lerp(v00, v10, fraction.x), math.lerp(v01, v11, fraction.x), fraction.y);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool TryGetChunk(int2 coord, out ChunkView view)
        {
            var slot = ResolveSlot(coord);
            if (slot < 0)
            {
                view = default;
                return false;
            }

            view = new ChunkView((int*)_data.GetUnsafeReadOnlyPtr() + slot * _spec.ElementsPerChunk,
                ChunkMath.ChunkBaseOf(coord, _spec.Log2), _spec);
            return true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private int ResolveSlot(int2 coord)
        {
            if (_memoValid != 0 && coord.x == _memoCoord.x && coord.y == _memoCoord.y)
                return _memoSlot;

            var slot = _slotByCoord.TryGetValue(coord, out var found) && _lastWrittenBySlot[found] == _frameId
                ? found
                : MemoMiss;

            _memoCoord = coord;
            _memoSlot = slot;
            _memoValid = 1;
            return slot;
        }
    }

    public unsafe struct ChunkView
    {
        [NativeDisableUnsafePtrRestriction] private readonly int* _field;
        private readonly int2 _base;
        private readonly int _stride;
        private readonly int _chunkSize;

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
            if (!ChunkMath.ContainsLocal(local, _chunkSize)) return 0;

            return _field[local.y * _stride + local.x];
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int ReadWorld(int2 cell)
        {
            return ReadLocal(cell - _base);
        }
    }
}
