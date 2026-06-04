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
