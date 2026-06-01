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

    public struct GridBasis
    {
        public float3 Right;
        public float3 Forward;
        public float3 Normal;

        public GridBasis(float3 normal)
        {
            Normal = math.normalizesafe(normal, math.up());

            // Pick a reference vector that is not parallel to the normal, then build an
            // orthonormal basis for every normal instead of special-casing near-vertical planes.
            float3 reference = math.abs(math.dot(Normal, math.up())) > 0.99f
                ? math.forward()
                : math.up();

            Right = math.normalizesafe(math.cross(reference, Normal), math.right());
            Forward = math.normalize(math.cross(Normal, Right));
        }

        public float2 ToGridSpace(float3 worldPos)
        {
            return new float2(math.dot(worldPos, Right), math.dot(worldPos, Forward));
        }

        public float3 ToWorldSpace(float2 gridPos, float heightOffset = 0f)
        {
            return Right * gridPos.x + Forward * gridPos.y + Normal * heightOffset;
        }
    }
}
