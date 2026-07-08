using BovineLabs.Timeline.Grid.Influence.Data;
using NUnit.Framework;
using Unity.Collections;
using Unity.Mathematics;

namespace BovineLabs.Timeline.Grid.Influence.Tests
{
    /// <summary>
    /// FieldRegistry churn probes. NOTE: the registry exposes no Unregister — slots are only
    /// reclaimed by disposing the whole registry — and FieldId carries no generation counter,
    /// so "stale handle" coverage here is limited to the range/validity guards that exist:
    /// invalid ids, out-of-range ids, ids held across a Dispose/Initialize cycle, duplicate-key
    /// rejection, and capacity exhaustion. See the report for the flagged design gap.
    /// </summary>
    public class AdversarialRegistryTests
    {
        private static FieldConfig Config(ushort key, string name)
        {
            return new FieldConfig
            {
                Key = key,
                Name = name,
                ChunkPower = 2,
                RetentionFrames = uint.MaxValue,
            };
        }

        private static void WriteOne(ref FieldRegistry reg, FieldId id, int weight, int2 cell)
        {
            ref var pair = ref reg.Slot(id.Value);
            pair.Tick++;
            var stamps = new NativeArray<Stamp>(
                new[] { new Stamp(InfluenceShape.SolidRect(int2.zero, new int2(1, 1), weight), cell) },
                Allocator.TempJob);
            pair.Front.Schedule(stamps, pair.Tick, default).Complete();
            stamps.Dispose();
        }

        [Test]
        public void DuplicateKeyRegistrationIsRejectedWithoutDisturbingTheOriginalField()
        {
            var reg = new FieldRegistry();
            reg.Initialize(4, Allocator.Persistent);

            try
            {
                var first = reg.Register(Config(7, "original"), Allocator.Persistent);
                Assert.IsTrue(first.IsValid, "test setup: first registration failed");
                WriteOne(ref reg, first, 33, new int2(2, 2));

                var dup = reg.Register(Config(7, "impostor"), Allocator.Persistent);
                Assert.IsFalse(dup.IsValid, "registering an already-used key must return FieldId.Invalid");
                Assert.AreEqual(1, reg.Count, "a rejected duplicate registration must not consume a slot");
                Assert.AreEqual(33, reg.Front(first).AsReader().ReadCell(new int2(2, 2)),
                    "a rejected duplicate registration must leave the original field's data untouched");
            }
            finally
            {
                reg.Dispose();
            }
        }

        [Test]
        public void CapacityExhaustionReturnsInvalidAndDoesNotCorruptExistingSlots()
        {
            var reg = new FieldRegistry();
            reg.Initialize(2, Allocator.Persistent);

            try
            {
                var a = reg.Register(Config(1, "a"), Allocator.Persistent);
                var b = reg.Register(Config(2, "b"), Allocator.Persistent);
                Assert.IsTrue(a.IsValid && b.IsValid, "test setup: registry should hold exactly two fields");
                WriteOne(ref reg, a, 11, int2.zero);
                WriteOne(ref reg, b, 22, int2.zero);

                var c = reg.Register(Config(3, "c"), Allocator.Persistent);
                Assert.IsFalse(c.IsValid, "registration past capacity must return FieldId.Invalid, not grow or throw");
                Assert.AreEqual(2, reg.Count, "a rejected over-capacity registration must not bump Count");
                Assert.IsFalse(reg.KeyToSlot.ContainsKey(3),
                    "a rejected over-capacity registration must not leave a dangling key mapping");
                Assert.AreEqual(11, reg.Front(a).AsReader().ReadCell(int2.zero), "slot a corrupted by over-capacity attempt");
                Assert.AreEqual(22, reg.Front(b).AsReader().ReadCell(int2.zero), "slot b corrupted by over-capacity attempt");
            }
            finally
            {
                reg.Dispose();
            }
        }

