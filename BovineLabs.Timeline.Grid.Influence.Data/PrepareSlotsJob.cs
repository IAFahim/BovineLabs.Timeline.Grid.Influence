using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace BovineLabs.Timeline.Grid.Influence.Data
{
    [BurstCompile]
    public struct PrepareSlotsAndOffsetsJob : IJob
    {
        [ReadOnly] public NativeArray<Stamp> Stamps;
        public NativeList<int> Offsets;
        public NativeList<WeightedRect> Spans;
        public NativeList<int> StampCount;
        
        public NativeFlatMap SlotByCoord;
        public NativeList<int> FreeSlots;
        public NativeList<int2> CoordBySlot;
        public NativeList<uint> LastWrittenBySlot;
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
        public int DecayPerMille;
        public int SpreadDenominator;

        public void Execute()
        {
            if (ResetFrame)
            {
                for (int i = 0; i < LastWrittenBySlot.Length; i++)
                {
                    LastWrittenBySlot[i] = 0;
                }
            }

            if (RetentionFrames != uint.MaxValue)
            {
                uint minValidFrame = FrameId > RetentionFrames ? FrameId - RetentionFrames : 0;
                for (int i = 0; i < LastWrittenBySlot.Length; i++)
                {
                    if (LastWrittenBySlot[i] != 0 && LastWrittenBySlot[i] < minValidFrame)
                    {
                        LastWrittenBySlot[i] = 0;
                        FreeSlots.Add(i);
                        SlotByCoord.Remove(CoordBySlot[i]);
                    }
                }
            }

            ActiveSlots.Clear();

            if (HasStencil)
            {
                ActivateStencilFrontier();
            }

            if (!Stamps.IsCreated)
            {
                StampCount.Length = 0;
                return;
            }
            StampCount.Length = Stamps.Length;
            long running = 0;
            Offsets.Length = Stamps.Length + 1;
            for (int i = 0; i < Stamps.Length; i++)
            {
                Offsets[i] = IntegerMath.ClampToInt(running);
                InfluenceShape shape = Stamps[i].Shape;
                running += Rasterizer.EstimateSpanCount(shape);
                ActivateBounds(Rasterizer.Bounds(shape, Stamps[i].Origin));
            }

            Offsets[Stamps.Length] = IntegerMath.ClampToInt(running);
            Spans.Length = Offsets[Stamps.Length];
        }

        void ActivateStencilFrontier()
        {
            int chunkSize = Spec.ChunkSize;
            int stride = Spec.Stride;
            int elements = Spec.ElementsPerChunk;

            for (int i = 0; i < StencilActiveSlots.Length; i++)
            {
                int slot = StencilActiveSlots[i];
                int2 coord = StencilCoordBySlot[slot];
                Activate(coord);

                int baseIndex = slot * elements;
                
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

        bool NeedsActivationEdge(int baseIndex, int startX, int startY, int dx, int dy, int count, int stride)
        {
            for (int i = 0; i < count; i++)
            {
                int x = startX + i * dx;
                int y = startY + i * dy;
                int v = StencilData[baseIndex + y * stride + x];
                if (v != 0)
                {
                    int vp = v - (int)((long)v * DecayPerMille / 1000);
                    if (vp / SpreadDenominator != 0) return true;
                }
            }
            return false;
        }

        void ActivateBounds(in CellRect bounds)
        {
            if (bounds.IsEmpty) return;
            int cx0 = bounds.Min.x >> Spec.Log2;
            int cy0 = bounds.Min.y >> Spec.Log2;
            int cx1 = (bounds.Max.x - 1) >> Spec.Log2;
            int cy1 = (bounds.Max.y - 1) >> Spec.Log2;

            for (int cy = cy0; cy <= cy1; cy++)
            {
                for (int cx = cx0; cx <= cx1; cx++)
                {
                    Activate(new int2(cx, cy));
                }
            }
        }

        void Activate(int2 coord)
        {
            int slot = EnsureSlot(coord);
            if (LastWrittenBySlot[slot] == FrameId) return;

            LastWrittenBySlot[slot] = FrameId;
            ActiveSlots.Add(slot);
        }

        int EnsureSlot(int2 coord)
        {
            if (SlotByCoord.TryGetValue(coord, out int existing)) return existing;

            int slot;
            if (FreeSlots.Length > 0)
            {
                slot = FreeSlots[FreeSlots.Length - 1];
                FreeSlots.RemoveAtSwapBack(FreeSlots.Length - 1);
                CoordBySlot[slot] = coord;
                LastWrittenBySlot[slot] = 0;
            }
            else
            {
                slot = CoordBySlot.Length;
                CoordBySlot.Add(coord);
                LastWrittenBySlot.Add(0);
                Data.ResizeUninitialized(Data.Length + Spec.ElementsPerChunk);
            }

            SlotByCoord.Add(coord, slot);
            return slot;
        }
    }
}
