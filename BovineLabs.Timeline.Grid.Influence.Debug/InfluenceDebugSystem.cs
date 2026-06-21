#if UNITY_EDITOR || BL_DEBUG
using BovineLabs.Core;
using BovineLabs.Core.ConfigVars;
using BovineLabs.Core.Extensions;
using BovineLabs.Core.Iterators;
using BovineLabs.Quill;
using BovineLabs.Reaction.Data.Core;
using BovineLabs.Timeline.Core.Debug;
using BovineLabs.Timeline.Data;
using BovineLabs.Timeline.EntityLinks;
using BovineLabs.Timeline.EntityLinks.Data;
using BovineLabs.Timeline.Grid.Influence.Data;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;

namespace BovineLabs.Timeline.Grid.Influence.Debug
{
    [Configurable]
    public static class InfluenceDebugSystemConfig
    {
        [ConfigVar("influencegizmo.draw-enabled", false, "Enable the grid influence gizmo.")]
        public static readonly SharedStatic<bool> Enabled = SharedStatic<bool>.GetOrCreate<Tags.Enabled>();

        [ConfigVar("influencegizmo.draw-stamps", false, "Draw individual influence stamps as wireframes.")]
        public static readonly SharedStatic<bool> DrawStamps = SharedStatic<bool>.GetOrCreate<Tags.DrawStamps>();

        [ConfigVar("influencegizmo.draw-stamp-labels", false, "Draw stamp weight labels.")]
        public static readonly SharedStatic<bool> DrawStampLabels =
            SharedStatic<bool>.GetOrCreate<Tags.DrawStampLabels>();

        [ConfigVar("influencegizmo.draw-grid", true, "Draw active chunk boundaries.")]
        public static readonly SharedStatic<bool> DrawGrid = SharedStatic<bool>.GetOrCreate<Tags.DrawGrid>();

        [ConfigVar("influencegizmo.draw-values", true, "Draw accumulated influence values inside the camera frustum.")]
        public static readonly SharedStatic<bool> DrawValues = SharedStatic<bool>.GetOrCreate<Tags.DrawValues>();

        [ConfigVar("influencegizmo.draw-value-text", false,
            "Draw numeric labels for influence cells. Very expensive on large worlds.")]
        public static readonly SharedStatic<bool> DrawValueText = SharedStatic<bool>.GetOrCreate<Tags.DrawValueText>();

        [ConfigVar("influencegizmo.cull-to-camera", true,
            "Only draw influence debug chunks intersecting the active camera frustum.")]
        public static readonly SharedStatic<bool> CullToCamera = SharedStatic<bool>.GetOrCreate<Tags.CullToCamera>();

        [ConfigVar("influencegizmo.value-stride", 1, "Draw one influence sample every N cells.")]
        public static readonly SharedStatic<int> ValueStride = SharedStatic<int>.GetOrCreate<Tags.ValueStride>();

        [ConfigVar("influencegizmo.positive-color", 0.2f, 0.8f, 0.4f, 1.0f, "Positive influence color.")]
        public static readonly SharedStatic<Color>
            PositiveColor = SharedStatic<Color>.GetOrCreate<Tags.PositiveColor>();

        [ConfigVar("influencegizmo.negative-color", 0.9f, 0.2f, 0.2f, 1.0f, "Negative influence color.")]
        public static readonly SharedStatic<Color>
            NegativeColor = SharedStatic<Color>.GetOrCreate<Tags.NegativeColor>();

        [ConfigVar("influencegizmo.grid-color", 1.0f, 1.0f, 1.0f, 0.15f, "Chunk boundary color.")]
        public static readonly SharedStatic<Color> GridColor = SharedStatic<Color>.GetOrCreate<Tags.GridColor>();

        private struct Tags
        {
            public struct Enabled
            {
            }

            public struct DrawStamps
            {
            }

            public struct DrawStampLabels
            {
            }

            public struct DrawGrid
            {
            }

