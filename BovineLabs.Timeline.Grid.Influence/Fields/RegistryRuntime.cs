using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using BovineLabs.Timeline.Grid.Influence.Data;

namespace BovineLabs.Timeline.Grid.Influence.Fields
{
    public struct FieldRegistrySingleton : IComponentData
    {
        public FieldRegistry Registry;
    }

    [UpdateInGroup(typeof(InitializationSystemGroup))]
    public partial struct FieldBootstrapSystem : ISystem
    {
        public void OnCreate(ref SystemState state)
        {
            if (!SystemAPI.HasSingleton<FieldRegistrySingleton>())
            {
                var e = state.EntityManager.CreateEntity();
                state.EntityManager.AddComponentData(e, new FieldRegistrySingleton
                {
                    Registry = new FieldRegistry { Pairs = new NativeArray<InfluenceFieldPair>(16, Allocator.Persistent) }
                });
            }
        }

        public void OnDestroy(ref SystemState state)
        {
            state.Dependency.Complete();
            if (SystemAPI.TryGetSingletonRW<FieldRegistrySingleton>(out var rw))
            {
                rw.ValueRW.Registry.Dispose();
            }
        }

        public void OnUpdate(ref SystemState state) { }
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
            ref var reg = ref SystemAPI.GetSingletonRW<FieldRegistrySingleton>().ValueRW.Registry;

            JobHandle combined = state.Dependency;
            for (int i = 0; i < reg.Count; i++)
            {
                ref var pair = ref reg.Slot(i);

                var target = pair.DoubleBuffered ? pair.Back : pair.Front;
                var h = target.Schedule(
                    pair.PendingStamps.IsCreated ? pair.PendingStamps.AsDeferredJobArray() : default, 
                    pair.WriterDependency);
                
                pair.Swap();

                // Clear for next frame. The stamps were allocated via WorldUpdateAllocator or TempJob if persistent.
                // Assuming features use WorldUpdateAllocator, we just drop the reference.
                pair.PendingStamps = default;
                pair.WriterDependency = default;

                combined = JobHandle.CombineDependencies(combined, h);
            }
            state.Dependency = combined;
        }
    }
}
