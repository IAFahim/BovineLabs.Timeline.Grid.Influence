#if UNITY_EDITOR || BL_DEBUG
using BovineLabs.Core;
using BovineLabs.Core.ConfigVars;
using BovineLabs.Quill;
using BovineLabs.Timeline.Core.Debug;
using BovineLabs.Timeline.Grid.Influence.Data;
using BovineLabs.Timeline.Grid.Influence.Data.Flows;
using Unity.Burst;
using Unity.Collections;
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
                    ref state, InfluenceQueryDebugConfig.Enabled.Data, out var drawer,
                    out var viewer, out var hasViewer))
                return;

            var settings = SystemAPI.GetSingleton<InfluenceGridSettings>();

            state.Dependency = new DrawQueryJob
            {
                Drawer = drawer,
                Viewer = viewer,
                HasViewer = hasViewer,
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
            public float3 Viewer;
            public bool HasViewer;
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

                var center = (min + max) * 0.5f;
                var centerWorld = Basis.ToWorldSpace(center, renderHeight);
                var tier = TimelineDebugTier.Resolve(centerWorld, Viewer, HasViewer);

                var direction = FieldGradient.Normalized(result.Direction);
                var hasDirection = math.lengthsq(direction) >= 1e-6f;

                if (hasDirection)
                    DrawArrow(center, direction, renderHeight);
                else
                    Drawer.Point(centerWorld, 0.12f * CellSize, ArrowColor);

                if (tier >= DebugTier.Mid)
                {
                    var c00 = Basis.ToWorldSpace(min, renderHeight);
                    var c10 = Basis.ToWorldSpace(new float2(max.x, min.y), renderHeight);
                    var c11 = Basis.ToWorldSpace(max, renderHeight);
                    var c01 = Basis.ToWorldSpace(new float2(min.x, max.y), renderHeight);

                    Drawer.Line(c00, c10, CellColor);
                    Drawer.Line(c10, c11, CellColor);
                    Drawer.Line(c11, c01, CellColor);
                    Drawer.Line(c01, c00, CellColor);

                    Drawer.Text32(centerWorld + Basis.Normal * 0.3f, "Influence Query", CellColor, 12f);
                }

                if (tier == DebugTier.Close)
                {
                    var magnitude = math.length(new float2(result.Direction.x, result.Direction.y));
                    var readout = new FixedString128Bytes();
                    readout.Append((FixedString32Bytes)"val ");
                    readout.Append(result.Value);
                    readout.Append((FixedString32Bytes)"  grad ");
                    readout.Append(magnitude);
                    readout.Append((FixedString32Bytes)"  cell ");
                    readout.Append(result.Cell.x);
                    readout.Append((FixedString32Bytes)",");
                    readout.Append(result.Cell.y);
                    Drawer.Text128(centerWorld + Basis.Normal * 0.6f, readout, TimelineDebugColors.Label, 11f);
                }
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