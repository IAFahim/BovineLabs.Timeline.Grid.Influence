using BovineLabs.Timeline.Grid.Influence.Data;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;

namespace BovineLabs.Timeline.Grid.Influence.Fields
{
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
                registry.Register(new FieldConfig
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

            var e = state.EntityManager.CreateEntity();
            state.EntityManager.AddComponentData(e, new FieldRegistrySingleton
            {
                Registry = registry,
                PendingStamps = new NativeParallelMultiHashMap<int, Stamp>(256, Allocator.Persistent)
            });

            _bootstrapped = true;
        }
    }

    [UpdateInGroup(typeof(SimulationSystemGroup), OrderLast = true)]
    public partial struct FieldTickSystem : ISystem
    {
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<FieldRegistrySingleton>();
        }

        public void OnUpdate(ref SystemState state)
        {
            ref var singleton = ref SystemAPI.GetSingletonRW<FieldRegistrySingleton>().ValueRW;
            ref var reg = ref singleton.Registry;
            var stampsMap = singleton.PendingStamps;

            var combinedWriters = state.Dependency;
            for (var i = 0; i < reg.Count; i++)
                combinedWriters = JobHandle.CombineDependencies(combinedWriters, reg.Slot(i).WriterDependency);
            combinedWriters.Complete();

            JobHandle combined = default;
            for (var i = 0; i < reg.Count; i++)
            {
                ref var pair = ref reg.Slot(i);

                if (pair.DoubleBuffered && pair.Config.DecayPerMille > 0)
                    pair.PendingStencil = new InfluenceField.StencilConfig
                    {
                        IsActive = true,
                        ActiveSlots = pair.Front.ActiveSlotsDeferred,
                        CoordBySlot = pair.Front.CoordBySlotDeferred,
                        Data = pair.Front.DataDeferred,
                        SlotByCoord = pair.Front.SlotByCoordReadOnly,
                        DecayPerMille = pair.Config.DecayPerMille,
                        SpreadDenominator = pair.Config.SpreadDenominator
                    };

                JobHandle h;
                if (pair.DoubleBuffered)
                    h = pair.Back.Schedule(stampsMap.AsReadOnly(), i, default, pair.PendingStencil);
                else
                    h = pair.Front.Schedule(stampsMap.AsReadOnly(), i, default, pair.PendingStencil);

                pair.Swap();

                pair.PendingStencil = default;
                pair.WriterDependency = default;

                combined = JobHandle.CombineDependencies(combined, h);
            }

            combined.Complete();
            state.Dependency = combined;
            stampsMap.Clear();
        }
    }
}