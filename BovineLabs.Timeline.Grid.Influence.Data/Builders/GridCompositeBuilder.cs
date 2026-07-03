using BovineLabs.Core.EntityCommands;
using BovineLabs.Timeline.EntityLinks.Data;
using Unity.Entities;
using Unity.Mathematics;

namespace BovineLabs.Timeline.Grid.Influence.Data.Builders
{
    public struct GridCompositeBuilder
    {
        public ushort FieldKey;
        public BlobAssetReference<CompositeShapeBlob> Composite;
        public float3 LocalOffset;
        public EntityLinkRef Origin;

        public void ApplyTo<T>(ref T builder)
            where T : struct, IEntityCommands
        {
            builder.AddComponent(new InfluenceClipData
            {
                FieldKey = FieldKey,
                Composite = Composite,
                LocalOffset = LocalOffset,
                Origin = Origin
            });
        }
    }
}