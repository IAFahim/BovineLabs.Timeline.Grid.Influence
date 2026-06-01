namespace BovineLabs.Timeline.Grid.Influence
{
    using BovineLabs.Timeline.Data;
    using BovineLabs.Timeline.Grid.Influence.Data;
    using Unity.Burst;
    using Unity.Collections;
    using Unity.Collections.LowLevel.Unsafe;
    using Unity.Entities;
    using Unity.Jobs;
    using Unity.Mathematics;
    using Unity.Transforms;

    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(TimelineComponentAnimationGroup))]
    [WorldSystemFilter(WorldSystemFilterFlags.LocalSimulation | WorldSystemFilterFlags.ServerSimulation | WorldSystemFilterFlags.ClientSimulation)]
    public partial struct InfluenceApplySystem : ISystem
    {
        private EntityQuery _activeQuery;

        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            var grid = InfluenceGrid.Create(16, Allocator.Persistent);
            state.EntityManager.AddComponentData(state.SystemHandle, new InfluenceGridComponent { Grid = grid });
            state.EntityManager.AddComponentData(state.SystemHandle, new InfluenceGridSettings { CellSize = 1f, PlaneNormal = new float3(0, 1, 0) });

            _activeQuery = SystemAPI.QueryBuilder()
                .WithAll<InfluenceClipData, TrackBinding, ClipActive>()
                .Build();
        }

        [BurstCompile]
        public void OnDestroy(ref SystemState state)
        {
            SystemAPI.GetComponent<InfluenceGridComponent>(state.SystemHandle).Grid.Dispose();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            var settings = SystemAPI.GetComponent<InfluenceGridSettings>(state.SystemHandle);
            var grid = SystemAPI.GetComponent<InfluenceGridComponent>(state.SystemHandle).Grid;

            var count = _activeQuery.CalculateEntityCountWithoutFiltering();
            var stamps = new NativeList<Stamp>(count, state.WorldUpdateAllocator);

            state.Dependency = new GatherStampsJob
            {
                Stamps = stamps.AsParallelWriter(),
                CellSize = settings.CellSize,
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
        }

        [BurstCompile]
        [WithAll(typeof(ClipActive))]
        private partial struct GatherStampsJob : IJobEntity
        {
            public NativeList<Stamp>.ParallelWriter Stamps;
            [ReadOnly] public ComponentLookup<LocalToWorld> LtwComponentLookup; 
            public float CellSize;

            private void Execute(in InfluenceClipData clipData, in TrackBinding binding)
            {
                if (binding.Value == Entity.Null) return;
                if (!LtwComponentLookup.TryGetComponent(binding.Value, out var ltw)) return;

                var worldPos = ltw.Position + math.rotate(ltw.Rotation, clipData.LocalOffset);

                var gridOrigin = new int2(
                    (int)math.floor(worldPos.x / CellSize),
                    (int)math.floor(worldPos.z / CellSize)
                );

                Stamps.AddNoResize(new Stamp(clipData.Shape, gridOrigin));
            }
        }

        [BurstCompile]
        private struct ScatterJob : IJob
        {
            [ReadOnly] public NativeArray<Stamp> Stamps;
            public InfluenceGrid Grid;

            public void Execute()
            {
                Grid.ActiveChunks.Clear();
                Grid.FrameId.Value++;
                if (Grid.FrameId.Value == 0) // overflow
                {
                    for(int i = 0; i < Grid.ChunkLastWrittenFrame.Length; i++)
                        Grid.ChunkLastWrittenFrame[i] = 0;
                    Grid.FrameId.Value = 1;
                }

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
                            if (!Grid.ChunkIndex.TryGetValue(coord, out int slotIdx))
                            {
                                slotIdx = Grid.ChunkCoords.Length;
                                Grid.ChunkCoords.Add(coord);
                                Grid.ChunkLastWrittenFrame.Add(0);
                                Grid.ChunkData.ResizeUninitialized(Grid.ChunkData.Length + elementsPerChunk);
                                Grid.ChunkIndex.Add(coord, slotIdx);
                            }

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

                            int baseX = cx << Grid.Log2;
                            int baseY = cy << Grid.Log2;
                            int lx0 = math.max(bounds.Min.x, baseX) - baseX;
                            int ly0 = math.max(bounds.Min.y, baseY) - baseY;
                            int lx1 = math.min(bounds.Max.x, baseX + Grid.ChunkSize) - baseX;
                            int ly1 = math.min(bounds.Max.y, baseY + Grid.ChunkSize) - baseY;

                            if (lx0 >= lx1 || ly0 >= ly1) continue;

                            int baseDataIdx = slotIdx * elementsPerChunk;
                            
                            Grid.ChunkData[baseDataIdx + ly0 * Grid.Stride + lx0] += weight;
                            Grid.ChunkData[baseDataIdx + ly0 * Grid.Stride + lx1] -= weight;
                            Grid.ChunkData[baseDataIdx + ly1 * Grid.Stride + lx0] -= weight;
                            Grid.ChunkData[baseDataIdx + ly1 * Grid.Stride + lx1] += weight;
                        }
                    }
                }
                rects.Dispose();
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