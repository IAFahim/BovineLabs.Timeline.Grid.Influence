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

        [ConfigVar("influencegizmo.flow-stride", 4, "Draw one flow arrow every N cells.")]
        public static readonly SharedStatic<int> Stride = SharedStatic<int>.GetOrCreate<Tags.Stride>();

        private struct Tags
        {
            public struct Enabled { }
            public struct FlowColor { }
            public struct Stride { }
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
            var cellSize = math.max(0.0001f, settings.CellSize);
            var basis = new GridBasis(settings.PlaneNormal);
            var cameraCulling = SystemAPI.GetSingleton<DrawSystem.Singleton>().CameraCulling;

            // If Quill hasn't populated camera data yet, skip to avoid drawing the whole world.
            if (cameraCulling.IsDefault)
                return;

            ref var reg = ref SystemAPI.GetSingletonRW<FieldRegistrySingleton>().ValueRW.Registry;

            for (var i = 0; i < reg.Count; i++)
            {
                ref var pair = ref reg.Slot(i);
                var field = pair.Front;
                if (!field.IsCreated)
                    continue;

                var dependency = JobHandle.CombineDependencies(state.Dependency, field.Dependency);

                dependency = new DrawFlowJob
                {
                    Drawer = drawer,
                    Reader = field.AsReader(),
                    ActiveSlots = field.ActiveSlotsList,
                    CoordBySlot = field.CoordBySlotList,
                    Spec = field.Spec,
                    CellSize = cellSize,
                    Basis = basis,
                    FlowColor = FlowFieldDebugConfig.FlowColor.Data,
                    Stride = math.max(1, FlowFieldDebugConfig.Stride.Data),
                    CameraCulling = cameraCulling,
                }.Schedule(dependency);

                field.PublishDependency(dependency);
                pair.Front = field;
                state.Dependency = dependency;
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
            public CameraCulling CameraCulling;

            private const float RenderHeight = 0.06f;

            public void Execute()
            {
                var chunkSize = Spec.ChunkSize;
                var step = math.max(1, Stride);

                for (var s = 0; s < ActiveSlots.Length; s++)
                {
                    var coord = CoordBySlot[ActiveSlots[s]];

                    // Cull entire chunks outside the camera frustum before reading cells or drawing arrows.
                    if (!IsChunkVisible(coord))
                        continue;

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

                        DrawArrow(cell, gradient / magnitude);
                    }
                }
            }

            private bool IsChunkVisible(int2 coord)
            {
                if (CameraCulling.IsDefault)
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
                    Extents = ((max - min) * 0.5f) + new float3(margin),
                };

                return CameraCulling.AnyIntersect(aabb);
            }

            private void DrawArrow(int2 cell, float2 direction)
            {
                var center = (new float2(cell.x, cell.y) + 0.5f) * CellSize;
                var half = CellSize * 0.42f;
                var headLength = CellSize * 0.18f;
                var perpendicular = new float2(-direction.y, direction.x);

                var tailGrid = center - direction * half;
                var tipGrid = center + direction * half;

                var tail = Basis.ToWorldSpace(tailGrid, RenderHeight);
                var tip = Basis.ToWorldSpace(tipGrid, RenderHeight);
                var wingA = Basis.ToWorldSpace(tipGrid + (-direction + perpendicular) * headLength, RenderHeight);
                var wingB = Basis.ToWorldSpace(tipGrid + (-direction - perpendicular) * headLength, RenderHeight);

                Drawer.Line(tail, tip, FlowColor);
                Drawer.Line(tip, wingA, FlowColor);
                Drawer.Line(tip, wingB, FlowColor);
            }
        }
    }
}
#endif
