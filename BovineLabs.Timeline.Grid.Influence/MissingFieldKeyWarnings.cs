using Unity.Collections;
using UnityEngine;

namespace BovineLabs.Timeline.Grid.Influence
{
    // Warn-once diagnostic shared by the apply/query/steering systems: a clip whose field schema is not in
    // InfluenceGridSettingsAuthoring.Fields resolves no slot and is a silent no-op. Main-thread only.
    internal struct MissingFieldKeyWarnings
    {
        private NativeHashSet<ushort> _warned;

        public bool IsCreated => _warned.IsCreated;

        public static MissingFieldKeyWarnings Create()
        {
            return new MissingFieldKeyWarnings { _warned = new NativeHashSet<ushort>(8, Allocator.Persistent) };
        }

        public void Dispose()
        {
            if (_warned.IsCreated)
                _warned.Dispose();
        }

        public void Report(ushort key, in NativeHashMap<ushort, int> keyToSlot, string context)
        {
            if (_warned.Contains(key) || keyToSlot.ContainsKey(key))
                return;

            _warned.Add(key);
            Debug.LogWarning($"{context}: references influence field key {key}, which is not registered; add the " +
                "field schema to InfluenceGridSettingsAuthoring.Fields so the field exists in the world (otherwise " +
                "the clip is a silent no-op).");
        }
    }
}
