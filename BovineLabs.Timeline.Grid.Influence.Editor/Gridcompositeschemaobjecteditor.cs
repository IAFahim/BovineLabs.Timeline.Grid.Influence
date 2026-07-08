using System.Collections.Generic;
using BovineLabs.Core.Editor.Inspectors;
using BovineLabs.Timeline.Grid.Influence.Authoring;
using BovineLabs.Timeline.Grid.Influence.Data;
using Unity.Collections;
using Unity.Mathematics;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace BovineLabs.Timeline.Grid.Influence.Editor
{
    [CustomEditor(typeof(GridCompositeSchemaObject))]
    public class GridCompositeSchemaObjectEditor : ElementEditor
    {
        private static readonly List<InfluenceShape> ScratchLayers = new();
        private Texture2D preview;

        protected override void PostElementCreation(VisualElement root, bool createdElements)
        {
            base.PostElementCreation(root, createdElements);
            root.Add(new IMGUIContainer(DrawPreview));
        }

        public static int CollectLayers(GridCompositeSchemaObject schema, List<InfluenceShape> into)
        {
            return CollectLayers(schema, 1, 1f, Quarter.R0, into);
        }

        // Mirrors CompositeBaking.TryBuild so previews match the baked footprint: rotate the base, sample depth
        // weights, apply sign * weightMultiplier per layer, then inset.
        public static int CollectLayers(GridCompositeSchemaObject schema, int sign, float weightMultiplier,
            Quarter rotation, List<InfluenceShape> into)
        {
            into.Clear();
            if (schema == null || schema.Base == null || schema.Base.Kind == ShapeKind.Painted)
                return 0;

            var baseShape = schema.Base.BuildShape(1f).WithWeight(1).Rotated(rotation);
            var weights = schema.Profile.SampleDepthWeights(baseShape, Allocator.Temp);
            for (var i = 0; i < weights.Length; i++)
                weights[i] = Mathf.RoundToInt(weights[i] * weightMultiplier) * sign;

            var count = CompositeBaker.LayerCount(baseShape, weights);
            var layers = new NativeArray<InfluenceShape>(math.max(1, count), Allocator.Temp);
            CompositeBaker.Fill(baseShape, weights, layers, true);
            for (var i = 0; i < count; i++)
                into.Add(layers[i]);

            layers.Dispose();
            weights.Dispose();
            return count;
        }

        private void DrawPreview()
        {
            if (target is not GridCompositeSchemaObject schema)
                return;

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Composite Footprint", EditorStyles.boldLabel);

            var count = CollectLayers(schema, ScratchLayers);
            if (count == 0 || !StampRasterizer.TryAccumulate(ScratchLayers, out var grid, out _, out var size))
            {
                EditorGUILayout.HelpBox("Empty composite for the current parameters.", MessageType.None);
                return;
            }

            BuildTexture(grid, size);

            var available = EditorGUIUtility.currentViewWidth - 40f;
            var cellPixels = math.clamp(available / size.x, 2f, 14f);
            var rect = GUILayoutUtility.GetRect(size.x * cellPixels, size.y * cellPixels, GUILayout.ExpandWidth(false));

            if (Event.current.type == EventType.Repaint)
                GUI.DrawTexture(rect, preview, ScaleMode.StretchToFill, true);

            EditorGUILayout.LabelField($"{size.x} x {size.y} cells, {count} layers", EditorStyles.miniLabel);
        }

        private void BuildTexture(int[] grid, int2 size)
        {
            if (preview == null || preview.width != size.x || preview.height != size.y)
            {
                if (preview != null)
                    DestroyImmediate(preview);

                preview = new Texture2D(size.x, size.y, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point };
            }

            var peak = 1;
            foreach (var value in grid)
                peak = math.max(peak, math.abs(value));

            for (var y = 0; y < size.y; y++)
            for (var x = 0; x < size.x; x++)
            {
                var value = grid[x + y * size.x];
                var intensity = math.abs(value) / (float)peak;
                var color = value == 0
                    ? new Color(0.16f, 0.16f, 0.18f, 1f)
                    : value > 0
                        ? Color.Lerp(new Color(0.2f, 0.18f, 0.1f), new Color(1f, 0.65f, 0.2f), intensity)
                        : Color.Lerp(new Color(0.1f, 0.14f, 0.2f), new Color(0.3f, 0.6f, 1f), intensity);

                // TODO-25 orientation: data row y (world +y) -> texel row y so the preview renders world +y up,
                // matching the scene gizmo and the stamp editor.
                preview.SetPixel(x, y, color);
            }

            preview.Apply();
        }
    }
}