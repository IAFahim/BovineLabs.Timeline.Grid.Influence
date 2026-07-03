using BovineLabs.Core.EntityCommands;
using BovineLabs.Timeline.EntityLinks.Data;
using Unity.Mathematics;

namespace BovineLabs.Timeline.Grid.Influence.Data.Builders
{
    public struct GridInfluenceQueryBuilder
    {
        public ushort FieldKey;
        public float3 LocalOffset;
        public EntityLinkRef Origin;

        public void ApplyTo<T>(ref T builder)
            where T : struct, IEntityCommands
        {
            builder.AddComponent(new InfluenceQueryData
            {
                FieldKey = FieldKey,
                LocalOffset = LocalOffset,
                Origin = Origin
            });

            builder.AddComponent(new InfluenceQueryResult());
        }
    }
}