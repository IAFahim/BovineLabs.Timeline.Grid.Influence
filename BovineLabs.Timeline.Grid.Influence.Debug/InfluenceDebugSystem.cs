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

        [ConfigVar("influencegizmo.positive-color", 0.2f, 0.8f, 0.4f, 0.8f, "Color for positive influence (Green)")]
        public static readonly SharedStatic<Color> PositiveColor = SharedStatic<Color>.GetOrCreate<Tags.PositiveColor>();

        [ConfigVar("influencegizmo.negative-color", 0.9f, 0.2f, 0.2f, 0.8f, "Color for negative influence (Red)")]
        public static readonly SharedStatic<Color> NegativeColor = SharedStatic<Color>.GetOrCreate<Tags.NegativeColor>();

        private struct Tags
        {
            public struct Enabled { }
            public struct PositiveColor { }
            public struct NegativeColor { }
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
            
            _query = SystemAPI.QueryBuilder()
                .WithAll<TrackBinding, InfluenceClipData, ClipActive>()
                .Build();
            state.RequireForUpdate(_query);
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            if (!TimelineDebugUtility.TryGetDrawer<InfluenceDebugSystem>(
                  ref state, InfluenceDebugSystemConfig.Enabled.Data, out var drawer))
                return;

            var settings = SystemAPI.GetSingleton<InfluenceGridSettings>();

            state.Dependency = new DrawJob
            {
                Drawer        = drawer,
                PositiveColor = InfluenceDebugSystemConfig.PositiveColor.Data,
                NegativeColor = InfluenceDebugSystemConfig.NegativeColor.Data,
                CellSize      = settings.CellSize,
                Basis         = new GridBasis(settings.PlaneNormal),
                LtwLookup     = SystemAPI.GetComponentLookup<LocalToWorld>(true),
                LocalTransformLookup = SystemAPI.GetComponentLookup<LocalTransform>(true),
                ParentLookup = SystemAPI.GetComponentLookup<Parent>(true)
            }.ScheduleParallel(_query, state.Dependency);
        }

        [BurstCompile]
        private partial struct DrawJob : IJobEntity
        {
            public Drawer Drawer;
            public Color PositiveColor;
            public Color NegativeColor;
            public float CellSize;
            public GridBasis Basis;

            [ReadOnly] public ComponentLookup<LocalToWorld> LtwLookup;
            [ReadOnly] public ComponentLookup<LocalTransform> LocalTransformLookup;
            [ReadOnly] public ComponentLookup<Parent> ParentLookup;

            private float3 GetAntiJitterPosition(Entity e, float3 fallback)
            {
                if (LocalTransformLookup.HasComponent(e) && !ParentLookup.HasComponent(e))
                {
                    return LocalTransformLookup[e].Position;
                }
                return fallback;
            }

            public void Execute(in TrackBinding binding, in InfluenceClipData active)
            {
                if (!LtwLookup.TryGetComponent(binding.Value, out var ltw))
                    return;

                var shape = active.Shape;
                var color = shape.Weight >= 0 ? PositiveColor : NegativeColor;

                var origin = GetAntiJitterPosition(binding.Value, ltw.Position) + math.rotate(ltw.Rotation, active.LocalOffset);

                // Preserve height offset relative to the normal
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
                        DrawRect(snappedWorldOrigin, shape.RectMin, shape.RectSize, color, heightOffset);
                        break;
                    case ShapeKind.RectShell:
                        DrawRect(snappedWorldOrigin, shape.ShellMin, shape.ShellSize, color, heightOffset);
                        var innerMin = shape.ShellMin + new int2(shape.ShellThickness);
                        var innerSize = shape.ShellSize - new int2(shape.ShellThickness * 2);
                        DrawRect(snappedWorldOrigin, innerMin, innerSize, color, heightOffset);
                        break;
                    case ShapeKind.Disc:
                        DrawDisc(snappedWorldOrigin, shape.DiscCenter, shape.DiscRadius, color, heightOffset);
                        break;
                    case ShapeKind.Annulus:
                        DrawDisc(snappedWorldOrigin, shape.AnnulusCenter, shape.AnnulusOuter, color, heightOffset);
                        if (shape.AnnulusInner >= 0)
                            DrawDisc(snappedWorldOrigin, shape.AnnulusCenter, shape.AnnulusInner, color, heightOffset);
                        break;
                    case ShapeKind.Capsule:
                        DrawCapsule(snappedWorldOrigin, shape.CapsuleA, shape.CapsuleB, shape.CapsuleRadius, color, heightOffset);
                        break;
                }

                Drawer.Point(snappedWorldOrigin, 0.15f * CellSize, color);
                Drawer.Text32(snappedWorldOrigin + Basis.Normal * 0.5f, $"Wt: {shape.Weight}", color, 12f);
            }

            private void DrawRect(float3 localOrigin, int2 min, int2 size, Color color, float heightOffset)
            {
                if (size.x <= 0 || size.y <= 0) return;
                
                var startGridPos = Basis.ToGridSpace(localOrigin) + new float2(min.x * CellSize, min.y * CellSize);
                startGridPos -= new float2(CellSize * 0.5f, CellSize * 0.5f);

                var p0 = Basis.ToWorldSpace(startGridPos, heightOffset);
                var p1 = Basis.ToWorldSpace(startGridPos + new float2(size.x * CellSize, 0), heightOffset);
                var p2 = Basis.ToWorldSpace(startGridPos + new float2(size.x * CellSize, size.y * CellSize), heightOffset);
                var p3 = Basis.ToWorldSpace(startGridPos + new float2(0, size.y * CellSize), heightOffset);

                Drawer.Quad(p0, p1, p2, p3, color);
            }

            private void DrawDisc(float3 localOrigin, int2 center, int radius, Color color, float heightOffset)
            {
                if (radius < 0) return;
                var centerGridPos = Basis.ToGridSpace(localOrigin) + new float2(center.x * CellSize, center.y * CellSize);
                var worldCenter = Basis.ToWorldSpace(centerGridPos, heightOffset);
                
                var r = (radius + 0.5f) * CellSize;
                Drawer.Circle(worldCenter, Basis.Normal * r, color);
            }

            private void DrawCapsule(float3 localOrigin, int2 a, int2 b, int radius, Color color, float heightOffset)
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
    }
}
#endif