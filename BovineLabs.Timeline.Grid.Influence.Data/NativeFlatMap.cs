using System;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Mathematics;

namespace BovineLabs.Timeline.Grid.Influence.Data
{
    public unsafe struct NativeFlatMap : INativeDisposable
    {
        internal struct State
        {
            public int2* keys;
            public int* vals;
            public byte* used;
            public int mask;
            public int count;
        }

        [NativeDisableUnsafePtrRestriction] internal State* _state;
        internal Allocator _allocator;

        public bool IsCreated => _state != null && _state->keys != null;
        public int Count => _state != null ? _state->count : 0;

        public static NativeFlatMap Create(int minCapacity, Allocator allocator)
        {
            int cap = 1;
            while (cap < minCapacity) cap <<= 1;
            if (cap < 8) cap = 8;
            var m = new NativeFlatMap { _allocator = allocator };
            m._state = (State*)UnsafeUtility.Malloc(sizeof(State), UnsafeUtility.AlignOf<State>(), allocator);
            m._state->count = 0;
            m._state->mask = cap - 1;
            m.Alloc(cap);
            return m;
        }

        void Alloc(int cap)
        {
            _state->keys = (int2*)UnsafeUtility.Malloc((long)cap * sizeof(int2), UnsafeUtility.AlignOf<int2>(), _allocator);
            _state->vals = (int*)UnsafeUtility.Malloc((long)cap * sizeof(int), UnsafeUtility.AlignOf<int>(), _allocator);
            _state->used = (byte*)UnsafeUtility.Malloc(cap, 1, _allocator);
            UnsafeUtility.MemClear(_state->used, cap);
        }

        static int Hash(int2 c)
        {
            uint h = (uint)(c.x * 73856093) ^ (uint)(c.y * 19349663);
            h ^= h >> 15; h *= 0x2c1b3c6du; h ^= h >> 12;
            return (int)h;
        }

        public bool TryGetValue(int2 key, out int value)
        {
            int i = Hash(key) & _state->mask;
            while (_state->used[i] != 0)
            {
                if (_state->keys[i].x == key.x && _state->keys[i].y == key.y) { value = _state->vals[i]; return true; }
                i = (i + 1) & _state->mask;
            }
            value = 0;
            return false;
        }

        public void Add(int2 key, int value)
        {
            if ((_state->count + 1) * 4 >= (_state->mask + 1) * 3) Grow();
            int i = Hash(key) & _state->mask;
            while (_state->used[i] != 0)
            {
                if (_state->keys[i].x == key.x && _state->keys[i].y == key.y) { _state->vals[i] = value; return; }
                i = (i + 1) & _state->mask;
            }
            _state->used[i] = 1; _state->keys[i] = key; _state->vals[i] = value; _state->count++;
        }

        // Backward-shift deletion: keeps probe sequences contiguous, no tombstones.
        public bool Remove(int2 key)
        {
            int i = Hash(key) & _state->mask;
            while (_state->used[i] != 0)
            {
                if (_state->keys[i].x == key.x && _state->keys[i].y == key.y) break;
                i = (i + 1) & _state->mask;
                if (_state->used[i] == 0) return false;
            }
            if (_state->used[i] == 0) return false;

            int j = i;
            while (true)
            {
                _state->used[i] = 0;
                int k;
                do
                {
                    j = (j + 1) & _state->mask;
                    if (_state->used[j] == 0) { _state->count--; return true; }
                    k = Hash(_state->keys[j]) & _state->mask;
                } while ((i <= j) ? (i < k && k <= j) : (i < k || k <= j));
                _state->keys[i] = _state->keys[j]; _state->vals[i] = _state->vals[j]; _state->used[i] = 1;
                i = j;
            }
        }

        void Grow()
        {
            int2* ok = _state->keys; int* ov = _state->vals; byte* ou = _state->used; int oldCap = _state->mask + 1;
            _state->mask = (oldCap << 1) - 1; _state->count = 0;
            Alloc(oldCap << 1);
            for (int t = 0; t < oldCap; t++) if (ou[t] != 0) Add(ok[t], ov[t]);
            UnsafeUtility.Free(ok, _allocator);
            UnsafeUtility.Free(ov, _allocator);
            UnsafeUtility.Free(ou, _allocator);
        }

        public ReadOnly AsReadOnly() => new ReadOnly(_state->keys, _state->vals, _state->used, _state->mask);

        public void Dispose()
        {
            if (_state != null)
            {
                if (_state->keys != null) UnsafeUtility.Free(_state->keys, _allocator);
                if (_state->vals != null) UnsafeUtility.Free(_state->vals, _allocator);
                if (_state->used != null) UnsafeUtility.Free(_state->used, _allocator);
                UnsafeUtility.Free(_state, _allocator);
                _state = null;
            }
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
