using System.Collections.Generic;
using BovineLabs.Timeline.Grid.Influence.Data;
using Unity.Mathematics;

namespace BovineLabs.Timeline.Grid.Influence.Authoring
{
    public static class GridInfluenceExpansion
    {
        public static void Collect(GridInfluenceClip clip, List<InfluenceShape> into)
        {
            into.Clear();
            CollectStamp(clip, clip.Stamp, into);

            if (clip.ExtraStamps == null)
                return;

            foreach (var extra in clip.ExtraStamps)
                CollectStamp(clip, extra, into);
        }

        public static void CollectStamp(GridInfluenceClip clip, GridStampSchemaObject schema, List<InfluenceShape> into)
        {
            var scale = clip.WeightMultiplier * clip.Polarity.Sign();
            var rings = clip.Falloff == FalloffMode.Stepped ? math.max(0, clip.FalloffSteps) : 0;
            var spacing = clip.FalloffSpacing < 1 ? 1 : clip.FalloffSpacing;

            AddExpanded(into, schema, scale, clip.Rotation, rings, spacing);
        }

        private static void AddExpanded(List<InfluenceShape> into, GridStampSchemaObject schema, float scale,
            Quarter rotation, int rings, int spacing)
        {
            if (schema == null)
                return;

            var baseShapes = new List<InfluenceShape>(1);
            schema.BuildShapes(scale, baseShapes);
            if (baseShapes.Count == 0)
                return;

            var ringCount = schema.Kind == ShapeKind.Painted ? 0 : rings;

            foreach (var baseShape in baseShapes)
                for (var k = 0; k <= ringCount; k++)
                {
                    var shape = baseShape.Inset(k * spacing).Rotated(rotation);
                    if (!Rasterizer.Bounds(shape, default).IsEmpty)
                        into.Add(shape);
                }
        }
    }
}