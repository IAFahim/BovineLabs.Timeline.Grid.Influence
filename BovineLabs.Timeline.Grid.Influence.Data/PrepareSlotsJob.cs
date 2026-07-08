using System.Collections.Generic;
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
        internal const int MaxChunksPerSchedule = 1 << 14;

        public NativeList<int> Offsets;
        public NativeList<WeightedRect> Spans;
        public NativeList<int> StampCount;

        public NativeFlatMap SlotByCoord;
        public NativeList<int> FreeSlots;
        public NativeList<int2> CoordBySlot;
        public NativeList<uint> LastWrittenBySlot;
        public NativeList<byte> NonZeroBySlot;
        public NativeList<uint> PreparedBySlot;
        public NativeList<int> ActiveSlots;
        public NativeList<int> Data;
        public NativeReference<FieldFrameStats> Stats;
        public GridSpec Spec;
        public uint FrameId;
        public uint ScheduleVersion;
        public bool ResetFrame;
        public uint RetentionFrames;

        public bool HasStencil;
        [ReadOnly] public NativeArray<int> StencilActiveSlots;
        [ReadOnly] public NativeArray<int2> StencilCoordBySlot;
        [ReadOnly] public NativeArray<int> StencilData;
        [ReadOnly] public NativeArray<byte> StencilNonZeroBySlot;
        [ReadOnly] public NativeArray<uint> StencilLastWrittenBySlot;
        public uint StencilFrameId;
        public int DecayPerMille;
        public int SpreadDenominator;

        private int _chunksActivated;
        private int _chunksEvicted;

        public void Execute()
        {
            if (ResetFrame)
                for (var i = 0; i < LastWrittenBySlot.Length; i++)
                {
                    if (LastWrittenBySlot[i] == 0) continue;

                    LastWrittenBySlot[i] = 0;
                    FreeSlots.Add(i);
                    SlotByCoord.Remove(CoordBySlot[i]);
                    _chunksEvicted++;
                }

            if (RetentionFrames != uint.MaxValue)
            {
                var minValidFrame = FrameId > RetentionFrames ? FrameId - RetentionFrames : 0;
                for (var i = 0; i < LastWrittenBySlot.Length; i++)
                    if (LastWrittenBySlot[i] != 0 && LastWrittenBySlot[i] < minValidFrame)
                    {
                        LastWrittenBySlot[i] = 0;
                        FreeSlots.Add(i);
                        SlotByCoord.Remove(CoordBySlot[i]);
                        _chunksEvicted++;
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
                    PreparedBySlot[freeSlot] = PreparedBySlot[highestSlot];
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
                    PreparedBySlot.Length = newCount;
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
            var stampCount = resolved.IsCreated ? resolved.Length : 0;
            StampCount.Length = stampCount;
            if (stampCount == 0)
            {
                WriteStats(0, 0, 0);
                return;
            }

            var droppedSpanBudget = 0;
            var droppedChunkBudget = 0;
            long activatedChunks = 0;

            var running = 0;
            Offsets.Length = stampCount + 1;
            for (var i = 0; i < stampCount; i++)
            {
                Offsets[i] = running;
                var shape = resolved[i].Shape;
                var estimate = Rasterizer.EstimateSpanCount(shape);
                if (estimate <= 0 || estimate > MaxSpansPerSchedule - running)
                {
                    if (estimate > 0) droppedSpanBudget++;
                    continue;
                }

                var bounds = Rasterizer.Bounds(shape, resolved[i].Origin);
                if (bounds.IsEmpty) continue;

                var chunkCount = ChunkCountOf(bounds);
                if (chunkCount > MaxChunksPerSchedule - activatedChunks)
                {
                    droppedChunkBudget++;
                    continue;
                }

                activatedChunks += chunkCount;
                running += estimate;
                ActivateBounds(bounds);
            }

            Offsets[stampCount] = running;
            Spans.Length = running;

            WriteStats(stampCount, droppedSpanBudget, droppedChunkBudget);
        }

        private void WriteStats(int stampsIn, int droppedSpanBudget, int droppedChunkBudget)
        {
            if (!Stats.IsCreated) return;

            Stats.Value = new FieldFrameStats
            {
                StampsIn = stampsIn,
                StampsDroppedSpanBudget = droppedSpanBudget,
                StampsDroppedChunkBudget = droppedChunkBudget,
                ChunksActivated = _chunksActivated,
                ChunksEvicted = _chunksEvicted
            };
        }

        private long ChunkCountOf(in CellRect bounds)
        {
            var chunks = ChunkMath.ChunkRangeOf(bounds, Spec.Log2);
            return (long)(chunks.Max.x - chunks.Min.x + 1) * (chunks.Max.y - chunks.Min.y + 1);
        }

        private void ActivateStencilFrontier()
        {
            var chunkSize = Spec.ChunkSize;
            var stride = Spec.Stride;
            var elements = Spec.ElementsPerChunk;

            for (var i = 0; i < StencilActiveSlots.Length; i++)
            {
                var slot = StencilActiveSlots[i];
                if ((uint)slot < (uint)StencilLastWrittenBySlot.Length && StencilLastWrittenBySlot[slot] != StencilFrameId)
                    continue;

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

            // Dedupe ActiveSlots membership on the per-schedule version, NOT on LastWrittenBySlot.
            // Two Schedule calls with the same tick share a FrameId, and the second call must still
            // clear + resolve chunks it touches; keying the early-out on FrameId left those chunks
            // out of ActiveSlots while ScatterJob kept writing raw corner deltas into resolved data.
            if (PreparedBySlot[slot] == ScheduleVersion) return;

            PreparedBySlot[slot] = ScheduleVersion;
            LastWrittenBySlot[slot] = FrameId;
            ActiveSlots.Add(slot);
            _chunksActivated++;
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
                PreparedBySlot[slot] = 0;
            }
            else
            {
                slot = CoordBySlot.Length;
                CoordBySlot.Add(coord);
                LastWrittenBySlot.Add(0);
                NonZeroBySlot.Add(0);
                PreparedBySlot.Add(0);
                Data.ResizeUninitialized(Data.Length + Spec.ElementsPerChunk);
            }

            SlotByCoord.Add(coord, slot);
            return slot;
        }
    }

    internal struct StampOrder : IComparer<Stamp>
    {
        public int Compare(Stamp a, Stamp b)
        {
            var c = a.Origin.x.CompareTo(b.Origin.x);
            if (c != 0) return c;
            c = a.Origin.y.CompareTo(b.Origin.y);
            if (c != 0) return c;
            c = ((byte)a.Shape.Kind).CompareTo((byte)b.Shape.Kind);
            if (c != 0) return c;
            c = a.Shape.Weight.CompareTo(b.Shape.Weight);
            if (c != 0) return c;
            c = a.Shape.RectMin.x.CompareTo(b.Shape.RectMin.x);
            if (c != 0) return c;
            c = a.Shape.RectMin.y.CompareTo(b.Shape.RectMin.y);
            if (c != 0) return c;
            c = a.Shape.RectSize.x.CompareTo(b.Shape.RectSize.x);
            if (c != 0) return c;
            c = a.Shape.RectSize.y.CompareTo(b.Shape.RectSize.y);
            if (c != 0) return c;
            c = a.Shape.ShellThickness.CompareTo(b.Shape.ShellThickness);
            if (c != 0) return c;
            c = a.Shape.AnnulusInnerRadius.CompareTo(b.Shape.AnnulusInnerRadius);
            if (c != 0) return c;
            c = a.Shape.SectorDir1.x.CompareTo(b.Shape.SectorDir1.x);
            if (c != 0) return c;
            return a.Shape.SectorDir1.y.CompareTo(b.Shape.SectorDir1.y);
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

            ExtractedStamps.AsArray().Sort(new StampOrder());

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
            Helper.ProcessStamps(Stamps);
        }
    }
}
