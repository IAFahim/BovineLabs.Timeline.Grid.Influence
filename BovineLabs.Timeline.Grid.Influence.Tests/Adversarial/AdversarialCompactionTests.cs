using BovineLabs.Timeline.Grid.Influence.Data;
using NUnit.Framework;
using Unity.Collections;
using Unity.Mathematics;

namespace BovineLabs.Timeline.Grid.Influence.Tests
{
    /// <summary>
    /// Compaction probes beyond CompactionTests: full-chunk bit-exact data comparison across the
    /// relocation, and post-compaction slot reuse without chunk aliasing.
    /// </summary>
    public class AdversarialCompactionTests
    {
        [Test]
        public void CompactionRelocatesSurvivorChunksBitExactly()
        {
            var spec = GridSpec.FromPowerOfTwo(2, 2u);
            var field = InfluenceField.Create(spec, Allocator.Persistent);

            const int stampCount = 30;
            var coords = new int2[stampCount];
            var survivor = new bool[stampCount];
            var survivorCount = 0;

            for (var i = 0; i < stampCount; i++)
            {
                // x spacing 97 > chunkSize guarantees one distinct chunk column per stamp.
                coords[i] = new int2(i * 97, (i * 53 % 997) - 400);
                survivor[i] = i % 2 == 0;
                if (survivor[i]) survivorCount++;
            }

            var allStamps = new NativeArray<Stamp>(stampCount, Allocator.Persistent);
            var survivorStamps = new NativeArray<Stamp>(survivorCount, Allocator.Persistent);
            var w = 0;
            for (var i = 0; i < stampCount; i++)
            {
                var stamp = new Stamp(InfluenceShape.SolidRect(int2.zero, new int2(1, 1), i * 1000 + 17), coords[i]);
                allStamps[i] = stamp;
                if (survivor[i]) survivorStamps[w++] = stamp;
            }

            try
            {
                field.OverrideFrameId(56);
                field.Schedule(allStamps, default).Complete(); // 57: everything written
                field.Schedule(survivorStamps, default).Complete(); // 58: survivors only
                field.Schedule(survivorStamps, default).Complete(); // 59: survivors only

                var chunkSize = spec.ChunkSize;
                var preLength = field.CoordBySlotList.Length;
                Assert.AreEqual(stampCount, preLength, "test setup: every stamp must occupy its own chunk");

                // Snapshot every survivor chunk cell-by-cell before the compaction frame.
                var snapshot = new int[stampCount, chunkSize, chunkSize];
                var preReader = field.AsReader();
                for (var i = 0; i < stampCount; i++)
                {
                    if (!survivor[i]) continue;

                    var chunkCoord = ChunkMath.ChunkCoordOf(coords[i], spec.Log2);
                    Assert.IsTrue(preReader.TryGetChunk(chunkCoord, out var view),
                        $"pre-compaction: survivor chunk {chunkCoord} (stamp {i}) missing");
                    for (var y = 0; y < chunkSize; y++)
                    for (var x = 0; x < chunkSize; x++)
                        snapshot[i, x, y] = view.ReadLocal(new int2(x, y));
                }

                // 60: non-survivors (last written 57) fall past retention 2, freeing slots, and
                // FrameId % 60 == 0 triggers compaction within the same prepare pass.
                field.Schedule(survivorStamps, default).Complete();
                Assert.AreEqual(60u, field.FrameId, "test setup: compaction frame not reached");

                var postLength = field.CoordBySlotList.Length;
                Assert.Less(postLength, preLength, "compaction did not shrink the slot arrays");

                var postReader = field.AsReader();
                for (var i = 0; i < stampCount; i++)
                {
                    if (!survivor[i]) continue;

                    var chunkCoord = ChunkMath.ChunkCoordOf(coords[i], spec.Log2);
                    Assert.IsTrue(postReader.TryGetChunk(chunkCoord, out var view),
                        $"post-compaction: survivor chunk {chunkCoord} (stamp {i}) lost its mapping");
                    for (var y = 0; y < chunkSize; y++)
                    for (var x = 0; x < chunkSize; x++)
                        Assert.AreEqual(snapshot[i, x, y], view.ReadLocal(new int2(x, y)),
                            $"survivor chunk {chunkCoord} (stamp {i}) local ({x},{y}) not bit-identical across compaction");
                }

                var freeSlots = field.FreeSlotsList;
                for (var i = 0; i < freeSlots.Length; i++)
                    Assert.Less(freeSlots[i], postLength,
                        $"free slot {freeSlots[i]} points past the compacted length {postLength} — stale handle survived compaction");
            }
            finally
            {
                allStamps.Dispose();
                survivorStamps.Dispose();
                if (field.IsCreated) field.Dispose();
            }
        }

