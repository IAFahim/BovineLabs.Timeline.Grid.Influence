#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using BovineLabs.Timeline.Grid.Influence.Data;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace BovineLabs.Timeline.Grid.Influence.Editor
{
    internal sealed class InfluenceFieldSnapshot
    {
        public int ActiveChunkCount;
        public int CapturedChunkCount;

        public ChunkSnapshot[] Chunks = Array.Empty<ChunkSnapshot>();

        public FieldSummary[] Fields = Array.Empty<FieldSummary>();
        public uint FrameId;
        public InfluenceGridSettings GridSettings;
        public bool IsDoubleBuffered;
        public int MaxValue;
        public int MinValue;
        public string SelectedFieldName = string.Empty;

        public int SelectedFieldSlot = -1;

        public GridSpec Spec;
        public long SumValue;
        public int TotalNonZeroCells;
        public string WorldName;

        public bool HasSelectedField => SelectedFieldSlot >= 0;

        public static bool TryCapture(
            World world,
            int selectedFieldSlot,
            int maxCapturedChunks,
            out InfluenceFieldSnapshot snapshot,
            out string error)
        {
            snapshot = null;
            error = string.Empty;

            if (world == null || !world.IsCreated)
            {
                error = "No valid DOTS world selected.";
                return false;
            }

            var entityManager = world.EntityManager;

            if (!TryGetSingletonEntity<FieldRegistrySingleton>(entityManager, out var registryEntity))
            {
                error = "No FieldRegistrySingleton found. Enter Play Mode or make sure FieldBootstrapSystem has run.";
                return false;
            }

            if (!TryGetSingletonEntity<InfluenceGridSettings>(entityManager, out var settingsEntity))
            {
                error = "No InfluenceGridSettings found.";
                return false;
            }

            var settings = entityManager.GetComponentData<InfluenceGridSettings>(settingsEntity);
            var singleton = entityManager.GetComponentData<FieldRegistrySingleton>(registryEntity);

            ref var registry = ref singleton.Registry;

            if (!registry.Pairs.IsCreated || registry.Count <= 0)
            {
                error = "Field registry exists, but no fields are registered.";
                return false;
            }

            for (var i = 0; i < registry.Count; i++)
            {
                ref var slotPair = ref registry.Slot(i);
                slotPair.WriterDependency.Complete();
                slotPair.Front.Complete();
            }

            var fields = CaptureFieldSummaries(ref registry);

            selectedFieldSlot = math.clamp(selectedFieldSlot, 0, registry.Count - 1);

            ref var pair = ref registry.Slot(selectedFieldSlot);

            pair.WriterDependency.Complete();
            pair.Front.Complete();

            var field = pair.Front;

            if (!field.IsCreated)
            {
                error = $"Selected field slot {selectedFieldSlot} is not created.";
                return false;
            }

            var chunks = CaptureChunks(
                ref field,
                maxCapturedChunks,
                out var totalNonZero,
                out var min,
                out var max,
                out var sum);

            snapshot = new InfluenceFieldSnapshot
            {
                WorldName = world.Name,
                GridSettings = settings,

                Fields = fields,

                SelectedFieldSlot = selectedFieldSlot,
                SelectedFieldName = fields[selectedFieldSlot].Name,

                Spec = field.Spec,
                FrameId = field.FrameId,
                IsDoubleBuffered = pair.DoubleBuffered,

                ActiveChunkCount = field.ActiveChunkCount,
                CapturedChunkCount = chunks.Length,
                TotalNonZeroCells = totalNonZero,
                MinValue = min,
                MaxValue = max,
                SumValue = sum,

                Chunks = chunks
            };

            return true;
        }

        private static FieldSummary[] CaptureFieldSummaries(ref FieldRegistry registry)
        {
            var summaries = new FieldSummary[registry.Count];

            for (var i = 0; i < registry.Count; i++)
            {
                ref var pair = ref registry.Slot(i);
                var field = pair.Front;
                var spec = field.IsCreated ? field.Spec : default;

                var allocatedChunks =
                    field.IsCreated && field.CoordBySlotList.IsCreated
                        ? field.CoordBySlotList.Length
                        : 0;

                var approxBytes =
                    (long)allocatedChunks *
                    math.max(0, spec.ElementsPerChunk) *
                    sizeof(int);

                summaries[i] = new FieldSummary(
                    i,
                    pair.Config.Key,
                    pair.Config.Name.ToString(),
                    spec,
                    pair.DoubleBuffered,
                    field.IsCreated ? field.FrameId : 0,
                    field.IsCreated ? field.ActiveSlotCount : 0,
                    allocatedChunks,
                    approxBytes);
            }

            return summaries;
        }

        private static ChunkSnapshot[] CaptureChunks(
            ref InfluenceField field,
            int maxCapturedChunks,
            out int totalNonZero,
            out int globalMin,
            out int globalMax,
            out long globalSum)
        {
            totalNonZero = 0;
            globalMin = 0;
            globalMax = 0;
            globalSum = 0;

            if (!field.IsCreated ||
                !field.ActiveSlotsList.IsCreated ||
                !field.CoordBySlotList.IsCreated ||
                !field.DataList.IsCreated)
                return Array.Empty<ChunkSnapshot>();

            var spec = field.Spec;
            var activeSlots = field.ActiveSlotsList.AsArray();
            var coordBySlot = field.CoordBySlotList.AsArray();
            var reader = field.AsReader();

            var count = math.min(activeSlots.Length, math.max(0, maxCapturedChunks));
            var chunks = new List<ChunkSnapshot>(count);

            var hasValue = false;

            for (var i = 0; i < count; i++)
            {
                var slot = activeSlots[i];

                if ((uint)slot >= (uint)coordBySlot.Length)
                    continue;

                var coord = coordBySlot[slot];
                var cellBase = ChunkMath.ChunkBaseOf(coord, spec.Log2);

                var cells = new int[spec.ChunkSize * spec.ChunkSize];

                var chunkMin = 0;
                var chunkMax = 0;
                var chunkSum = 0L;
                var nonZero = 0;
                var chunkHasValue = false;

                if (!reader.TryGetChunk(coord, out var view))
                    continue;

                for (var y = 0; y < spec.ChunkSize; y++)
                for (var x = 0; x < spec.ChunkSize; x++)
                {
                    var value = view.ReadLocal(new int2(x, y));
                    cells[y * spec.ChunkSize + x] = value;

                    if (!chunkHasValue)
                    {
                        chunkMin = value;
                        chunkMax = value;
                        chunkHasValue = true;
                    }
                    else
                    {
                        chunkMin = math.min(chunkMin, value);
                        chunkMax = math.max(chunkMax, value);
                    }

                    if (!hasValue)
                    {
                        globalMin = value;
                        globalMax = value;
                        hasValue = true;
                    }
                    else
                    {
                        globalMin = math.min(globalMin, value);
                        globalMax = math.max(globalMax, value);
                    }

                    if (value != 0)
                        nonZero++;

                    chunkSum += value;
                }

                totalNonZero += nonZero;
                globalSum += chunkSum;

                chunks.Add(new ChunkSnapshot(
                    slot,
                    coord,
                    cellBase,
                    cells,
                    chunkMin,
                    chunkMax,
                    nonZero,
                    chunkSum));
            }

            if (!hasValue)
            {
                globalMin = 0;
                globalMax = 0;
                globalSum = 0;
            }

            chunks.Sort(static (a, b) =>
            {
                var y = a.Coord.y.CompareTo(b.Coord.y);
                return y != 0 ? y : a.Coord.x.CompareTo(b.Coord.x);
            });

            return chunks.ToArray();
        }

        private static bool TryGetSingletonEntity<T>(EntityManager entityManager, out Entity entity)
            where T : unmanaged, IComponentData
        {
            using var query = entityManager.CreateEntityQuery(ComponentType.ReadOnly<T>());

            if (query.IsEmptyIgnoreFilter)
            {
                entity = Entity.Null;
                return false;
            }

            using var entities = query.ToEntityArray(Allocator.Temp);
            entity = entities.Length > 0 ? entities[0] : Entity.Null;
            return entity != Entity.Null;
        }

        public readonly struct FieldSummary
        {
            public readonly int Slot;
            public readonly ushort Key;
            public readonly string Name;
            public readonly GridSpec Spec;
            public readonly bool DoubleBuffered;
            public readonly uint FrameId;
            public readonly int ActiveChunks;
            public readonly int AllocatedChunks;
            public readonly long ApproxDataBytes;

            public FieldSummary(
                int slot,
                ushort key,
                string name,
                GridSpec spec,
                bool doubleBuffered,
                uint frameId,
                int activeChunks,
                int allocatedChunks,
                long approxDataBytes)
            {
                Slot = slot;
                Key = key;
                Name = name;
                Spec = spec;
                DoubleBuffered = doubleBuffered;
                FrameId = frameId;
                ActiveChunks = activeChunks;
                AllocatedChunks = allocatedChunks;
                ApproxDataBytes = approxDataBytes;
            }

            public string DisplayName =>
                string.IsNullOrWhiteSpace(Name)
                    ? $"#{Slot} / Key {Key}"
                    : $"{Name}  —  Key {Key}";
        }

        public readonly struct ChunkSnapshot
        {
            public readonly int Slot;
            public readonly int2 Coord;
            public readonly int2 CellBase;
            public readonly int[] Cells;

            public readonly int MinValue;
            public readonly int MaxValue;
            public readonly int NonZeroCells;
            public readonly long SumValue;

            public ChunkSnapshot(
                int slot,
                int2 coord,
                int2 cellBase,
                int[] cells,
                int minValue,
                int maxValue,
                int nonZeroCells,
                long sumValue)
            {
                Slot = slot;
                Coord = coord;
                CellBase = cellBase;
                Cells = cells;
                MinValue = minValue;
                MaxValue = maxValue;
                NonZeroCells = nonZeroCells;
                SumValue = sumValue;
            }
        }
    }
}
#endif