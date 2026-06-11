using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Mathematics;

namespace BovineLabs.Timeline.Grid.Influence.Data
{
    public unsafe struct PrepareSlotsHelper
    {
        internal const int MaxSpansPerSchedule = 1 << 20;

        public NativeList<int> Offsets;
        public NativeList<WeightedRect> Spans;
        public NativeList<int> StampCount;

        public NativeFlatMap SlotByCoord;
        public NativeList<int> FreeSlots;
        public NativeList<int2> CoordBySlot;
        public NativeList<uint> LastWrittenBySlot;
        public NativeList<byte> NonZeroBySlot;
        public NativeList<int> ActiveSlots;
        public NativeList<int> Data;
        public GridSpec Spec;
        public uint FrameId;
        public bool ResetFrame;
        public uint RetentionFrames;

        public bool HasStencil;
        [ReadOnly] public NativeArray<int> StencilActiveSlots;
        [ReadOnly] public NativeArray<int2> StencilCoordBySlot;
        [ReadOnly] public NativeArray<int> StencilData;
        [ReadOnly] public NativeArray<byte> StencilNonZeroBySlot;
        public int DecayPerMille;
        public int SpreadDenominator;

        public void Execute()
        {
            if (ResetFrame)
                for (var i = 0; i < LastWrittenBySlot.Length; i++)
                    LastWrittenBySlot[i] = 0;

            if (RetentionFrames != uint.MaxValue)
            {
                var minValidFrame = FrameId > RetentionFrames ? FrameId - RetentionFrames : 0;
                for (var i = 0; i < LastWrittenBySlot.Length; i++)
                    if (LastWrittenBySlot[i] != 0 && LastWrittenBySlot[i] < minValidFrame)
                    {
                        LastWrittenBySlot[i] = 0;
                        FreeSlots.Add(i);
                        SlotByCoord.Remove(CoordBySlot[i]);
                    }
            }

            if (FrameId % 60 == 0 && FreeSlots.Length > 0)
            {
                var highestSlot = CoordBySlot.Length - 1;
                for (var i = 0; i < FreeSlots.Length; i++)
                {
                    while (highestSlot >= 0 && LastWrittenBySlot[highestSlot] == 0) highestSlot--;

                    var freeSlot = FreeSlots[i];
                    if (freeSlot >= highestSlot) continue;

                    var elements = Spec.ElementsPerChunk;
                    void* dst = Data.GetUnsafePtr() + freeSlot * elements;
                    void* src = Data.GetUnsafePtr() + highestSlot * elements;
                    UnsafeUtility.MemCpy(dst, src, elements * sizeof(int));

                    var coord = CoordBySlot[highestSlot];
                    CoordBySlot[freeSlot] = coord;
                    LastWrittenBySlot[freeSlot] = LastWrittenBySlot[highestSlot];
                    NonZeroBySlot[freeSlot] = NonZeroBySlot[highestSlot];
                    SlotByCoord.Add(coord, freeSlot);

                    LastWrittenBySlot[highestSlot] = 0;
                    highestSlot--;
                }

                var newCount = highestSlot + 1;
                if (newCount < CoordBySlot.Length)
                {
                    CoordBySlot.Length = newCount;
                    LastWrittenBySlot.Length = newCount;
                    NonZeroBySlot.Length = newCount;
                    Data.Length = newCount * Spec.ElementsPerChunk;
                }

                FreeSlots.Clear();
                for (var slot = 0; slot < newCount; slot++)
                    if (LastWrittenBySlot[slot] == 0)
                        FreeSlots.Add(slot);
            }

            ActiveSlots.Clear();

            if (HasStencil) ActivateStencilFrontier();
        }

        public void ProcessStamps(NativeArray<Stamp> resolved)
        {
            StampCount.Length = resolved.Length;
            if (resolved.Length == 0) return;

            var running = 0;
            Offsets.Length = resolved.Length + 1;
            for (var i = 0; i < resolved.Length; i++)
            {
                Offsets[i] = running;
                var shape = resolved[i].Shape;
                var estimate = Rasterizer.EstimateSpanCount(shape);
                if (estimate <= 0 || estimate > MaxSpansPerSchedule - running)
                    continue;

                running += estimate;
                ActivateBounds(Rasterizer.Bounds(shape, resolved[i].Origin));
            }

            Offsets[resolved.Length] = running;
            Spans.Length = running;
        }