            public struct DrawValues
            {
            }

            public struct DrawValueText
            {
            }

            public struct CullToCamera
            {
            }

            public struct ValueStride
            {
            }

            public struct PositiveColor
            {
            }

            public struct NegativeColor
            {
            }

            public struct GridColor
            {
            }
        }
    }

    [WorldSystemFilter(WorldSystemFilterFlags.LocalSimulation | WorldSystemFilterFlags.ServerSimulation |
                       WorldSystemFilterFlags.ClientSimulation | WorldSystemFilterFlags.Editor)]
    [UpdateInGroup(typeof(DebugSystemGroup))]
    public partial struct InfluenceDebugSystem : ISystem
    {
        private EntityQuery _stampQuery;

        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<DrawSystem.Singleton>();
            state.RequireForUpdate<InfluenceGridSettings>();

            _stampQuery = SystemAPI.QueryBuilder()
                .WithAll<TrackBinding, InfluenceClipData, ClipActive, ClipWeight>()
                .Build();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            if (!TimelineDebugUtility.TryGetDrawer<InfluenceDebugSystem>(
                    ref state, InfluenceDebugSystemConfig.Enabled.Data, out var drawer,
                    out var viewer, out var hasViewer))
                return;

            var settings = SystemAPI.GetSingleton<InfluenceGridSettings>();
            var cellSize = math.max(0.0001f, settings.CellSize);
            var basis = new GridBasis(settings.PlaneNormal);

            var cullToCamera = InfluenceDebugSystemConfig.CullToCamera.Data;
            var cameraCulling = SystemAPI.GetSingleton<DrawSystem.Singleton>().CameraCulling;

            // CameraCulling.AnyIntersect(default) returns false.
            // If Quill hasn't populated camera data yet, skip to avoid drawing the whole world.
            if (cullToCamera && cameraCulling.IsDefault)
                return;

            if (InfluenceDebugSystemConfig.DrawStamps.Data && !_stampQuery.IsEmpty)
                state.Dependency = new DrawStampsJob
                {
                    Drawer = drawer,
                    Viewer = viewer,
                    HasViewer = hasViewer,
                    PositiveColor = InfluenceDebugSystemConfig.PositiveColor.Data,
                    NegativeColor = InfluenceDebugSystemConfig.NegativeColor.Data,
                    CellSize = cellSize,
                    Basis = basis,
                    CameraCulling = cameraCulling,
                    CullToCamera = cullToCamera,
                    DrawStampLabels = InfluenceDebugSystemConfig.DrawStampLabels.Data,
                    LocalToWorldLookup = state.GetUnsafeComponentLookup<LocalToWorld>(true),
                    TargetsLookup = state.GetUnsafeComponentLookup<Targets>(true),
                    LinkSources = state.GetUnsafeComponentLookup<EntityLinkSource>(true),
                    Links = state.GetUnsafeBufferLookup<EntityLinkEntry>(true)
                }.ScheduleParallel(_stampQuery, state.Dependency);

            var drawGrid = InfluenceDebugSystemConfig.DrawGrid.Data;
            var drawValues = InfluenceDebugSystemConfig.DrawValues.Data;

            if ((drawGrid || drawValues) &&
                SystemAPI.TryGetSingletonRW<FieldRegistrySingleton>(out var regRw))
            {
                ref var reg = ref regRw.ValueRW.Registry;
                for (var i = 0; i < reg.Count; i++)
                {
                    ref var pair = ref reg.Slot(i);
                    if (!pair.Front.IsCreated) continue;

                    state.Dependency = ScheduleDrawGrid(ref pair.Front, state.Dependency, drawer, cellSize, basis,
                        cameraCulling, cullToCamera, drawGrid, drawValues, viewer, hasViewer);
                }
            }
        }

