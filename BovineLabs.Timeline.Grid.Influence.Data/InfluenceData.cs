namespace BovineLabs.Timeline.Grid.Influence.Data
{
    using Unity.Entities;
    using Unity.Mathematics;

    [System.Serializable]
    public struct InfluenceClipData : IComponentData
    {
        public InfluenceShape Shape;
        public float3 LocalOffset;
    }
}