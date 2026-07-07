using System.Collections.Generic;
using BovineLabs.Timeline.Grid.Influence.Authoring;
using BovineLabs.Timeline.Grid.Influence.Data;
using Unity.Mathematics;
using Unity.Scripting.LifecycleManagement;
using UnityEditor;
using UnityEditor.Timeline;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace BovineLabs.Timeline.Grid.Influence.Editor
{
    [InitializeOnLoad]
    public static partial class GridInfluenceClipGizmo
    {
        private static readonly List<InfluenceShape> Shapes = new();

        static GridInfluenceClipGizmo()
        {
            SceneView.duringSceneGui += OnSceneGui;
        }

        // CoreCLR/no-domain-reload: unsubscribe before this assembly unloads on a code reload, else the sub accumulates per recompile.
        [OnCodeUnloading]
        private static void OnCodeUnloading() => SceneView.duringSceneGui -= OnSceneGui;

        private static void OnSceneGui(SceneView view)
        {
            var clips = TimelineEditor.selectedClips;
            if (clips == null || clips.Length == 0)
                return;

            ResolveGrid(out var basis, out var cellSize);
            var director = TimelineEditor.inspectedDirector;

            foreach (var timelineClip in clips)
            {
                if (timelineClip == null || timelineClip.asset is not GridInfluenceClip clip)
                    continue;

                var transform = ResolveBoundTransform(director, timelineClip);
                if (transform == null)
                    continue;

                CollectClipShapes(clip, Shapes);
                Draw(basis, cellSize, transform, clip, Shapes);
            }
        }

        // Matches GridInfluenceClip.Bake precedence: a Composite with a (non-Painted) Base replaces the primary
        // Stamp, so preview its rotated/weighted layers; otherwise preview the stamp expansion.
        private static void CollectClipShapes(GridInfluenceClip clip, List<InfluenceShape> shapes)
        {
            if (clip.Composite != null && clip.Composite.Base != null &&
                clip.Composite.Base.Kind != ShapeKind.Painted)
            {
                GridCompositeSchemaObjectEditor.CollectLayers(clip.Composite, clip.Polarity.Sign(),
                    clip.WeightMultiplier, clip.Rotation, shapes);
                return;
            }

            GridInfluenceExpansion.Collect(clip, shapes);
        }

        private static Transform ResolveBoundTransform(PlayableDirector director, TimelineClip timelineClip)
        {
            if (director == null)
                return null;

            var track = timelineClip.GetParentTrack();
            if (track == null)
                return null;

            return director.GetGenericBinding(track) switch
            {
                Component component => component.transform,
                GameObject go => go.transform,
                _ => null
            };
        }

        private static void ResolveGrid(out GridBasis basis, out float cellSize)
        {
            basis = new GridBasis(math.up());
            cellSize = 1f;

            var guids = AssetDatabase.FindAssets("t:" + nameof(InfluenceGridSettingsAuthoring));
            if (guids.Length == 0)
                return;

            var path = AssetDatabase.GUIDToAssetPath(guids[0]);
            var settings = AssetDatabase.LoadAssetAtPath<InfluenceGridSettingsAuthoring>(path);
            if (settings == null)
                return;

            basis = new GridBasis(settings.PlaneNormal);
            cellSize = math.max(0.0001f, settings.CellSize);
        }

        private static void Draw(GridBasis basis, float cellSize, Transform transform, GridInfluenceClip clip,
            List<InfluenceShape> shapes)
        {
            if (!StampRasterizer.TryAccumulate(shapes, out var grid, out var min, out var size))
                return;

            var world = (float3)transform.TransformPoint(clip.LocalOffset);
            var projected = basis.ToGridSpace(world);
            var height = math.dot(world, basis.Normal);
            var originCell = new int2(
                (int)math.floor(projected.x / cellSize),
                (int)math.floor(projected.y / cellSize));

            var tint = GridFieldCategoryPalette.Of(clip.Category);

            for (var y = 0; y < size.y; y++)
            for (var x = 0; x < size.x; x++)
            {
                var value = grid[x + y * size.x];
                if (value == 0)
                    continue;

                var cell = originCell + new int2(min.x + x, min.y + y);
                var gridPos = new float2(cell.x * cellSize, cell.y * cellSize);

                var fill = value > 0 ? tint : new Color(0.3f, 0.6f, 1f);
                fill.a = math.clamp(math.abs(value) / 10f, 0.12f, 0.6f);

                Handles.color = fill;
                Handles.DrawAAConvexPolygon(
                    basis.ToWorldSpace(gridPos, height),
                    basis.ToWorldSpace(gridPos + new float2(cellSize, 0), height),
                    basis.ToWorldSpace(gridPos + new float2(cellSize, cellSize), height),
                    basis.ToWorldSpace(gridPos + new float2(0, cellSize), height));
            }
        }
    }
}