using Unity.Entities;
using Unity.Mathematics;

namespace BovineLabs.Timeline.Grid.Influence.Data
{
    public struct InfluenceGridSettings : IComponentData
    {
        public float CellSize;
        public float3 PlaneNormal;
    }
}