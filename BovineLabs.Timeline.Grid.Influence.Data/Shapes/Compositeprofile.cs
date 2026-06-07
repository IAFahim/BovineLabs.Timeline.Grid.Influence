using System;
using BovineLabs.Timeline.Grid.Influence.Data;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;

namespace BovineLabs.Timeline.Grid.Influence.Authoring
{
    [Serializable]
    public struct CompositeProfile
    {
        public int Peak;
        public int Levels;

        [Tooltip("Weight along distance from center: x=0 center, x=1 edge.")]
        public AnimationCurve DistanceToWeight;

        public static CompositeProfile Default => new()
        {
            Peak = 8,
            Levels = 8,
            DistanceToWeight = AnimationCurve.Linear(0f, 1f, 1f, 0f)
        };

        public int ClampedLevels(in InfluenceShape baseShape)
        {
            return math.clamp(Levels, 1, CompositeBaker.MaxInset(baseShape) + 1);
        }

        public int WeightAtDepth(int depth, int levels)
        {
            var span = math.max(1, levels - 1);
            var distance01 = 1f - math.saturate(depth / (float)span);
            var curve = DistanceToWeight ?? AnimationCurve.Linear(0f, 1f, 1f, 0f);
            return (int)math.round(math.max(0, Peak) * math.saturate(curve.Evaluate(distance01)));
        }

        public NativeArray<int> SampleDepthWeights(in InfluenceShape baseShape, Allocator allocator)
        {
            var levels = ClampedLevels(baseShape);
            var weights = new NativeArray<int>(levels, allocator);
            for (var depth = 0; depth < levels; depth++)
                weights[depth] = WeightAtDepth(depth, levels);

            return weights;
        }
    }
}