        private static JobHandle ScheduleDrawGrid(ref InfluenceField field, JobHandle inputDeps, Drawer drawer,
            float cellSize, GridBasis basis, CameraCulling cameraCulling, bool cullToCamera, bool drawGrid,
            bool drawValues, float3 viewer, bool hasViewer)
        {
            var dependency = JobHandle.CombineDependencies(inputDeps, field.Dependency);

            dependency = new DrawGridJob
            {
                Drawer = drawer,
                PositiveColor = InfluenceDebugSystemConfig.PositiveColor.Data,
                NegativeColor = InfluenceDebugSystemConfig.NegativeColor.Data,
                GridColor = InfluenceDebugSystemConfig.GridColor.Data,
                CellSize = cellSize,
                Basis = basis,
                DrawGrid = drawGrid,
                DrawValues = drawValues,
                DrawValueText = InfluenceDebugSystemConfig.DrawValueText.Data,
                Viewer = viewer,
                HasViewer = hasViewer,
                ValueStride = math.max(1, InfluenceDebugSystemConfig.ValueStride.Data),
                CameraCulling = cameraCulling,
                CullToCamera = cullToCamera,
                ActiveSlots = field.ActiveSlotsList,
                CoordBySlot = field.CoordBySlotList,
                Data = field.DataList,
                Spec = field.Spec
            }.Schedule(dependency);

            field.PublishDependency(dependency);
            return dependency;
        }

        [BurstCompile]
        private partial struct DrawStampsJob : IJobEntity
        {
            public Drawer Drawer;
            public float3 Viewer;
            public bool HasViewer;
            public Color PositiveColor;
            public Color NegativeColor;
            public float CellSize;
            public GridBasis Basis;
            public CameraCulling CameraCulling;
            public bool CullToCamera;
            public bool DrawStampLabels;

            [ReadOnly] public UnsafeComponentLookup<LocalToWorld> LocalToWorldLookup;
            [ReadOnly] public UnsafeComponentLookup<Targets> TargetsLookup;
            [ReadOnly] public UnsafeComponentLookup<EntityLinkSource> LinkSources;
            [ReadOnly] public UnsafeBufferLookup<EntityLinkEntry> Links;

            private const float RenderHeight = 0.07f;

            public void Execute(in TrackBinding binding, in InfluenceClipData clip, in ClipWeight weight,
                in DynamicBuffer<InfluenceStampElement> extras)
            {
                var targetEntity = binding.Value;
                if (targetEntity == Entity.Null) return;

                var originEntity = targetEntity;

                if (clip.OriginTarget != Target.None && clip.OriginTarget != Target.Self)
                {
                    var targets = TargetsLookup.TryGetComponent(targetEntity, out var t) ? t : default;
                    var baseTarget = targets.Get(clip.OriginTarget, targetEntity);
                    if (baseTarget != Entity.Null)
                    {
                        originEntity = baseTarget;
                        if (clip.OriginLinkKey != 0 && EntityLinkResolver.TryResolve(baseTarget, clip.OriginLinkKey,
                                LinkSources, Links, out var linked)) originEntity = linked;
                    }
                }

                if (!LocalToWorldLookup.TryGetComponent(originEntity, out var localToWorld)) return;

                var origin = localToWorld.Position + math.rotate(localToWorld.Rotation, clip.LocalOffset);
                var projected = Basis.ToGridSpace(origin);

                var gridOrigin = new int2(
                    (int)math.floor(projected.x / CellSize),
                    (int)math.floor(projected.y / CellSize));

                // Draw stamps on the same debug plane as the grid, not at entity height.
                var heightOffset = RenderHeight;

                // Mirror GridInfluenceApplySystem exactly: composite layers XOR the primary shape
                // (if/else, never both), then always the ExtraStamps elements.
                if (clip.Composite.IsCreated)
                {
                    ref var composite = ref clip.Composite.Value;

                    // Cull the whole multi-layer composite at once before touching individual layers.
                    var bounds = CompositeShapeReader.Bounds(ref composite, int2.zero);
                    if (!CullToCamera || (!bounds.IsEmpty && RectVisible(gridOrigin, bounds.Min, bounds.Max, heightOffset)))
                    {
                        ref var layers = ref composite.Layers;
                        for (var i = 0; i < layers.Length; i++)
                            DrawShape(layers[i], weight.Value, gridOrigin, heightOffset);
                    }
                }
                else
                {
                    DrawShape(clip.Shape, weight.Value, gridOrigin, heightOffset);
                }

                for (var i = 0; i < extras.Length; i++)
                    DrawShape(extras[i].Shape, weight.Value, gridOrigin, heightOffset);
            }

