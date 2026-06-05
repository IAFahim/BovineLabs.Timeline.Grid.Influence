using BovineLabs.Core.ConfigVars;
using BovineLabs.Core.Extensions;
using BovineLabs.Core.Iterators;
using BovineLabs.Timeline.Grid.Influence.Data;
using BovineLabs.Timeline.Physics;
using BovineLabs.Timeline.Physics.Infrastructure;
using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace BovineLabs.Timeline.Grid.Influence
{
    [Configurable]
    [UpdateInGroup(typeof(PhysicsProducerGroup))]
    [WorldSystemFilter(WorldSystemFilterFlags.LocalSimulation | WorldSystemFilterFlags.ClientSimulation | WorldSystemFilterFlags.ServerSimulation)]
    public partial struct GridFlowSteeringApplySystem : ISystem
    {
        private EntityTypeHandle _entityHandle;
        private ComponentTypeHandle<ActiveGridFlowSteering> _activeHandle;
        private ComponentTypeHandle<GridFlowSteeringState> _stateHandle;
        private ComponentTypeHandle<LocalToWorld> _ltwHandle;
        private BufferTypeHandle<PendingForce> _forceHandle;

        private EntityQuery _query;

        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<FieldRegistrySingleton>();
            state.RequireForUpdate<InfluenceGridSettings>();

            _entityHandle = state.GetEntityTypeHandle();
            _activeHandle = state.GetComponentTypeHandle<ActiveGridFlowSteering>(true);
            _stateHandle = state.GetComponentTypeHandle<GridFlowSteeringState>();
            _ltwHandle = state.GetComponentTypeHandle<LocalToWorld>(true);
            _forceHandle = state.GetBufferTypeHandle<PendingForce>();

            _query = SystemAPI.QueryBuilder()
                .WithAllRW<GridFlowSteeringState, PendingForce>()
                .WithAll<ActiveGridFlowSteering, LocalToWorld>()
                .Build();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            var dt = SystemAPI.Time.DeltaTime;

            _entityHandle.Update(ref state);
            _activeHandle.Update(ref state);
            _stateHandle.Update(ref state);
            _ltwHandle.Update(ref state);
            _forceHandle.Update(ref state);

            var settings = SystemAPI.GetSingleton<InfluenceGridSettings>();
            ref var fieldSingleton = ref SystemAPI.GetSingletonRW<FieldRegistrySingleton>().ValueRW;

            state.Dependency = new ComputeSteeringJob
            {
                DeltaTime = dt,
                CellSize = math.max(0.0001f, settings.CellSize),
                Basis = new GridBasis(settings.PlaneNormal),
                KeyToSlot = fieldSingleton.Registry.KeyToSlot,
                Registry = fieldSingleton.Registry,
                EntityHandle = _entityHandle,
                ActiveHandle = _activeHandle,
                StateHandle = _stateHandle,
                LtwHandle = _ltwHandle,
                ForceHandle = _forceHandle
            }.ScheduleParallel(_query, state.Dependency);
        }

        [BurstCompile]
        private struct ComputeSteeringJob : IJobChunk
        {
            public float DeltaTime;
            public float CellSize;
            public GridBasis Basis;

            [ReadOnly] public NativeHashMap<ushort, int> KeyToSlot;
            [ReadOnly] public FieldRegistry Registry;

            [ReadOnly] public EntityTypeHandle EntityHandle;
            [ReadOnly] public ComponentTypeHandle<ActiveGridFlowSteering> ActiveHandle;
            public ComponentTypeHandle<GridFlowSteeringState> StateHandle;
            [ReadOnly] public ComponentTypeHandle<LocalToWorld> LtwHandle;
            public BufferTypeHandle<PendingForce> ForceHandle;

            public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                var actives = chunk.GetNativeArray(ref ActiveHandle);
                var states = chunk.GetNativeArray(ref StateHandle);
                var ltws = chunk.GetNativeArray(ref LtwHandle);
                var forces = chunk.GetBufferAccessor(ref ForceHandle);

                var enumerator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
                while (enumerator.NextEntityIndex(out var i))
                {
                    var config = actives[i].Config;
                    var state = states[i];

                    if (config.Mode == PhysicsForceMode.Impulse && state.Fired) continue;
                    if (config.Mode == PhysicsForceMode.Continuous && DeltaTime <= 0.0001f) continue;
                    if (config.Strength <= 0.0001f) continue;

                    if (!KeyToSlot.TryGetValue(config.FieldKey, out var slotIndex)) continue;

                    var field = Registry.Front(new FieldId(slotIndex));
                    if (!field.IsCreated) continue;

                    var reader = field.AsReader();
                    var pos = ltws[i].Position;
                    
                    var projected = Basis.ToGridSpace(pos);
                    var origin = new int2(
                        (int)math.floor(projected.x / CellSize),
                        (int)math.floor(projected.y / CellSize));

                    var gradient = CalculatePotentialGradient(in config.SamplerShape, origin, in reader, in Basis, CellSize, pos);
                    var magnitudeSq = math.lengthsq(gradient);

                    if (magnitudeSq > 1e-5f)
                    {
                        var forceDir = (gradient * math.rsqrt(magnitudeSq)) * (config.Strength * config.Polarity);
                        var timeScale = config.Mode == PhysicsForceMode.Impulse ? 1f : DeltaTime;
                        
                        forces[i].Add(new PendingForce
                        {
                            Linear = forceDir * timeScale,
                            Angular = float3.zero
                        });
                    }

                    if (config.Mode == PhysicsForceMode.Impulse)
                    {
                        state.Fired = true;
                        states[i] = state;
                    }
                }
            }

            private unsafe float3 CalculatePotentialGradient(
                in InfluenceShape shape, 
                int2 origin, 
                in FieldReader reader, 
                in GridBasis basis, 
                float cellSize, 
                float3 entityPos)
            {
                var capacity = Rasterizer.EstimateSpanCount(shape);
                if (capacity <= 0) return float3.zero;

                var spans = new NativeArray<WeightedRect>(capacity, Allocator.Temp);
                var sink = new SpanSink((WeightedRect*)spans.GetUnsafePtr(), capacity);
                Rasterizer.Emit(new Stamp(shape, origin), ref sink);

                var totalVector = float3.zero;
                var entityPlanar = basis.Right * math.dot(entityPos, basis.Right) + basis.Forward * math.dot(entityPos, basis.Forward);

                for (var i = 0; i < sink.Count; i++)
                {
                    var span = spans[i];
                    if (span.IsEmpty) continue;

                    for (var y = span.Bounds.Min.y; y < span.Bounds.Max.y; y++)
                    for (var x = span.Bounds.Min.x; x < span.Bounds.Max.x; x++)
                    {
                        var cell = new int2(x, y);
                        var influence = reader.ReadCell(cell);
                        if (influence == 0) continue;

                        var cellGridPos = new float2(x + 0.5f, y + 0.5f) * cellSize;
                        var cellPlanar = basis.Right * cellGridPos.x + basis.Forward * cellGridPos.y;

                        var diff = cellPlanar - entityPlanar;
                        var distSq = math.lengthsq(diff);

                        if (distSq > 1e-4f)
                            totalVector += (diff * math.rsqrt(distSq)) * influence * span.Weight;
                    }
                }

                spans.Dispose();
                return totalVector;
            }
        }
    }
}
