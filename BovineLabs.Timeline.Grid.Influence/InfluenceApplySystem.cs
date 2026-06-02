using BovineLabs.Timeline.Data;
using BovineLabs.Timeline.Grid.Influence.Data;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Transforms;

namespace BovineLabs.Timeline.Grid.Influence
{
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(TimelineComponentAnimationGroup))]
    [WorldSystemFilter(WorldSystemFilterFlags.LocalSimulation | WorldSystemFilterFlags.ServerSimulation |
                       WorldSystemFilterFlags.ClientSimulation | WorldSystemFilterFlags.Editor)]
    public partial struct InfluenceApplySystem : ISystem
    {
        EntityQuery _activeQuery;

        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<InfluenceGridSettings>();

            _activeQuery = SystemAPI.QueryBuilder()
                .WithAll<InfluenceClipData, TrackBinding, ClipActive>()
                .Build();

            if (!SystemAPI.HasSingleton<InfluenceFieldSingleton>())
            {
                state.EntityManager.AddComponent<InfluenceFieldSingleton>(state.EntityManager.CreateEntity());
            }

            if (!SystemAPI.HasSingleton<InfluenceFieldDependency>())
            {
                state.EntityManager.AddComponent<InfluenceFieldDependency>(state.EntityManager.CreateEntity());
            }
        }

        [BurstCompile]
        public void OnDestroy(ref SystemState state)
        {
            if (SystemAPI.TryGetSingleton<InfluenceFieldDependency>(out var dependency))
            {
                dependency.Value.Complete();
            }

            state.Dependency.Complete();

            if (SystemAPI.TryGetSingletonRW<InfluenceFieldSingleton>(out var fieldRw) && fieldRw.ValueRO.Field.IsCreated)
            {
                fieldRw.ValueRW.Field.Dispose();
                fieldRw.ValueRW.Field = default;
            }
        }

        public void OnUpdate(ref SystemState state)
        {
            var settings = SystemAPI.GetSingleton<InfluenceGridSettings>();
            var fieldRw = SystemAPI.GetSingletonRW<InfluenceFieldSingleton>();
            var dependencyRw = SystemAPI.GetSingletonRW<InfluenceFieldDependency>();

            var field = fieldRw.ValueRO.Field;
            if (!field.IsCreated)
            {
                field = InfluenceField.Create(
                    1 << settings.ChunkSizePowerOfTwo,
                    settings.ChunkRetentionFrames,
                    Allocator.Persistent);
            }

            int count = _activeQuery.CalculateEntityCount();
            JobHandle publishedDependency = dependencyRw.ValueRO.Value;

            JobHandle handle;
            if (count == 0)
            {
                handle = field.ScheduleBatched(default, publishedDependency);
            }
            else
            {
                var stamps = new NativeList<Stamp>(count, state.WorldUpdateAllocator);

                var gather = new GatherStampsJob
                {
                    Stamps = stamps.AsParallelWriter(),
                    CellSize = settings.CellSize,
                    Basis = new GridBasis(settings.PlaneNormal),
                    LocalToWorldLookup = SystemAPI.GetComponentLookup<LocalToWorld>(true)
                }.ScheduleParallel(_activeQuery, state.Dependency);

                gather.Complete();

                handle = field.ScheduleBatched(stamps.AsArray(), publishedDependency);
            }

            fieldRw.ValueRW.Field = field;
            dependencyRw.ValueRW.Value = handle;
            state.Dependency = handle;
        }

        [BurstCompile]
        [WithAll(typeof(ClipActive))]
        partial struct GatherStampsJob : IJobEntity
        {
            public NativeList<Stamp>.ParallelWriter Stamps;
            [ReadOnly] public ComponentLookup<LocalToWorld> LocalToWorldLookup;
            public float CellSize;
            public GridBasis Basis;

            void Execute(in InfluenceClipData clip, in TrackBinding binding)
            {
                if (binding.Value == Entity.Null || !LocalToWorldLookup.TryGetComponent(binding.Value, out var ltw))
                {
                    return;
                }

                float3 world = ltw.Position + math.rotate(ltw.Rotation, clip.LocalOffset);
                float2 projected = Basis.ToGridSpace(world);

                int2 origin = new int2(
                    (int)math.floor(projected.x / CellSize),
                    (int)math.floor(projected.y / CellSize));

                Stamps.AddNoResize(new Stamp(clip.Shape, origin));
            }
        }
    }
}