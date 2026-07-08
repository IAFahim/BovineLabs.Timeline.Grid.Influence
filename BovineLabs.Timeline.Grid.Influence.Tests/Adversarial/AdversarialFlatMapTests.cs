using System.Collections.Generic;
using BovineLabs.Timeline.Grid.Influence.Data;
using NUnit.Framework;
using Unity.Collections;
using Unity.Mathematics;

namespace BovineLabs.Timeline.Grid.Influence.Tests
{
    /// <summary>
    /// Adversarial probes of NativeFlatMap: collision chains straddling growth boundaries, the
    /// default (zero) key, backward-shift deletion on hostile chains, and drain/refill reclamation.
    /// </summary>
    public class AdversarialFlatMapTests
    {
        [Test]
        public void CollidingKeysSurviveGrowthBoundariesAndChainRemoval()
        {
            // 14 keys that share a bucket modulo 32 collide maximally at capacity 8, 16 and 32,
            // so the chain crosses both growth boundaries (6th add grows 8->16, 12th grows 16->32).
            var keys = CollidingKeys(14, 31);
            var map = NativeFlatMap.Create(8, Allocator.Persistent);

            try
            {
                for (var i = 0; i < keys.Count; i++)
                    map.Add(keys[i], keys[i].x);

                Assert.AreEqual(keys.Count, map.Count, "count wrong after inserting one maximal collision chain");

                for (var i = 0; i < keys.Count; i++)
                {
                    Assert.IsTrue(map.TryGetValue(keys[i], out var v),
                        $"colliding key #{i} {keys[i]} lost across growth boundary");
                    Assert.AreEqual(keys[i].x, v, $"colliding key #{i} {keys[i]} has wrong value after growth");
                }

                // Remove the middle of the chain; backward-shift must keep both chain halves reachable.
                for (var i = 3; i <= 7; i++)
                    Assert.IsTrue(map.Remove(keys[i]), $"remove of live chain-middle key #{i} returned false");

                for (var i = 0; i < keys.Count; i++)
                {
                    var found = map.TryGetValue(keys[i], out var v);
                    if (i >= 3 && i <= 7)
                    {
                        Assert.IsFalse(found, $"removed chain key #{i} {keys[i]} still present");
                    }
                    else
                    {
                        Assert.IsTrue(found, $"surviving chain key #{i} {keys[i]} unreachable after middle removal");
                        Assert.AreEqual(keys[i].x, v, $"surviving chain key #{i} corrupted after middle removal");
                    }
                }

                Assert.AreEqual(keys.Count - 5, map.Count, "count wrong after removing chain middle");
            }
            finally
            {
                map.Dispose();
            }
        }

        [Test]
        public void DefaultZeroKeyRoundTripsThroughGrowthRemovalAndReAdd()
        {
            var map = NativeFlatMap.Create(8, Allocator.Persistent);

            try
            {
                map.Add(int2.zero, 42);
                Assert.IsTrue(map.TryGetValue(int2.zero, out var v0), "default(int2) key not found after Add");
                Assert.AreEqual(42, v0, "default(int2) key returned wrong value");

                // Force at least one growth with the zero key resident.
                for (var i = 1; i <= 10; i++)
                    map.Add(new int2(1000 + i, i), i);

                Assert.IsTrue(map.TryGetValue(int2.zero, out var v1), "default(int2) key lost during Grow");
                Assert.AreEqual(42, v1, "default(int2) key value corrupted during Grow");

                Assert.IsTrue(map.Remove(int2.zero), "Remove of live default(int2) key returned false");
                Assert.IsFalse(map.TryGetValue(int2.zero, out _), "default(int2) key still present after Remove");
                Assert.AreEqual(10, map.Count, "count wrong after removing default(int2) key");

                map.Add(int2.zero, 7);
                Assert.IsTrue(map.TryGetValue(int2.zero, out var v2), "default(int2) key not found after re-Add");
                Assert.AreEqual(7, v2, "default(int2) key wrong value after re-Add");
                Assert.AreEqual(11, map.Count, "count wrong after re-adding default(int2) key");
            }
            finally
            {
                map.Dispose();
            }
        }

        [Test]
        public void UpdatingExistingKeyAtGrowthThresholdKeepsCountAndValues()
        {
            // At capacity 8 the 6th Add triggers Grow. Add's grow check runs BEFORE the existing-key
            // probe, so updating a key while count == 5 takes the grow path; the update must still be
            // an update (count stable, single entry) and not a duplicate insert.
            var map = NativeFlatMap.Create(8, Allocator.Persistent);

            try
            {
                for (var i = 1; i <= 5; i++)
                    map.Add(new int2(i, i), i * 7);

                Assert.AreEqual(5, map.Count, "precondition: five distinct keys");

                map.Add(new int2(3, 3), 999);

                Assert.AreEqual(5, map.Count,
                    "updating an existing key at the growth threshold changed Count (duplicate insert?)");
                Assert.IsTrue(map.TryGetValue(new int2(3, 3), out var updated), "updated key lost");
                Assert.AreEqual(999, updated, "updated key holds stale value");

                for (var i = 1; i <= 5; i++)
                {
                    if (i == 3) continue;
                    Assert.IsTrue(map.TryGetValue(new int2(i, i), out var v), $"unrelated key {i} lost by update-grow");
                    Assert.AreEqual(i * 7, v, $"unrelated key {i} corrupted by update-grow");
                }
            }
            finally
            {
                map.Dispose();
            }
        }

