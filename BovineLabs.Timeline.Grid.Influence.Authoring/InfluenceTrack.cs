using System.ComponentModel;
using BovineLabs.Timeline.Authoring;
using UnityEngine.Timeline;

namespace BovineLabs.Timeline.Grid.Influence.Authoring
{
    [System.Serializable]
    [DisplayName("BovineLabs/Grid/Influence")]
    [TrackColor(0.2f, 0.8f, 0.4f)]
    [TrackClipType(typeof(InfluenceClip))]
    [TrackBindingType(typeof(UnityEngine.GameObject))]
    public sealed class InfluenceTrack : DOTSTrack
    {
    }
}
