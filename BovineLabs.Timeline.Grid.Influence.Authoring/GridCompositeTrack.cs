using System;
using System.ComponentModel;
using BovineLabs.Reaction.Authoring.Core;
using BovineLabs.Timeline.Authoring;
using UnityEngine.Timeline;

namespace BovineLabs.Timeline.Grid.Influence.Authoring
{
    [Serializable]
    [DisplayName("BovineLabs/Grid/Composite")]
    [TrackColor(0.55f, 0.45f, 0.85f)]
    [TrackClipType(typeof(GridCompositeClip))]
    [TrackBindingType(typeof(TargetsAuthoring))]
    public sealed class GridCompositeTrack : DOTSTrack
    {
    }
}