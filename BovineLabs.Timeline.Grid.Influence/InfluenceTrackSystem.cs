
namespace BovineLabs.Timeline.Grid.Influence
{
    using BovineLabs.Core.Jobs;
    using BovineLabs.Timeline.Data;
    using BovineLabs.Timeline.Grid.Influence.Data;
    using Unity.Burst;
    using Unity.Burst.Intrinsics;
    using Unity.Collections;
    using Unity.Collections.LowLevel.Unsafe;
    using Unity.Entities;

    [UpdateInGroup(typeof(TimelineComponentAnimationGroup))]
    [WorldSystemFilter(WorldSystemFilterFlags.LocalSimulation | WorldSystemFilterFlags.ClientSimulation | WorldSystemFilterFlags.ServerSimulation)]
    public partial struct InfluenceTrackSystem : ISystem
    {
        private TrackBlendImpl<InfluenceData, InfluenceAnimated> _blendImpl;
        private ComponentLookup<ActiveInfluence> _activeLookup;

        private EntityQuery _prepareQuery;
        private EntityQuery _disableStaleQuery;

        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            _blendImpl.OnCreate(ref state);
            _activeLookup = state.GetComponentLookup<ActiveInfluence>(false);

            _prepareQuery = SystemAPI.QueryBuilder()
                .WithAllRW<InfluenceAnimated>()
                .WithAll<ClipActive>()
                .Build();

            _disableStaleQuery = SystemAPI.QueryBuilder()
                .WithAll<TrackBinding, TimelineActivePrevious, InfluenceAnimated>()
                .WithNone<TimelineActive>()
                .Build();
        }

        [BurstCompile]
        public void OnDestroy(ref SystemState state)
        {
            _blendImpl.OnDestroy(ref state);
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            _activeLookup.Update(ref state);

            var ecbSystem = SystemAPI.GetSingleton<BeginSimulationEntityCommandBufferSystem.Singleton>();
            var ecb = ecbSystem.CreateCommandBuffer(state.WorldUnmanaged).AsParallelWriter();

            var bindingType = SystemAPI.GetComponentTypeHandle<TrackBinding>(true);

            var animatedType = SystemAPI.GetComponentTypeHandle<InfluenceAnimated>();
            state.Dependency = new PrepareJob
            {
                AnimatedTypeHandle = animatedType
            }.ScheduleParallel(_prepareQuery, state.Dependency);

            state.Dependency = new InfluenceDisableStaleTrackJob<ActiveInfluence>
            {
                TrackBindingTypeHandle = bindingType,
                ActiveLookup = _activeLookup
            }.ScheduleParallel(_disableStaleQuery, state.Dependency);

            var blendData = _blendImpl.Update(ref state);

            state.Dependency = new WriteActiveJob
            {
                BlendData = blendData,
                ActiveLookup = _activeLookup,
                ECB = ecb
            }.ScheduleParallel(blendData, 64, state.Dependency);
        }

        [BurstCompile]
        private struct PrepareJob : IJobChunk
        {
            public ComponentTypeHandle<InfluenceAnimated> AnimatedTypeHandle;

            public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                var animatedData = chunk.GetNativeArray(ref AnimatedTypeHandle);
                var enumerator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
                while (enumerator.NextEntityIndex(out var i))
                {
                    var data = animatedData[i];
                    data.Value = data.AuthoredData;
                    animatedData[i] = data;
                }
            }
        }

        [BurstCompile]
        private struct WriteActiveJob : IJobParallelHashMapDefer
        {
            [ReadOnly] public NativeParallelHashMap<Entity, MixData<InfluenceData>>.ReadOnly BlendData;
            [ReadOnly] public ComponentLookup<ActiveInfluence> ActiveLookup;
            public EntityCommandBuffer.ParallelWriter ECB;

            public void ExecuteNext(int entryIndex, int jobIndex)
            {
                this.Read(BlendData, entryIndex, out var entity, out var mixData);
                if (!ActiveLookup.HasComponent(entity)) return;

                ECB.SetComponentEnabled<ActiveInfluence>(entryIndex, entity, true);
                ECB.SetComponent(entryIndex, entity, new ActiveInfluence
                {
                    Config = JobHelpers.Blend<InfluenceData, InfluenceMixer>(ref mixData, default)
                });
            }
        }

        [BurstCompile]
        private struct InfluenceDisableStaleTrackJob<TActive> : IJobChunk
            where TActive : unmanaged, IComponentData, IEnableableComponent
        {
            [ReadOnly] public ComponentTypeHandle<TrackBinding> TrackBindingTypeHandle;
            [NativeDisableParallelForRestriction] public ComponentLookup<TActive> ActiveLookup;

            public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                var bindings = chunk.GetNativeArray(ref TrackBindingTypeHandle);
                var enumerator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
                while (enumerator.NextEntityIndex(out var i))
                {
                    var target = bindings[i].Value;
                    if (target == Entity.Null) continue;
                    if (ActiveLookup.HasComponent(target))
                        ActiveLookup.SetComponentEnabled(target, false);
                }
            }
        }
    }
}
