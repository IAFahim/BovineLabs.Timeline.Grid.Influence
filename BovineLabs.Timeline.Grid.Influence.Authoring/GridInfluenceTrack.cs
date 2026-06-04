using System;
using System.ComponentModel;
using BovineLabs.Reaction.Authoring.Core;
using BovineLabs.Timeline.Authoring;
using UnityEngine.Timeline;

namespace BovineLabs.Timeline.Grid.Influence.Authoring
{
    [Serializable]
    [DisplayName("BovineLabs/Grid/Influence")]
    [TrackColor(0.2f, 0.8f, 0.8f)]
    [TrackClipType(typeof(GridInfluenceClip))]
    [TrackBindingType(typeof(TargetsAuthoring))]
    public sealed class GridInfluenceTrack : DOTSTrack
    {
    }
}