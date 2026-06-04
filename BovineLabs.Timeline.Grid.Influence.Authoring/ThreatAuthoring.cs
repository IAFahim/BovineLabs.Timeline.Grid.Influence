using System.ComponentModel;
using BovineLabs.Reaction.Authoring.Core;
using BovineLabs.Reaction.Data.Core;
using BovineLabs.Timeline.Authoring;
using BovineLabs.Timeline.EntityLinks.Authoring;
using BovineLabs.Timeline.Grid.Influence.Data;
using BovineLabs.Timeline.Grid.Influence.Features.Threat;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Timeline;

namespace BovineLabs.Timeline.Grid.Influence.Authoring
{
    [System.Serializable]
    public sealed class ThreatClip : DOTSClip, ITimelineClipAsset
    {
        [Header("Origin Routing")]
        public Target originTarget = Target.Owner;
        public EntityLinkSchema originLink;

        [Header("Threat")]
        [Tooltip("Positive = danger radiates here. Negative = this is cover / safe.")]
        public int Weight = 1;
        public int Radius = 5;
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
                Shape = InfluenceShape.Disc(int2.zero, math.max(0, Radius), Weight),
                LocalOffset = LocalOffset,
                OriginTarget = originTarget,
                OriginLinkKey = linkKey
            });
            context.Baker.AddComponent<ThreatClipTag>(clipEntity);
            base.Bake(clipEntity, context);
        }
    }

    [System.Serializable]
    [DisplayName("BovineLabs/Grid/Threat")]
    [TrackColor(0.9f, 0.3f, 0.2f)]
    [TrackClipType(typeof(ThreatClip))]
    [TrackBindingType(typeof(TargetsAuthoring))]
    public sealed class ThreatTrack : DOTSTrack { }
}
