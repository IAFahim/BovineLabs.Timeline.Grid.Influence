using Unity.Collections;

namespace BovineLabs.Timeline.Grid.Influence.Data
{
    public partial struct InfluenceField
    {
        internal NativeArray<uint> LastWrittenBySlotArray => _lastWrittenBySlot.AsArray();
        internal NativeArray<uint> LastWrittenBySlotDeferred => _lastWrittenBySlot.AsDeferredJobArray();
        internal int DataLength => _data.Length;
    }
}