        private void ActivateStencilFrontier()
        {
            var chunkSize = Spec.ChunkSize;
            var stride = Spec.Stride;
            var elements = Spec.ElementsPerChunk;

            for (var i = 0; i < StencilActiveSlots.Length; i++)
            {
                var slot = StencilActiveSlots[i];
                if ((uint)slot < (uint)StencilNonZeroBySlot.Length && StencilNonZeroBySlot[slot] == 0)
                    continue;

                var coord = StencilCoordBySlot[slot];
                Activate(coord);

                var baseIndex = slot * elements;

                if (NeedsActivationEdge(baseIndex, 0, 0, 0, 1, chunkSize, stride))
                    Activate(coord + new int2(-1, 0));

                if (NeedsActivationEdge(baseIndex, chunkSize - 1, 0, 0, 1, chunkSize, stride))
                    Activate(coord + new int2(1, 0));

                if (NeedsActivationEdge(baseIndex, 0, 0, 1, 0, chunkSize, stride))
                    Activate(coord + new int2(0, -1));

                if (NeedsActivationEdge(baseIndex, 0, chunkSize - 1, 1, 0, chunkSize, stride))
                    Activate(coord + new int2(0, 1));
            }
        }

        private bool NeedsActivationEdge(int baseIndex, int startX, int startY, int dx, int dy, int count, int stride)
        {
            for (var i = 0; i < count; i++)
            {
                var x = startX + i * dx;
                var y = startY + i * dy;
                if (IntegerMath.Outflow(StencilData[baseIndex + y * stride + x], DecayPerMille, SpreadDenominator) !=
                    0)
                    return true;
            }

            return false;
        }

        private void ActivateBounds(in CellRect bounds)
        {
            if (bounds.IsEmpty) return;
            var chunks = ChunkMath.ChunkRangeOf(bounds, Spec.Log2);

            for (var cy = chunks.Min.y; cy <= chunks.Max.y; cy++)
            for (var cx = chunks.Min.x; cx <= chunks.Max.x; cx++)
                Activate(new int2(cx, cy));
        }

        private void Activate(int2 coord)
        {
            var slot = EnsureSlot(coord);
            if (LastWrittenBySlot[slot] == FrameId) return;

            LastWrittenBySlot[slot] = FrameId;
            ActiveSlots.Add(slot);
        }

        private int EnsureSlot(int2 coord)
        {
            if (SlotByCoord.TryGetValue(coord, out var existing)) return existing;

            int slot;
            if (FreeSlots.Length > 0)
            {
                slot = FreeSlots[FreeSlots.Length - 1];
                FreeSlots.RemoveAtSwapBack(FreeSlots.Length - 1);
                CoordBySlot[slot] = coord;
                LastWrittenBySlot[slot] = 0;
                NonZeroBySlot[slot] = 0;
            }
            else
            {
                slot = CoordBySlot.Length;
                CoordBySlot.Add(coord);
                LastWrittenBySlot.Add(0);
                NonZeroBySlot.Add(0);
                Data.ResizeUninitialized(Data.Length + Spec.ElementsPerChunk);
            }

            SlotByCoord.Add(coord, slot);
            return slot;
        }
    }

    [BurstCompile]
    public struct PrepareSlotsFromMapJob : IJob
    {
        [ReadOnly] public NativeParallelMultiHashMap<int, Stamp>.ReadOnly StampsMap;
        public int SlotIndex;
        public NativeList<Stamp> ExtractedStamps;

        public PrepareSlotsHelper Helper;

        public void Execute()
        {
            Helper.Execute();

            ExtractedStamps.Clear();
            if (StampsMap.TryGetFirstValue(SlotIndex, out var stamp, out var it))
            {
                ExtractedStamps.Add(stamp);
                while (StampsMap.TryGetNextValue(out stamp, ref it)) ExtractedStamps.Add(stamp);
            }

            Helper.ProcessStamps(ExtractedStamps.AsArray());
        }
    }

    [BurstCompile]
    public struct PrepareSlotsFromArrayJob : IJob
    {
        [ReadOnly] public NativeArray<Stamp> Stamps;
        public PrepareSlotsHelper Helper;

        public void Execute()
        {
            Helper.Execute();

            if (!Stamps.IsCreated)
            {
                Helper.StampCount.Length = 0;
                return;
            }

            Helper.ProcessStamps(Stamps);
        }
    }
}