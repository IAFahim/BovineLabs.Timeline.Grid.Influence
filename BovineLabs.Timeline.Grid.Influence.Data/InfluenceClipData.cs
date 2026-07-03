using BovineLabs.Timeline.EntityLinks.Data;
using Unity.Entities;
using Unity.Mathematics;

namespace BovineLabs.Timeline.Grid.Influence.Data
{
    public struct InfluenceClipData : IComponentData
    {
        public ushort FieldKey;
        public InfluenceShape Shape;
        public BlobAssetReference<CompositeShapeBlob> Composite;
        public float3 LocalOffset;

        public EntityLinkRef Origin;
    }
}