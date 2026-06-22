using System;
using System.Runtime.CompilerServices;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace BovineLabs.Timeline.Grid.Influence.Data.Flows
{
    public struct FlowField : INativeDisposable
    {
        private NativeList<int2> _direction;
        private JobHandle _dependency;

        public bool IsCreated => _direction.IsCreated;
        public JobHandle Dependency => _dependency;

        public static FlowField Create(Allocator allocator)
        {
            return new FlowField
            {
                _direction = new NativeList<int2>(64, allocator),
                _dependency = default
            };
        }

        public JobHandle Resolve(ref InfluenceField source, JobHandle dependsOn)
        {
            ThrowIfNotCreated();

            var combined = JobHandle.CombineDependencies(_dependency, dependsOn, source.Dependency);

            var resize = new ResizeJob
            {
                Direction = _direction,
                Source = source.DataDeferred
            }.Schedule(combined);

            var resolve = new ResolveJob
            {
                Reader = source.AsDeferredReader(),
                ActiveSlots = source.ActiveSlotsDeferred,
                CoordBySlot = source.CoordBySlotDeferred,
                Direction = _direction.AsDeferredJobArray(),
                Spec = source.Spec
            }.Schedule(source.ActiveSlotsList, 1, resize);

            _dependency = resolve;
            source.PublishDependency(resolve);
            return resolve;
        }

        public FlowReader AsReader(ref InfluenceField source)
        {
            ThrowIfNotCreated();
            return new FlowReader(
                source.SlotByCoordReadOnly,
                source.LastWrittenBySlotArray,
                _direction.AsArray(),
                source.Spec,
                source.FrameId);
        }

        internal FlowReader AsDeferredReader(ref InfluenceField source)
        {
            ThrowIfNotCreated();
            return new FlowReader(
                source.SlotByCoordReadOnly,
                source.LastWrittenBySlotDeferred,
                _direction.AsDeferredJobArray(),
                source.Spec,
                source.FrameId);
        }

        internal void PublishDependency(JobHandle handle)
        {
            _dependency = JobHandle.CombineDependencies(_dependency, handle);
        }

        public void Complete()
        {
            _dependency.Complete();
            _dependency = default;
        }

        public void Dispose()
        {
            if (!IsCreated) return;
            Complete();
            _direction.Dispose();
            this = default;
        }

        public JobHandle Dispose(JobHandle inputDeps)
        {
            if (!IsCreated) return inputDeps;
            var handle = JobHandle.CombineDependencies(inputDeps, _dependency);
            handle = _direction.Dispose(handle);
            this = default;
            return handle;
        }

        private void ThrowIfNotCreated()
        {
            if (!_direction.IsCreated) throw new ObjectDisposedException(nameof(FlowField));
        }

        [BurstCompile]
        private struct ResizeJob : IJob
        {
            public NativeList<int2> Direction;
            [ReadOnly] public NativeArray<int> Source;

            public void Execute()
            {
                if (Direction.Length != Source.Length)
                    Direction.Resize(Source.Length, NativeArrayOptions.ClearMemory);
            }
        }

        [BurstCompile]
        private struct ResolveJob : IJobParallelForDefer
        {
            public FieldReader Reader;
            [ReadOnly] public NativeArray<int> ActiveSlots;
            [ReadOnly] public NativeArray<int2> CoordBySlot;
            [NativeDisableParallelForRestriction] public NativeArray<int2> Direction;
            public GridSpec Spec;

            public void Execute(int index)
            {
                var slot = ActiveSlots[index];
                var baseCell = ChunkMath.ChunkBaseOf(CoordBySlot[slot], Spec.Log2);
                var baseIndex = slot * Spec.ElementsPerChunk;
                var stride = Spec.Stride;
                var chunkSize = Spec.ChunkSize;

                for (var ly = 0; ly < chunkSize; ly++)
                for (var lx = 0; lx < chunkSize; lx++)
                {
                    var cell = baseCell + new int2(lx, ly);
                    var dx = Reader.ReadCell(cell + new int2(1, 0)) - Reader.ReadCell(cell - new int2(1, 0));
                    var dy = Reader.ReadCell(cell + new int2(0, 1)) - Reader.ReadCell(cell - new int2(0, 1));
                    Direction[baseIndex + ly * stride + lx] = new int2(dx, dy);
                }
            }
        }
    }

    public struct FlowReader
    {
        [ReadOnly] private readonly NativeFlatMap.ReadOnly _slotByCoord;
        [ReadOnly] private NativeArray<uint> _lastWrittenBySlot;
        [ReadOnly] private NativeArray<int2> _direction;
        private readonly GridSpec _spec;
        private readonly uint _frameId;

        internal FlowReader(
            NativeFlatMap.ReadOnly slotByCoord,
            NativeArray<uint> lastWrittenBySlot,
            NativeArray<int2> direction,
            in GridSpec spec,
            uint frameId)
        {
            _slotByCoord = slotByCoord;
            _lastWrittenBySlot = lastWrittenBySlot;
            _direction = direction;
            _spec = spec;
            _frameId = frameId;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int2 Sample(int2 cell)
        {
            var coord = ChunkMath.ChunkCoordOf(cell, _spec.Log2);
            if (!_slotByCoord.TryGetValue(coord, out var slot) || _lastWrittenBySlot[slot] != _frameId)
                return int2.zero;

            var local = ChunkMath.LocalOf(cell, ChunkMath.ChunkBaseOf(coord, _spec.Log2));
            if (!ChunkMath.ContainsLocal(local, _spec.ChunkSize))
                return int2.zero;

            return _direction[ChunkMath.DataIndex(slot, local.x, local.y, _spec)];
        }
    }
}