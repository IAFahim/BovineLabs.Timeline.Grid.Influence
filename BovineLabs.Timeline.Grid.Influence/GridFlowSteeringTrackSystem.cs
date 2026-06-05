using BovineLabs.Core.Jobs;
using BovineLabs.Timeline.Data;
using BovineLabs.Timeline.EntityLinks;
using BovineLabs.Timeline.Grid.Influence.Data;
using BovineLabs.Timeline.Physics.Infrastructure;
using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Collections;
using Unity.Entities;

namespace BovineLabs.Timeline.Grid.Influence
{
    [UpdateInGroup(typeof(TimelineComponentAnimationGroup))]
    [UpdateAfter(typeof(EntityLinkTargetPatchSystem))]
    [WorldSystemFilter(WorldSystemFilterFlags.LocalSimulation | WorldSystemFilterFlags.ClientSimulation | WorldSystemFilterFlags.ServerSimulation)]
    public partial struct GridFlowSteeringTrackSystem : ISystem
    {
        private TrackBlendImpl<GridFlowSteeringData, GridFlowSteeringAnimated> _blendImpl;
        private ComponentLookup<ActiveGridFlowSteering> _activeLookup;
        private ComponentLookup<GridFlowSteeringState> _stateLookup;

        private EntityQuery _resetQuery;
        private EntityQuery _prepareQuery;
        private EntityQuery _disableStaleQuery;

        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            _blendImpl.OnCreate(ref state);
            _activeLookup = state.GetComponentLookup<ActiveGridFlowSteering>();
            _stateLookup = state.GetComponentLookup<GridFlowSteeringState>();

            _resetQuery = SystemAPI.QueryBuilder()
                .WithAll<TrackBinding, GridFlowSteeringAnimated, ClipActive>()
                .WithNone<ClipActivePrevious>()
                .Build();

            _prepareQuery = SystemAPI.QueryBuilder()
                .WithAllRW<GridFlowSteeringAnimated>()
                .WithAll<ClipActive>()
                .Build();

            _disableStaleQuery = SystemAPI.QueryBuilder()
                .WithAll<TrackBinding, TimelineActivePrevious, GridFlowSteeringAnimated>()
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
            _stateLookup.Update(ref state);

            var ecbSystem = SystemAPI.GetSingleton<BeginSimulationEntityCommandBufferSystem.Singleton>();
            var ecbWrite = ecbSystem.CreateCommandBuffer(state.WorldUnmanaged).AsParallelWriter();

            var bindingType = SystemAPI.GetComponentTypeHandle<TrackBinding>(true);
            
            state.Dependency = new ResetStateTrackJob<GridFlowSteeringState, ActiveGridFlowSteering>
            {
                TrackBindingTypeHandle = bindingType,
                StateLookup = _stateLookup,
                ActiveLookup = _activeLookup,
                ResetValue = new GridFlowSteeringState { Fired = false }
            }.ScheduleParallel(_resetQuery, state.Dependency);

            var animatedType = SystemAPI.GetComponentTypeHandle<GridFlowSteeringAnimated>();
            
            state.Dependency = new PrepareJob
            {
                AnimatedTypeHandle = animatedType
            }.ScheduleParallel(_prepareQuery, state.Dependency);

            state.Dependency = new DisableStaleTrackJob<ActiveGridFlowSteering>
            {
                TrackBindingTypeHandle = bindingType,
                ActiveLookup = _activeLookup
            }.ScheduleParallel(_disableStaleQuery, state.Dependency);

            var blendData = _blendImpl.Update(ref state);

            state.Dependency = new WriteActiveJob
            {
                BlendData = blendData,
                ActiveLookup = _activeLookup,
                ECB = ecbWrite
            }.ScheduleParallel(blendData, 64, state.Dependency);
        }

        [BurstCompile]
        private struct PrepareJob : IJobChunk
        {
            public ComponentTypeHandle<GridFlowSteeringAnimated> AnimatedTypeHandle;

            public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                var animateds = chunk.GetNativeArray(ref AnimatedTypeHandle);
                var enumerator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
                while (enumerator.NextEntityIndex(out var i))
                {
                    var animated = animateds[i];
                    animated.Value = animated.AuthoredData;
                    animateds[i] = animated;
                }
            }
        }

        [BurstCompile]
        private struct WriteActiveJob : IJobParallelHashMapDefer
        {
            [ReadOnly] public NativeParallelHashMap<Entity, MixData<GridFlowSteeringData>>.ReadOnly BlendData;
            [ReadOnly] public ComponentLookup<ActiveGridFlowSteering> ActiveLookup;
            public EntityCommandBuffer.ParallelWriter ECB;

            public void ExecuteNext(int entryIndex, int jobIndex)
            {
                this.Read(BlendData, entryIndex, out var entity, out var mixData);

                if (!ActiveLookup.HasComponent(entity))
                {
                    ECB.AddComponent<ActiveGridFlowSteering>(entryIndex, entity);
                    ECB.AddComponent<GridFlowSteeringState>(entryIndex, entity);
                    ECB.SetComponentEnabled<ActiveGridFlowSteering>(entryIndex, entity, false);
                }

                ECB.SetComponentEnabled<ActiveGridFlowSteering>(entryIndex, entity, true);
                ECB.SetComponent(entryIndex, entity, new ActiveGridFlowSteering
                {
                    Config = JobHelpers.Blend<GridFlowSteeringData, GridFlowSteeringMixer>(ref mixData, default)
                });
            }
        }
    }
}
