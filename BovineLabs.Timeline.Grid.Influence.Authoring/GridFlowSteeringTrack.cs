using System;
using System.ComponentModel;
using BovineLabs.Reaction.Authoring.Core;
using BovineLabs.Timeline.Authoring;
using UnityEngine.Timeline;

namespace BovineLabs.Timeline.Grid.Influence.Authoring
{
    [Serializable]
    [DisplayName("BovineLabs/Grid/Flow Steering")]
    [TrackColor(0.2f, 0.4f, 0.9f)]
    [TrackClipType(typeof(GridFlowSteeringClip))]
    [TrackBindingType(typeof(TargetsAuthoring))]
    public sealed class GridFlowSteeringTrack : DOTSTrack
    {
    }
}