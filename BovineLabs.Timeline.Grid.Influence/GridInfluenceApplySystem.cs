using BovineLabs.Core.Extensions;
using BovineLabs.Core.Iterators;
using BovineLabs.Reaction.Data.Core;
using BovineLabs.Timeline.Data;
using BovineLabs.Timeline.EntityLinks;
using BovineLabs.Timeline.EntityLinks.Data;
using BovineLabs.Timeline.Grid.Influence.Data;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace BovineLabs.Timeline.Grid.Influence
{
    [UpdateInGroup(typeof(TimelineSystemGroup))]
    [UpdateAfter(typeof(TimelineComponentAnimationGroup))]
    [WorldSystemFilter(WorldSystemFilterFlags.LocalSimulation | WorldSystemFilterFlags.ServerSimulation |
                       WorldSystemFilterFlags.ClientSimulation | WorldSystemFilterFlags.Editor)]
    public partial struct GridInfluenceApplySystem : ISystem
    {
        private UnsafeComponentLookup<Targets> _targetsLookup;
        private UnsafeComponentLookup<EntityLinkSource> _linkSourceLookup;
        private UnsafeBufferLookup<EntityLinkEntry> _linkLookup;
        private UnsafeComponentLookup<LocalToWorld> _localToWorldLookup;

        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<InfluenceGridSettings>();
            state.RequireForUpdate<FieldRegistrySingleton>();

            _targetsLookup = state.GetUnsafeComponentLookup<Targets>(true);
            _linkSourceLookup = state.GetUnsafeComponentLookup<EntityLinkSource>(true);
            _linkLookup = state.GetUnsafeBufferLookup<EntityLinkEntry>(true);
            _localToWorldLookup = state.GetUnsafeComponentLookup<LocalToWorld>(true);
        }

        public void OnUpdate(ref SystemState state)
        {
            _targetsLookup.Update(ref state);
            _linkSourceLookup.Update(ref state);
            _linkLookup.Update(ref state);
            _localToWorldLookup.Update(ref state);

            var settings = SystemAPI.GetSingleton<InfluenceGridSettings>();
            ref var fieldSingleton = ref SystemAPI.GetSingletonRW<FieldRegistrySingleton>().ValueRW;

            var activeQuery = SystemAPI.QueryBuilder()
                .WithAll<InfluenceClipData, TrackBinding, ClipActive, ClipWeight>()
                .Build();

            state.Dependency = new GatherStampsJob
            {
                StampsMap = fieldSingleton.PendingStamps.AsParallelWriter(),
                KeyToSlot = fieldSingleton.Registry.KeyToSlot,
                CellSize = math.max(0.0001f, settings.CellSize),
                Basis = new GridBasis(settings.PlaneNormal),
                LocalToWorldLookup = _localToWorldLookup,
                TargetsLookup = _targetsLookup,
                LinkSources = _linkSourceLookup,
                Links = _linkLookup
            }.ScheduleParallel(activeQuery, state.Dependency);
        }

        [BurstCompile]
        [WithAll(typeof(ClipActive))]
        partial struct GatherStampsJob : IJobEntity
        {
            public NativeParallelMultiHashMap<int, Stamp>.ParallelWriter StampsMap;
            [ReadOnly] public NativeHashMap<ushort, int> KeyToSlot;

            [ReadOnly] public UnsafeComponentLookup<LocalToWorld> LocalToWorldLookup;
            [ReadOnly] public UnsafeComponentLookup<Targets> TargetsLookup;
            [ReadOnly] public UnsafeComponentLookup<EntityLinkSource> LinkSources;
            [ReadOnly] public UnsafeBufferLookup<EntityLinkEntry> Links;

            public float CellSize;
            public GridBasis Basis;

            void Execute(in InfluenceClipData clip, in TrackBinding binding, in ClipWeight weight)
            {
                if (!KeyToSlot.TryGetValue(clip.FieldKey, out int slotIndex)) return;

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

                StampsMap.Add(slotIndex, new Stamp(clip.Shape.WithWeight(scaledWeight), origin));
            }
        }
    }
}
