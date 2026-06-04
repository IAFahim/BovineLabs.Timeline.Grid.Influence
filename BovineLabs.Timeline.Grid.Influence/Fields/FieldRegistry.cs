using System;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Collections.LowLevel.Unsafe;
using BovineLabs.Timeline.Grid.Influence.Data;

namespace BovineLabs.Timeline.Grid.Influence.Fields
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
        public FixedString64Bytes Name;
        public int ChunkPower;
        public uint RetentionFrames;
        public bool HasFeedback;

        public bool NeedsDoubleBuffer => HasFeedback;
    }

    public struct FieldRegistry : IComponentData, IDisposable
    {
        public NativeArray<InfluenceFieldPair> Pairs;
        public int Count;


        public FieldId Register(in FieldConfig config, Allocator allocator)
        {
            if (!Pairs.IsCreated) return FieldId.Invalid;
            if (Count >= Pairs.Length) return FieldId.Invalid;
            int i = Count++;
            ref var pair = ref this.Slot(i);
            var spec = GridSpec.FromPowerOfTwo(config.ChunkPower, config.RetentionFrames);
            pair.Config = config;
            pair.Front = InfluenceField.Create(spec, allocator);
            pair.DoubleBuffered = config.NeedsDoubleBuffer;
            if (pair.DoubleBuffered)
                pair.Back = InfluenceField.Create(spec, allocator);
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
            Count = 0;
        }
    }

    public struct InfluenceFieldPair
    {
        public FieldConfig Config;
        public InfluenceField Front;
        public InfluenceField Back;
        public bool DoubleBuffered;

        public NativeList<Stamp> PendingStamps;
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
            // Pointer arithmetic to return ref from NativeArray
            unsafe
            {
                var ptr = (InfluenceFieldPair*)NativeArrayUnsafeUtility.GetUnsafePtr(r.Pairs);
                return ref ptr[i];
            }
        }
    }
}
