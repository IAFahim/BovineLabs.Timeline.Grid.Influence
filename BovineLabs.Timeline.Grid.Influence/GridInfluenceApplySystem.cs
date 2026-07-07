using BovineLabs.Core.Extensions;
using BovineLabs.Core.Iterators;
using BovineLabs.Reaction.Data.Core;
using BovineLabs.Timeline.Data;
using BovineLabs.Timeline.EntityLinks.Data;
using BovineLabs.Timeline.Grid.Influence.Data;
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
    [UpdateAfter(typeof(TimelineComponentAnimationGroup))]
    [WorldSystemFilter(WorldSystemFilterFlags.LocalSimulation | WorldSystemFilterFlags.ServerSimulation |
                       WorldSystemFilterFlags.ClientSimulation | WorldSystemFilterFlags.Editor)]
    public partial struct GridInfluenceApplySystem : ISystem
    {
        private UnsafeComponentLookup<Targets> _targetsLookup;
        private UnsafeComponentLookup<EntityLinkSource> _linkSourceLookup;
        private UnsafeBufferLookup<EntityLinkEntry> _linkLookup;
        private UnsafeComponentLookup<LocalToWorld> _localToWorldLookup;

        private EntityQuery _activeQuery;
        private MissingFieldKeyWarnings _warnings;

        private int _lastActiveCount;
        private int _worstStampsPerClip;

        private NativeReference<int> _missingLtw;
        private JobHandle _missingLtwWriter;
        private bool _warnedMissingLtw;

        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<InfluenceGridSettings>();
            state.RequireForUpdate<FieldRegistrySingleton>();

            _targetsLookup = state.GetUnsafeComponentLookup<Targets>(true);
            _linkSourceLookup = state.GetUnsafeComponentLookup<EntityLinkSource>(true);
            _linkLookup = state.GetUnsafeBufferLookup<EntityLinkEntry>(true);
            _localToWorldLookup = state.GetUnsafeComponentLookup<LocalToWorld>(true);

            _activeQuery = SystemAPI.QueryBuilder()
                .WithAll<InfluenceClipData, InfluenceStampElement, TrackBinding, ClipActive, ClipWeight>()
                .Build();

            _warnings = MissingFieldKeyWarnings.Create();
            _worstStampsPerClip = 1;
            _missingLtw = new NativeReference<int>(Allocator.Persistent);
        }

        [BurstCompile]
        public void OnDestroy(ref SystemState state)
        {
            _warnings.Dispose();
            if (_missingLtw.IsCreated)
                _missingLtw.Dispose();
        }

        public void OnUpdate(ref SystemState state)
        {
            _targetsLookup.Update(ref state);
            _linkSourceLookup.Update(ref state);
            _linkLookup.Update(ref state);
            _localToWorldLookup.Update(ref state);

            // TODO-24: the gather job writes a flag when an origin resolves to an entity lacking LocalToWorld (a
            // silent no-op). Read it a frame later without ever blocking on the job, and warn once.
            if (!_warnedMissingLtw && _missingLtwWriter.IsCompleted)
            {
                _missingLtwWriter.Complete();
                if (_missingLtw.Value != 0)
                {
                    _warnedMissingLtw = true;
                    Debug.LogWarning("GridInfluence apply: a clip origin resolved to an entity without " +
                        "LocalToWorld; the stamp is skipped. Ensure the origin/link target is a transform entity.");
                }
            }

            var settings = SystemAPI.GetSingleton<InfluenceGridSettings>();
            ref var fieldSingleton = ref SystemAPI.GetSingletonRW<FieldRegistrySingleton>().ValueRW;

            // TODO-07: warn once per unregistered field key referenced by an active clip.
            foreach (var clip in SystemAPI.Query<RefRO<InfluenceClipData>>()
                         .WithAll<TrackBinding, ClipActive, ClipWeight>())
                _warnings.Report(clip.ValueRO.FieldKey, fieldSingleton.Registry.KeyToSlot, "GridInfluence apply");

            var entityCount = _activeQuery.CalculateEntityCount();
            if (entityCount == 0)
                return;

            // TODO-17(b): the parallel writer needs capacity >= total stamps. Clip stamp contributions are baked and
            // immutable, so requiredCapacity only changes when the active set does — run the exact per-clip walk only
            // when the active count changed, or when capacity fell below the worst observed per-clip bound. Capacity
            // is grow-only.
            if (entityCount != _lastActiveCount ||
                fieldSingleton.PendingStamps.Capacity < entityCount * _worstStampsPerClip)
            {
                var requiredCapacity = 0;
                foreach (var (clip, extras) in SystemAPI
                             .Query<RefRO<InfluenceClipData>, DynamicBuffer<InfluenceStampElement>>()
                             .WithAll<TrackBinding, ClipActive, ClipWeight>())
                {
                    var clipData = clip.ValueRO;
                    var perClip = (clipData.Composite.IsCreated ? clipData.Composite.Value.Layers.Length : 1) +
                                  extras.Length;
                    requiredCapacity += perClip;
                    _worstStampsPerClip = math.max(_worstStampsPerClip, perClip);
                }

                if (requiredCapacity > fieldSingleton.PendingStamps.Capacity)
                {
                    var map = fieldSingleton.PendingStamps;
                    map.Capacity = math.ceilpow2(requiredCapacity);
                    fieldSingleton.PendingStamps = map;
                }
            }

            _lastActiveCount = entityCount;

            state.Dependency = new GatherStampsJob
            {
                StampsMap = fieldSingleton.PendingStamps.AsParallelWriter(),
                KeyToSlot = fieldSingleton.Registry.KeyToSlot,
                CellSize = math.max(0.0001f, settings.CellSize),
                Basis = new GridBasis(settings.PlaneNormal),
                LocalToWorldLookup = _localToWorldLookup,
                TargetsLookup = _targetsLookup,
                LinkSources = _linkSourceLookup,
                Links = _linkLookup,
                MissingLtw = _missingLtw,
            }.ScheduleParallel(_activeQuery, state.Dependency);

            _missingLtwWriter = state.Dependency;
        }

        [BurstCompile]
        [WithAll(typeof(ClipActive))]
        private partial struct GatherStampsJob : IJobEntity
        {
            public NativeParallelMultiHashMap<int, Stamp>.ParallelWriter StampsMap;
            [ReadOnly] public NativeHashMap<ushort, int> KeyToSlot;

            [ReadOnly] public UnsafeComponentLookup<LocalToWorld> LocalToWorldLookup;
            [ReadOnly] public UnsafeComponentLookup<Targets> TargetsLookup;
            [ReadOnly] public UnsafeComponentLookup<EntityLinkSource> LinkSources;
            [ReadOnly] public UnsafeBufferLookup<EntityLinkEntry> Links;

            [NativeDisableParallelForRestriction] public NativeReference<int> MissingLtw;

            public float CellSize;
            public GridBasis Basis;

            private void Execute(in InfluenceClipData clip, in DynamicBuffer<InfluenceStampElement> extras,
                in TrackBinding binding, in ClipWeight weight)
            {
                if (!KeyToSlot.TryGetValue(clip.FieldKey, out var slotIndex))
                    return;

                var targetEntity = binding.Value;
                if (targetEntity == Entity.Null)
                    return;

                var originEntity = OriginResolution.TryResolveOrigin(
                    clip.Origin, targetEntity, TargetsLookup, LinkSources, Links);
                if (!LocalToWorldLookup.TryGetComponent(originEntity, out var localToWorld))
                {
                    MissingLtw.Value = 1;
                    return;
                }

                var cellSpace = Basis.CellSpace(localToWorld.Position, localToWorld.Rotation, clip.LocalOffset, CellSize);
                var origin = GridBasis.Cell(cellSpace);

                if (clip.Composite.IsCreated)
                {
                    ref var layers = ref clip.Composite.Value.Layers;
                    for (var i = 0; i < layers.Length; i++)
                        Emit(slotIndex, layers[i], weight.Value, origin);
                }
                else
                {
                    Emit(slotIndex, clip.Shape, weight.Value, origin);
                }

                for (var i = 0; i < extras.Length; i++)
                    Emit(slotIndex, extras[i].Shape, weight.Value, origin);
            }

            private void Emit(int slotIndex, in InfluenceShape shape, float clipWeight, int2 origin)
            {
                if (!shape.TryScaleWeight(clipWeight, out var scaled))
                    return;

                StampsMap.Add(slotIndex, new Stamp(scaled, origin));
            }
        }
    }
}
