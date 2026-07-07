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

        // Field keys we've already warned are unregistered, so the diagnostic fires once per key, not every frame.
        private MissingFieldKeyWarnings _warnings;

        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<InfluenceGridSettings>();
            state.RequireForUpdate<FieldRegistrySingleton>();

            _transformLookup = state.GetComponentLookup<LocalTransform>();
            _warnings = MissingFieldKeyWarnings.Create();
        }

        public void OnDestroy(ref SystemState state)
        {
            _warnings.Dispose();
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

            // TODO-07: warn once per unregistered field key — otherwise a silent no-op (the clip never moves the
            // agent and there is no field to steer by).
            foreach (var key in activeKeys)
                _warnings.Report(key, reg.KeyToSlot, "GridFlowSteering");

            // TODO-17(c): resolve each active field's flow (the per-field Resolve chain must stay), then run ONE
            // SteerJob that resolves the field per entity via KeyToSlot instead of scheduling a full-query SteerJob
            // per field.
            var readersBySlot = CollectionHelper.CreateNativeArray<FlowReader>(reg.Count, state.WorldUpdateAllocator);
            var validBySlot = CollectionHelper.CreateNativeArray<byte>(reg.Count, state.WorldUpdateAllocator);

            var resolved = state.Dependency;
            var anyValid = false;
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

                var combined = JobHandle.CombineDependencies(state.Dependency, field.Dependency, pair.WriterDependency);
                var handle = flow.Resolve(ref field, combined);

                readersBySlot[i] = flow.AsDeferredReader(ref field);
                validBySlot[i] = 1;
                anyValid = true;

                pair.Flow = flow;
                pair.Front = field;
                resolved = JobHandle.CombineDependencies(resolved, handle);
            }

            if (!anyValid)
                return;

            var steer = new SteerJob
            {
                ReadersBySlot = readersBySlot,
                ValidBySlot = validBySlot,
                KeyToSlot = reg.KeyToSlot,
                CellSize = cellSize,
                Basis = basis,
                DeltaTime = deltaTime,
                TransformLookup = _transformLookup,
            }.Schedule(resolved);

            // Publish the single SteerJob into every involved field AND flow so the next writer waits on it.
            for (var i = 0; i < reg.Count; i++)
            {
                if (validBySlot[i] == 0)
                    continue;

                ref var pair = ref reg.Slot(i);

                var field = pair.Front;
                field.PublishDependency(steer);
                pair.Front = field;

                var flow = pair.Flow;
                flow.PublishDependency(steer);
                pair.Flow = flow;
            }

            state.Dependency = steer;
        }

        [BurstCompile]
        [WithAll(typeof(ClipActive))]
        private partial struct SteerJob : IJobEntity
        {
            [ReadOnly] public NativeArray<FlowReader> ReadersBySlot;
            [ReadOnly] public NativeArray<byte> ValidBySlot;
            [ReadOnly] public NativeHashMap<ushort, int> KeyToSlot;
            public float CellSize;
            public GridBasis Basis;
            public float DeltaTime;
            public ComponentLookup<LocalTransform> TransformLookup;

            private void Execute(in GridFlowSteeringData data, in TrackBinding binding, in ClipWeight weight)
            {
                if (!KeyToSlot.TryGetValue(data.FieldKey, out var slot) || ValidBySlot[slot] == 0)
                    return;

                var target = binding.Value;
                if (target == Entity.Null || !TransformLookup.HasComponent(target))
                    return;

                var flow = ReadersBySlot[slot];

                var transform = TransformLookup[target];
                var cellSpace = Basis.CellSpace(transform.Position, transform.Rotation, data.LocalOffset, CellSize);
                var cell = GridBasis.Cell(cellSpace);

                var gradient = data.Bias.Sign() * flow.Sample(cell);
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
