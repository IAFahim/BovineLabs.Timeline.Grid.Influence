using BovineLabs.Reaction.Data.Core;
using Unity.Entities;
using Unity.Mathematics;

namespace BovineLabs.Timeline.Grid.Influence.Data
{
    public struct InfluenceQueryData : IComponentData
    {
        public ushort FieldKey;
        public float3 LocalOffset;
        public Target OriginTarget;
        public ushort OriginLinkKey;
    }

    public struct InfluenceQueryResult : IComponentData
    {
        public int Value;
        public int2 Direction;
        public int2 Cell;
        public byte Valid;

        public float ValueSmooth;
        public float2 DirectionSmooth;
    }
}