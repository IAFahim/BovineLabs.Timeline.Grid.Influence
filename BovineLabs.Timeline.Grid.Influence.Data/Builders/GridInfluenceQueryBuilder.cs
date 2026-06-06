using BovineLabs.Core.EntityCommands;
using BovineLabs.Reaction.Data.Core;
using Unity.Mathematics;

namespace BovineLabs.Timeline.Grid.Influence.Data.Builders
{
    public struct GridInfluenceQueryBuilder
    {
        public ushort FieldKey;
        public float3 LocalOffset;
        public Target OriginTarget;
        public ushort OriginLinkKey;

        public void ApplyTo<T>(ref T builder)
            where T : struct, IEntityCommands
        {
            builder.AddComponent(new InfluenceQueryData
            {
                FieldKey = FieldKey,
                LocalOffset = LocalOffset,
                OriginTarget = OriginTarget,
                OriginLinkKey = OriginLinkKey
            });

            builder.AddComponent(new InfluenceQueryResult());
        }
    }
}