#if UNITY_EDITOR || BL_DEBUG
using BovineLabs.Core;
using BovineLabs.Core.ConfigVars;
using BovineLabs.Quill;
using BovineLabs.Timeline.Core.Debug;
using BovineLabs.Timeline.Grid.Influence.Data;
using BovineLabs.Timeline.Grid.Influence.Data.Flows;
using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace BovineLabs.Timeline.Grid.Influence.Debug
{
    [Configurable]
    public static class InfluenceQueryDebugConfig
    {
        [ConfigVar("influencegizmo.draw-query", false,
            "Draw query results as a sampled cell outline and direction arrow.")]
        public static readonly SharedStatic<bool> Enabled = SharedStatic<bool>.GetOrCreate<Tags.Enabled>();

        [ConfigVar("influencegizmo.query-cell-color", 0.95f, 0.65f, 0.2f, 0.9f, "Sampled cell outline color.")]
        public static readonly SharedStatic<Color> CellColor = SharedStatic<Color>.GetOrCreate<Tags.CellColor>();

        [ConfigVar("influencegizmo.query-arrow-color", 1.0f, 0.4f, 0.2f, 1.0f, "Direction arrow color.")]
        public static readonly SharedStatic<Color> ArrowColor = SharedStatic<Color>.GetOrCreate<Tags.ArrowColor>();

        private struct Tags
        {
            public struct Enabled
            {
            }

            public struct CellColor
            {
            }

            public struct ArrowColor
            {
            }
        }
    }

    [WorldSystemFilter(WorldSystemFilterFlags.LocalSimulation | WorldSystemFilterFlags.ServerSimulation |
                       WorldSystemFilterFlags.ClientSimulation | WorldSystemFilterFlags.Editor)]
    [UpdateInGroup(typeof(DebugSystemGroup))]
    public partial struct InfluenceQueryDebugSystem : ISystem
    {
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<DrawSystem.Singleton>();
            state.RequireForUpdate<InfluenceGridSettings>();
        }

        public void OnUpdate(ref SystemState state)
        {
            if (!TimelineDebugUtility.TryGetDrawer<InfluenceQueryDebugSystem>(
                    ref state, InfluenceQueryDebugConfig.Enabled.Data, out var drawer))
                return;

            var settings = SystemAPI.GetSingleton<InfluenceGridSettings>();

            state.Dependency = new DrawQueryJob
            {
                Drawer = drawer,
                Basis = new GridBasis(settings.PlaneNormal),
                CellSize = math.max(0.0001f, settings.CellSize),
                CellColor = InfluenceQueryDebugConfig.CellColor.Data,
                ArrowColor = InfluenceQueryDebugConfig.ArrowColor.Data
            }.Schedule(state.Dependency);
        }

        [BurstCompile]
        private partial struct DrawQueryJob : IJobEntity
        {
            public Drawer Drawer;
            public GridBasis Basis;
            public float CellSize;
            public Color CellColor;
            public Color ArrowColor;

            private void Execute(in InfluenceQueryResult result)
            {
                if (result.Valid == 0)
                    return;

                const float renderHeight = 0.06f;

                var min = new float2(result.Cell.x, result.Cell.y) * CellSize;
                var max = min + new float2(CellSize, CellSize);

                var c00 = Basis.ToWorldSpace(min, renderHeight);
                var c10 = Basis.ToWorldSpace(new float2(max.x, min.y), renderHeight);
                var c11 = Basis.ToWorldSpace(max, renderHeight);
                var c01 = Basis.ToWorldSpace(new float2(min.x, max.y), renderHeight);

                Drawer.Line(c00, c10, CellColor);
                Drawer.Line(c10, c11, CellColor);
                Drawer.Line(c11, c01, CellColor);
                Drawer.Line(c01, c00, CellColor);

                var direction = FieldGradient.Normalized(result.Direction);
                if (math.lengthsq(direction) < 1e-6f)
                    return;

                DrawArrow((min + max) * 0.5f, direction, renderHeight);
            }

            private void DrawArrow(float2 center, float2 direction, float height)
            {
                var half = CellSize * 0.42f;
                var headLength = CellSize * 0.18f;
                var perpendicular = new float2(-direction.y, direction.x);

                var tailGrid = center - direction * half;
                var tipGrid = center + direction * half;

                var tail = Basis.ToWorldSpace(tailGrid, height);
                var tip = Basis.ToWorldSpace(tipGrid, height);
                var wingA = Basis.ToWorldSpace(tipGrid + (-direction + perpendicular) * headLength, height);
                var wingB = Basis.ToWorldSpace(tipGrid + (-direction - perpendicular) * headLength, height);

                Drawer.Line(tail, tip, ArrowColor);
                Drawer.Line(tip, wingA, ArrowColor);
                Drawer.Line(tip, wingB, ArrowColor);
            }
        }
    }
}
#endif