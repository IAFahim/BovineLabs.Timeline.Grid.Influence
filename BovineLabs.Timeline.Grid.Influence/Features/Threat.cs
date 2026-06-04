using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Transforms;
using BovineLabs.Timeline.Data;
using BovineLabs.Timeline.Grid.Influence.Data;
using BovineLabs.Timeline.Grid.Influence.Fields;

namespace BovineLabs.Timeline.Grid.Influence.Features.Threat
{




    public struct ThreatClipTag : IComponentData { }



    public struct ThreatField : IComponentData { public FieldId Id; }

    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(TimelineComponentAnimationGroup))]
    [WorldSystemFilter(WorldSystemFilterFlags.LocalSimulation | WorldSystemFilterFlags.ClientSimulation |
                       WorldSystemFilterFlags.ServerSimulation | WorldSystemFilterFlags.Editor)]
    public partial struct ThreatFieldSystem : ISystem
    {
        EntityQuery _clips;

        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<InfluenceGridSettings>();
            state.RequireForUpdate<FieldRegistrySingleton>();
            _clips = SystemAPI.QueryBuilder()
                .WithAll<InfluenceClipData, TrackBinding, ClipActive, ThreatClipTag>()
                .Build();
        }

        public void OnUpdate(ref SystemState state)
        {
            ref var regSingleton = ref SystemAPI.GetSingletonRW<FieldRegistrySingleton>().ValueRW;
            if (!SystemAPI.HasSingleton<ThreatField>())
            {
                var settings = SystemAPI.GetSingleton<InfluenceGridSettings>();
                var id = regSingleton.Registry.Register(new FieldConfig
                {
                    Name = "Threat",
                    ChunkPower = settings.ChunkSizePowerOfTwo,
                    RetentionFrames = 1,
                    HasFeedback = false,
                }, Allocator.Persistent);
                var e = state.EntityManager.CreateEntity();
                state.EntityManager.AddComponentData(e, new ThreatField { Id = id });
                
                // Guard first frame
                return;
            }

            var threatId = SystemAPI.GetSingleton<ThreatField>().Id;
            ref var pair = ref regSingleton.Registry.Slot(threatId.Value);

            int count = _clips.CalculateEntityCount();
            if (count > 0)
            {
                var settings = SystemAPI.GetSingleton<InfluenceGridSettings>();
                
                if (!pair.PendingStamps.IsCreated)
                {
                    pair.PendingStamps = new NativeList<Stamp>(count, state.WorldUpdateAllocator);
                }
                else
                {
                    pair.PendingStamps.Capacity = math.max(pair.PendingStamps.Capacity, pair.PendingStamps.Length + count);
                }

                JobHandle gather = new GatherThreatStampsJob
                {
                    Stamps = pair.PendingStamps.AsParallelWriter(),
                    CellSize = math.max(0.0001f, settings.CellSize),
                    Basis = new GridBasis(settings.PlaneNormal),
                    LocalToWorldLookup = SystemAPI.GetComponentLookup<LocalToWorld>(true)
                }.ScheduleParallel(_clips, state.Dependency);

                pair.WriterDependency = JobHandle.CombineDependencies(pair.WriterDependency, gather);
                state.Dependency = gather;
            }
        }

        [BurstCompile]
        [WithAll(typeof(ClipActive), typeof(ThreatClipTag))]
        partial struct GatherThreatStampsJob : IJobEntity
        {
            public NativeList<Stamp>.ParallelWriter Stamps;
            [ReadOnly] public ComponentLookup<LocalToWorld> LocalToWorldLookup;
            public float CellSize;
            public GridBasis Basis;

            void Execute(in InfluenceClipData clip, in TrackBinding binding)
            {
                if (binding.Value == Entity.Null || !LocalToWorldLookup.TryGetComponent(binding.Value, out var localToWorld))
                    return;

                float3 world = localToWorld.Position + math.rotate(localToWorld.Rotation, clip.LocalOffset);
                float2 projected = Basis.ToGridSpace(world);

                int2 origin = new int2(
                    (int)math.floor(projected.x / CellSize),
                    (int)math.floor(projected.y / CellSize));

                Stamps.AddNoResize(new Stamp(clip.Shape, origin));
            }
        }
    }

    public static class ThreatReader
    {
        public static bool IsSafe(in FieldRegistry registry, FieldId threatId, int2 cell, int maxTolerated)
            => registry.Front(threatId).AsReader().ReadCell(cell) <= maxTolerated;

        public static bool IsCrossfire(in FieldRegistry registry, FieldId threatId, int2 cell, int singleSourceMax)
            => registry.Front(threatId).AsReader().ReadCell(cell) > singleSourceMax;
    }
}
