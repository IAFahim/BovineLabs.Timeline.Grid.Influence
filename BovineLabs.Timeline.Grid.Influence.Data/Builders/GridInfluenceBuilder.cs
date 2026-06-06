using BovineLabs.Core.EntityCommands;
using BovineLabs.Reaction.Data.Core;
using Unity.Mathematics;

namespace BovineLabs.Timeline.Grid.Influence.Data.Builders
{
    public struct GridInfluenceBuilder
    {
        public ushort FieldKey;
        public InfluenceShape Shape;
        public float3 LocalOffset;
        public Target OriginTarget;
        public ushort OriginLinkKey;

        public void ApplyTo<T>(ref T builder)
            where T : struct, IEntityCommands
        {
            builder.AddComponent(new InfluenceClipData
            {
                FieldKey = FieldKey,
                Shape = Shape,
                LocalOffset = LocalOffset,
                OriginTarget = OriginTarget,
                OriginLinkKey = OriginLinkKey
            });
        }
    }
}