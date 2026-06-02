#if UNITY_EDITOR || BL_DEBUG
using BovineLabs.Core;
using BovineLabs.Core.ConfigVars;
using BovineLabs.Quill;
using BovineLabs.Timeline.Core.Debug;
using BovineLabs.Timeline.Data;
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

        [ConfigVar("influencegizmo.draw-stamps", true, "Draw individual influence stamps (clips) as wireframes.")]
        public static readonly SharedStatic<bool> DrawStamps = SharedStatic<bool>.GetOrCreate<Tags.DrawStamps>();

        [ConfigVar("influencegizmo.draw-grid", true, "Draw the active grid chunk boundaries.")]
        public static readonly SharedStatic<bool> DrawGrid = SharedStatic<bool>.GetOrCreate<Tags.DrawGrid>();

        [ConfigVar("influencegizmo.draw-values", true, "Draw the accumulated influence values in the grid.")]
        public static readonly SharedStatic<bool> DrawValues = SharedStatic<bool>.GetOrCreate<Tags.DrawValues>();

        [ConfigVar("influencegizmo.positive-color", 0.2f, 0.8f, 0.4f, 1.0f, "Color for positive influence (Green)")]
        public static readonly SharedStatic<Color> PositiveColor = SharedStatic<Color>.GetOrCreate<Tags.PositiveColor>();

        [ConfigVar("influencegizmo.negative-color", 0.9f, 0.2f, 0.2f, 1.0f, "Color for negative influence (Red)")]
        public static readonly SharedStatic<Color> NegativeColor = SharedStatic<Color>.GetOrCreate<Tags.NegativeColor>();

        [ConfigVar("influencegizmo.grid-color", 1.0f, 1.0f, 1.0f, 0.15f, "Color for the chunk boundary lines")]
        public static readonly SharedStatic<Color> GridColor = SharedStatic<Color>.GetOrCreate<Tags.GridColor>();

        private struct Tags
        {
            public struct Enabled { }
            public struct DrawStamps { }
            public struct DrawGrid { }
            public struct DrawValues { }
            public struct PositiveColor { }
            public struct NegativeColor { }
            public struct GridColor { }
        }
    }

    [WorldSystemFilter(WorldSystemFilterFlags.LocalSimulation | WorldSystemFilterFlags.ServerSimulation |
                       WorldSystemFilterFlags.ClientSimulation | WorldSystemFilterFlags.Editor)]
    [UpdateInGroup(typeof(DebugSystemGroup))]
    public partial struct InfluenceDebugSystem : ISystem
    {
        private EntityQuery _query;

        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<DrawSystem.Singleton>();
            state.RequireForUpdate<InfluenceGridSettings>();
            state.RequireForUpdate<InfluenceFieldDependency>();

            _query = SystemAPI.QueryBuilder()
                .WithAll<TrackBinding, InfluenceClipData, ClipActive>()
                .Build();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            if (!TimelineDebugUtility.TryGetDrawer<InfluenceDebugSystem>(
                  ref state, InfluenceDebugSystemConfig.Enabled.Data, out var drawer))
                return;

            var settings = SystemAPI.GetSingleton<InfluenceGridSettings>();
            var basis = new GridBasis(settings.PlaneNormal);

            if (InfluenceDebugSystemConfig.DrawStamps.Data && !_query.IsEmpty)
            {
                state.Dependency = new DrawStampsJob
                {
                    Drawer        = drawer,
                    PositiveColor = InfluenceDebugSystemConfig.PositiveColor.Data,
                    NegativeColor = InfluenceDebugSystemConfig.NegativeColor.Data,
                    CellSize      = settings.CellSize,
                    Basis         = basis,
                    LtwLookup     = SystemAPI.GetComponentLookup<LocalToWorld>(true)
                }.ScheduleParallel(_query, state.Dependency);
            }

            bool drawGrid = InfluenceDebugSystemConfig.DrawGrid.Data;
            bool drawValues = InfluenceDebugSystemConfig.DrawValues.Data;

            if ((drawGrid || drawValues) && SystemAPI.TryGetSingleton<InfluenceFieldSingleton>(out var fieldComp))
            {
                var field = fieldComp.Field;
                if (field.IsCreated)
                {
                    var dependencyRw = SystemAPI.GetSingletonRW<InfluenceFieldDependency>();
                    state.Dependency = JobHandle.CombineDependencies(state.Dependency, dependencyRw.ValueRO.Value);

                    state.Dependency = new DrawGridJob
                    {
                        Drawer        = drawer,
                        PositiveColor = InfluenceDebugSystemConfig.PositiveColor.Data,
                        NegativeColor = InfluenceDebugSystemConfig.NegativeColor.Data,
                        GridColor     = InfluenceDebugSystemConfig.GridColor.Data,
                        CellSize      = settings.CellSize,
                        Basis         = basis,
                        DrawGrid      = drawGrid,
                        DrawValues    = drawValues,
                        ActiveSlots   = field.ActiveSlots,
                        CoordBySlot   = field.CoordBySlot,
                        Data          = field.Data,
                        ChunkSize     = field.ChunkSize,
                        Stride        = field.Stride,
                        Dimension     = field.Dimension
                    }.Schedule(state.Dependency);

                    dependencyRw.ValueRW.Value = state.Dependency;
                }
            }
        }

        [BurstCompile]
        private partial struct DrawStampsJob : IJobEntity
        {
            public Drawer Drawer;
            public Color PositiveColor;
            public Color NegativeColor;
            public float CellSize;
            public GridBasis Basis;

            [ReadOnly] public ComponentLookup<LocalToWorld> LtwLookup;

            public void Execute(in TrackBinding binding, in InfluenceClipData active)
            {
                if (!LtwLookup.TryGetComponent(binding.Value, out var ltw))
                    return;

                var shape = active.Shape;

                var color = shape.Weight >= 0 ? PositiveColor : NegativeColor;
                color.a *= 0.8f;

                var origin = ltw.Position + math.rotate(ltw.Rotation, active.LocalOffset);

                var heightOffset = math.dot(origin, Basis.Normal);
                var projectedPos = Basis.ToGridSpace(origin);

                var gridOrigin = new int2(
                    (int)math.floor(projectedPos.x / CellSize),
                    (int)math.floor(projectedPos.y / CellSize)
                );

                var snappedGridPos = new float2(
                    gridOrigin.x * CellSize + CellSize * 0.5f,
                    gridOrigin.y * CellSize + CellSize * 0.5f);

                var snappedWorldOrigin = Basis.ToWorldSpace(snappedGridPos, heightOffset);

                switch (shape.Kind)
                {
                    case ShapeKind.SolidRect:
                        DrawRectWireframe(snappedWorldOrigin, shape.RectMin, shape.RectSize, color, heightOffset);
                        break;
                    case ShapeKind.RectShell:
                        DrawRectWireframe(snappedWorldOrigin, shape.ShellMin, shape.ShellSize, color, heightOffset);
                        var innerMin = shape.ShellMin + new int2(shape.ShellThickness);
                        var innerSize = shape.ShellSize - new int2(shape.ShellThickness * 2);
                        DrawRectWireframe(snappedWorldOrigin, innerMin, innerSize, color, heightOffset);
                        break;
                    case ShapeKind.Disc:
                        DrawDiscWireframe(snappedWorldOrigin, shape.DiscCenter, shape.DiscRadius, color, heightOffset);
                        break;
                    case ShapeKind.Annulus:
                        DrawDiscWireframe(snappedWorldOrigin, shape.AnnulusCenter, shape.AnnulusOuter, color, heightOffset);
                        if (shape.AnnulusInner >= 0)
                            DrawDiscWireframe(snappedWorldOrigin, shape.AnnulusCenter, shape.AnnulusInner, color, heightOffset);
                        break;
                    case ShapeKind.Capsule:
                        DrawCapsuleWireframe(snappedWorldOrigin, shape.CapsuleA, shape.CapsuleB, shape.CapsuleRadius, color, heightOffset);
                        break;
                }

                Drawer.Point(snappedWorldOrigin, 0.1f * CellSize, color);

                FixedString32Bytes label = default;
                label.Append("Wt: ");
                label.Append(shape.Weight);
                Drawer.Text32(snappedWorldOrigin + Basis.Normal * 0.3f, label, color, 10f);
            }

            private void DrawRectWireframe(float3 localOrigin, int2 min, int2 size, Color color, float heightOffset)
            {
                if (size.x <= 0 || size.y <= 0) return;

                var startGridPos = Basis.ToGridSpace(localOrigin) + new float2(min.x * CellSize, min.y * CellSize);
                startGridPos -= new float2(CellSize * 0.5f, CellSize * 0.5f);

                var p0 = Basis.ToWorldSpace(startGridPos, heightOffset);
                var p1 = Basis.ToWorldSpace(startGridPos + new float2(size.x * CellSize, 0), heightOffset);
                var p2 = Basis.ToWorldSpace(startGridPos + new float2(size.x * CellSize, size.y * CellSize), heightOffset);
                var p3 = Basis.ToWorldSpace(startGridPos + new float2(0, size.y * CellSize), heightOffset);

                Drawer.Line(p0, p1, color);
                Drawer.Line(p1, p2, color);
                Drawer.Line(p2, p3, color);
                Drawer.Line(p3, p0, color);
            }

            private void DrawDiscWireframe(float3 localOrigin, int2 center, int radius, Color color, float heightOffset)
            {
                if (radius < 0) return;
                var centerGridPos = Basis.ToGridSpace(localOrigin) + new float2(center.x * CellSize, center.y * CellSize);
                var worldCenter = Basis.ToWorldSpace(centerGridPos, heightOffset);

                var r = (radius + 0.5f) * CellSize;
                Drawer.Circle(worldCenter, Basis.Normal * r, color);
            }

            private void DrawCapsuleWireframe(float3 localOrigin, int2 a, int2 b, int radius, Color color, float heightOffset)
            {
                if (radius < 0) return;

                var localGridSpace = Basis.ToGridSpace(localOrigin);
                var centerA = Basis.ToWorldSpace(localGridSpace + new float2(a.x * CellSize, a.y * CellSize), heightOffset);
                var centerB = Basis.ToWorldSpace(localGridSpace + new float2(b.x * CellSize, b.y * CellSize), heightOffset);
                var r = (radius + 0.5f) * CellSize;

                Drawer.Circle(centerA, Basis.Normal * r, color);
                Drawer.Circle(centerB, Basis.Normal * r, color);

                if (math.any(a != b))
                {
                    var dir = math.normalize(centerB - centerA);
                    var right = math.cross(Basis.Normal, dir) * r;
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

            [ReadOnly] public NativeArray<int> ActiveSlots;
            [ReadOnly] public NativeArray<int2> CoordBySlot;
            [ReadOnly] public NativeArray<int> Data;

            public int ChunkSize;
            public int Stride;
            public int Dimension;

            public void Execute()
            {
                float renderHeightOffset = 0.05f;

                for (int i = 0; i < ActiveSlots.Length; i++)
                {
                    int slotIdx = ActiveSlots[i];
                    int2 coord = CoordBySlot[slotIdx];
                    int baseDataIdx = slotIdx * Stride * Dimension;

                    float2 chunkGridOrigin = new float2(coord.x * ChunkSize, coord.y * ChunkSize) * CellSize;

                    if (DrawGrid)
                    {
                        float3 p0 = Basis.ToWorldSpace(chunkGridOrigin, renderHeightOffset);
                        float3 p1 = Basis.ToWorldSpace(chunkGridOrigin + new float2(ChunkSize * CellSize, 0), renderHeightOffset);
                        float3 p2 = Basis.ToWorldSpace(chunkGridOrigin + new float2(ChunkSize * CellSize, ChunkSize * CellSize), renderHeightOffset);
                        float3 p3 = Basis.ToWorldSpace(chunkGridOrigin + new float2(0, ChunkSize * CellSize), renderHeightOffset);

                        Drawer.Line(p0, p1, GridColor);
                        Drawer.Line(p1, p2, GridColor);
                        Drawer.Line(p2, p3, GridColor);
                        Drawer.Line(p3, p0, GridColor);
                    }

                    if (DrawValues)
                    {
                        for (int y = 0; y < ChunkSize; y++)
                        {
                            for (int x = 0; x < ChunkSize; x++)
                            {
                                int val = Data[baseDataIdx + y * Stride + x];
                                if (val == 0) continue;

                                float2 cellGridPos = chunkGridOrigin + new float2(x * CellSize, y * CellSize);
                                float2 cellCenter = cellGridPos + new float2(CellSize * 0.5f);
                                float3 worldCenter = Basis.ToWorldSpace(cellCenter, renderHeightOffset);

                                Color cellColor = val > 0 ? PositiveColor : NegativeColor;

                                float intensity = math.clamp(math.abs(val) / 10f, 0.15f, 0.75f);
                                Color quadColor = cellColor;
                                quadColor.a *= intensity;

                                float pad = CellSize * 0.05f;
                                float3 c0 = Basis.ToWorldSpace(cellGridPos + new float2(pad, pad), renderHeightOffset);
                                float3 c1 = Basis.ToWorldSpace(cellGridPos + new float2(CellSize - pad, pad), renderHeightOffset);
                                float3 c2 = Basis.ToWorldSpace(cellGridPos + new float2(CellSize - pad, CellSize - pad), renderHeightOffset);
                                float3 c3 = Basis.ToWorldSpace(cellGridPos + new float2(pad, CellSize - pad), renderHeightOffset);

                                Drawer.Quad(c0, c1, c2, c3, quadColor);

                                FixedString32Bytes text = default;
                                text.Append(val);
                                Drawer.Text32(worldCenter, text, cellColor, 12f);
                            }
                        }
                    }
                }
            }
        }
    }
}
#endif