#if UNITY_EDITOR || BL_DEBUG
using BovineLabs.Core.ConfigVars;
using BovineLabs.Core;
using BovineLabs.Quill;
using BovineLabs.Timeline.Core.Debug;
using BovineLabs.Timeline.Data;
using BovineLabs.Core.Extensions;
using BovineLabs.Core.Iterators;
using BovineLabs.Reaction.Data.Core;
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

        [ConfigVar("influencegizmo.draw-stamps", true, "Draw individual influence stamps as wireframes.")]
        public static readonly SharedStatic<bool> DrawStamps = SharedStatic<bool>.GetOrCreate<Tags.DrawStamps>();

        [ConfigVar("influencegizmo.draw-grid", true, "Draw active chunk boundaries.")]
        public static readonly SharedStatic<bool> DrawGrid = SharedStatic<bool>.GetOrCreate<Tags.DrawGrid>();

        [ConfigVar("influencegizmo.draw-values", true, "Draw accumulated influence values.")]
        public static readonly SharedStatic<bool> DrawValues = SharedStatic<bool>.GetOrCreate<Tags.DrawValues>();

        [ConfigVar("influencegizmo.positive-color", 0.2f, 0.8f, 0.4f, 1.0f, "Positive influence color.")]
        public static readonly SharedStatic<Color> PositiveColor = SharedStatic<Color>.GetOrCreate<Tags.PositiveColor>();

        [ConfigVar("influencegizmo.negative-color", 0.9f, 0.2f, 0.2f, 1.0f, "Negative influence color.")]
        public static readonly SharedStatic<Color> NegativeColor = SharedStatic<Color>.GetOrCreate<Tags.NegativeColor>();

        [ConfigVar("influencegizmo.grid-color", 1.0f, 1.0f, 1.0f, 0.15f, "Chunk boundary color.")]
        public static readonly SharedStatic<Color> GridColor = SharedStatic<Color>.GetOrCreate<Tags.GridColor>();

        struct Tags
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
        EntityQuery _stampQuery;

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
                    ref state, InfluenceDebugSystemConfig.Enabled.Data, out var drawer))
            {
                return;
            }

            var settings = SystemAPI.GetSingleton<InfluenceGridSettings>();
            var basis = new GridBasis(settings.PlaneNormal);

            if (InfluenceDebugSystemConfig.DrawStamps.Data && !_stampQuery.IsEmpty)
            {
                state.Dependency = new DrawStampsJob
                {
                    Drawer = drawer,
                    PositiveColor = InfluenceDebugSystemConfig.PositiveColor.Data,
                    NegativeColor = InfluenceDebugSystemConfig.NegativeColor.Data,
                    CellSize = settings.CellSize,
                    Basis = basis,
                    LocalToWorldLookup = state.GetUnsafeComponentLookup<LocalToWorld>(true),
                    TargetsLookup = state.GetUnsafeComponentLookup<Targets>(true),
                    LinkSources = state.GetUnsafeComponentLookup<EntityLinkSource>(true),
                    Links = state.GetUnsafeBufferLookup<EntityLinkEntry>(true)
                }.ScheduleParallel(_stampQuery, state.Dependency);
            }

            bool drawGrid = InfluenceDebugSystemConfig.DrawGrid.Data;
            bool drawValues = InfluenceDebugSystemConfig.DrawValues.Data;

            if ((drawGrid || drawValues) &&
                SystemAPI.TryGetSingletonRW<InfluenceFieldSingleton>(out var fieldRw) &&
                fieldRw.ValueRO.Field.IsCreated)
            {
                var field = fieldRw.ValueRO.Field;
                var dependency = JobHandle.CombineDependencies(state.Dependency, field.Dependency);

                dependency = new DrawGridJob
                {
                    Drawer = drawer,
                    PositiveColor = InfluenceDebugSystemConfig.PositiveColor.Data,
                    NegativeColor = InfluenceDebugSystemConfig.NegativeColor.Data,
                    GridColor = InfluenceDebugSystemConfig.GridColor.Data,
                    CellSize = settings.CellSize,
                    Basis = basis,
                    DrawGrid = drawGrid,
                    DrawValues = drawValues,
                    ActiveSlots = field.ActiveSlotsList,
                    CoordBySlot = field.CoordBySlotList,
                    Data = field.DataList,
                    Spec = field.Spec
                }.Schedule(dependency);

                field.PublishDependency(dependency);
                fieldRw.ValueRW.Field = field;
                state.Dependency = dependency;
            }
        }

        [BurstCompile]
        partial struct DrawStampsJob : IJobEntity
        {
            public Drawer Drawer;
            public Color PositiveColor;
            public Color NegativeColor;
            public float CellSize;
            public GridBasis Basis;

            [ReadOnly] public UnsafeComponentLookup<LocalToWorld> LocalToWorldLookup;
            [ReadOnly] public UnsafeComponentLookup<Targets> TargetsLookup;
            [ReadOnly] public UnsafeComponentLookup<EntityLinkSource> LinkSources;
            [ReadOnly] public UnsafeBufferLookup<EntityLinkEntry> Links;

            public void Execute(in TrackBinding binding, in InfluenceClipData clip, in ClipWeight weight)
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
                        if (clip.OriginLinkKey != 0 && EntityLinkResolver.TryResolve(baseTarget, clip.OriginLinkKey, LinkSources, Links, out var linked))
                        {
                            originEntity = linked;
                        }
                    }
                }

                if (!LocalToWorldLookup.TryGetComponent(originEntity, out var localToWorld)) return;

                int scaledWeight = (int)math.round(clip.Shape.Weight * weight.Value);
                if (scaledWeight == 0) return;

                InfluenceShape shape = clip.Shape.WithWeight(scaledWeight);
                Color color = shape.Weight >= 0 ? PositiveColor : NegativeColor;
                color.a *= 0.8f;

                float3 origin = localToWorld.Position + math.rotate(localToWorld.Rotation, clip.LocalOffset);
                float heightOffset = math.dot(origin, Basis.Normal);
                float2 projected = Basis.ToGridSpace(origin);

                int2 gridOrigin = new int2(
                    (int)math.floor(projected.x / CellSize),
                    (int)math.floor(projected.y / CellSize));

                float2 snappedGrid = new float2(
                    gridOrigin.x * CellSize + CellSize * 0.5f,
                    gridOrigin.y * CellSize + CellSize * 0.5f);

                float3 snappedWorld = Basis.ToWorldSpace(snappedGrid, heightOffset);

                switch (shape.Kind)
                {
                    case ShapeKind.SolidRect:
                        DrawRect(snappedWorld, shape.RectMin, shape.RectSize, color, heightOffset);
                        break;
                    case ShapeKind.RectShell:
                        DrawRect(snappedWorld, shape.ShellMin, shape.ShellSize, color, heightOffset);
                        DrawRect(snappedWorld,
                            shape.ShellMin + new int2(shape.ShellThickness, shape.ShellThickness),
                            shape.ShellSize - new int2(shape.ShellThickness * 2, shape.ShellThickness * 2),
                            color, heightOffset);
                        break;
                    case ShapeKind.Disc:
                        DrawDisc(snappedWorld, shape.DiscCenter, shape.DiscRadius, color, heightOffset);
                        break;
                    case ShapeKind.Annulus:
                        DrawDisc(snappedWorld, shape.AnnulusCenter, shape.AnnulusOuterRadius, color, heightOffset);
                        if (shape.AnnulusInnerRadius >= 0)
                        {
                            DrawDisc(snappedWorld, shape.AnnulusCenter, shape.AnnulusInnerRadius, color, heightOffset);
                        }

                        break;
                    case ShapeKind.Capsule:
                        DrawCapsule(snappedWorld, shape.CapsuleStart, shape.CapsuleEnd, shape.CapsuleRadius, color, heightOffset);
                        break;
                }

                Drawer.Point(snappedWorld, 0.1f * CellSize, color);

                FixedString32Bytes label = default;
                label.Append((FixedString32Bytes)"Wt: ");
                label.Append(shape.Weight);
                Drawer.Text32(snappedWorld + Basis.Normal * 0.3f, label, color, 10f);
            }

            void DrawRect(float3 localOrigin, int2 min, int2 size, Color color, float heightOffset)
            {
                if (size.x <= 0 || size.y <= 0)
                {
                    return;
                }

                float2 start = Basis.ToGridSpace(localOrigin)
                    + new float2(min.x * CellSize, min.y * CellSize)
                    - new float2(CellSize * 0.5f, CellSize * 0.5f);

                float3 p0 = Basis.ToWorldSpace(start, heightOffset);
                float3 p1 = Basis.ToWorldSpace(start + new float2(size.x * CellSize, 0), heightOffset);
                float3 p2 = Basis.ToWorldSpace(start + new float2(size.x * CellSize, size.y * CellSize), heightOffset);
                float3 p3 = Basis.ToWorldSpace(start + new float2(0, size.y * CellSize), heightOffset);

                Drawer.Line(p0, p1, color);
                Drawer.Line(p1, p2, color);
                Drawer.Line(p2, p3, color);
                Drawer.Line(p3, p0, color);
            }

            void DrawDisc(float3 localOrigin, int2 center, int radius, Color color, float heightOffset)
            {
                if (radius < 0)
                {
                    return;
                }

                float2 grid = Basis.ToGridSpace(localOrigin) + new float2(center.x * CellSize, center.y * CellSize);
                Drawer.Circle(Basis.ToWorldSpace(grid, heightOffset), Basis.Normal * ((radius + 0.5f) * CellSize), color);
            }

            void DrawCapsule(float3 localOrigin, int2 a, int2 b, int radius, Color color, float heightOffset)
            {
                if (radius < 0)
                {
                    return;
                }

                float2 grid = Basis.ToGridSpace(localOrigin);
                float3 centerA = Basis.ToWorldSpace(grid + new float2(a.x * CellSize, a.y * CellSize), heightOffset);
                float3 centerB = Basis.ToWorldSpace(grid + new float2(b.x * CellSize, b.y * CellSize), heightOffset);
                float r = (radius + 0.5f) * CellSize;

                Drawer.Circle(centerA, Basis.Normal * r, color);
                Drawer.Circle(centerB, Basis.Normal * r, color);

                if (math.any(a != b))
                {
                    float3 direction = math.normalize(centerB - centerA);
                    float3 right = math.cross(Basis.Normal, direction) * r;
                    Drawer.Line(centerA + right, centerB + right, color);
                    Drawer.Line(centerA - right, centerB - right, color);
                }
            }
        }

        [BurstCompile]
        struct DrawGridJob : IJob
        {
            public Drawer Drawer;
            public Color PositiveColor;
            public Color NegativeColor;
            public Color GridColor;
            public float CellSize;
            public GridBasis Basis;
            public bool DrawGrid;
            public bool DrawValues;

            [ReadOnly] public NativeList<int> ActiveSlots;
            [ReadOnly] public NativeList<int2> CoordBySlot;
            [ReadOnly] public NativeList<int> Data;
            public GridSpec Spec;

            public void Execute()
            {
                const float renderHeight = 0.05f;
                int chunkSize = Spec.ChunkSize;
                int stride = Spec.Stride;
                int elements = Spec.ElementsPerChunk;

                for (int i = 0; i < ActiveSlots.Length; i++)
                {
                    int slot = ActiveSlots[i];
                    int2 coord = CoordBySlot[slot];
                    int baseIndex = slot * elements;
                    float2 chunkOrigin = new float2(coord.x * chunkSize, coord.y * chunkSize) * CellSize;

                    if (DrawGrid)
                    {
                        float edge = chunkSize * CellSize;
                        float3 p0 = Basis.ToWorldSpace(chunkOrigin, renderHeight);
                        float3 p1 = Basis.ToWorldSpace(chunkOrigin + new float2(edge, 0), renderHeight);
                        float3 p2 = Basis.ToWorldSpace(chunkOrigin + new float2(edge, edge), renderHeight);
                        float3 p3 = Basis.ToWorldSpace(chunkOrigin + new float2(0, edge), renderHeight);

                        Drawer.Line(p0, p1, GridColor);
                        Drawer.Line(p1, p2, GridColor);
                        Drawer.Line(p2, p3, GridColor);
                        Drawer.Line(p3, p0, GridColor);
                    }

                    if (!DrawValues)
                    {
                        continue;
                    }

                    for (int y = 0; y < chunkSize; y++)
                    {
                        for (int x = 0; x < chunkSize; x++)
                        {
                            int value = Data[baseIndex + y * stride + x];
                            if (value == 0)
                            {
                                continue;
                            }

                            float2 cellGrid = chunkOrigin + new float2(x * CellSize, y * CellSize);
                            Color baseColor = value > 0 ? PositiveColor : NegativeColor;

                            Color fill = baseColor;
                            fill.a *= math.clamp(math.abs(value) / 10f, 0.15f, 0.75f);

                            float pad = CellSize * 0.05f;
                            float3 c0 = Basis.ToWorldSpace(cellGrid + new float2(pad, pad), renderHeight);
                            float3 c1 = Basis.ToWorldSpace(cellGrid + new float2(CellSize - pad, pad), renderHeight);
                            float3 c2 = Basis.ToWorldSpace(cellGrid + new float2(CellSize - pad, CellSize - pad), renderHeight);
                            float3 c3 = Basis.ToWorldSpace(cellGrid + new float2(pad, CellSize - pad), renderHeight);

                            Drawer.Quad(c0, c1, c2, c3, fill);

                            FixedString32Bytes text = default;
                            text.Append(value);
                            Drawer.Text32(Basis.ToWorldSpace(cellGrid + new float2(CellSize * 0.5f), renderHeight), text, baseColor, 12f);
                        }
                    }
                }
            }
        }
    }
}
#endif
