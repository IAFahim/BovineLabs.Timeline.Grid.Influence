using Unity.Collections;

namespace BovineLabs.Timeline.Grid.Influence.Data
{
    public partial struct InfluenceField
    {
        internal NativeArray<uint> LastWrittenBySlotArray => _lastWrittenBySlot.AsArray();
        internal int DataLength => _data.Length;
    }
}