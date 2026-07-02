using BovineLabs.Timeline.Data;
using BovineLabs.Timeline.Grid.Influence.Data;
using BovineLabs.Timeline.Grid.Influence.Data.Flows;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;

namespace BovineLabs.Timeline.Grid.Influence
{
    [UpdateInGroup(typeof(TimelineSystemGroup))]
    [UpdateAfter(typeof(GridInfluenceApplySystem))]
    [WorldSystemFilter(WorldSystemFilterFlags.LocalSimulation | WorldSystemFilterFlags.ServerSimulation |
                       WorldSystemFilterFlags.ClientSimulation | WorldSystemFilterFlags.Editor)]
    public partial struct GridFlowSteeringSystem : ISystem
    {
        private ComponentLookup<LocalTransform> _transformLookup;

        // Field keys we've already warned are unregistered, so the diagnostic fires once per key, not every frame.
        private NativeHashSet<ushort> _warnedMissing;

        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<InfluenceGridSettings>();
            state.RequireForUpdate<FieldRegistrySingleton>();

            _transformLookup = state.GetComponentLookup<LocalTransform>();
            _warnedMissing = new NativeHashSet<ushort>(8, Allocator.Persistent);
        }

        public void OnDestroy(ref SystemState state)
        {
            if (_warnedMissing.IsCreated)
                _warnedMissing.Dispose();
        }

        public void OnUpdate(ref SystemState state)
        {
            _transformLookup.Update(ref state);

            var settings = SystemAPI.GetSingleton<InfluenceGridSettings>();
            ref var reg = ref SystemAPI.GetSingletonRW<FieldRegistrySingleton>().ValueRW.Registry;

            var cellSize = math.max(0.0001f, settings.CellSize);
            var basis = new GridBasis(settings.PlaneNormal);
            var deltaTime = SystemAPI.Time.DeltaTime;

            // Only resolve flow for fields an active steering clip actually references — resolving every
            // registered field's gradient each frame is wasted work for query-only / inactive fields.
            var activeKeys = new NativeHashSet<ushort>(math.max(1, reg.Count), state.WorldUpdateAllocator);
            foreach (var data in SystemAPI.Query<RefRO<GridFlowSteeringData>>().WithAll<ClipActive>())
                activeKeys.Add(data.ValueRO.FieldKey);

            if (activeKeys.IsEmpty)
                return;

            // Diagnose steering clips pointing at an unregistered field key — otherwise a silent no-op (the clip
            // never moves the agent and there is no field to steer by). Warned once per key.
            foreach (var key in activeKeys)
            {
                if (_warnedMissing.Contains(key))
                    continue;

                var registered = false;
                for (var i = 0; i < reg.Count; i++)
                {
                    if (reg.Slot(i).Config.Key == key)
                    {
                        registered = true;
                        break;
                    }
                }

                if (!registered)
                {
                    _warnedMissing.Add(key);
                    Debug.LogWarning($"GridFlowSteering: a steering clip references field key {key}, which is not " +
                        "registered; the clip will do nothing until an InfluenceField with that key exists in the world.");
                }
            }

            var dependency = state.Dependency;
            for (var i = 0; i < reg.Count; i++)
            {
                ref var pair = ref reg.Slot(i);
                if (!activeKeys.Contains(pair.Config.Key))
                    continue;

                var field = pair.Front;
                if (!field.IsCreated)
                    continue;

                var flow = pair.Flow;
                if (!flow.IsCreated)
                    flow = FlowField.Create(Allocator.Persistent);

                var combined = JobHandle.CombineDependencies(dependency, field.Dependency, pair.WriterDependency);

                var handle = flow.Resolve(ref field, combined);

                handle = new SteerJob
                {
                    Flow = flow.AsDeferredReader(ref field),
                    FieldKey = pair.Config.Key,
                    CellSize = cellSize,
                    Basis = basis,
                    DeltaTime = deltaTime,
                    TransformLookup = _transformLookup
                }.Schedule(handle);

                field.PublishDependency(handle);
                flow.PublishDependency(handle);
                pair.Flow = flow;
                pair.Front = field;
                dependency = handle;
            }

            state.Dependency = dependency;
        }

        [BurstCompile]
        [WithAll(typeof(ClipActive))]
        private partial struct SteerJob : IJobEntity
        {
            public FlowReader Flow;
            public ushort FieldKey;
            public float CellSize;
            public GridBasis Basis;
            public float DeltaTime;
            public ComponentLookup<LocalTransform> TransformLookup;

            private void Execute(in GridFlowSteeringData data, in TrackBinding binding, in ClipWeight weight)
            {
                if (data.FieldKey != FieldKey)
                    return;

                var target = binding.Value;
                if (target == Entity.Null || !TransformLookup.HasComponent(target))
                    return;

                var transform = TransformLookup[target];
                var cellSpace = Basis.CellSpace(transform.Position, transform.Rotation, data.LocalOffset, CellSize);
                var cell = GridBasis.Cell(cellSpace);

                var gradient = data.Bias.Sign() * Flow.Sample(cell);
                var planar = FieldGradient.Normalized(gradient);
                if (math.all(planar == float2.zero))
                    return;

                var velocity = Basis.ToWorldSpace(planar * data.MaxSpeed, 0f);
                transform.Position += velocity * (DeltaTime * weight.Value);
                TransformLookup[target] = transform;
            }
        }
    }
}