        [Test]
        public void FillDrainRefillPastOriginalCapacityReclaimsAllSlots()
        {
            var map = NativeFlatMap.Create(8, Allocator.Persistent);

            try
            {
                for (var i = 0; i < 64; i++)
                    map.Add(new int2(i, i * 31), i + 1);

                Assert.AreEqual(64, map.Count, "count wrong after fill");

                for (var i = 0; i < 64; i++)
                    Assert.IsTrue(map.Remove(new int2(i, i * 31)), $"drain: remove of live key {i} returned false");

                Assert.AreEqual(0, map.Count, "map not empty after full drain");

                // Refill past the original fill size with entirely new keys; no residue may block probes.
                for (var i = 0; i < 128; i++)
                    map.Add(new int2(i + 1000, -i), i);

                Assert.AreEqual(128, map.Count, "count wrong after refill past original capacity");

                for (var i = 0; i < 128; i++)
                {
                    Assert.IsTrue(map.TryGetValue(new int2(i + 1000, -i), out var v), $"refill key {i} unreachable");
                    Assert.AreEqual(i, v, $"refill key {i} wrong value");
                }

                for (var i = 0; i < 64; i++)
                    Assert.IsFalse(map.TryGetValue(new int2(i, i * 31), out _),
                        $"drained key {i} resurrected after refill");
            }
            finally
            {
                map.Dispose();
            }
        }

        [Test]
        public void RemoveOfMissingKeyOnLiveCollisionChainIsFalseAndHarmless()
        {
            var keys = CollidingKeys(3, 31);
            var map = NativeFlatMap.Create(8, Allocator.Persistent);

            try
            {
                map.Add(keys[0], 1);
                map.Add(keys[2], 3);

                Assert.IsFalse(map.Remove(keys[1]),
                    "Remove of a missing key that hashes into a live chain must return false");
                Assert.AreEqual(2, map.Count, "failed Remove changed Count");

                Assert.IsTrue(map.TryGetValue(keys[0], out var a), "chain head lost by failed Remove");
                Assert.AreEqual(1, a, "chain head corrupted by failed Remove");
                Assert.IsTrue(map.TryGetValue(keys[2], out var c), "chain tail lost by failed Remove");
                Assert.AreEqual(3, c, "chain tail corrupted by failed Remove");
            }
            finally
            {
                map.Dispose();
            }
        }

        [Test]
        public void AddRemoveReAddSameKeyRepeatedlyLeavesNeighborsIntact()
        {
            var keys = CollidingKeys(4, 31);
            var map = NativeFlatMap.Create(8, Allocator.Persistent);

            try
            {
                map.Add(keys[1], 11);
                map.Add(keys[2], 22);
                map.Add(keys[3], 33);

                for (var i = 0; i < 100; i++)
                {
                    map.Add(keys[0], i);
                    Assert.IsTrue(map.TryGetValue(keys[0], out var v), $"churn iteration {i}: key missing after Add");
                    Assert.AreEqual(i, v, $"churn iteration {i}: wrong value after Add");
                    Assert.IsTrue(map.Remove(keys[0]), $"churn iteration {i}: Remove returned false");
                    Assert.IsFalse(map.TryGetValue(keys[0], out _), $"churn iteration {i}: key present after Remove");
                }

                Assert.AreEqual(3, map.Count, "count drifted under add/remove/re-add churn of one key");
                Assert.IsTrue(map.TryGetValue(keys[1], out var n1) && n1 == 11, "colliding neighbor 1 corrupted by churn");
                Assert.IsTrue(map.TryGetValue(keys[2], out var n2) && n2 == 22, "colliding neighbor 2 corrupted by churn");
                Assert.IsTrue(map.TryGetValue(keys[3], out var n3) && n3 == 33, "colliding neighbor 3 corrupted by churn");
            }
            finally
            {
                map.Dispose();
            }
        }

        /// <summary>
        /// Replica of NativeFlatMap's private hash (NativeFlatMap.cs Hash). Used only to construct
        /// adversarial collision chains; if the production hash changes, these keys simply stop
        /// colliding and the tests degrade to ordinary coverage without becoming incorrect.
        /// </summary>
        private static int Hash(int2 c)
        {
            unchecked
            {
                var h = (uint)(c.x * 73856093) ^ (uint)(c.y * 19349663);
                h ^= h >> 15;
                h *= 0x2c1b3c6du;
                h ^= h >> 12;
                return (int)h;
            }
        }

        private static List<int2> CollidingKeys(int count, int mask)
        {
            var keys = new List<int2>(count);
            var target = -1;
            for (var x = 1; keys.Count < count && x < 5_000_000; x++)
            {
                var key = new int2(x, -x);
                var bucket = Hash(key) & mask;
                if (target < 0) target = bucket;
                if (bucket == target) keys.Add(key);
            }

            Assert.AreEqual(count, keys.Count, "test setup: not enough colliding keys found in scan range");
            return keys;
        }
    }
}