            private void DrawShape(in InfluenceShape rawShape, float weightValue, int2 gridOrigin, float heightOffset)
            {
                var scaledWeight = (int)math.round(rawShape.Weight * weightValue);
                if (scaledWeight == 0) return;

                var shape = rawShape.WithWeight(scaledWeight);
                var color = shape.Weight >= 0 ? PositiveColor : NegativeColor;
                color.a *= 0.8f;

                // Cull stamps whose shape bounds are outside the camera frustum.
                if (!IsStampVisible(gridOrigin, shape, heightOffset))
                    return;

                var snappedGrid = new float2(
                    gridOrigin.x * CellSize + CellSize * 0.5f,
                    gridOrigin.y * CellSize + CellSize * 0.5f);

                var snappedWorld = Basis.ToWorldSpace(snappedGrid, heightOffset);
                var tier = TimelineDebugTier.Resolve(snappedWorld, Viewer, HasViewer);

                DrawShapeOutline(shape, snappedWorld, color, heightOffset);

                // Far: just the stamp shape outline (drawn above) is the coarse summary. No center point or text.
                if (tier >= DebugTier.Mid)
                {
                    // Mid: mark the stamp origin + one short label.
                    Drawer.Point(snappedWorld, 0.1f * CellSize, color);
                    if (DrawStampLabels || tier == DebugTier.Close)
                        Drawer.Text32(snappedWorld + Basis.Normal * 0.3f, "Stamp", color, 10f);
                }

                if (tier == DebugTier.Close)
                {
                    // Close: every number — weight + cell origin coords.
                    var readout = new FixedString128Bytes();
                    readout.Append((FixedString32Bytes)"wt ");
                    readout.Append(shape.Weight);
                    readout.Append((FixedString32Bytes)"  cell ");
                    readout.Append(gridOrigin.x);
                    readout.Append((FixedString32Bytes)",");
                    readout.Append(gridOrigin.y);
                    Drawer.Text128(snappedWorld + Basis.Normal * 0.55f, readout, TimelineDebugColors.Label, 9f);
                }
            }

            private bool IsStampVisible(int2 gridOrigin, InfluenceShape shape, float heightOffset)
            {
                if (!CullToCamera)
                    return true;

                return TryGetShapeBounds(shape, out var minCell, out var maxCell) &&
                       RectVisible(gridOrigin, minCell, maxCell, heightOffset);
            }

            private bool RectVisible(int2 gridOrigin, int2 minCell, int2 maxCell, float heightOffset)
            {
                var minGrid = new float2(gridOrigin.x + minCell.x, gridOrigin.y + minCell.y) * CellSize;
                var maxGrid = new float2(gridOrigin.x + maxCell.x, gridOrigin.y + maxCell.y) * CellSize;

                var p0 = Basis.ToWorldSpace(minGrid, heightOffset);
                var p1 = Basis.ToWorldSpace(new float2(maxGrid.x, minGrid.y), heightOffset);
                var p2 = Basis.ToWorldSpace(maxGrid, heightOffset);
                var p3 = Basis.ToWorldSpace(new float2(minGrid.x, maxGrid.y), heightOffset);

                var min = math.min(math.min(p0, p1), math.min(p2, p3));
                var max = math.max(math.max(p0, p1), math.max(p2, p3));

                var margin = math.max(0.1f, CellSize);

                var aabb = new AABB
                {
                    Center = (min + max) * 0.5f,
                    Extents = (max - min) * 0.5f + new float3(margin)
                };

                return CameraCulling.AnyIntersect(aabb);
            }

