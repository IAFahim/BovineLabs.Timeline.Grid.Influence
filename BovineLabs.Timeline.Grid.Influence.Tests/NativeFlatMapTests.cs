using System.Collections.Generic;
using BovineLabs.Timeline.Grid.Influence.Data;
using NUnit.Framework;
using Unity.Collections;
using Unity.Mathematics;

namespace BovineLabs.Timeline.Grid.Influence.Tests
{
    public class NativeFlatMapTests
    {
        [Test]
        public void MatchesDictionaryUnderRandomInterleavedOps()
        {
            for (var seed = 1u; seed <= 8u; seed++)
                RunAgainstDictionary((seed * 2654435761u) | 1u, 600, 8);
        }

        [Test]
        public void GrowsAndStaysConsistentThroughBulkInsertAndRemoval()
        {
            var map = NativeFlatMap.Create(8, Allocator.Persistent);
            try
            {
                const int count = 1000;

                for (var i = 0; i < count; i++)
                    map.Add(new int2(i, -i), (i * 3) + 1);

                Assert.AreEqual(count, map.Count, "count after bulk insert");

                for (var i = 0; i < count; i++)
                {
                    Assert.IsTrue(map.TryGetValue(new int2(i, -i), out var v), $"missing key {i} after grow");
                    Assert.AreEqual((i * 3) + 1, v, $"wrong value for key {i}");
                }

                for (var i = 0; i < count; i += 2)
                    Assert.IsTrue(map.Remove(new int2(i, -i)), $"remove of live key {i} returned false");

                Assert.AreEqual(count / 2, map.Count, "count after removing half");

                for (var i = 0; i < count; i++)
                {
                    var found = map.TryGetValue(new int2(i, -i), out var v);
                    if ((i & 1) == 0)
                    {
                        Assert.IsFalse(found, $"removed key {i} still present");
                    }
                    else
                    {
                        Assert.IsTrue(found, $"survivor key {i} lost");
                        Assert.AreEqual((i * 3) + 1, v, $"survivor key {i} wrong value");
                    }
                }
            }
            finally
            {
                map.Dispose();
            }
        }

        [Test]
        public void BackwardShiftDeleteKeepsAllSurvivors()
        {
            for (var seed = 1u; seed <= 4u; seed++)
                RunBackwardShiftStress((seed * 747796405u) | 1u, 12);
        }

        private static void RunAgainstDictionary(uint seed, int ops, int range)
        {
            var map = NativeFlatMap.Create(8, Allocator.Persistent);
            var dict = new Dictionary<long, int>();
            var rng = new Random(seed);

            try
            {
                for (var n = 0; n < ops; n++)
                {
                    var key = new int2(rng.NextInt(-range, range + 1), rng.NextInt(-range, range + 1));
                    var packed = Pack(key);

                    switch (rng.NextInt(0, 3))
                    {
                        case 0:
                            var value = rng.NextInt();
                            map.Add(key, value);
                            dict[packed] = value;
                            break;

                        case 1:
                            var removedFromMap = map.Remove(key);
                            var removedFromDict = dict.Remove(packed);
                            Assert.AreEqual(removedFromDict, removedFromMap,
                                $"remove parity (seed {seed}, op {n}, key {key})");
                            break;

                        default:
                            var foundInMap = map.TryGetValue(key, out var mapValue);
                            var foundInDict = dict.TryGetValue(packed, out var dictValue);
                            Assert.AreEqual(foundInDict, foundInMap,
                                $"contains parity (seed {seed}, op {n}, key {key})");
                            if (foundInDict)
                                Assert.AreEqual(dictValue, mapValue,
                                    $"value parity (seed {seed}, op {n}, key {key})");
                            break;
                    }

                    Assert.AreEqual(dict.Count, map.Count, $"count parity (seed {seed}, op {n})");
                }

                for (var x = -range; x <= range; x++)
                for (var y = -range; y <= range; y++)
                {
                    var key = new int2(x, y);
                    var foundInMap = map.TryGetValue(key, out var mapValue);
                    var foundInDict = dict.TryGetValue(Pack(key), out var dictValue);
                    Assert.AreEqual(foundInDict, foundInMap, $"final contains parity (seed {seed}, key {key})");
                    if (foundInDict)
                        Assert.AreEqual(dictValue, mapValue, $"final value parity (seed {seed}, key {key})");
                }
            }
            finally
            {
                map.Dispose();
            }
        }

        private static void RunBackwardShiftStress(uint seed, int side)
        {
            var map = NativeFlatMap.Create(8, Allocator.Persistent);
            var remaining = new List<int2>(side * side);

            try
            {
                for (var x = 0; x < side; x++)
                for (var y = 0; y < side; y++)
                {
                    var key = new int2(x, y);
                    map.Add(key, (x * 1000) + y);
                    remaining.Add(key);
                }

                var rng = new Random(seed);

                while (remaining.Count > 0)
                {
                    var index = rng.NextInt(0, remaining.Count);
                    var key = remaining[index];
                    remaining[index] = remaining[remaining.Count - 1];
                    remaining.RemoveAt(remaining.Count - 1);

                    Assert.IsTrue(map.Remove(key), $"remove of live key {key} returned false (seed {seed})");
                    Assert.IsFalse(map.TryGetValue(key, out _), $"removed key {key} still present (seed {seed})");

                    foreach (var survivor in remaining)
                    {
                        Assert.IsTrue(map.TryGetValue(survivor, out var v),
                            $"survivor {survivor} lost after removing {key} (seed {seed})");
                        Assert.AreEqual((survivor.x * 1000) + survivor.y, v,
                            $"survivor {survivor} wrong value after removing {key} (seed {seed})");
                    }
                }

                Assert.AreEqual(0, map.Count, $"map not empty after removing everything (seed {seed})");
            }
            finally
            {
                map.Dispose();
            }
        }

        private static long Pack(int2 key)
        {
            return ((long)key.x << 32) | (uint)key.y;
        }
    }
}
