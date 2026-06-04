using System;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Mathematics;

namespace BovineLabs.Timeline.Grid.Influence.Data
{
    public unsafe struct NativeFlatMap : INativeDisposable
    {
        [NativeDisableUnsafePtrRestriction] int2* _keys;
        [NativeDisableUnsafePtrRestriction] int* _vals;
        [NativeDisableUnsafePtrRestriction] byte* _used;
        int _mask;       // capacity - 1, capacity is power of two
        int _count;
        Allocator _allocator;

        public bool IsCreated => _keys != null;
        public int Count => _count;

        public static NativeFlatMap Create(int minCapacity, Allocator allocator)
        {
            int cap = 1;
            while (cap < minCapacity) cap <<= 1;
            if (cap < 8) cap = 8;
            var m = new NativeFlatMap { _allocator = allocator, _mask = cap - 1, _count = 0 };
            m.Alloc(cap);
            return m;
        }

        void Alloc(int cap)
        {
            _keys = (int2*)UnsafeUtility.Malloc((long)cap * sizeof(int2), UnsafeUtility.AlignOf<int2>(), _allocator);
            _vals = (int*)UnsafeUtility.Malloc((long)cap * sizeof(int), UnsafeUtility.AlignOf<int>(), _allocator);
            _used = (byte*)UnsafeUtility.Malloc(cap, 1, _allocator);
            UnsafeUtility.MemClear(_used, cap);
        }

        static int Hash(int2 c)
        {
            uint h = (uint)(c.x * 73856093) ^ (uint)(c.y * 19349663);
            h ^= h >> 15; h *= 0x2c1b3c6du; h ^= h >> 12;
            return (int)h;
        }

        public bool TryGetValue(int2 key, out int value)
        {
            int i = Hash(key) & _mask;
            while (_used[i] != 0)
            {
                if (_keys[i].x == key.x && _keys[i].y == key.y) { value = _vals[i]; return true; }
                i = (i + 1) & _mask;
            }
            value = 0;
            return false;
        }

        public void Add(int2 key, int value)
        {
            if ((_count + 1) * 4 >= (_mask + 1) * 3) Grow();
            int i = Hash(key) & _mask;
            while (_used[i] != 0)
            {
                if (_keys[i].x == key.x && _keys[i].y == key.y) { _vals[i] = value; return; }
                i = (i + 1) & _mask;
            }
            _used[i] = 1; _keys[i] = key; _vals[i] = value; _count++;
        }

        // Backward-shift deletion: keeps probe sequences contiguous, no tombstones.
        public bool Remove(int2 key)
        {
            int i = Hash(key) & _mask;
            while (_used[i] != 0)
            {
                if (_keys[i].x == key.x && _keys[i].y == key.y) break;
                i = (i + 1) & _mask;
                if (_used[i] == 0) return false;
            }
            if (_used[i] == 0) return false;

            int j = i;
            while (true)
            {
                _used[i] = 0;
                int k;
                do
                {
                    j = (j + 1) & _mask;
                    if (_used[j] == 0) { _count--; return true; }
                    k = Hash(_keys[j]) & _mask;
                    // is k cyclically in (i, j]? if not, this entry may move into slot i
                } while ((i <= j) ? (i < k && k <= j) : (i < k || k <= j));
                _keys[i] = _keys[j]; _vals[i] = _vals[j]; _used[i] = 1;
                i = j;
            }
        }

        void Grow()
        {
            int2* ok = _keys; int* ov = _vals; byte* ou = _used; int oldCap = _mask + 1;
            _mask = (oldCap << 1) - 1; _count = 0;
            Alloc(oldCap << 1);
            for (int t = 0; t < oldCap; t++) if (ou[t] != 0) Add(ok[t], ov[t]);
            UnsafeUtility.Free(ok, _allocator);
            UnsafeUtility.Free(ov, _allocator);
            UnsafeUtility.Free(ou, _allocator);
        }

        public ReadOnly AsReadOnly() => new ReadOnly(_keys, _vals, _used, _mask);

        public void Dispose()
        {
            if (_keys != null) UnsafeUtility.Free(_keys, _allocator);
            if (_vals != null) UnsafeUtility.Free(_vals, _allocator);
            if (_used != null) UnsafeUtility.Free(_used, _allocator);
            this = default;
        }

        public Unity.Jobs.JobHandle Dispose(Unity.Jobs.JobHandle inputDeps)
        {
            // Pointers are plain allocations; safe to free immediately after deps complete.
            inputDeps.Complete();
            Dispose();
            return default;
        }

        public readonly struct ReadOnly
        {
            [NativeDisableUnsafePtrRestriction] readonly int2* _keys;
            [NativeDisableUnsafePtrRestriction] readonly int* _vals;
            [NativeDisableUnsafePtrRestriction] readonly byte* _used;
            readonly int _mask;

            internal ReadOnly(int2* keys, int* vals, byte* used, int mask)
            { _keys = keys; _vals = vals; _used = used; _mask = mask; }

            public bool TryGetValue(int2 key, out int value)
            {
                int i = Hash(key) & _mask;
                while (_used[i] != 0)
                {
                    if (_keys[i].x == key.x && _keys[i].y == key.y) { value = _vals[i]; return true; }
                    i = (i + 1) & _mask;
                }
                value = 0;
                return false;
            }
        }
    }
}
