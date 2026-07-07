using BovineLabs.Timeline.Grid.Influence.Data;
using NUnit.Framework;
using Unity.Collections;
using Unity.Mathematics;

namespace BovineLabs.Timeline.Grid.Influence.Tests
{
    public class CompactionTests
    {
        [Test]
        public void CompactionPreservesAllMappingsAndValues()
        {
            RunCompactionScenario(3, 0x1234u, 40);
        }

        [Test]
        public void CompactionFuzzPreservesMappingsAcrossChunkPowers()
        {
            foreach (var power in new[] { 1, 2, 3 })
                for (var seed = 1u; seed <= 5u; seed++)
                    RunCompactionScenario(power, (seed * 2654435761u) | 1u, 33);
        }

        private static void RunCompactionScenario(int chunkPower, uint seed, int stampCount)
        {
            var spec = GridSpec.FromPowerOfTwo(chunkPower, 2u);
            var field = InfluenceField.Create(spec, Allocator.Persistent);

            var coords = new int2[stampCount];
            var values = new int[stampCount];
            var survivor = new bool[stampCount];

            var rng = new Random(seed);
            for (var i = 0; i < stampCount; i++)
            {
                coords[i] = new int2(i * 100 + rng.NextInt(0, 8), rng.NextInt(-500, 500));
                values[i] = rng.NextInt(1, 1_000_000);
                survivor[i] = rng.NextBool();
            }

            survivor[0] = false;
            survivor[stampCount - 1] = true;

            var survivorCount = 0;
            for (var i = 0; i < stampCount; i++)
                if (survivor[i])
                    survivorCount++;

            var allStamps = new NativeArray<Stamp>(stampCount, Allocator.Persistent);
            var survivorStamps = new NativeArray<Stamp>(survivorCount, Allocator.Persistent);

            var w = 0;
            for (var i = 0; i < stampCount; i++)
            {
                var stamp = new Stamp(InfluenceShape.SolidRect(int2.zero, new int2(1, 1), values[i]), coords[i]);
                allStamps[i] = stamp;
                if (survivor[i])
                    survivorStamps[w++] = stamp;
            }

            try
            {
                field.OverrideFrameId(56);

                field.Schedule(allStamps, default).Complete();
                field.Schedule(survivorStamps, default).Complete();
                field.Schedule(survivorStamps, default).Complete();

                var preCompactionLength = field.CoordBySlotList.Length;
                Assert.AreEqual(stampCount, preCompactionLength,
                    $"pre-compaction slot count wrong (power {chunkPower}, seed {seed})");

                field.Schedule(survivorStamps, default).Complete();

                Assert.AreEqual(60u, field.FrameId,
                    $"compaction frame not reached (power {chunkPower}, seed {seed})");

                var reader = field.AsReader();
                for (var i = 0; i < stampCount; i++)
                {
                    if (!survivor[i])
                        continue;

                    Assert.AreEqual(values[i], reader.ReadCell(coords[i]),
                        $"survivor value lost after compaction (power {chunkPower}, seed {seed}, i {i}, coord {coords[i]})");
                }

                var postCompactionLength = field.CoordBySlotList.Length;
                Assert.Less(postCompactionLength, preCompactionLength,
                    $"compaction did not shrink CoordBySlot (power {chunkPower}, seed {seed})");

                var freeSlots = field.FreeSlotsList;
                for (var i = 0; i < freeSlots.Length; i++)
                    Assert.Less(freeSlots[i], postCompactionLength,
                        $"free slot {freeSlots[i]} points past compacted length {postCompactionLength} (power {chunkPower}, seed {seed})");
            }
            finally
            {
                allStamps.Dispose();
                survivorStamps.Dispose();
                field.Dispose();
            }
        }
    }
}
