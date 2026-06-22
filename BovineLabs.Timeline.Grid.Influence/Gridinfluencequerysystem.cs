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
            ref var reg = ref fieldSingleton.Registry;

            var readers = state.Dependency;
            for (var i = 0; i < reg.Count; i++)
                readers = JobHandle.CombineDependencies(readers, reg.Slot(i).Front.Dependency);

            state.Dependency = new SampleQueryJob
            {
                KeyToSlot = reg.KeyToSlot,
                Registry = reg,
                CellSize = math.max(0.0001f, settings.CellSize),
                Basis = new GridBasis(settings.PlaneNormal),
                LocalToWorldLookup = _localToWorldLookup,
                TargetsLookup = _targetsLookup,
                LinkSources = _linkSourceLookup,
                Links = _linkLookup
            }.ScheduleParallel(readers);
        }

        [BurstCompile]
        [WithAll(typeof(ClipActive))]
        private partial struct SampleQueryJob : IJobEntity
        {
            [ReadOnly] public NativeHashMap<ushort, int> KeyToSlot;
            [ReadOnly] public FieldRegistry Registry;

            [ReadOnly] public UnsafeComponentLookup<LocalToWorld> LocalToWorldLookup;
            [ReadOnly] public UnsafeComponentLookup<Targets> TargetsLookup;
            [ReadOnly] public UnsafeComponentLookup<EntityLinkSource> LinkSources;
            [ReadOnly] public UnsafeBufferLookup<EntityLinkEntry> Links;

            public float CellSize;
            public GridBasis Basis;

            private void Execute(in InfluenceQueryData query, in TrackBinding binding, ref InfluenceQueryResult result)
            {
                result.Valid = 0;

                if (!KeyToSlot.TryGetValue(query.FieldKey, out var slotIndex))
                    return;

                var targetEntity = binding.Value;
                if (targetEntity == Entity.Null)
                    return;

                var originEntity = OriginResolution.TryResolveOrigin(
                    query.OriginTarget, query.OriginLinkKey, targetEntity, TargetsLookup, LinkSources, Links);
                if (!LocalToWorldLookup.TryGetComponent(originEntity, out var localToWorld))
                    return;

                var field = Registry.Front(new FieldId(slotIndex));
                if (!field.IsCreated)
                    return;

                var cellSpace = Basis.CellSpace(localToWorld.Position, localToWorld.Rotation, query.LocalOffset, CellSize);
                var cell = GridBasis.Cell(cellSpace);

                var reader = field.AsReader();

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
    }
}