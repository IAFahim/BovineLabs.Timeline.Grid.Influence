namespace BovineLabs.Timeline.Grid.Influence.Data
{
    using Unity.Entities;
    using Unity.Mathematics;

    [System.Serializable]
    public struct InfluenceGridSettings : IComponentData
    {
        public float CellSize;
        public float3 PlaneNormal;
    }
}