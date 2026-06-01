#if UNITY_EDITOR || BL_DEBUG


namespace BovineLabs.Timeline.Grid.Influence.Debug
{
    using BovineLabs.Core;
    using BovineLabs.Core.ConfigVars;
    using BovineLabs.Quill;
    using BovineLabs.Timeline.Core.Debug;
    using BovineLabs.Timeline.Data;
    using BovineLabs.Timeline.Grid.Influence.Data;
    using Unity.Burst;
    using Unity.Collections;
    using Unity.Entities;
    using Unity.Mathematics;
    using Unity.Transforms;
    using UnityEngine;

    [Configurable]
    public static class InfluenceDebugSystemConfig
    {
        [ConfigVar("influencegizmo.draw-enabled", false, "Enable the grid influence gizmo.")]
        public static readonly SharedStatic<bool> Enabled = SharedStatic<bool>.GetOrCreate<Tags.Enabled>();

        [ConfigVar("influencegizmo.positive-color", 0.2f, 0.8f, 0.4f, 0.8f, "Color for positive influence (Green)")]
        public static readonly SharedStatic<Color> PositiveColor = SharedStatic<Color>.GetOrCreate<Tags.PositiveColor>();

        [ConfigVar("influencegizmo.negative-color", 0.9f, 0.2f, 0.2f, 0.8f, "Color for negative influence (Red)")]
        public static readonly SharedStatic<Color> NegativeColor = SharedStatic<Color>.GetOrCreate<Tags.NegativeColor>();

        [ConfigVar("influencegizmo.cell-size", 1f, "Size of a single grid cell in world units.")]
        public static readonly SharedStatic<float> CellSize = SharedStatic<float>.GetOrCreate<Tags.CellSize>();

        private struct Tags
        {
            public struct Enabled { }
            public struct PositiveColor { }
            public struct NegativeColor { }
            public struct CellSize { }
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
            _query = SystemAPI.QueryBuilder()
                .WithAll<TrackBinding, ActiveInfluence, ClipActive>()
                .Build();
            state.RequireForUpdate(_query);
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            if (!TimelineDebugUtility.TryGetDrawer<InfluenceDebugSystem>(
                  ref state, InfluenceDebugSystemConfig.Enabled.Data, out var drawer))
                return;

            state.Dependency = new DrawJob
            {
                Drawer        = drawer,
                PositiveColor = InfluenceDebugSystemConfig.PositiveColor.Data,
                NegativeColor = InfluenceDebugSystemConfig.NegativeColor.Data,
                CellSize      = InfluenceDebugSystemConfig.CellSize.Data,
                LtwLookup     = SystemAPI.GetComponentLookup<LocalToWorld>(true)
            }.ScheduleParallel(_query, state.Dependency);
        }

        [BurstCompile]
        private partial struct DrawJob : IJobEntity
        {
            public Drawer Drawer;
            public Color PositiveColor;
            public Color NegativeColor;
            public float CellSize;

            [ReadOnly] public ComponentLookup<LocalToWorld> LtwLookup;

            public void Execute(in TrackBinding binding, in ActiveInfluence active)
            {
                if (!LtwLookup.TryGetComponent(binding.Value, out var ltw))
                    return;

                var shape = active.Config.Shape;
                var color = shape.Weight >= 0 ? PositiveColor : NegativeColor;

                var origin = ltw.Position;

                var gridOrigin = new int2(
                    (int)math.floor(origin.x / CellSize),
                    (int)math.floor(origin.z / CellSize)
                );

                var snappedOrigin = new float3(
                    gridOrigin.x * CellSize + CellSize * 0.5f,
                    origin.y,
                    gridOrigin.y * CellSize + CellSize * 0.5f);

                switch (shape.Kind)
                {
                    case ShapeKind.SolidRect:
                    {
                        DrawRect(snappedOrigin, shape.RectMin, shape.RectSize, color);
                        break;
                    }
                    case ShapeKind.RectShell:
                    {
                        DrawRect(snappedOrigin, shape.ShellMin, shape.ShellSize, color);
                        var innerMin = shape.ShellMin + new int2(shape.ShellThickness);
                        var innerSize = shape.ShellSize - new int2(shape.ShellThickness * 2);
                        DrawRect(snappedOrigin, innerMin, innerSize, color);
                        break;
                    }
                    case ShapeKind.Disc:
                    {
                        DrawDisc(snappedOrigin, shape.DiscCenter, shape.DiscRadius, color);
                        break;
                    }
                    case ShapeKind.Annulus:
                    {
                        DrawDisc(snappedOrigin, shape.AnnulusCenter, shape.AnnulusOuter, color);
                        if (shape.AnnulusInner >= 0)
                            DrawDisc(snappedOrigin, shape.AnnulusCenter, shape.AnnulusInner, color);
                        break;
                    }
                    case ShapeKind.Capsule:
                    {
                        DrawCapsule(snappedOrigin, shape.CapsuleA, shape.CapsuleB, shape.CapsuleRadius, color);
                        break;
                    }
                }

                Drawer.Point(snappedOrigin, 0.15f * CellSize, color);
                Drawer.Text32(snappedOrigin + new float3(0f, 0.5f, 0f), $"Wt: {shape.Weight}", color, 12f);
            }

            private void DrawRect(float3 worldOrigin, int2 min, int2 size, Color color)
            {
                if (size.x <= 0 || size.y <= 0) return;

                var start = worldOrigin + new float3(min.x * CellSize, 0, min.y * CellSize);

                start -= new float3(CellSize * 0.5f, 0, CellSize * 0.5f);

                var p0 = start;
                var p1 = start + new float3(size.x * CellSize, 0, 0);
                var p2 = start + new float3(size.x * CellSize, 0, size.y * CellSize);
                var p3 = start + new float3(0, 0, size.y * CellSize);

                Drawer.Quad(p0, p1, p2, p3, color);
            }

            private void DrawDisc(float3 worldOrigin, int2 center, int radius, Color color)
            {
                if (radius < 0) return;

                var worldCenter = worldOrigin + new float3(center.x * CellSize, 0, center.y * CellSize);

                var r = (radius + 0.5f) * CellSize;

                Drawer.Circle(worldCenter, math.up() * r, color);
            }

            private void DrawCapsule(float3 worldOrigin, int2 a, int2 b, int radius, Color color)
            {
                if (radius < 0) return;

                var centerA = worldOrigin + new float3(a.x * CellSize, 0, a.y * CellSize);
                var centerB = worldOrigin + new float3(b.x * CellSize, 0, b.y * CellSize);
                var r = (radius + 0.5f) * CellSize;

                Drawer.Circle(centerA, math.up() * r, color);
                Drawer.Circle(centerB, math.up() * r, color);

                if (math.any(a != b))
                {
                    var dir = math.normalize(centerB - centerA);
                    var right = math.cross(math.up(), dir) * r;

                    Drawer.Line(centerA + right, centerB + right, color);
                    Drawer.Line(centerA - right, centerB - right, color);
                }
            }
        }
    }
}

#endif
