using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace BovineLabs.Timeline.Grid.Influence.Data
{
    public unsafe partial struct InfluenceField
    {
        internal NativeArray<int> ActiveSlotsDeferred => _activeSlots.AsDeferredJobArray();
        internal NativeArray<int2> CoordBySlotDeferred => _coordBySlot.AsDeferredJobArray();
        internal NativeArray<int> DataDeferred => _data.AsDeferredJobArray();
        internal int ActiveChunkCount => _activeSlots.Length;

        internal void PublishDependency(JobHandle handle)
            => _dependency = JobHandle.CombineDependencies(_dependency, handle);
    }
}