            private static bool TryGetShapeBounds(InfluenceShape shape, out int2 minCell, out int2 maxCell)
            {
                switch (shape.Kind)
                {
                    case ShapeKind.SolidRect:
                        minCell = shape.RectMin;
                        maxCell = shape.RectMin + shape.RectSize;
                        return shape.RectSize.x > 0 && shape.RectSize.y > 0;

                    case ShapeKind.RectShell:
                        minCell = shape.ShellMin;
                        maxCell = shape.ShellMin + shape.ShellSize;
                        return shape.ShellSize.x > 0 && shape.ShellSize.y > 0;

                    case ShapeKind.Disc:
                        minCell = shape.DiscCenter - shape.DiscRadius;
                        maxCell = shape.DiscCenter + shape.DiscRadius + 1;
                        return shape.DiscRadius >= 0;

                    case ShapeKind.Annulus:
                        minCell = shape.AnnulusCenter - shape.AnnulusOuterRadius;
                        maxCell = shape.AnnulusCenter + shape.AnnulusOuterRadius + 1;
                        return shape.AnnulusOuterRadius >= 0;

                    case ShapeKind.Capsule:
                    {
                        var r = shape.CapsuleRadius;
                        minCell = math.min(shape.CapsuleStart, shape.CapsuleEnd) - r;
                        maxCell = math.max(shape.CapsuleStart, shape.CapsuleEnd) + r + 1;
                        return r >= 0;
                    }

                    case ShapeKind.Sector:
                    {
                        var r = shape.SectorRadius;
                        minCell = shape.SectorCenter - r;
                        maxCell = shape.SectorCenter + r + 1;
                        return r >= 0;
                    }

                    default:
                        minCell = default;
                        maxCell = default;
                        return false;
                }
            }

            private void DrawShapeOutline(in InfluenceShape shape, float3 localOrigin, Color color, float heightOffset)
            {
                switch (shape.Kind)
                {
                    case ShapeKind.SolidRect:
                    case ShapeKind.RoundedRect:
                        DrawRect(localOrigin, shape.RectMin, shape.RectSize, color, heightOffset);
                        break;
                    case ShapeKind.RectShell:
                        DrawRect(localOrigin, shape.ShellMin, shape.ShellSize, color, heightOffset);
                        break;
                    case ShapeKind.Disc:
                        DrawDisc(localOrigin, shape.DiscCenter, shape.DiscRadius, color, heightOffset);
                        break;
                    case ShapeKind.Annulus:
                        DrawDisc(localOrigin, shape.AnnulusCenter, shape.AnnulusOuterRadius, color, heightOffset);
                        DrawDisc(localOrigin, shape.AnnulusCenter, shape.AnnulusInnerRadius, color, heightOffset);
                        break;
                    case ShapeKind.Ellipse:
                        DrawDisc(localOrigin, shape.EllipseCenter, math.cmax(shape.EllipseRadii), color, heightOffset);
                        break;
                    case ShapeKind.Capsule:
                    case ShapeKind.ThickLine:
                        DrawCapsule(localOrigin, shape.CapsuleStart, shape.CapsuleEnd, shape.CapsuleRadius, color,
                            heightOffset);
                        break;
                    case ShapeKind.Sector:
                        DrawDisc(localOrigin, shape.SectorCenter, shape.SectorRadius, color, heightOffset);
                        break;
                }
            }

