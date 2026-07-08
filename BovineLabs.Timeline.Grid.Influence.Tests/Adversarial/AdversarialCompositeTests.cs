using BovineLabs.Timeline.Grid.Influence.Data;
using NUnit.Framework;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Mathematics;

namespace BovineLabs.Timeline.Grid.Influence.Tests
{
    /// <summary>
    /// Adversarial probes of the runtime composite pipeline (CompositeBaker / CompositeShapeReader):
    /// telescoping layer weights against an exact oracle, degenerate/zero/negative extents, and
    /// overflow/saturation on maximum-size shapes. NOTE: the Authoring-side CompositeBaking.TryBuild
    /// (Painted rejection, priority ordering) lives in the Authoring assembly which this test
    /// assembly does not reference; those paths are covered here only down to the Data-level
    /// contracts they delegate to.
    /// </summary>
    public unsafe class AdversarialCompositeTests
    {
        [Test]
        public void TelescopingLayerWeightsMatchBorderDistanceOracleOnRect()
        {
            // For a SolidRect, the inset-at-depth-k shape is exactly the set of cells whose L-inf
            // border distance is >= k, so the emitted composite must satisfy:
            //   value(cell) = targetPerDepth[min(borderDistance, depthLimit - 1)]
            // including a NEGATIVE delta between depth 1 and depth 2 (target 3 -> 2).
            var baseShape = InfluenceShape.SolidRect(int2.zero, new int2(8, 6), 1);
            var target = new NativeArray<int>(new[] { 1, 3, 2 }, Allocator.Temp);
            var blobRef = CompositeBaker.Build(baseShape, target, Allocator.Persistent);

            try
            {
                ref var blob = ref blobRef.Value;
                Assert.AreEqual(CompositeBaker.LayerCount(baseShape, target), blob.Layers.Length,
                    "LayerCount and Build disagree on the number of layers");
                Assert.AreEqual(3, blob.Layers.Length, "deltas 1, +2, -1 must produce exactly three layers");

                var capacity = CompositeShapeReader.EstimateSpanCount(ref blob);
                var spans = new NativeArray<WeightedRect>(math.max(1, capacity), Allocator.Temp);
                var sink = new SpanSink((WeightedRect*)spans.GetUnsafePtr(), capacity);
                CompositeShapeReader.Emit(ref blob, int2.zero, ref sink);

                for (var y = -1; y <= 6; y++)
                for (var x = -1; x <= 8; x++)
                {
                    var inside = x >= 0 && x < 8 && y >= 0 && y < 6;
                    var expected = 0;
                    if (inside)
                    {
                        var borderDistance = math.min(math.min(x, 7 - x), math.min(y, 5 - y));
                        expected = target[math.min(borderDistance, 2)];
                    }

                    var actual = 0;
                    for (var s = 0; s < sink.Count; s++)
                    {
                        var b = spans[s].Bounds;
                        if (x >= b.Min.x && x < b.Max.x && y >= b.Min.y && y < b.Max.y)
                            actual += spans[s].Weight;
                    }

                    Assert.AreEqual(expected, actual,
                        $"composite value wrong at ({x},{y}): telescoping deltas must reproduce targetPerDepth at every border distance");
                }

                spans.Dispose();
            }
            finally
            {
                blobRef.Dispose();
                target.Dispose();
            }
        }

        [Test]
        public void DegenerateInputsYieldEmptyCompositesWithoutThrowing()
        {
            var target3 = new NativeArray<int>(new[] { 1, 2, 3 }, Allocator.Temp);
            var target1 = new NativeArray<int>(new[] { 7 }, Allocator.Temp);
            var target0 = new NativeArray<int>(0, Allocator.Temp);
            var targetFlat = new NativeArray<int>(new[] { 5, 5, 5 }, Allocator.Temp);

            try
            {
                // Zero extent.
                var zeroRect = InfluenceShape.SolidRect(int2.zero, new int2(0, 5), 1);
                Assert.AreEqual(0, CompositeBaker.LayerCount(zeroRect, target3),
                    "zero-extent rect must produce no layers");
                var blobRef = CompositeBaker.Build(zeroRect, target3, Allocator.Persistent);
                Assert.AreEqual(0, blobRef.Value.Layers.Length, "zero-extent rect blob must be empty");
                Assert.IsTrue(CompositeShapeReader.Bounds(ref blobRef.Value, int2.zero).IsEmpty,
                    "empty composite must report empty bounds");
                Assert.AreEqual(0, CompositeShapeReader.EstimateSpanCount(ref blobRef.Value),
                    "empty composite must estimate zero spans");
                blobRef.Dispose();

                // Negative extent.
                var negativeRect = InfluenceShape.SolidRect(int2.zero, new int2(-4, -4), 1);
                Assert.AreEqual(0, CompositeBaker.LayerCount(negativeRect, target3),
                    "negative-extent rect must produce no layers");

                // Empty falloff table.
                var disc = InfluenceShape.Disc(int2.zero, 3, 1);
                Assert.AreEqual(0, CompositeBaker.LayerCount(disc, target0),
                    "empty targetPerDepth must produce no layers");

                // Degenerate annulus (inner >= outer) as the base shape.
                var badAnnulus = InfluenceShape.Annulus(int2.zero, 3, 5, 1);
                Assert.AreEqual(0, CompositeBaker.LayerCount(badAnnulus, target1),
                    "annulus with inner >= outer must produce no layers");

                // Flat falloff: only the depth-0 delta is non-zero.
                Assert.AreEqual(1, CompositeBaker.LayerCount(disc, targetFlat),
                    "constant targetPerDepth must collapse to a single depth-0 layer");
            }
            finally
            {
                target3.Dispose();
                target1.Dispose();
                target0.Dispose();
                targetFlat.Dispose();
            }
        }

        [Test]
        public void MaximumSizeShapesSaturateInsteadOfOverflowing()
        {
            var target2 = new NativeArray<int>(new[] { 1, 2 }, Allocator.Temp);
            var target1 = new NativeArray<int>(new[] { 1 }, Allocator.Temp);

            try
            {
                // Huge-but-valid radius: span estimates must clamp to int.MaxValue, and summing two
                // clamped layers must saturate rather than wrap negative.
                var hugeDisc = InfluenceShape.Disc(int2.zero, 1 << 30, 1);
                Assert.AreEqual(2, CompositeBaker.LayerCount(hugeDisc, target2),
                    "huge disc with two distinct depth targets must bake two layers");
                var blobRef = CompositeBaker.Build(hugeDisc, target2, Allocator.Persistent);
                var estimate = CompositeShapeReader.EstimateSpanCount(ref blobRef.Value);
                Assert.AreEqual(int.MaxValue, estimate,
                    "two saturated layer estimates must saturate at int.MaxValue, not overflow to a negative count");
                blobRef.Dispose();

                // int.MaxValue radius makes the disc bounds arithmetic overflow; the documented
                // treatment is rejection as empty, never a wrapped/corrupted composite.
                var overflowDisc = InfluenceShape.Disc(int2.zero, int.MaxValue, 1);
                Assert.AreEqual(0, CompositeBaker.LayerCount(overflowDisc, target1),
                    "disc whose bounds overflow int range must be treated as empty, not wrapped");
            }
            finally
            {
                target2.Dispose();
                target1.Dispose();
            }
        }
    }
}
