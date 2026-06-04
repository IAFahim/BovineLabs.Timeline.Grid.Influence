using System;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Jobs;

namespace BovineLabs.Timeline.Grid.Influence.Data
{
    public readonly struct FieldId
    {
        public readonly int Value;
        public FieldId(int v) { Value = v; }
        public bool IsValid => Value >= 0;
        public static FieldId Invalid => new FieldId(-1);
    }

    public struct FieldConfig
    {
        public ushort Key;
        public FixedString64Bytes Name;
        public int ChunkPower;
        public uint RetentionFrames;
        public bool HasFeedback;
        public int DecayPerMille;
        public int SpreadDenominator;
        public int StrideAlignment;

        public bool NeedsDoubleBuffer => HasFeedback || DoubleBuffered;
        public bool DoubleBuffered;
    }

    public struct FieldRegistry : IDisposable
    {
        public NativeArray<InfluenceFieldPair> Pairs;
        public NativeHashMap<ushort, int> KeyToSlot;
        public int Count;

        public void Initialize(int capacity, Allocator allocator)
        {
            Pairs = new NativeArray<InfluenceFieldPair>(capacity, allocator);
            KeyToSlot = new NativeHashMap<ushort, int>(capacity, allocator);
            Count = 0;
        }

        public FieldId Register(in FieldConfig config, Allocator allocator)
        {
            if (!Pairs.IsCreated || Count >= Pairs.Length) return FieldId.Invalid;

            int i = Count++;
            ref var pair = ref this.Slot(i);

            int align = config.StrideAlignment == 0 ? 8 : config.StrideAlignment;
            var spec = GridSpec.FromPowerOfTwo(config.ChunkPower, config.RetentionFrames, align);

            pair.Config = config;
            pair.Front = InfluenceField.Create(spec, allocator);
            pair.DoubleBuffered = config.NeedsDoubleBuffer;
            if (pair.DoubleBuffered)
                pair.Back = InfluenceField.Create(spec, allocator);

            KeyToSlot.Add(config.Key, i);
            return new FieldId(i);
        }

        public InfluenceField Front(FieldId id) => this.Slot(id.Value).Front;

        public void Dispose()
        {
            for (int i = 0; i < Count; i++)
            {
                ref var p = ref this.Slot(i);
                p.WriterDependency.Complete();
                if (p.Front.IsCreated) p.Front.Dispose();
                if (p.DoubleBuffered && p.Back.IsCreated) p.Back.Dispose();
            }
            if (Pairs.IsCreated) Pairs.Dispose();
            if (KeyToSlot.IsCreated) KeyToSlot.Dispose();
            Count = 0;
        }
    }

    public struct InfluenceFieldPair
    {
        public FieldConfig Config;
        public InfluenceField Front;
        public InfluenceField Back;
        public bool DoubleBuffered;

        public InfluenceField.StencilConfig PendingStencil;
        public JobHandle WriterDependency;

        public void Swap()
        {
            if (!DoubleBuffered) return;
            (Front, Back) = (Back, Front);
        }
    }

    public static class FieldRegistryExtensions
    {
        public static ref InfluenceFieldPair Slot(this ref FieldRegistry r, int i)
        {
            unsafe
            {
                var ptr = (InfluenceFieldPair*)r.Pairs.GetUnsafePtr();
                return ref ptr[i];
            }
        }
    }
}
