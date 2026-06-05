#if UNITY_EDITOR || BL_DEBUG
using BovineLabs.Core;
using BovineLabs.Core.ConfigVars;
using BovineLabs.Quill;
using BovineLabs.Timeline.Core.Debug;
using BovineLabs.Timeline.Grid.Influence.Data;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

namespace BovineLabs.Timeline.Grid.Influence.Debug
{
    [Configurable]
    public static class FlowFieldDebugConfig
    {
        [ConfigVar("influencegizmo.draw-flow", false, "Draw fields as flow arrows along the negative gradient.")]
        public static readonly SharedStatic<bool> Enabled = SharedStatic<bool>.GetOrCreate<Tags.Enabled>();

        [ConfigVar("influencegizmo.flow-color", 0.3f, 0.8f, 1.0f, 0.9f, "Flow arrow color.")]
        public static readonly SharedStatic<Color> FlowColor = SharedStatic<Color>.GetOrCreate<Tags.FlowColor>();

        [ConfigVar("influencegizmo.flow-stride", 1, "Draw one arrow every N cells.")]
        public static readonly SharedStatic<int> Stride = SharedStatic<int>.GetOrCreate<Tags.Stride>();

        private struct Tags
        {
            public struct Enabled
            {
            }

            public struct FlowColor
            {
            }

            public struct Stride
            {
            }
        }
    }

    [WorldSystemFilter(WorldSystemFilterFlags.LocalSimulation | WorldSystemFilterFlags.ServerSimulation |
                       WorldSystemFilterFlags.ClientSimulation | WorldSystemFilterFlags.Editor)]
    [UpdateInGroup(typeof(DebugSystemGroup))]
    public partial struct FlowFieldDebugSystem : ISystem
    {
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<DrawSystem.Singleton>();
            state.RequireForUpdate<InfluenceGridSettings>();
            state.RequireForUpdate<FieldRegistrySingleton>();
        }

        public void OnUpdate(ref SystemState state)
        {
            if (!TimelineDebugUtility.TryGetDrawer<FlowFieldDebugSystem>(
                    ref state, FlowFieldDebugConfig.Enabled.Data, out var drawer))
                return;

            var settings = SystemAPI.GetSingleton<InfluenceGridSettings>();
            var basis = new GridBasis(settings.PlaneNormal);

            ref var reg = ref SystemAPI.GetSingletonRW<FieldRegistrySingleton>().ValueRW.Registry;

            for (var i = 0; i < reg.Count; i++)
            {
                ref var pair = ref reg.Slot(i);
                var field = pair.Front;
                if (!field.IsCreated)
                    continue;

                var dependency = JobHandle.CombineDependencies(state.Dependency, field.Dependency);

                state.Dependency = new DrawFlowJob
                {
                    Drawer = drawer,
                    Reader = field.AsReader(),
                    ActiveSlots = field.ActiveSlotsList,
                    CoordBySlot = field.CoordBySlotList,
                    Spec = field.Spec,
                    CellSize = math.max(0.0001f, settings.CellSize),
                    Basis = basis,
                    FlowColor = FlowFieldDebugConfig.FlowColor.Data,
                    Stride = FlowFieldDebugConfig.Stride.Data
                }.Schedule(dependency);
            }
        }

        [BurstCompile]
        private struct DrawFlowJob : IJob
        {
            public Drawer Drawer;
            public FieldReader Reader;

            [ReadOnly] public NativeList<int> ActiveSlots;
            [ReadOnly] public NativeList<int2> CoordBySlot;

            public GridSpec Spec;
            public float CellSize;
            public GridBasis Basis;
            public Color FlowColor;
            public int Stride;

            public void Execute()
            {
                const float renderHeight = 0.06f;
                var chunkSize = Spec.ChunkSize;
                var step = math.max(1, Stride);

                for (var s = 0; s < ActiveSlots.Length; s++)
                {
                    var coord = CoordBySlot[ActiveSlots[s]];
                    var chunkBase = new int2(coord.x * chunkSize, coord.y * chunkSize);

                    for (var y = 0; y < chunkSize; y += step)
                    for (var x = 0; x < chunkSize; x += step)
                    {
                        var cell = chunkBase + new int2(x, y);

                        var gx = Reader.ReadCell(cell + new int2(1, 0)) - Reader.ReadCell(cell + new int2(-1, 0));
                        var gy = Reader.ReadCell(cell + new int2(0, 1)) - Reader.ReadCell(cell + new int2(0, -1));

                        var gradient = new float2(-gx, -gy);
                        var magnitude = math.length(gradient);
                        if (magnitude < 1e-4f)
                            continue;

                        DrawArrow(cell, gradient / magnitude, renderHeight);
                    }
                }
            }

            private void DrawArrow(int2 cell, float2 direction, float height)
            {
                var center = (new float2(cell.x, cell.y) + 0.5f) * CellSize;
                var half = CellSize * 0.42f;
                var headLength = CellSize * 0.18f;
                var perpendicular = new float2(-direction.y, direction.x);

                var tailGrid = center - direction * half;
                var tipGrid = center + direction * half;

                var tail = Basis.ToWorldSpace(tailGrid, height);
                var tip = Basis.ToWorldSpace(tipGrid, height);
                var wingA = Basis.ToWorldSpace(tipGrid + (-direction + perpendicular) * headLength, height);
                var wingB = Basis.ToWorldSpace(tipGrid + (-direction - perpendicular) * headLength, height);

                Drawer.Line(tail, tip, FlowColor);
                Drawer.Line(tip, wingA, FlowColor);
                Drawer.Line(tip, wingB, FlowColor);
            }
        }
    }
}
#endif