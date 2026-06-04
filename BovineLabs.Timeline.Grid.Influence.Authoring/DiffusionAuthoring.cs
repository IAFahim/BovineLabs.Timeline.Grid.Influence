using System.ComponentModel;
using BovineLabs.Reaction.Authoring.Core;
using BovineLabs.Reaction.Data.Core;
using BovineLabs.Timeline.Authoring;
using BovineLabs.Timeline.EntityLinks.Authoring;
using BovineLabs.Timeline.Grid.Influence.Data;
using BovineLabs.Timeline.Grid.Influence.Features;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Timeline;

namespace BovineLabs.Timeline.Grid.Influence.Authoring
{
    [System.Serializable]
    public sealed class DiffusionClip : DOTSClip, ITimelineClipAsset
    {
        [Header("Origin Routing")]
        public Target originTarget = Target.Owner;
        public EntityLinkSchema originLink;

        [Header("Diffusion")]
        [Tooltip("Weight to seed the diffusion grid")]
        public int Weight = 1000;
        public Vector3 LocalOffset;

        public override double duration => 1.0;
        public ClipCaps clipCaps => ClipCaps.Blending | ClipCaps.Looping;

        public override void Bake(Entity clipEntity, BakingContext context)
        {
            ushort linkKey = 0;
            if (originLink != null && EntityLinkAuthoringUtility.TryGetKey(originLink, out var key))
            {
                linkKey = key;
            }

            if (context.Binding != null && context.Binding.Target != Entity.Null)
            {
                context.Baker.AddTransformUsageFlags(context.Binding.Target, TransformUsageFlags.Dynamic);
            }

            context.Baker.AddComponent(clipEntity, new InfluenceClipData
            {
                Shape = InfluenceShape.SolidRect(int2.zero, new int2(1, 1), Weight),
                LocalOffset = LocalOffset,
                OriginTarget = originTarget,
                OriginLinkKey = linkKey
            });
            context.Baker.AddComponent<DiffusionClipTag>(clipEntity);
            base.Bake(clipEntity, context);
        }
    }

    [System.Serializable]
    [DisplayName("BovineLabs/Grid/Diffusion")]
    [TrackColor(0.8f, 0.4f, 0.1f)]
    [TrackClipType(typeof(DiffusionClip))]
    [TrackBindingType(typeof(TargetsAuthoring))]
    public sealed class DiffusionTrack : DOTSTrack { }
}