            private void DrawRect(float3 localOrigin, int2 min, int2 size, Color color, float heightOffset)
            {
                if (size.x <= 0 || size.y <= 0) return;

                var start = Basis.ToGridSpace(localOrigin)
                            + new float2(min.x * CellSize, min.y * CellSize)
                            - new float2(CellSize * 0.5f, CellSize * 0.5f);

                var p0 = Basis.ToWorldSpace(start, heightOffset);
                var p1 = Basis.ToWorldSpace(start + new float2(size.x * CellSize, 0), heightOffset);
                var p2 = Basis.ToWorldSpace(start + new float2(size.x * CellSize, size.y * CellSize), heightOffset);
                var p3 = Basis.ToWorldSpace(start + new float2(0, size.y * CellSize), heightOffset);

                Drawer.Line(p0, p1, color);
                Drawer.Line(p1, p2, color);
                Drawer.Line(p2, p3, color);
                Drawer.Line(p3, p0, color);
            }

            private void DrawDisc(float3 localOrigin, int2 center, int radius, Color color, float heightOffset)
            {
                if (radius < 0) return;

                var grid = Basis.ToGridSpace(localOrigin) + new float2(center.x * CellSize, center.y * CellSize);
                Drawer.Circle(Basis.ToWorldSpace(grid, heightOffset), Basis.Normal * ((radius + 0.5f) * CellSize),
                    color);
            }

            private void DrawCapsule(float3 localOrigin, int2 a, int2 b, int radius, Color color, float heightOffset)
            {
                if (radius < 0) return;

                var grid = Basis.ToGridSpace(localOrigin);
                var centerA = Basis.ToWorldSpace(grid + new float2(a.x * CellSize, a.y * CellSize), heightOffset);
                var centerB = Basis.ToWorldSpace(grid + new float2(b.x * CellSize, b.y * CellSize), heightOffset);
                var r = (radius + 0.5f) * CellSize;

                Drawer.Circle(centerA, Basis.Normal * r, color);
                Drawer.Circle(centerB, Basis.Normal * r, color);

                if (math.any(a != b))
                {
                    var direction = math.normalize(centerB - centerA);
                    var right = math.cross(Basis.Normal, direction) * r;
                    Drawer.Line(centerA + right, centerB + right, color);
                    Drawer.Line(centerA - right, centerB - right, color);
                }
            }
        }

        [BurstCompile]
        private struct DrawGridJob : IJob
        {
            public Drawer Drawer;
            public Color PositiveColor;
            public Color NegativeColor;
            public Color GridColor;
            public float CellSize;
            public GridBasis Basis;
            public bool DrawGrid;
            public bool DrawValues;
            public bool DrawValueText;
            public float3 Viewer;
            public bool HasViewer;
            public int ValueStride;
            public CameraCulling CameraCulling;
            public bool CullToCamera;

            [ReadOnly] public NativeList<int> ActiveSlots;
            [ReadOnly] public NativeList<int2> CoordBySlot;
            [ReadOnly] public NativeList<int> Data;
            public GridSpec Spec;

            private const float RenderHeight = 0.05f;

            public void Execute()
            {
                var chunkSize = Spec.ChunkSize;
                var stride = Spec.Stride;
                var elements = Spec.ElementsPerChunk;
                var step = math.max(1, ValueStride);

                for (var i = 0; i < ActiveSlots.Length; i++)
                {
                    var slot = ActiveSlots[i];
                    var coord = CoordBySlot[slot];

                    // Cull entire chunks outside the camera frustum before doing any work.
                    if (!IsChunkVisible(coord))
                        continue;

                    var baseIndex = slot * elements;
                    var chunkOrigin = new float2(coord.x * chunkSize, coord.y * chunkSize) * CellSize;

                    // Far (always when enabled): chunk bounds are the coarse field outline.
                    if (DrawGrid) DrawChunkBounds(chunkOrigin, chunkSize);

                    if (!DrawValues) continue;

                    // Mid: one short label per visible chunk, above the per-cell fills.
                    var chunkCenterWorld = Basis.ToWorldSpace(
                        chunkOrigin + new float2(chunkSize * CellSize * 0.5f), RenderHeight);
                    if (TimelineDebugTier.Resolve(chunkCenterWorld, Viewer, HasViewer) >= DebugTier.Mid)
                        Drawer.Text32(chunkCenterWorld + Basis.Normal * 0.5f, "Influence", GridColor, 11f);

                    for (var y = 0; y < chunkSize; y += step)
                    for (var x = 0; x < chunkSize; x += step)
                        DrawCell(Data[baseIndex + y * stride + x], chunkOrigin + new float2(x * CellSize, y * CellSize));
                }
            }

