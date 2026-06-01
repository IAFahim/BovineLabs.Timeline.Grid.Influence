using BovineLabs.Timeline.Data;
using BovineLabs.Timeline.Grid.Influence.Data;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Transforms;

namespace BovineLabs.Timeline.Grid.Influence
{
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(TimelineComponentAnimationGroup))]
    [WorldSystemFilter(WorldSystemFilterFlags.LocalSimulation | WorldSystemFilterFlags.ServerSimulation | WorldSystemFilterFlags.ClientSimulation | WorldSystemFilterFlags.Editor)]
    public partial struct InfluenceApplySystem : ISystem
    {
        private EntityQuery _activeQuery;

        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<InfluenceGridSettings>();

            _activeQuery = SystemAPI.QueryBuilder()
                .WithAll<InfluenceClipData, TrackBinding, ClipActive>()
                .Build();

            if (!SystemAPI.HasSingleton<InfluenceGridDependency>())
            {
                var entity = state.EntityManager.CreateEntity();
                state.EntityManager.AddComponentData(entity, new InfluenceGridDependency());
            }

            if (!SystemAPI.HasSingleton<InfluenceGridComponent>())
            {
                var entity = state.EntityManager.CreateEntity();
                state.EntityManager.AddComponentData(entity, new InfluenceGridComponent());
            }
        }

        [BurstCompile]
        public void OnDestroy(ref SystemState state)
        {
            if (SystemAPI.TryGetSingleton<InfluenceGridDependency>(out var gridDependency))
            {
                gridDependency.Value.Complete();
            }

            state.Dependency.Complete();

            if (SystemAPI.TryGetSingletonRW<InfluenceGridComponent>(out var gridComp))
            {
                if (gridComp.ValueRO.Grid.IsCreated)
                {
                    gridComp.ValueRW.Grid.Dispose();
                    gridComp.ValueRW.Grid = default;
                }
            }
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            var settings = SystemAPI.GetSingleton<InfluenceGridSettings>();

            var gridDependencyRw = SystemAPI.GetSingletonRW<InfluenceGridDependency>();
            state.Dependency = JobHandle.CombineDependencies(
                state.Dependency,
                gridDependencyRw.ValueRO.Value);

            var gridRw = SystemAPI.GetSingletonRW<InfluenceGridComponent>();

            if (!gridRw.ValueRO.Grid.IsCreated)
            {
                gridRw.ValueRW.Grid = InfluenceGrid.Create(
                    settings.ChunkSizePowerOfTwo,
                    settings.ChunkRetentionFrames,
                    Allocator.Persistent);

                gridDependencyRw.ValueRW.Value = state.Dependency;
                return;
            }

            var grid = gridRw.ValueRW.Grid;

            var count = _activeQuery.CalculateEntityCountWithoutFiltering();
            if (count == 0)
            {
                state.Dependency = new BeginFrameJob
                {
                    Grid = grid
                }.Schedule(state.Dependency);

                gridDependencyRw.ValueRW.Value = state.Dependency;
                return;
            }

            var stamps = new NativeList<Stamp>(count, state.WorldUpdateAllocator);
            var basis = new GridBasis(settings.PlaneNormal);

            state.Dependency = new GatherStampsJob
            {
                Stamps = stamps.AsParallelWriter(),
                CellSize = settings.CellSize,
                Basis = basis,
                LtwComponentLookup = SystemAPI.GetComponentLookup<LocalToWorld>(true)
            }.ScheduleParallel(_activeQuery, state.Dependency);

            state.Dependency = new ScatterJob
            {
                Stamps = stamps.AsDeferredJobArray(),
                Grid = grid
            }.Schedule(state.Dependency);

            state.Dependency = new ResolveJob
            {
                ActiveChunks = grid.ActiveChunks.AsDeferredJobArray(),
                ChunkData = grid.ChunkData.AsDeferredJobArray(),
                Stride = grid.Stride,
                Dimension = grid.Dimension
            }.Schedule(grid.ActiveChunks, 1, state.Dependency);

            gridDependencyRw.ValueRW.Value = state.Dependency;
        }

        [BurstCompile]
        [WithAll(typeof(ClipActive))]
        private partial struct GatherStampsJob : IJobEntity
        {
            public NativeList<Stamp>.ParallelWriter Stamps;
            [ReadOnly] public ComponentLookup<LocalToWorld> LtwComponentLookup; 
            public float CellSize;
            public GridBasis Basis;

            private void Execute(in InfluenceClipData clipData, in TrackBinding binding)
            {
                if (binding.Value == Entity.Null || !LtwComponentLookup.TryGetComponent(binding.Value, out var ltw)) 
                    return;

                var worldPos = ltw.Position + math.rotate(ltw.Rotation, clipData.LocalOffset);
                var projectedPos = Basis.ToGridSpace(worldPos);

                var gridOrigin = new int2(
                    (int)math.floor(projectedPos.x / CellSize),
                    (int)math.floor(projectedPos.y / CellSize)
                );

                Stamps.AddNoResize(new Stamp(clipData.Shape, gridOrigin));
            }
        }

        [BurstCompile]
        private struct BeginFrameJob : IJob
        {
            public InfluenceGrid Grid;

            public void Execute()
            {
                Grid.BeginFrame();
            }
        }

        [BurstCompile]
        private struct ScatterJob : IJob
        {
            [ReadOnly] public NativeArray<Stamp> Stamps;
            public InfluenceGrid Grid;

            public void Execute()
            {
                Grid.BeginFrame();

                var rects = new NativeList<WorldRect>(Stamps.Length * 2, Allocator.Temp);
                for (int i = 0; i < Stamps.Length; i++)
                {
                    Rasterizer.Emit(Stamps[i], ref rects);
                }

                int elementsPerChunk = Grid.Stride * Grid.Dimension;

                for (int i = 0; i < rects.Length; i++)
                {
                    var bounds = rects[i].Bounds;
                    if (bounds.IsEmpty) continue;

                    int weight = rects[i].Weight;
                    int cx0 = bounds.Min.x >> Grid.Log2;
                    int cy0 = bounds.Min.y >> Grid.Log2;
                    int cx1 = (bounds.Max.x - 1) >> Grid.Log2;
                    int cy1 = (bounds.Max.y - 1) >> Grid.Log2;

                    for (int cy = cy0; cy <= cy1; cy++)
                    {
                        for (int cx = cx0; cx <= cx1; cx++)
                        {
                            var coord = new int2(cx, cy);
                            int slotIdx = Grid.GetOrCreateChunkSlot(coord, elementsPerChunk);

                            if (Grid.ChunkLastWrittenFrame[slotIdx] != Grid.FrameId.Value)
                            {
                                int startIdx = slotIdx * elementsPerChunk;
                                unsafe 
                                {
                                    UnsafeUtility.MemClear((int*)Grid.ChunkData.GetUnsafePtr() + startIdx, elementsPerChunk * sizeof(int));
                                }
                                Grid.ChunkLastWrittenFrame[slotIdx] = Grid.FrameId.Value;
                                Grid.ActiveChunks.Add(slotIdx);
                            }

                            int baseX = IntegerMath.ShiftLeftSaturating(cx, Grid.Log2);
                            int baseY = IntegerMath.ShiftLeftSaturating(cy, Grid.Log2);
                            int lx0 = math.max(bounds.Min.x, baseX) - baseX;
                            int ly0 = math.max(bounds.Min.y, baseY) - baseY;
                            int lx1 = math.min(bounds.Max.x, baseX + Grid.ChunkSize) - baseX;
                            int ly1 = math.min(bounds.Max.y, baseY + Grid.ChunkSize) - baseY;

                            if (lx0 >= lx1 || ly0 >= ly1) continue;

                            int baseDataIdx = slotIdx * elementsPerChunk;
                            AddDifference(ref Grid, baseDataIdx + ly0 * Grid.Stride + lx0, weight);
                            AddDifference(ref Grid, baseDataIdx + ly0 * Grid.Stride + lx1, -weight);
                            AddDifference(ref Grid, baseDataIdx + ly1 * Grid.Stride + lx0, -weight);
                            AddDifference(ref Grid, baseDataIdx + ly1 * Grid.Stride + lx1, weight);
                        }
                    }
                }
                rects.Dispose();
            }

            private static void AddDifference(ref InfluenceGrid grid, int index, int delta)
            {
                grid.ChunkData[index] = IntegerMath.SaturatingAdd(grid.ChunkData[index], delta);
            }
        }

        [BurstCompile]
        private struct ResolveJob : IJobParallelForDefer
        {
            [ReadOnly] public NativeArray<int> ActiveChunks;
            [NativeDisableParallelForRestriction] public NativeArray<int> ChunkData;
            public int Stride;
            public int Dimension;

            public unsafe void Execute(int index)
            {
                int slotIdx = ActiveChunks[index];
                int baseDataIdx = slotIdx * Stride * Dimension;

                int* field = (int*)ChunkData.GetUnsafePtr() + baseDataIdx;
                PrefixSumResolve.Run(field, Stride, Dimension);
            }
        }
    }
}
