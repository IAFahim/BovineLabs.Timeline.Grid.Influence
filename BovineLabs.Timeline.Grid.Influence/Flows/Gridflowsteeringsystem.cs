using BovineLabs.Timeline.Data;
using BovineLabs.Timeline.Grid.Influence.Data;
using BovineLabs.Timeline.Grid.Influence.Data.Flows;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Transforms;

namespace BovineLabs.Timeline.Grid.Influence
{
    [UpdateInGroup(typeof(TimelineSystemGroup))]
    [UpdateAfter(typeof(GridInfluenceApplySystem))]
    [WorldSystemFilter(WorldSystemFilterFlags.LocalSimulation | WorldSystemFilterFlags.ServerSimulation |
                       WorldSystemFilterFlags.ClientSimulation | WorldSystemFilterFlags.Editor)]
    public partial struct GridFlowSteeringSystem : ISystem
    {
        private ComponentLookup<LocalTransform> _transformLookup;

        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<InfluenceGridSettings>();
            state.RequireForUpdate<FieldRegistrySingleton>();

            _transformLookup = state.GetComponentLookup<LocalTransform>();
        }

        public void OnUpdate(ref SystemState state)
        {
            _transformLookup.Update(ref state);

            var settings = SystemAPI.GetSingleton<InfluenceGridSettings>();
            ref var reg = ref SystemAPI.GetSingletonRW<FieldRegistrySingleton>().ValueRW.Registry;

            var cellSize = math.max(0.0001f, settings.CellSize);
            var basis = new GridBasis(settings.PlaneNormal);
            var deltaTime = SystemAPI.Time.DeltaTime;

            var dependency = state.Dependency;
            for (var i = 0; i < reg.Count; i++)
            {
                ref var pair = ref reg.Slot(i);
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
                var world = transform.Position + math.rotate(transform.Rotation, data.LocalOffset);
                var projected = Basis.ToGridSpace(world);
                var cell = new int2(
                    (int)math.floor(projected.x / CellSize),
                    (int)math.floor(projected.y / CellSize));

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