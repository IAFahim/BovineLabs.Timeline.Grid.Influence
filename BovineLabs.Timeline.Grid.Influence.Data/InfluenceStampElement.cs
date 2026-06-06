using Unity.Entities;

namespace BovineLabs.Timeline.Grid.Influence.Data
{
    [InternalBufferCapacity(0)]
    public struct InfluenceStampElement : IBufferElementData
    {
        public InfluenceShape Shape;
    }
}