            private void DrawCell(int value, float2 cellGrid)
            {
                if (value == 0) return;

                var cellCenterWorld = Basis.ToWorldSpace(cellGrid + new float2(CellSize * 0.5f), RenderHeight);
                var tier = TimelineDebugTier.Resolve(cellCenterWorld, Viewer, HasViewer);

                // Far: the chunk bounds (drawn above) are the coarse summary; skip per-cell fills entirely.
                if (tier == DebugTier.Far)
                    return;

                var baseColor = value > 0 ? PositiveColor : NegativeColor;

                var fill = baseColor;
                fill.a *= math.clamp(math.abs(value) / 10f, 0.15f, 0.75f);

                var pad = CellSize * 0.05f;
                var c0 = Basis.ToWorldSpace(cellGrid + new float2(pad, pad), RenderHeight);
                var c1 = Basis.ToWorldSpace(cellGrid + new float2(CellSize - pad, pad), RenderHeight);
                var c2 = Basis.ToWorldSpace(cellGrid + new float2(CellSize - pad, CellSize - pad),
                    RenderHeight);
                var c3 = Basis.ToWorldSpace(cellGrid + new float2(pad, CellSize - pad), RenderHeight);

                // Mid: the gradient cell fills + one short label once per chunk.
                Drawer.Quad(c0, c1, c2, c3, fill);

                // Close: every cell's value as text. DrawValueText stays as an extra opt-in.
                if (tier != DebugTier.Close)
                    return;

                if (!DrawValueText)
                    return;

                FixedString32Bytes text = default;
                text.Append(value);
                Drawer.Text32(cellCenterWorld, text, baseColor, 12f);
            }

            private bool IsChunkVisible(int2 coord)
            {
                if (!CullToCamera)
                    return true;

                var chunkSize = Spec.ChunkSize;
                var edge = chunkSize * CellSize;

                var origin = new float2(coord.x * chunkSize, coord.y * chunkSize) * CellSize;

                var p0 = Basis.ToWorldSpace(origin, RenderHeight);
                var p1 = Basis.ToWorldSpace(origin + new float2(edge, 0f), RenderHeight);
                var p2 = Basis.ToWorldSpace(origin + new float2(edge, edge), RenderHeight);
                var p3 = Basis.ToWorldSpace(origin + new float2(0f, edge), RenderHeight);

                var min = math.min(math.min(p0, p1), math.min(p2, p3));
                var max = math.max(math.max(p0, p1), math.max(p2, p3));

                var margin = math.max(0.1f, CellSize);

                var aabb = new AABB
                {
                    Center = (min + max) * 0.5f,
                    Extents = (max - min) * 0.5f + new float3(margin)
                };

                return CameraCulling.AnyIntersect(aabb);
            }

            private void DrawChunkBounds(float2 chunkOrigin, int chunkSize)
            {
                var edge = chunkSize * CellSize;
                var p0 = Basis.ToWorldSpace(chunkOrigin, RenderHeight);
                var p1 = Basis.ToWorldSpace(chunkOrigin + new float2(edge, 0), RenderHeight);
                var p2 = Basis.ToWorldSpace(chunkOrigin + new float2(edge, edge), RenderHeight);
                var p3 = Basis.ToWorldSpace(chunkOrigin + new float2(0, edge), RenderHeight);

                Drawer.Line(p0, p1, GridColor);
                Drawer.Line(p1, p2, GridColor);
                Drawer.Line(p2, p3, GridColor);
                Drawer.Line(p3, p0, GridColor);
            }
        }
    }
}
#endif