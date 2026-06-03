using Unity.Entities;
using Unity.Mathematics;

namespace BovineLabs.Timeline.Grid.Influence.Data
{
    public struct InfluenceClipData : IComponentData
    {
        public InfluenceShape Shape;
        public float3 LocalOffset;
    }
}