        [Test]
        public void SlotReuseAfterCompactionDoesNotAliasNewChunksOntoSurvivors()
        {
            var spec = GridSpec.FromPowerOfTwo(2, 2u);
            var field = InfluenceField.Create(spec, Allocator.Persistent);

            const int stampCount = 12;
            const int newCount = 3;
            var coords = new int2[stampCount];
            var survivor = new bool[stampCount];
            var survivorCount = 0;

            for (var i = 0; i < stampCount; i++)
            {
                coords[i] = new int2(i * 97, (i * 53 % 997) - 400);
                survivor[i] = i % 2 == 0;
                if (survivor[i]) survivorCount++;
            }

            var allStamps = new NativeArray<Stamp>(stampCount, Allocator.Persistent);
            var survivorStamps = new NativeArray<Stamp>(survivorCount, Allocator.Persistent);
            var churnStamps = new NativeArray<Stamp>(survivorCount + newCount, Allocator.Persistent);
            var w = 0;
            for (var i = 0; i < stampCount; i++)
            {
                var stamp = new Stamp(InfluenceShape.SolidRect(int2.zero, new int2(1, 1), i * 1000 + 17), coords[i]);
                allStamps[i] = stamp;
                if (survivor[i])
                {
                    survivorStamps[w] = stamp;
                    churnStamps[w] = stamp;
                    w++;
                }
            }

            var newCoords = new int2[newCount];
            for (var i = 0; i < newCount; i++)
            {
                newCoords[i] = new int2(5000 + i * 97, 123);
                churnStamps[survivorCount + i] =
                    new Stamp(InfluenceShape.SolidRect(int2.zero, new int2(1, 1), 900_000 + i), newCoords[i]);
            }

            try
            {
                field.OverrideFrameId(56);
                field.Schedule(allStamps, default).Complete(); // 57
                field.Schedule(survivorStamps, default).Complete(); // 58
                field.Schedule(survivorStamps, default).Complete(); // 59
                field.Schedule(survivorStamps, default).Complete(); // 60: evict + compact
                Assert.AreEqual(60u, field.FrameId, "test setup: compaction frame not reached");
                Assert.AreEqual(survivorCount, field.CoordBySlotList.Length,
                    "test setup: compaction should pack the live chunks tightly");

                // 61: re-stamp survivors and add brand-new chunks that must take recycled/appended
                // slots without landing on top of any survivor.
                field.Schedule(churnStamps, default).Complete();

                var reader = field.AsReader();
                for (var i = 0; i < stampCount; i++)
                {
                    if (!survivor[i]) continue;

                    Assert.AreEqual(i * 1000 + 17, reader.ReadCell(coords[i]),
                        $"survivor {coords[i]} (stamp {i}) corrupted by post-compaction slot reuse");
                }

                for (var i = 0; i < newCount; i++)
                    Assert.AreEqual(900_000 + i, reader.ReadCell(newCoords[i]),
                        $"new chunk {newCoords[i]} got a wrong value from a reused slot after compaction");

                Assert.AreEqual(survivorCount + newCount, field.CoordBySlotList.Length,
                    "post-compaction growth is wrong: new chunks must extend the packed slot range exactly");
            }
            finally
            {
                allStamps.Dispose();
                survivorStamps.Dispose();
                churnStamps.Dispose();
                if (field.IsCreated) field.Dispose();
            }
        }
    }
}
