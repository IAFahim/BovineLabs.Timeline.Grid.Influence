using System.ComponentModel;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Timeline;
using BovineLabs.Timeline.Authoring;
using BovineLabs.Timeline.Grid.Influence.Data;
using BovineLabs.Timeline.Grid.Influence.Features.Threat;

namespace BovineLabs.Timeline.Grid.Influence.Authoring
{
    [System.Serializable]
    public sealed class ThreatClip : DOTSClip, ITimelineClipAsset
    {
        [Header("Threat")]
        [Tooltip("Positive = danger radiates here. Negative = this is cover / safe.")]
        public int Weight = 1;
        public int Radius = 5;
        public Vector3 LocalOffset;

        public override double duration => 1.0;
        public ClipCaps clipCaps => ClipCaps.Blending;

        public override void Bake(Entity clipEntity, BakingContext context)
        {
            context.Baker.AddComponent(clipEntity, new InfluenceClipData
            {
                Shape = InfluenceShape.Disc(int2.zero, math.max(0, Radius), Weight),
                LocalOffset = LocalOffset,
            });
            context.Baker.AddComponent<ThreatClipTag>(clipEntity);
            base.Bake(clipEntity, context);
        }
    }

    [System.Serializable]
    [DisplayName("BovineLabs/Grid/Threat")]
    [TrackColor(0.9f, 0.3f, 0.2f)]
    [TrackClipType(typeof(ThreatClip))]
    [TrackBindingType(typeof(GameObject))]
    public sealed class ThreatTrack : DOTSTrack { }
}
