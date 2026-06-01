
namespace BovineLabs.Timeline.Grid.Influence.Data
{
    using Unity.Entities;
    using Unity.Mathematics;

    [System.Serializable]
    public struct InfluenceData
    {
        public InfluenceShape Shape;

        public float3 LocalOffset;
    }

    public struct ActiveInfluence : IComponentData, IEnableableComponent
    {
        public InfluenceData Config;
    }
}
