using System.ComponentModel;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Timeline;
using BovineLabs.Timeline.Authoring;
using BovineLabs.Timeline.Grid.Influence.Data;
using BovineLabs.Timeline.Grid.Influence.Features.Diffusion;

namespace BovineLabs.Timeline.Grid.Influence.Authoring
{
    [System.Serializable]
    public sealed class DiffusionClip : DOTSClip, ITimelineClipAsset
    {
        [Header("Diffusion")]
        [Tooltip("Weight to seed the diffusion grid")]
        public int Weight = 1000;
        public Vector3 LocalOffset;

        public override double duration => 1.0;
        public ClipCaps clipCaps => ClipCaps.None;

        public override void Bake(Entity clipEntity, BakingContext context)
        {
            context.Baker.AddComponent(clipEntity, new InfluenceClipData
            {
                Shape = InfluenceShape.SolidRect(int2.zero, new int2(1,1), Weight),
                LocalOffset = LocalOffset,
            });
            context.Baker.AddComponent<DiffusionClipTag>(clipEntity);
            base.Bake(clipEntity, context);
        }
    }

    [System.Serializable]
    [DisplayName("BovineLabs/Grid/Diffusion")]
    [TrackColor(0.8f, 0.4f, 0.1f)]
    [TrackClipType(typeof(DiffusionClip))]
    [TrackBindingType(typeof(GameObject))]
    public sealed class DiffusionTrack : DOTSTrack { }
}
