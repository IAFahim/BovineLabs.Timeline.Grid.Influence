using BovineLabs.Reaction.Data.Core;
using BovineLabs.Core.Extensions;
using BovineLabs.Core.Iterators;
using BovineLabs.Timeline.Data;
using BovineLabs.Timeline.EntityLinks;
using BovineLabs.Timeline.EntityLinks.Data;
using BovineLabs.Timeline.Grid.Influence.Data;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Transforms;

namespace BovineLabs.Timeline.Grid.Influence
{
    [UpdateInGroup(typeof(BovineLabs.Timeline.TimelineSystemGroup))]
    [UpdateAfter(typeof(BovineLabs.Timeline.TimelineComponentAnimationGroup))]
    [WorldSystemFilter(WorldSystemFilterFlags.LocalSimulation | WorldSystemFilterFlags.ServerSimulation |
                       WorldSystemFilterFlags.ClientSimulation | WorldSystemFilterFlags.Editor)]
    public partial struct InfluenceApplySystem : ISystem
    {
        EntityQuery _activeQuery;
        
        private UnsafeComponentLookup<Targets> _targetsLookup;
        private UnsafeComponentLookup<EntityLinkSource> _linkSourceLookup;
        private UnsafeBufferLookup<EntityLinkEntry> _linkLookup;
        private UnsafeComponentLookup<LocalToWorld> _localToWorldLookup;

        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<InfluenceGridSettings>();

            _targetsLookup = state.GetUnsafeComponentLookup<Targets>(true);
            _linkSourceLookup = state.GetUnsafeComponentLookup<EntityLinkSource>(true);
            _linkLookup = state.GetUnsafeBufferLookup<EntityLinkEntry>(true);
            _localToWorldLookup = state.GetUnsafeComponentLookup<LocalToWorld>(true);

            _activeQuery = SystemAPI.QueryBuilder()
                .WithAll<InfluenceClipData, TrackBinding, ClipActive, ClipWeight>()
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
            _targetsLookup.Update(ref state);
            _linkSourceLookup.Update(ref state);
            _linkLookup.Update(ref state);
            _localToWorldLookup.Update(ref state);

            var settings = SystemAPI.GetSingleton<InfluenceGridSettings>();
            var fieldRw = SystemAPI.GetSingletonRW<InfluenceFieldSingleton>();
            var field = fieldRw.ValueRO.Field;

            if (!field.IsCreated)
            {
                field = InfluenceField.Create(
                    GridSpec.FromPowerOfTwo(settings.ChunkSizePowerOfTwo, settings.ChunkRetentionFrames, settings.StrideAlignment),
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
                    LocalToWorldLookup = _localToWorldLookup,
                    TargetsLookup = _targetsLookup,
                    LinkSources = _linkSourceLookup,
                    Links = _linkLookup
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
            [ReadOnly] public UnsafeComponentLookup<LocalToWorld> LocalToWorldLookup;
            [ReadOnly] public UnsafeComponentLookup<Targets> TargetsLookup;
            [ReadOnly] public UnsafeComponentLookup<EntityLinkSource> LinkSources;
            [ReadOnly] public UnsafeBufferLookup<EntityLinkEntry> Links;
            
            public float CellSize;
            public GridBasis Basis;

            void Execute(in InfluenceClipData clip, in TrackBinding binding, in ClipWeight weight)
            {
                var targetEntity = binding.Value;
                if (targetEntity == Entity.Null) return;

                var originEntity = targetEntity;

                if (clip.OriginTarget != Target.None && clip.OriginTarget != Target.Self)
                {
                    var targets = TargetsLookup.TryGetComponent(targetEntity, out var t) ? t : default;
                    var baseTarget = targets.Get(clip.OriginTarget, targetEntity);
                    if (baseTarget != Entity.Null)
                    {
                        originEntity = baseTarget;
                        if (clip.OriginLinkKey != 0 && EntityLinkResolver.TryResolve(baseTarget, clip.OriginLinkKey, LinkSources, Links, out var linked))
                        {
                            originEntity = linked;
                        }
                    }
                }

                if (!LocalToWorldLookup.TryGetComponent(originEntity, out var localToWorld)) return;

                int scaledWeight = (int)math.round(clip.Shape.Weight * weight.Value);
                if (scaledWeight == 0) return;

                float3 world = localToWorld.Position + math.rotate(localToWorld.Rotation, clip.LocalOffset);
                float2 projected = Basis.ToGridSpace(world);

                int2 origin = new int2(
                    (int)math.floor(projected.x / CellSize),
                    (int)math.floor(projected.y / CellSize));

                Stamps.AddNoResize(new Stamp(clip.Shape.WithWeight(scaledWeight), origin));
            }
        }
    }
}
