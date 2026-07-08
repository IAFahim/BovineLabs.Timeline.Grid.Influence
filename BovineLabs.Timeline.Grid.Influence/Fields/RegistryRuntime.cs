using BovineLabs.Core.ConfigVars;
using BovineLabs.Timeline.Grid.Influence.Data;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using UnityEngine;

namespace BovineLabs.Timeline.Grid.Influence.Fields
{
    [WorldSystemFilter(WorldSystemFilterFlags.LocalSimulation | WorldSystemFilterFlags.ServerSimulation |
                       WorldSystemFilterFlags.ClientSimulation | WorldSystemFilterFlags.Editor)]
    [UpdateInGroup(typeof(InitializationSystemGroup))]
    public partial struct FieldBootstrapSystem : ISystem
    {
        private bool _bootstrapped;

        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<InfluenceGridSettings>();
        }

        public void OnDestroy(ref SystemState state)
        {
            state.Dependency.Complete();
            if (SystemAPI.TryGetSingletonRW<FieldRegistrySingleton>(out var rw))
            {
                rw.ValueRW.Registry.Dispose();
                if (rw.ValueRO.PendingStamps.IsCreated)
                    rw.ValueRW.PendingStamps.Dispose();
            }
        }

        public void OnUpdate(ref SystemState state)
        {
            if (_bootstrapped) return;

            if (!SystemAPI.TryGetSingletonBuffer<GridFieldConfigData>(out var configs)) return;

            var registry = new FieldRegistry();
            registry.Initialize(configs.Length, Allocator.Persistent);

            foreach (var config in configs)
            {
                var id = registry.Register(new FieldConfig
                {
                    Key = config.Key,
                    Name = config.Name,
                    ChunkPower = config.ChunkPower,
                    RetentionFrames = config.RetentionFrames,
                    DoubleBuffered = config.DoubleBuffered,
                    DecayPerMille = config.DecayPerMille,
                    SpreadDenominator = config.SpreadDenominator,
                    StrideAlignment = config.StrideAlignment
                }, Allocator.Persistent);

                if (!id.IsValid)
                    Debug.LogWarning($"[GridInfluence] Field '{config.Name}' (key {config.Key}) was dropped (duplicate or capacity); clips using this key will resolve to a different field.");
            }

            var e = state.EntityManager.CreateEntity();
            state.EntityManager.AddComponentData(e, new FieldRegistrySingleton
            {
                Registry = registry,
                PendingStamps = new NativeParallelMultiHashMap<int, Stamp>(256, Allocator.Persistent)
            });

            _bootstrapped = true;
        }
    }

    [Configurable]
    public static class FieldTickConfig
    {
        // 0 = tick every rendered frame (default, matches historical behaviour). When > 0 the tick pump
        // accumulates DeltaTime and runs 0..2 field ticks per frame so decay/spread/retention are measured
        // in wall-clock time instead of frames (TODO-03). Catch-up is clamped to 2 sub-steps per frame.
        [ConfigVar("influencefield.tick-interval", 0f, "Fixed influence-field tick interval in seconds. 0 = tick every frame.")]
        public static readonly SharedStatic<float> TickInterval = SharedStatic<float>.GetOrCreate<TickIntervalTag>();

        private struct TickIntervalTag
        {
        }
    }

    [WorldSystemFilter(WorldSystemFilterFlags.LocalSimulation | WorldSystemFilterFlags.ServerSimulation |
                       WorldSystemFilterFlags.ClientSimulation | WorldSystemFilterFlags.Editor)]
    [UpdateInGroup(typeof(SimulationSystemGroup), OrderLast = true)]
    public partial struct FieldTickSystem : ISystem
    {
        private double _accumulator;

        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<FieldRegistrySingleton>();
        }

        public void OnUpdate(ref SystemState state)
        {
            ref var singleton = ref SystemAPI.GetSingletonRW<FieldRegistrySingleton>().ValueRW;
            ref var reg = ref singleton.Registry;
            var stampsMap = singleton.PendingStamps;

            WarnOnceOnDrops(ref reg);

            var subSteps = ResolveSubSteps(FieldTickConfig.TickInterval.Data, SystemAPI.Time.DeltaTime);

            if (subSteps == 0)
            {
                // GridInfluenceApplySystem re-adds this frame's stamps every rendered frame. When no sub-step
                // consumes them we still clear the map so the next frame starts from a fresh set rather than
                // doubling this frame's stamps into the next tick.
                state.Dependency = new ClearMapJob { Map = stampsMap }.Schedule(state.Dependency);
                return;
            }

            var combinedWriters = state.Dependency;
            for (var i = 0; i < reg.Count; i++)
                combinedWriters = JobHandle.CombineDependencies(combinedWriters, reg.Slot(i).WriterDependency);

            JobHandle combined = default;
            for (var step = 0; step < subSteps; step++)
            {
                var consumeStamps = step == 0;
                for (var i = 0; i < reg.Count; i++)
                {
                    ref var pair = ref reg.Slot(i);
                    pair.Tick++;

                    var stencil = default(InfluenceField.StencilConfig);
                    if (pair.DoubleBuffered && pair.Config.DecayPerMille > 0)
                        stencil = new InfluenceField.StencilConfig
                        {
                            IsActive = true,
                            ActiveSlots = pair.Front.ActiveSlotsDeferred,
                            CoordBySlot = pair.Front.CoordBySlotDeferred,
                            Data = pair.Front.DataDeferred,
                            NonZeroBySlot = pair.Front.NonZeroBySlotDeferred,
                            SlotByCoord = pair.Front.SlotByCoordReadOnly,
                            LastWrittenBySlot = pair.Front.LastWrittenBySlotDeferred,
                            FrameId = pair.Front.FrameId,
                            DecayPerMille = pair.Config.DecayPerMille,
                            SpreadDenominator = pair.Config.SpreadDenominator
                        };

                    JobHandle h;
                    if (pair.DoubleBuffered)
                        h = consumeStamps
                            ? pair.Back.Schedule(stampsMap.AsReadOnly(), i, pair.Tick, combinedWriters, stencil)
                            : pair.Back.Schedule(default(NativeArray<Stamp>), pair.Tick, combinedWriters, stencil);
                    else
                        h = consumeStamps
                            ? pair.Front.Schedule(stampsMap.AsReadOnly(), i, pair.Tick, combinedWriters, stencil)
                            : pair.Front.Schedule(default(NativeArray<Stamp>), pair.Tick, combinedWriters, stencil);

                    pair.Swap();
                    pair.WriterDependency = default;

                    combined = JobHandle.CombineDependencies(combined, h);
                }

                combinedWriters = combined;
            }

            state.Dependency = new ClearMapJob { Map = stampsMap }.Schedule(combined);
        }

        private int ResolveSubSteps(float interval, float deltaTime)
        {
            if (interval <= 0f)
            {
                _accumulator = 0d;
                return 1;
            }

            _accumulator += deltaTime;

            var subSteps = 0;
            while (_accumulator >= interval && subSteps < 2)
            {
                _accumulator -= interval;
                subSteps++;
            }

            if (_accumulator > interval)
                _accumulator = interval;

            return subSteps;
        }

        private static void WarnOnceOnDrops(ref FieldRegistry reg)
        {
#if UNITY_EDITOR || BL_DEBUG
            for (var i = 0; i < reg.Count; i++)
            {
                ref var pair = ref reg.Slot(i);
                if (pair.DropWarned != 0) continue;

                var field = pair.Front;
                if (!field.IsCreated || !field.Dependency.IsCompleted) continue;

                field.Complete();
                var stats = field.LastStats;
                var dropped = stats.StampsDroppedSpanBudget + stats.StampsDroppedChunkBudget;
                if (dropped <= 0) continue;

                pair.DropWarned = 1;
                Debug.LogWarning(
                    $"[GridInfluence] Field '{pair.Config.Name}' (key {pair.Config.Key}) dropped {dropped} stamp(s) last tick " +
                    $"(span-budget {stats.StampsDroppedSpanBudget}, chunk-budget {stats.StampsDroppedChunkBudget}). " +
                    "Reduce stamp radius/size/count or raise the budget.");
            }
#endif
        }

        [BurstCompile]
        private struct ClearMapJob : IJob
        {
            public NativeParallelMultiHashMap<int, Stamp> Map;

            public void Execute()
            {
                Map.Clear();
            }
        }
    }
}
