using UnityEngine;

namespace BovineLabs.Timeline.Grid.Influence.Authoring
{
    public enum GridFieldCategory : byte
    {
        Generic,
        Threat,
        Vision,
        Territory,
        Objective,
        Flow
    }

    public static class GridFieldCategoryPalette
    {
        public static Color Of(GridFieldCategory category)
        {
            return category switch
            {
                GridFieldCategory.Threat => new Color(0.85f, 0.25f, 0.25f),
                GridFieldCategory.Vision => new Color(0.95f, 0.85f, 0.30f),
                GridFieldCategory.Territory => new Color(0.30f, 0.55f, 0.90f),
                GridFieldCategory.Objective => new Color(0.55f, 0.35f, 0.85f),
                GridFieldCategory.Flow => new Color(0.30f, 0.80f, 0.55f),
                _ => new Color(0.55f, 0.55f, 0.55f)
            };
        }
    }
}