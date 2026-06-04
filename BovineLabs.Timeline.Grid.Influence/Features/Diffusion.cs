using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Transforms;
using BovineLabs.Timeline.Data;
using BovineLabs.Timeline.Grid.Influence.Data;
using BovineLabs.Timeline.Grid.Influence.Fields;

namespace BovineLabs.Timeline.Grid.Influence.Features.Diffusion
{
    public struct DiffusionClipTag : IComponentData { }

    public struct DiffusionFieldConfig : IComponentData
    {
        public FieldId Id;
        public int SpreadDenominator;
        public int DecayPerMille;
    }

    [UpdateInGroup(typeof(TimelineSystemGroup))]
    [UpdateAfter(typeof(TimelineComponentAnimationGroup))]
    [WorldSystemFilter(WorldSystemFilterFlags.LocalSimulation | WorldSystemFilterFlags.ClientSimulation |
                       WorldSystemFilterFlags.ServerSimulation | WorldSystemFilterFlags.Editor)]
    public partial struct DiffusionFieldSystem : ISystem
    {
        EntityQuery _clips;

        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<InfluenceGridSettings>();
            state.RequireForUpdate<FieldRegistrySingleton>();
            _clips = SystemAPI.QueryBuilder()
                .WithAll<InfluenceClipData, TrackBinding, ClipActive, DiffusionClipTag>()
                .Build();
        }

        public void OnUpdate(ref SystemState state)
        {
            ref var regSingleton = ref SystemAPI.GetSingletonRW<FieldRegistrySingleton>().ValueRW;
            if (!SystemAPI.HasSingleton<DiffusionFieldConfig>())
            {
                var settings = SystemAPI.GetSingleton<InfluenceGridSettings>();
                var id = regSingleton.Registry.Register(new FieldConfig
                {
                    Name = "Diffusion",
                    ChunkPower = settings.ChunkSizePowerOfTwo,
                    RetentionFrames = uint.MaxValue,
                    HasFeedback = true,
                    StrideAlignment = settings.StrideAlignment
                }, Allocator.Persistent);
                var e = state.EntityManager.CreateEntity();
                state.EntityManager.AddComponentData(e, new DiffusionFieldConfig { Id = id, SpreadDenominator = 5, DecayPerMille = 30 });

                return;
            }

            var config = SystemAPI.GetSingleton<DiffusionFieldConfig>();
            ref var pair = ref regSingleton.Registry.Slot(config.Id.Value);
            
            int count = _clips.CalculateEntityCount();

            if (count > 0)
            {
                if (!pair.PendingStamps.IsCreated)
                {
                    pair.PendingStamps = new NativeList<Stamp>(count, state.WorldUpdateAllocator);
                }
                else
                {
                    pair.WriterDependency.Complete();
                    pair.PendingStamps.Capacity = math.max(pair.PendingStamps.Capacity, pair.PendingStamps.Length + count);
                }
            }

            if (pair.Front.IsCreated)
            {
                pair.PendingStencil = new InfluenceField.StencilConfig
                {
                    IsActive = true,
                    ActiveSlots = pair.Front.ActiveSlotsDeferred,
                    CoordBySlot = pair.Front.CoordBySlotDeferred,
                    Data = pair.Front.DataDeferred,
                    SlotByCoord = pair.Front.SlotByCoordReadOnly,
                    DecayPerMille = config.DecayPerMille,
                    SpreadDenominator = config.SpreadDenominator
                };
            }

            if (count > 0)
            {
                var settings = SystemAPI.GetSingleton<InfluenceGridSettings>();

                JobHandle gather = new GatherDiffusionStampsJob
                {
                    Stamps = pair.PendingStamps.AsParallelWriter(),
                    CellSize = math.max(0.0001f, settings.CellSize),
                    Basis = new GridBasis(settings.PlaneNormal),
                    LocalToWorldLookup = SystemAPI.GetComponentLookup<LocalToWorld>(true)
                }.ScheduleParallel(_clips, state.Dependency);

                pair.WriterDependency = JobHandle.CombineDependencies(pair.WriterDependency, gather);
                state.Dependency = gather;
            }
        }

        [BurstCompile]
        [WithAll(typeof(ClipActive), typeof(DiffusionClipTag))]
        partial struct GatherDiffusionStampsJob : IJobEntity
        {
            public NativeList<Stamp>.ParallelWriter Stamps;
            [Unity.Collections.ReadOnly] public ComponentLookup<LocalToWorld> LocalToWorldLookup;
            public float CellSize;
            public GridBasis Basis;

            void Execute(in InfluenceClipData clip, in TrackBinding binding)
            {
                if (binding.Value == Entity.Null || !LocalToWorldLookup.TryGetComponent(binding.Value, out var localToWorld))
                    return;

                float3 world = localToWorld.Position + math.rotate(localToWorld.Rotation, clip.LocalOffset);
                float2 projected = Basis.ToGridSpace(world);

                int2 origin = new int2(
                    (int)math.floor(projected.x / CellSize),
                    (int)math.floor(projected.y / CellSize));

                Stamps.AddNoResize(new Stamp(clip.Shape, origin));
            }
        }
    }

    [BurstCompile]
    public unsafe struct DiffusionFeedbackJob : IJob
    {
        [Unity.Collections.ReadOnly] public NativeArray<int> ActiveSlots;
        [Unity.Collections.ReadOnly] public NativeArray<int2> CoordBySlot;
        [Unity.Collections.ReadOnly] public NativeArray<int> Data;
        public GridSpec Spec;
        public int SpreadDenominator;
        public int DecayPerMille;
        public NativeList<Stamp>.ParallelWriter Emit;

        public void Execute()
        {
            int chunkSize = Spec.ChunkSize;
            int stride = Spec.Stride;
            int elements = Spec.ElementsPerChunk;

            for (int i = 0; i < ActiveSlots.Length; i++)
            {
                int slot = ActiveSlots[i];
                int2 coord = CoordBySlot[slot];
                int baseX = coord.x * chunkSize;
                int baseY = coord.y * chunkSize;
                int baseIndex = slot * elements;

                for (int y = 0; y < chunkSize; y++)
                for (int x = 0; x < chunkSize; x++)
                {
                    int v = Data[baseIndex + y * stride + x];
                    if (v == 0) continue;

                    int vp = v - (int)((long)v * DecayPerMille / 1000);
                    if (vp == 0) continue;

                    int q = vp / SpreadDenominator;
                    int keep = vp - 4 * q;
                    int2 c = new int2(baseX + x, baseY + y);

                    if (keep != 0) Emit.AddNoResize(new Stamp(InfluenceShape.SolidRect(c, new int2(1, 1), keep), int2.zero));
                    if (q != 0)
                    {
                        Emit.AddNoResize(new Stamp(InfluenceShape.SolidRect(c + new int2(1, 0), new int2(1, 1), q), int2.zero));
                        Emit.AddNoResize(new Stamp(InfluenceShape.SolidRect(c + new int2(-1, 0), new int2(1, 1), q), int2.zero));
                        Emit.AddNoResize(new Stamp(InfluenceShape.SolidRect(c + new int2(0, 1), new int2(1, 1), q), int2.zero));
                        Emit.AddNoResize(new Stamp(InfluenceShape.SolidRect(c + new int2(0, -1), new int2(1, 1), q), int2.zero));
                    }
                }
            }
        }
    }
}
