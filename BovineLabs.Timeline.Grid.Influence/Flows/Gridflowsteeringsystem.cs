using BovineLabs.Timeline.Data;
using BovineLabs.Timeline.Grid.Influence.Data;
using BovineLabs.Timeline.Grid.Influence.Data.Flows;
using Unity.Entities;
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
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<InfluenceGridSettings>();
            state.RequireForUpdate<FieldRegistrySingleton>();
        }

        public void OnUpdate(ref SystemState state)
        {
            state.CompleteDependency();

            var settings = SystemAPI.GetSingleton<InfluenceGridSettings>();
            ref var reg = ref SystemAPI.GetSingletonRW<FieldRegistrySingleton>().ValueRW.Registry;

            for (var i = 0; i < reg.Count; i++)
            {
                ref var pair = ref reg.Slot(i);
                pair.WriterDependency.Complete();
                pair.WriterDependency = default;
                pair.Front.Complete();
                if (pair.DoubleBuffered)
                    pair.Back.Complete();
            }

            var cellSize = math.max(0.0001f, settings.CellSize);
            var basis = new GridBasis(settings.PlaneNormal);
            var deltaTime = SystemAPI.Time.DeltaTime;
            var transformLookup = SystemAPI.GetComponentLookup<LocalTransform>(false);

            foreach (var (steering, binding, weight) in
                     SystemAPI.Query<RefRO<GridFlowSteeringData>, RefRO<TrackBinding>, RefRO<ClipWeight>>()
                         .WithAll<ClipActive>())
            {
                var data = steering.ValueRO;
                if (!reg.KeyToSlot.TryGetValue(data.FieldKey, out var slotIndex))
                    continue;

                var target = binding.ValueRO.Value;
                if (target == Entity.Null || !transformLookup.HasComponent(target))
                    continue;

                var field = reg.Front(new FieldId(slotIndex));
                if (!field.IsCreated)
                    continue;

                var transform = transformLookup[target];
                var world = transform.Position + math.rotate(transform.Rotation, data.LocalOffset);
                var projected = basis.ToGridSpace(world);
                var cell = new int2(
                    (int)math.floor(projected.x / cellSize),
                    (int)math.floor(projected.y / cellSize));

                var reader = field.AsReader();
                var gradient = data.Bias.Sign() * FieldGradient.Ascent(reader, cell);
                var planar = FieldGradient.Normalized(gradient);
                if (math.all(planar == float2.zero))
                    continue;

                var velocity = basis.ToWorldSpace(planar * data.MaxSpeed, 0f);
                transform.Position += velocity * (deltaTime * weight.ValueRO.Value);
                transformLookup[target] = transform;
            }
        }
    }
}