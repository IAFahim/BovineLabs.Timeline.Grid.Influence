namespace BovineLabs.Timeline.Grid.Influence
{
    using Unity.Burst;
    using Unity.Collections;
    using Unity.Entities;
    using Unity.Mathematics;
    using Unity.Transforms;
    using Data;

    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(TimelineComponentAnimationGroup))]
    public partial struct InfluenceApplySystem : ISystem
    {
        private EntityQuery _activeQuery;
        private EntityQuery _settingsQuery;

        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            _activeQuery = SystemAPI.QueryBuilder()
                .WithAll<ActiveInfluence, LocalToWorld>()
                .WithOptions(EntityQueryOptions.FilterWriteGroup)
                .Build();

            _settingsQuery = SystemAPI.QueryBuilder()
                .WithAll<InfluenceGridSettings>()
                .Build();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            var count = _activeQuery.CalculateEntityCount();
            if (count == 0) return;

            float cellSize = 1f;
            float3 planeNormal = new float3(0f, 1f, 0f);

            if (_settingsQuery.CalculateEntityCount() > 0)
            {
                var settings = SystemAPI.GetSingleton<InfluenceGridSettings>();
                cellSize = settings.CellSize;
                planeNormal = settings.PlaneNormal;
            }

            var stamps = new NativeList<Stamp>(count, state.WorldUpdateAllocator);

            var gatherJob = new GatherStampsJob
            {
                Stamps = stamps.AsParallelWriter(),
                CellSize = cellSize
            };
            state.Dependency = gatherJob.ScheduleParallel(_activeQuery, state.Dependency);

            if (!SystemAPI.TryGetSingletonRW<InfluenceFieldComponent>(out var fieldSingleton))
                return;

            ref var field = ref fieldSingleton.ValueRW.Field;

            state.Dependency = field.ScheduleBatched(stamps.AsDeferredJobArray(), state.Dependency);
        }

        [BurstCompile]
        private partial struct GatherStampsJob : IJobEntity
        {
            public NativeList<Stamp>.ParallelWriter Stamps;
            public float CellSize;

            private void Execute(ref ActiveInfluence active, in LocalToWorld ltw)
            {
                var worldPos = ltw.Position + math.rotate(ltw.Rotation, active.Config.LocalOffset);

                var gridOrigin = new int2(
                    (int)math.floor(worldPos.x / CellSize),
                    (int)math.floor(worldPos.z / CellSize)
                );

                Stamps.AddNoResize(new Stamp(active.Config.Shape, gridOrigin));
            }
        }
    }
}
