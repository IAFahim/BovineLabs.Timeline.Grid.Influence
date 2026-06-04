using BovineLabs.Timeline.Grid.Influence.Authoring;
using UnityEditor.Timeline;
using UnityEngine.Timeline;

namespace BovineLabs.Timeline.Grid.Influence.Editor
{
    [CustomTimelineEditor(typeof(GridInfluenceClip))]
    public sealed class GridInfluenceClipEditor : ClipEditor
    {
        public override ClipDrawOptions GetClipOptions(TimelineClip clip)
        {
            var options = base.GetClipOptions(clip);
            if (clip.asset is GridInfluenceClip influence)
                options.highlightColor = GridFieldCategoryPalette.Of(influence.Category);
            return options;
        }
    }
}