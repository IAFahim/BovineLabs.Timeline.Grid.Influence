using Unity.Entities;
using Unity.Mathematics;

namespace BovineLabs.Timeline.Grid.Influence.Data
{
    [System.Serializable]
    public struct InfluenceGridSettings : IComponentData
    {
        public float CellSize;
        public float3 PlaneNormal;
        public int ChunkSizePowerOfTwo;
        public uint ChunkRetentionFrames;
    }
}
