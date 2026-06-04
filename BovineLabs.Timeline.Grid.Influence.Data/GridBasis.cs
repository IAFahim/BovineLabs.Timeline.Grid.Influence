using Unity.Mathematics;

namespace BovineLabs.Timeline.Grid.Influence.Data
{
    public readonly struct GridBasis
    {
        public readonly float3 Right;
        public readonly float3 Forward;
        public readonly float3 Normal;

        public GridBasis(float3 normal)
        {
            var unit = math.normalizesafe(normal, math.up());
            var reference = math.abs(math.dot(unit, math.up())) > 0.99f ? math.forward() : math.up();
            var right = math.normalizesafe(math.cross(reference, unit), math.right());
            Right = right;
            Forward = math.normalizesafe(math.cross(unit, right), math.forward());
            Normal = unit;
        }

        public float2 ToGridSpace(float3 worldPosition)
        {
            return new float2(math.dot(worldPosition, Right), math.dot(worldPosition, Forward));
        }

        public float3 ToWorldSpace(float2 gridPosition, float heightOffset)
        {
            return Right * gridPosition.x + Forward * gridPosition.y + Normal * heightOffset;
        }
    }
}