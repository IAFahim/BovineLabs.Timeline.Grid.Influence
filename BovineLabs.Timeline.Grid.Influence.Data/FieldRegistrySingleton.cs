using Unity.Collections;
using Unity.Entities;

namespace BovineLabs.Timeline.Grid.Influence.Data
{
    public struct FieldRegistrySingleton : IComponentData
    {
        public FieldRegistry Registry;
        public NativeParallelMultiHashMap<int, Stamp> PendingStamps;
    }
}