        [Test]
        public void FrontGuardsInvalidAndOutOfRangeIdsByReturningAnUncreatedField()
        {
            var reg = new FieldRegistry();
            reg.Initialize(2, Allocator.Persistent);

            try
            {
                var a = reg.Register(Config(1, "a"), Allocator.Persistent);
                Assert.IsTrue(a.IsValid, "test setup: registration failed");

                Assert.IsFalse(reg.Front(FieldId.Invalid).IsCreated,
                    "Front(FieldId.Invalid) must return an uncreated field, never slot 0");
                Assert.IsFalse(reg.Front(new FieldId(1)).IsCreated,
                    "Front(id == Count) must return an uncreated field");
                Assert.IsFalse(reg.Front(new FieldId(int.MaxValue)).IsCreated,
                    "Front(huge id) must return an uncreated field");
                Assert.IsFalse(reg.Front(new FieldId(-2)).IsCreated,
                    "Front(negative id) must return an uncreated field");
            }
            finally
            {
                reg.Dispose();
            }
        }

        [Test]
        public void KeyReuseAcrossRegistryLifetimesYieldsAFreshEmptyField()
        {
            // Closest expressible analogue of unregister/re-register churn: dispose the registry,
            // re-initialize, and re-register the same key. The new field must be empty — no data
            // may bleed across lifetimes — and the key must map to the new slot.
            var reg = new FieldRegistry();
            reg.Initialize(2, Allocator.Persistent);

            var staleId = FieldId.Invalid;
            try
            {
                staleId = reg.Register(Config(9, "gen0"), Allocator.Persistent);
                Assert.IsTrue(staleId.IsValid, "test setup: registration failed");
                WriteOne(ref reg, staleId, 77, new int2(1, 1));
                Assert.AreEqual(77, reg.Front(staleId).AsReader().ReadCell(new int2(1, 1)),
                    "test setup: write not visible");
            }
            finally
            {
                reg.Dispose();
            }

            reg = new FieldRegistry();
            reg.Initialize(2, Allocator.Persistent);
            try
            {
                // Before any registration, the id held across the lifetime boundary must be rejected
                // by the Count guard rather than dereferencing a dead slot.
                Assert.IsFalse(reg.Front(staleId).IsCreated,
                    "an id held across Dispose/Initialize must not resolve while its slot is unregistered");

                var fresh = reg.Register(Config(9, "gen1"), Allocator.Persistent);
                Assert.IsTrue(fresh.IsValid, "re-registering a key after registry disposal must succeed");
                Assert.AreEqual(0, reg.Front(fresh).AsReader().ReadCell(new int2(1, 1)),
                    "a re-registered key must start with an empty field; data bled across registry lifetimes");
            }
            finally
            {
                reg.Dispose();
            }
        }

        [Test]
        public void DecayForcesABackBufferAndSwapIsANoOpForSingleBufferedFields()
        {
            var reg = new FieldRegistry();
            reg.Initialize(2, Allocator.Persistent);

            try
            {
                var decayCfg = Config(1, "decays");
                decayCfg.DecayPerMille = 100; // DoubleBuffered stays false: NeedsDoubleBuffer must kick in
                var decayId = reg.Register(decayCfg, Allocator.Persistent);
                Assert.IsTrue(decayId.IsValid, "test setup: registration failed");
                ref var decayPair = ref reg.Slot(decayId.Value);
                Assert.IsTrue(decayPair.DoubleBuffered && decayPair.Back.IsCreated,
                    "DecayPerMille > 0 must force a back buffer even when DoubleBuffered is not set (decay reads prev frame)");

                var plainId = reg.Register(Config(2, "plain"), Allocator.Persistent);
                Assert.IsTrue(plainId.IsValid, "test setup: registration failed");
                WriteOne(ref reg, plainId, 55, int2.zero);
                ref var plainPair = ref reg.Slot(plainId.Value);
                Assert.IsFalse(plainPair.Back.IsCreated, "single-buffered field must not allocate a back buffer");

                plainPair.Swap();
                Assert.AreEqual(55, reg.Front(plainId).AsReader().ReadCell(int2.zero),
                    "Swap on a single-buffered pair must be a no-op; it swapped in the uncreated back buffer");
            }
            finally
            {
                reg.Dispose();
            }
        }
    }
}
