using BovineLabs.Core.Extensions;
using BovineLabs.Core.Iterators;
using BovineLabs.Reaction.Data.Core;
using BovineLabs.Timeline.Data;
using BovineLabs.Timeline.EntityLinks.Data;
using BovineLabs.Timeline.Grid.Influence.Data;
using BovineLabs.Timeline.Grid.Influence.Data.Flows;
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
    [UpdateAfter(typeof(GridInfluenceApplySystem))]
    [WorldSystemFilter(WorldSystemFilterFlags.LocalSimulation | WorldSystemFilterFlags.ServerSimulation |
                       WorldSystemFilterFlags.ClientSimulation | WorldSystemFilterFlags.Editor)]
    public partial struct GridInfluenceQuerySystem : ISystem
    {
        private UnsafeComponentLookup<Targets> _targetsLookup;
        private UnsafeComponentLookup<EntityLinkSource> _linkSourceLookup;
        private UnsafeBufferLookup<EntityLinkEntry> _linkLookup;
        private UnsafeComponentLookup<LocalToWorld> _localToWorldLookup;

        private EntityQuery _resetQuery;
        private MissingFieldKeyWarnings _warnings;

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

            _resetQuery = SystemAPI.QueryBuilder()
                .WithAllRW<InfluenceQueryResult>()
                .WithDisabled<ClipActive>()
                .Build();

            _warnings = MissingFieldKeyWarnings.Create();
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

            // TODO-24: the sample job writes a flag when an origin resolves to an entity lacking LocalToWorld (a
            // silent no-op). Read it a frame later without ever blocking on the job, and warn once.
            if (!_warnedMissingLtw && _missingLtwWriter.IsCompleted)
            {
                _missingLtwWriter.Complete();
                if (_missingLtw.Value != 0)
                {
                    _warnedMissingLtw = true;
                    Debug.LogWarning("GridInfluence query: a query origin resolved to an entity without " +
                        "LocalToWorld; the sample is skipped. Ensure the origin/link target is a transform entity.");
                }
            }

            // TODO-31: only schedule the reset when query-result entities without an active clip can exist.
            if (!_resetQuery.IsEmptyIgnoreFilter)
                state.Dependency = new ResetQueryJob().ScheduleParallel(_resetQuery, state.Dependency);

            var settings = SystemAPI.GetSingleton<InfluenceGridSettings>();
            ref var fieldSingleton = ref SystemAPI.GetSingletonRW<FieldRegistrySingleton>().ValueRW;
            ref var reg = ref fieldSingleton.Registry;

            // TODO-07: warn once per unregistered field key referenced by an active query clip.
            foreach (var query in SystemAPI.Query<RefRO<InfluenceQueryData>>().WithAll<ClipActive>())
                _warnings.Report(query.ValueRO.FieldKey, reg.KeyToSlot, "GridInfluence query");

            // TODO-04: build one FieldReader per slot on the main thread instead of passing the registry (nested
            // native containers, invisible to the job safety system) and calling AsReader() per entity in parallel.
            var readersBySlot = CollectionHelper.CreateNativeArray<FieldReader>(reg.Count, state.WorldUpdateAllocator);
            var validBySlot = CollectionHelper.CreateNativeArray<byte>(reg.Count, state.WorldUpdateAllocator);

            var readers = state.Dependency;
            for (var i = 0; i < reg.Count; i++)
            {
                ref var pair = ref reg.Slot(i);
                readers = JobHandle.CombineDependencies(readers, pair.Front.Dependency);

                if (pair.Front.IsCreated)
                {
                    readersBySlot[i] = pair.Front.AsReader();
                    validBySlot[i] = 1;
                }
            }

            state.Dependency = new SampleQueryJob
            {
                KeyToSlot = reg.KeyToSlot,
                ReadersBySlot = readersBySlot,
                ValidBySlot = validBySlot,
                CellSize = math.max(0.0001f, settings.CellSize),
                Basis = new GridBasis(settings.PlaneNormal),
                LocalToWorldLookup = _localToWorldLookup,
                TargetsLookup = _targetsLookup,
                LinkSources = _linkSourceLookup,
                Links = _linkLookup,
                MissingLtw = _missingLtw,
            }.ScheduleParallel(readers);

            _missingLtwWriter = state.Dependency;
        }

        [BurstCompile]
        [WithAll(typeof(ClipActive))]
        private partial struct SampleQueryJob : IJobEntity
        {
            [ReadOnly] public NativeHashMap<ushort, int> KeyToSlot;
            [ReadOnly] public NativeArray<FieldReader> ReadersBySlot;
            [ReadOnly] public NativeArray<byte> ValidBySlot;

            [ReadOnly] public UnsafeComponentLookup<LocalToWorld> LocalToWorldLookup;
            [ReadOnly] public UnsafeComponentLookup<Targets> TargetsLookup;
            [ReadOnly] public UnsafeComponentLookup<EntityLinkSource> LinkSources;
            [ReadOnly] public UnsafeBufferLookup<EntityLinkEntry> Links;

            [NativeDisableParallelForRestriction] public NativeReference<int> MissingLtw;

            public float CellSize;
            public GridBasis Basis;

            private void Execute(in InfluenceQueryData query, in TrackBinding binding, ref InfluenceQueryResult result)
            {
                result.Valid = 0;

                if (!KeyToSlot.TryGetValue(query.FieldKey, out var slotIndex))
                    return;

                if (ValidBySlot[slotIndex] == 0)
                    return;

                var targetEntity = binding.Value;
                if (targetEntity == Entity.Null)
                    return;

                var originEntity = OriginResolution.TryResolveOrigin(
                    query.Origin, targetEntity, TargetsLookup, LinkSources, Links);
                if (!LocalToWorldLookup.TryGetComponent(originEntity, out var localToWorld))
                {
                    MissingLtw.Value = 1;
                    return;
                }

                var cellSpace = Basis.CellSpace(localToWorld.Position, localToWorld.Rotation, query.LocalOffset, CellSize);
                var cell = GridBasis.Cell(cellSpace);

                var reader = ReadersBySlot[slotIndex];

                result.Cell = cell;
                result.Value = reader.ReadCell(cell);
                result.Direction = FieldGradient.Ascent(reader, cell);

                result.ValueSmooth = reader.SampleBilinear(cellSpace);
                result.DirectionSmooth = new float2(
                    reader.SampleBilinear(cellSpace + new float2(1f, 0f)) -
                    reader.SampleBilinear(cellSpace - new float2(1f, 0f)),
                    reader.SampleBilinear(cellSpace + new float2(0f, 1f)) -
                    reader.SampleBilinear(cellSpace - new float2(0f, 1f)));

                result.Valid = 1;
            }
        }
        [BurstCompile]
        [WithDisabled(typeof(ClipActive))]
        private partial struct ResetQueryJob : IJobEntity
        {
            private void Execute(ref InfluenceQueryResult result)
            {
                if (result.Valid != 0)
                    result = default;
            }
        }
    }
}
