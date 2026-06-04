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
    [DisableAutoCreation]
    [UpdateInGroup(typeof(BovineLabs.Timeline.TimelineSystemGroup))]
    [UpdateAfter(typeof(BovineLabs.Timeline.TimelineComponentAnimationGroup))]
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
        }

        [BurstCompile]
        public void OnDestroy(ref SystemState state)
        {
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
            var field = fieldRw.ValueRO.Field;

            if (!field.IsCreated)
            {
                field = InfluenceField.Create(
                    GridSpec.FromPowerOfTwo(settings.ChunkSizePowerOfTwo, settings.ChunkRetentionFrames),
                    Allocator.Persistent);
            }

            int count = _activeQuery.CalculateEntityCount();

            JobHandle handle;
            if (count == 0)
            {
                handle = field.Schedule(default, default);
            }
            else
            {
                var stamps = new NativeList<Stamp>(count, state.WorldUpdateAllocator);

                JobHandle gather = new GatherStampsJob
                {
                    Stamps = stamps.AsParallelWriter(),
                    CellSize = math.max(0.0001f, settings.CellSize),
                    Basis = new GridBasis(settings.PlaneNormal),
                    LocalToWorldLookup = SystemAPI.GetComponentLookup<LocalToWorld>(true)
                }.ScheduleParallel(_activeQuery, state.Dependency);

                gather.Complete();
                handle = field.Schedule(stamps.AsArray(), default);
            }

            fieldRw.ValueRW.Field = field;
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
                if (binding.Value == Entity.Null || !LocalToWorldLookup.TryGetComponent(binding.Value, out var localToWorld))
                {
                    return;
                }

                float3 world = localToWorld.Position + math.rotate(localToWorld.Rotation, clip.LocalOffset);
                float2 projected = Basis.ToGridSpace(world);

                int2 origin = new int2(
                    (int)math.floor(projected.x / CellSize),
                    (int)math.floor(projected.y / CellSize));

                Stamps.AddNoResize(new Stamp(clip.Shape, origin));
            }
        }
    }
}
