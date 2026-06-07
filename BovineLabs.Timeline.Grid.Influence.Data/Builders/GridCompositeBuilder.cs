using BovineLabs.Core.EntityCommands;
using BovineLabs.Reaction.Data.Core;
using Unity.Entities;
using Unity.Mathematics;

namespace BovineLabs.Timeline.Grid.Influence.Data.Builders
{
    public struct GridCompositeBuilder
    {
        public ushort FieldKey;
        public BlobAssetReference<CompositeShapeBlob> Composite;
        public float3 LocalOffset;
        public Target OriginTarget;
        public ushort OriginLinkKey;

        public void ApplyTo<T>(ref T builder)
            where T : struct, IEntityCommands
        {
            builder.AddComponent(new InfluenceClipData
            {
                FieldKey = FieldKey,
                Composite = Composite,
                LocalOffset = LocalOffset,
                OriginTarget = OriginTarget,
                OriginLinkKey = OriginLinkKey
            });
        }
    }
}
